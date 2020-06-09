using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SyncProLibrary
{
    /// <summary>
    /// Telemetry message class. Each message is consturcted of a common section and a custom section. Each are collections of key:values.
    /// </summary>
    public class TelemetryMessage
    {
        #region Delegates
        public delegate void TelemetryChangeEventHandler(TelemetryMessage tMsg);
        #endregion

        #region Events
        /// This events triggers when a new field is added or updated 
        public event TelemetryChangeEventHandler TelemetryChange;
        #endregion
        public Dictionary<string, object> common;
        public Dictionary<string, object> custom;

        /// <summary>
        /// Default constructor
        /// </summary>
        public TelemetryMessage()
        {

        }

        /// <summary>
        /// Creates a new telemetry message. 
        /// Status and firmwar_version fields are mandatory, so they are added at the constructor
        /// </summary>
        /// <param name="status"></param>
        public TelemetryMessage(string status, string fwVersion)
        {
            common = new Dictionary<string, object>();
            custom = new Dictionary<string, object>();

            common.Add("status", status);   //Add status field
            common.Add("firmware_version", fwVersion);
        }

        /// <summary>
        /// Checks to see if the key exists, if it does - update the value,
        /// if not, create the key and set the value to it.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void AddCommonKeyValue(string key, object value)
        {
            if (common.ContainsKey(key))
                common[key] = value;
            else
                common.Add(key, value);

            //Trigger TelemetryChange
            OnTelemetryUpdate();
        }

        /// <summary>
        /// Checks to see if the key exists, if it does - update the value,
        /// if not, create the key and set the value to it.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void AddCustomKeyValue(string key, object value)
        {
            if (custom.ContainsKey(key))
                custom[key] = value;
            else
                custom.Add(key, value);

            //Trigger TelemetryChange
            OnTelemetryUpdate();
        }

        protected virtual void OnTelemetryUpdate()
        {
            TelemetryChangeEventHandler handler = TelemetryChange;
            if (handler != null)
                handler(this);
        }

        public override string ToString()
        {
            string res = "";

            res += "{   \n";
            res += "common:  \n[";
            foreach (KeyValuePair<string, object> kv in common)
            {
                res += string.Format("{0}:{1},", kv.Key, kv.Value);
            }

            res += "]   \n  custom:  \n[";
            foreach (KeyValuePair<string, object> kv in custom)
            {
                res += string.Format("{0}:{1},", kv.Key, kv.Value);
            }
            res += "]   \n  }";

            return res;//.Replace("\n", Environment.NewLine); ;
        }

    }

}