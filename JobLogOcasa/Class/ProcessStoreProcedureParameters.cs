﻿using System.Data;


namespace JobLogOcasa.Class
{
    public class ProcessStoreProcedureParameters
    {
        public string name { get; set; }

        public SqlDbType type { get; set; }

        public object value { get; set; }
    }
}