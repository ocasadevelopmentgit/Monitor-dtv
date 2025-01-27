﻿using Newtonsoft.Json;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using GenericProcessLog.Class;
using GenericProcessLog.Interface;
using System.Text;
using System.Collections.Generic;

namespace GenericProcessLog
{
    public class Citaciones : IProcessTemplate
    {


        /// <summary>
        /// Proceso principal para el procesamiento
        /// </summary>
        /// <param name="parameters">Lista de parametros que provienen del lanzador</param>
        /// <returns></returns>
        public object Process(object[] parameters)
        {
            Configuration config = null;
            string exeConfigPath = this.GetType().Assembly.Location;
            AppConfiguration appConfig = new AppConfiguration();

            GenericResponse response = null;
            try
            {
                // Configuración de la DLL 
                // Este proceso busca el archivo .config de la DLL para obtener los 
                // parámetros de configuración
                try
                {
                    // Por defecto inicializo la variable del SP
                    //appConfig.sp_get_process_config = "Monitor_ObtenerConfiguracionJob";
                    config = ConfigurationManager.OpenExeConfiguration(exeConfigPath);
                    if (config != null) {
                        //
                        //Agregar aqui las claves de configuracion que se agregan al archivo .config
                        //
                        appConfig.sp_get_process_config = GetAppSetting(config, "StoredProcedureJobsConfig");
                        appConfig.sp_update_config = GetAppSetting(config, "StoredProcedureUpdateConfig");

                    }
                }
                catch (Exception) {
                    //Si se produce un error, significa que la dll no tiene archivo de configuracion.                   
                }

                // Identificador de aplicación
                // Se utiliza para hacer búsquedas de datos específicos para esta aplicación
                // Llega por parámetro desde el lanzador con el nombre "idapp"
                object[] data;
                foreach (string param in parameters) {
                    if (param.Contains("idapp"))
                    {
                        data = param.Split("=".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                        if (data.Length > 1)
                        {
                            appConfig.id_application = int.Parse(data[1].ToString());
                        }
                    }
                }

                if(appConfig.id_application > 0)
                    response = FullProcess(appConfig);
                //else
                //  RegisterError(result.message);
            }
            catch(System.Exception ex)
            {
                // Invocar a la api para loguear el error
                RegisterLog(new LogClass()
                {
                    id_aplicacion = appConfig.id_application,
                    fecha = DateTime.Now,
                    id_tipo_log = 3,
                    procedencia = "dll",
                    descripcion_error = ex.Message,
                    codigo_agrupador = "",
                    descripcion_general = "Error en DLL que registra errores de archivo LOG",
                    descripcion_paquete = "DLL",
                    descripcion_respuesta = ""

                });
            }
            return response;
        }

        /// <summary>
        /// Esta funcion es la que tiene toda la logica para obtener los logs de un repositorio
        /// y guardarlos en la tabla de logs
        /// </summary>
        /// <returns></returns>
        private GenericResponse FullProcess(AppConfiguration appConfig)
        {
            Records<ProcessParameters> configurations = new Records<ProcessParameters>();
            GenericResponse response = new GenericResponse();
            dbServices services = new dbServices();
            try
            {
                // Obtiene la configuracion para el procesamiento 
                //
                configurations = services.GetConfiguracion(new ProcessStoreProcedure() {
                    name = appConfig.sp_get_process_config,
                    parameters = {
                        new ProcessStoreProcedureParameters()
                        {
                            name="@id_aplicacion",
                            type=System.Data.SqlDbType.Int,
                            value= appConfig.id_application
                        }
                    }

                });

                // Debe conocer que SP ejecutar para poder generar el log
                if(configurations != null)
                {
                    response = RegisterLogFromFile(configurations, appConfig);
                }
                else
                    response = new GenericResponse()
                    {
                        description = "El proceso no tiene configurado los parametros de operaciones",
                        operation = false,
                        response = "ERROR"
                    };


            }
            catch (Exception ex)
            {

                response.operation = false;
                response.response = "ERROR";
                response.description = "Job Citaciones: " + ex.Message;
            }

            return response;
        }

        /// <summary>
        /// Registra cada operacion
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        private GenericResponse RegisterLogFromFile(Records<ProcessParameters> param, AppConfiguration appConfig)
        {
            ProcessConfiguration conf = new ProcessConfiguration(param);
            GenericResponse response = new GenericResponse();

            string message = string.Empty;
            DateTime date = DateTime.Now;
            DateTime lastModified = System.IO.File.GetLastWriteTime(conf.path);   // Fecha del archivo de log

            if (lastModified.Date > conf.ultima_fecha_procesada.Date) { conf.ultima_linea_procesada = 0; conf.ultima_posicion_procesada = 0; }
            if (lastModified.Date < conf.ultima_fecha_procesada.Date) return new GenericResponse
            {
                operation = false,
                description = "Fecha de archivo inferior a la ultima fecha procesada"
            };
            // Lee el archivo
            List<string> allLines = FastReadLogFile(conf, appConfig);

            // Procesa todas las lineas leidas
            foreach (string line in allLines)
            {
                if (line.ToUpper().Contains(" " + conf.palabra_clave_busqueda.ToUpper() + " ") || conf.palabra_clave_busqueda == "")
                {

                    DateTime.TryParse(line.Substring(7, 19).Replace(",", "."), out date);
                    message = line.Substring(6, 23) + " ";
                    message += line.Substring(84);
                    // Registra el error
                    response = RegisterLog(new LogClass()
                    {
                        id_aplicacion = appConfig.id_application,
                        fecha = date,
                        id_tipo_log = 3,
                        procedencia = "JOB",
                        id_cliente = "0",
                        descripcion_error = message,
                        codigo_agrupador = "ErrJOB",
                        descripcion_general = "Error encontrado en archivo de log",
                        descripcion_paquete = "JOB",
                        descripcion_respuesta = "-"

                    });
                }
            }

            // Actualiza la linea leida
            UpdateConfiguration(appConfig, "ultima_fecha_procesada", DateTime.Now.ToString("yyyy-MM-dd"));
            UpdateConfiguration(appConfig, "ultima_hora_procesada", DateTime.Now.ToString("HH:mm:ss"));
            return response;
        }

        /// <summary>
        /// Obtiene la clave de configuracion
        /// </summary>
        /// <param name="config">Clase de configuracion de la dll</param>
        /// <param name="key">clave a obtener</param>
        /// <returns></returns>
        string GetAppSetting(Configuration config, string key)
        {
            KeyValueConfigurationElement element = config.AppSettings.Settings[key];
            if (element != null)
            {
                string value = element.Value;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            return string.Empty;
        }

        /// <summary>
        /// Registra el error en la api
        /// </summary>
        /// <param name="message"></param>
        private GenericResponse RegisterLog(LogClass log)
        {
            GenericResponse result = new GenericResponse();
            string api = System.Configuration.ConfigurationManager.AppSettings["apiAddLog"];
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback =
                new System.Net.Security.RemoteCertificateValidationCallback(CheckValidationResult);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(api);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";

                //token                 
                //request.Headers.Add("Authorization", token);

                System.Text.UTF8Encoding encoding = new System.Text.UTF8Encoding();
                LogClass requestMessage = new LogClass();

                requestMessage.id_aplicacion = log.id_aplicacion;
                requestMessage.fecha = log.fecha;
                requestMessage.id_tipo_log = log.id_tipo_log;
                requestMessage.descripcion_general = log.descripcion_general;
                requestMessage.procedencia = log.procedencia;
                requestMessage.id_cliente = log.id_cliente;
                requestMessage.descripcion_paquete = log.descripcion_paquete;
                requestMessage.descripcion_error = log.descripcion_error;
                requestMessage.descripcion_respuesta = log.descripcion_respuesta;
                requestMessage.codigo_agrupador = log.codigo_agrupador;

                var requestLog = Newtonsoft.Json.JsonConvert.SerializeObject(requestMessage);

                Byte[] byteArray = encoding.GetBytes(requestLog);
                request.ContentLength = byteArray.Length;

                using (Stream dataStream = request.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }

                WebResponse response = request.GetResponse();
                if (((System.Net.HttpWebResponse)response).StatusCode == System.Net.HttpStatusCode.OK)
                {
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {

                        var content = reader.ReadToEnd();
                        GenericResponse data = (GenericResponse)JsonConvert.DeserializeObject(content, typeof(GenericResponse));
                        result = data;

                    }

                }
                else
                    // Se produjo un error al llamar a la api.
                    result.operation = false;

            }
            catch (WebException web_ex)
            {
                return new GenericResponse()
                {
                    description = web_ex.Message,
                    response = "ERROR",
                    operation = false
                };
            }
            catch (Exception ex)
            {
                return new GenericResponse()
                {
                    description = ex.Message,
                    response = "ERROR",
                    operation = false
                };
            }
            return result;
        }

        /// <summary>
        /// Validacion del certificado de envio a la api.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="certificate"></param>
        /// <param name="chain"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        private static bool CheckValidationResult(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="appConfig"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private bool UpdateConfiguration(AppConfiguration appConfig, string key, string value)
        {
            dbServices services = new dbServices();
            return(services.UpdateConfiguration(new ProcessStoreProcedure()
            {
                name = appConfig.sp_update_config,
                parameters = {
                        new ProcessStoreProcedureParameters()
                        {
                            name="@id_applicacion",
                            type=System.Data.SqlDbType.Int,
                            value= appConfig.id_application
                        },
                        new ProcessStoreProcedureParameters()
                        {
                            name="@clave",
                            type=System.Data.SqlDbType.VarChar,
                            value= key
                        },
                        new ProcessStoreProcedureParameters()
                        {
                            name="@valor",
                            type=System.Data.SqlDbType.VarChar,
                            value= value
                        }
                    }

            }));


        }

        /// <summary>
        /// Lee el archivo de log
        /// </summary>
        /// <param name="conf"></param>
        /// <returns></returns>
        private List<string> ReadLogFile(ProcessConfiguration conf, int skip)
        {
            List<string> lines = new List<string>();
            long index = 1;
            try
            {
                // Lee todas las lineas del archivo de log, a partir de la ultima registrada
                // Abre el archivo en modo compartido
                using (var logfile = new FileStream(conf.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(logfile))
                {
                    List<string> file = new List<string>();                                        
                    while (!sr.EndOfStream)
                    {
                        string line = sr.ReadLine();
                        if (index>skip) 
                            lines.Add(line);
                        index++;
                    }

                };
            }
            catch(System.Exception)
            {

            }
            return lines;
        }

        /// <summary>
        /// Lee el archivo de log a gran velocidad
        /// </summary>
        /// <param name="conf"></param>
        /// <param name="appConfig"></param>
        /// <returns></returns>
        private List<string> FastReadLogFile(ProcessConfiguration conf, AppConfiguration appConfig)
        {
            List<string> lines = new List<string>();

            // Abre archivo en modo compartido
            FileStream stream = new FileStream(conf.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;

                // Hace un salto a la ultima posicion leida del archivo
                stream.Seek(conf.ultima_posicion_procesada, SeekOrigin.Begin);

                // Lee lina a linea a partir de la posicion del puntero de lectura
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                    // Mantiene el tracking de posicion
                    conf.ultima_linea_procesada += 1;
                    conf.ultima_posicion_procesada += line.Length + 2; // Add 2 for newline
                }
            }
            UpdateConfiguration(appConfig, "ultima_posicion_procesada", conf.ultima_posicion_procesada.ToString());
            UpdateConfiguration(appConfig, "ultima_linea_procesada", conf.ultima_linea_procesada.ToString());
            return lines;
        }
    }

}