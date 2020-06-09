using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SyncProLibrary
{
    public class ServerResponses
    {
        /// <summary>
        /// Represents a server response object
        /// </summary>
        public class TelemetryResponse
        {
            public bool success { get; set; }
            //These fields are returned after sending a telemetry message
            public int config_version { get; set; }         //this is used to notify that there's a new Configurations waiting
            public int info_version { get; set; }           //New info updated for device
            public bool command { get; set; }               //New command is waiting
            public bool new_licenses { get; set; }          //New license is waiting

            public override string ToString()
            {
                string res = string.Format("{{  \n  success:{0}  \n info_version: {1}    \n config_version:{2} \n  command:{3} \n  new_licenses:{4} \n}}", success, info_version, config_version, command, new_licenses);

                return res;//.Replace("\n",Environment.NewLine);
            }
        }

        public class SetConfigResponse
        {
            public bool success { get; set; }
            public int version { get; set; }
            public string error { get; set; }

            public string ToString() { return string.Format("SetConfigResponse - success:{0}; version - {1}; error - {2}", success, version, error); }
        }

        public class GetCommandResponse
        {
            public int id { get; set; }
            public string status { get; set; }
            public string name { get; set; }
            public string parameters { get; set; }
            public string error { get; set; }
        }

        public class SendDumpResponse
        {
            public string success { get; set; }
            public string error { get; set; }
        }
    }
}