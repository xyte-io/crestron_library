using System;
using System.Text;
using Crestron.SimplSharp;                          				// For Basic SIMPL# Classes
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Crestron.SimplSharp.Net;
using Crestron.SimplSharp.Net.Https;
using Crestron.SimplSharp.CrestronIO;
using Crestron.SimplSharp.Cryptography;
using Crestron.SimplSharp.Ssh;
using Crestron.SimplSharp.CrestronAuthentication;

namespace SyncProLibrary
{
    /// <summary>
    /// This holds the device info class as reported by the device Info API call
    /// </summary>
    public class DeviceInfo
    {
        public string id { get; set; }
        public Manufacturer manufacturer { get; set; }
        public Object config { get; set; }
        public State state { get; set; }
        public DateTime last_seen { get; set; }
        public int version { get; set; }
        public Custom custom { get; set; }
        public string name { get; set; }

        public class Manufacturer
        {
            public string sn { get; set; }
            public string mac { get; set; }
            public string model { get; set; }
            //public string type { get; set; }
        }

        public class State
        {
            public string status { get; set; }
            public string firmware_version { get; set; }
        }

        public class Crestron
        {
            public string manifest { get; set; }
            public string project { get; set; }
            public string commands { get; set; }
            public string firmware { get; set; }
            public string user_program { get; set; }
            public string user_program_number { get; set; }
        }

        public class Custom
        {
            public Crestron crestron { get; set; }
        }
    }

    /// <summary>
    /// Parent class for all SyncPro devices. This also servers as a custom device
    /// </summary>
    public class SyncProDevice
    {
        #region Delegates
        public delegate void ConfigurationsUpdateHandler(object sender, SyncProDeviceConfig config);
        public delegate void DeviceInfoUpdateHandler(object sender, DeviceInfo config);
        #endregion

        #region Events
        //This is an event that S+ registers to, to get notficiations when there's a Configurations update
        public event ConfigurationsUpdateHandler ConfigUpdate;
        public event DeviceInfoUpdateHandler DeviceInfoUpdate;
        #endregion

        #region Constants
        protected const string FIRMWARE_VERSION = "0.3"; //This is the fixed library version reported with each telemetry message
        private const string STAGING_SERVER_URL = "https://hub.staging.syncpro.io/v1/devices/";
        private const string PRODUCTION_SERVER_URL = "https://hub.syncpro.io/v1/devices/";
        private const string MANIFEST_URI_PREFIX = "https://hub.staging.syncpro.io/external/crestron/";
        private const int KEEP_ALIVE_TIMEOUT_MS = 300000;
        #endregion

        #region Device properties
        //Device's credentials. Needed for every API call.
        protected string _deviceUuid { get; set; }
        protected string _deviceAccessKey { get; set; }

        protected DirectoryInfo _ConfigurationsFilesDirectory { get; set; }
        public SyncProDeviceConfig deviceConfig { get; set; }   //Full device Configurations
        public DeviceInfo deviceInfo { get; set; }              //The device's information

        protected CTimer _keepAliveTimer { get; set; }          //a Timer to send telemetry every predefiend seconds
        protected TelemetryMessage _tMsg { get; set; }          //Telemetry message. As we add telemetry key:values, they are kept here. This ensures that if one is missed, it will be sent next time.
        protected string _activeServerUrl { get; set; }         //The server we're working with, based on the file Configurations
        protected bool _debugMode { get; set; }                 //Curent _debugMode mode



        #endregion

        /// <summary>
        /// Default constracutor
        /// </summary>
        public SyncProDevice()
        {
            this.deviceConfig = new SyncProDeviceConfig();                  //Create devault object                  
            _tMsg = new TelemetryMessage("offline", FIRMWARE_VERSION);      //Create the basic telemetry message.
            //Status is offline until otherwise is reported

            //Create device Configurations folder, where all devices' Configurations files will be saved
            this._ConfigurationsFilesDirectory = SyncProMethods.CreateConfigurationsFilesDirectory();

            //Listen to telemetry change event, and when it changes, triger the send telemetry method
            _tMsg.TelemetryChange += new TelemetryMessage.TelemetryChangeEventHandler(SendTelemetry);

            //Configure keep alive timeout to sent telemetry every 5 minutes
            _keepAliveTimer = new CTimer(this.SendKeepAlive, null, KEEP_ALIVE_TIMEOUT_MS, KEEP_ALIVE_TIMEOUT_MS);

            //Set running mode - in dev or production
            SetupDevMode();
        }

        /// <summary>
        /// This will be called by S+ when the program starts. It is used to set the device's credentials, 
        /// and allows the device to run some initialiozation if required.
        /// </summary>
        public virtual void InitDevice(string uuid, string accessKey)
        {
            this._deviceUuid = uuid;
            this._deviceAccessKey = accessKey;

            GetCurrentDeviceValues();        //Each device update on it's own local values

            //Get device's information fro mserver
            GetDeviceInformation();

            //Sync device's Configurations betwqeen the local version and the server's version for the first time
            SyncConfigOnInit();

        }

        /// <summary>
        /// This should be called when the devie first init.
        /// </summary>
        protected void SyncConfigOnInit()
        {
            //      Now, we need to decide which config we're working on - local file (if found), a complete new one, or from the server.
            //      The logic is as follows - 
            //1.    First, we look for a local Configurations file that was saved. If we find one, we load it.
            if (_debugMode) CrestronConsole.PrintLine("Looking for local file - {0}", this._ConfigurationsFilesDirectory.FullName + this._deviceUuid + ".dat");
            string decviceLocalConfig = SyncProMethods.ReadJsonFromFile(this._ConfigurationsFilesDirectory.FullName + this._deviceUuid + ".dat");
            if (decviceLocalConfig != null)
            {
                if (_debugMode) CrestronConsole.PrintLine("Found local Configurations for device {0} ({1})", this.deviceInfo.name, this._deviceUuid);
                this.deviceConfig = JsonConvert.DeserializeObject<SyncProDeviceConfig>(decviceLocalConfig);
                SetDeviceConfigurations();      //We set the last known version that we have to the server. If the server holds a newer one, it will ignore it
            }

            //2.    If a local config could not be found, it's either the first time we connect or the config was deleted for some reason.
            //      So, we look for config on the server. If it is > 0, this means that the device was pre-configured from the server - 
            else
            {
                if (_debugMode) CrestronConsole.PrintLine("Could not find local Configurations for device {0} ({1}). Getting device Configurations", this.deviceInfo.name, this._deviceUuid);
                //Get Configurations from the cloud
                SyncProDeviceConfig dConfig = GetDeviceConfigurations();

                //Device was pre-configured from the cloud, we should applky Configurations.
                if (dConfig.version > 0)
                    SaveAndApplyNewDeviceConfigurations(dConfig);
                else
                {
                    //3. Finally, if we can't find it in the server, than it's a new device that has not been configured on the server yet.
                    SetDeviceConfigurations();  //We then report the device's config to the cloud
                }
            }
        }

        /// <summary>
        /// This method will be called when the deviec is first initizliaed. It pulls the device's current status into the deviceConfig class.
        /// This methos must be ovveride by the derived class, and it should update its local config, and set it to the cloud service.
        /// </summary>
        protected virtual void GetCurrentDeviceValues() { }

        /// <summary>
        /// Looks for _debugMode Configurations file. If a file is found, then sets _debugMode parameters. 
        /// If not, setup parameters for production.
        /// </summary>
        protected void SetupDevMode()
        {
            if (_debugMode) CrestronConsole.PrintLine("Configuring dev mode");
            //See if there's a _debugMode mode file in the config directory
            string _debugModeConfigFile = SyncProMethods.ReadJsonFromFile(this._ConfigurationsFilesDirectory + "config.json");
            if (_debugModeConfigFile == null)
            {
                //No config file was found
                if (_debugMode) CrestronConsole.PrintLine("No _debugModeConfigFile found");
                this._debugMode = false;
                this._activeServerUrl = PRODUCTION_SERVER_URL;
            }
            else
            {
                if (_debugMode) CrestronConsole.PrintLine("Found _debugModeConfigFile found");
                this._debugMode = true;
                this._activeServerUrl = STAGING_SERVER_URL;
            }

            if (_debugMode) CrestronConsole.PrintLine("ACTIVE SERVER = {0} and _debugMode mode = {1}", this._activeServerUrl, this._debugMode);

        }

        /// <summary>
        /// In some cases, a device could have read only values, that when are read from the server for the first time, are null. This method is called 
        /// in "SaveAndApplyNewDeviceConfigurations" to override the null again with the device's data, before it is set to the server.
        /// </summary>
        /// <param name="config"></param>
        protected virtual void OverrideReadOnlyFields(SyncProDeviceConfig config) { }

        /// <summary>
        /// This method saves the new Configurations locally, applies it and notifiy on the update the SyncPro Device S+ module.
        /// If this is overriden bu a derived class, don't forget to call SetDeviceConfigurations at the end to nptify the server that appling the Configurations worked.
        /// </summary>
        /// <param name="config"></param>
        protected virtual void SaveAndApplyNewDeviceConfigurations(SyncProDeviceConfig config)
        {
            try
            {
                if (config != null)
                {
                    this.deviceConfig = config;

                    OverrideReadOnlyFields(config);

                    //Save new Configurations to file
                    SyncProMethods.WriteJsonToFile(_ConfigurationsFilesDirectory.FullName + _deviceUuid + ".dat", config);

                    //Set device config to the server
                    SetDeviceConfigurations();

                    //Update the values, and notify S+ if needed
                    OnConfigurationsUpdate(deviceConfig);
                }

            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ ApplyNewDeviceConfigurations", ex);
            }
        }

        /// <summary>
        /// This method calls the Get device info API call, and saves the returned value locally
        /// </summary>
        public virtual void GetDeviceInformation()
        {
            try
            {
                string response = SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/", RequestType.Get, _deviceAccessKey, "", "application/JSON");
                this.deviceInfo = JsonConvert.DeserializeObject<DeviceInfo>(response);

                OnDeviceInfoUpdate(this.deviceInfo);
            }
            catch (Exception ex) { SyncProMethods.LogException(@"SyncProDevice \ GetDeviceInformation", ex); }
        }

        /// <summary>
        /// This is a simple method to trigger get&apply from S+
        /// </summary>
        public virtual void GetAndApplyDeviceConfigurations()
        {
            SaveAndApplyNewDeviceConfigurations(GetDeviceConfigurations());
        }

        /// <summary>
        /// Downloads the last know device's Configurations from the server and saves them locally
        /// </summary>
        /// <returns>the server's Configurations version. null if could not get the config</returns>
        public virtual SyncProDeviceConfig GetDeviceConfigurations()
        {
            try
            {
                string response = SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/config", RequestType.Get, _deviceAccessKey, "", "application/JSON");
                //this.deviceConfig = JsonConvert.DeserializeObject<SyncProDeviceConfig>(response);

                return (JsonConvert.DeserializeObject<SyncProDeviceConfig>(response));
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ GetDeviceConfigurations", ex);
                return null;
            }

        }

        /// <summary>
        /// Sends to the server the current device's Configurations. This is used by S# or S+
        /// </summary>
        public virtual void SetDeviceConfigurations()
        {
            SetCustomConfigurations(this.deviceConfig);
        }

        /// <summary>
        /// This is a generci device to set any json object
        /// </summary>
        /// <param name="obj"></param>
        protected virtual void SetCustomConfigurations(Object obj)
        {
            try
            {
                deviceConfig.version++;                         //Advancing the Configurations version

                ServerResponses.SetConfigResponse sr = JsonConvert.DeserializeObject<ServerResponses.SetConfigResponse>(
                    SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/config", RequestType.Post, _deviceAccessKey,
                    JsonConvert.SerializeObject(obj, Formatting.None, new Newtonsoft.Json.Converters.StringEnumConverter()), "application/JSON"));

                if (sr.success)
                    //Succesfully updated server with new Configurations. This is not really needed when incrementing the version above - right? 
                    deviceConfig.version = sr.version;
                else
                    SyncProMethods.LogError(this, string.Format("Failed to set device Configurations with error - {0}", sr.ToString()));
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ SetDeviceConfigurations", ex);
            }
        }

        /// <summary>
        /// Returns the manifest URL for S+
        /// </summary>
        /// <returns></returns>
        public string GetManifestUrl()
        {
            if (this.deviceInfo != null && this.deviceInfo.custom != null && this.deviceInfo.custom.crestron != null)
                return this.deviceInfo.custom.crestron.manifest;

            return "";
        }

        /// <summary>
        /// Sets the device's hostname. This is common for many Crestron devices
        /// </summary>
        /// <param name="hostname"></param>
        public virtual void SetHostname(string hostname)
        {
            this.deviceConfig.networkProperties.hostName = hostname;
        }

        /// <summary>
        /// Sets the device's IP Address. This is common for many Crestron devices
        /// </summary>
        /// <param name="hostname"></param>
        public virtual void SetIPAddress(string ipAddress)
        {
            this.deviceConfig.networkProperties.staticIpAddress = ipAddress;
        }

        /// <summary>
        /// Looks at the server response to a telemetry message to see if needs to get new information, Configurations or other commands
        /// </summary>
        /// <param name="sr"></param>
        protected virtual void CheckAndCompareServerResponsToLastKnownValues(ServerResponses.TelemetryResponse sr)
        {
            if (sr.config_version > deviceConfig.version)
            {
                if (_debugMode) CrestronConsole.PrintLine("Found new Configurations version. Old version - {0}, new version - {1}", deviceConfig.version, sr.config_version);
                //Save and apply new Configurations from server
                SaveAndApplyNewDeviceConfigurations(GetDeviceConfigurations());
            }
            if (sr.info_version > deviceInfo.version)
            {
                //New info
                GetDeviceInformation();
            }
            if (sr.command == true)
            {
                //There's new command waiting for the device - go get it Tiger!
                if (_debugMode) CrestronConsole.PrintLine("New Command is waiting");
                ApplyCommand(GetCommand());
            }

            if (sr.new_licenses)
            {
                //Todo:Implement
                if (_debugMode) CrestronConsole.PrintLine("New License");
            }

        }

        /// <summary>
        /// Get a pending command for the device
        /// </summary>
        /// <returns></returns>
        public virtual ServerResponses.GetCommandResponse GetCommand()
        {
            ServerResponses.GetCommandResponse sr;

            try
            {
                if (_deviceUuid != null && _deviceAccessKey != null)
                {
                    string response = SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/command", RequestType.Get, _deviceAccessKey, "", "application/JSON");

                    //convert response to JSON
                    sr = JsonConvert.DeserializeObject<ServerResponses.GetCommandResponse>(response);
                    if (sr != null) return (sr);

                }
                return null;
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ GetCommand", ex);
                return null;
            }
        }

        /// <summary>
        /// Run the command on the device
        /// </summary>
        /// <param name="command"></param>
        public virtual void ApplyCommand(ServerResponses.GetCommandResponse command) { }

        /// <summary>
        /// Collect device's dump and reports it
        /// </summary>
        public virtual void ReportDeviceDump() { }

        /// <summary>
        /// This updates and stores the current status of the device, and then sends a telemetry with its updated status
        /// </summary>
        /// <param name="status">fasle - offline; true - online</param>
        public virtual void UpdateStatus(string status)
        {
            if (status != null)
            {
                if (_debugMode) CrestronConsole.PrintLine("Updateing device status = {0}", status);
                //Update the telemetry object. Once update, it will automatically send an updated telemetry
                this._tMsg.AddCommonKeyValue("status", status);
            }
            else
                if (_debugMode) CrestronConsole.PrintLine("UpdateStatus = null");
        }

        //Telemetry
        /// <summary>
        /// Send a complete telemetry message, constructed of common and custom objects
        /// </summary>
        /// <param name="tMsg"></param>
        protected virtual void SendTelemetry(TelemetryMessage tMsg)
        {
            ServerResponses.TelemetryResponse sr;
            try
            {
                //serialize the tMsg and send it as telemetry
                if (_deviceUuid != null && _deviceAccessKey != null && tMsg != null)
                {
                    string response = SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/telemetry", RequestType.Post, _deviceAccessKey,
                        JsonConvert.SerializeObject(tMsg, Formatting.None, new Newtonsoft.Json.Converters.StringEnumConverter()), "application/JSON");

                    //convert response to JSON
                    sr = JsonConvert.DeserializeObject<ServerResponses.TelemetryResponse>(response);
                    if (_debugMode)
                        if (sr.success)
                            CrestronConsole.PrintLine("Sent telemetry message for device {1} ({0}); Message: {2} {3} Server response = {4}\n", _deviceUuid, deviceInfo.name, tMsg.ToString(), Environment.NewLine, sr.ToString());
                        else
                            CrestronConsole.PrintLine("Failed to send telemetry message for device {1} ({0}); Message: {2} {3} Server response = {4}\n", _deviceUuid, deviceInfo.name, tMsg.ToString(), Environment.NewLine, sr.ToString());

                    CheckAndCompareServerResponsToLastKnownValues(sr);
                }
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ SendTelemetry", ex);
            }
        }

        /// <summary>
        /// Callback function that sends "keep alive" telemetry every 5 minutes.
        /// </summary>
        /// <param name="userObject"></param>
        protected virtual void SendKeepAlive(object userObject)
        {
            this.SendTelemetry(_tMsg);
        }

        /// <summary>
        /// This method is used for S+ to send telemetry easily. The string must be valid key-value pair in JSON format (i.e. {"commonKey":"value"})
        /// </summary>
        /// <param name="str"></param>
        /// 
        public virtual void SendCommonTelemetry(string str)
        {
            try
            {
                KeyValuePair<string, object> kv = JsonConvert.DeserializeObject<KeyValuePair<string, object>>(str);
                _tMsg.AddCommonKeyValue(kv.Key, kv.Value);
            }
            catch (Exception ex) { SyncProMethods.LogException(this, ex); }
        }

        /// <summary>
        /// This method is used for S+ to send telemetry easily. The string must be valid key-value pair in JSON format (i.e. {"commonKey":"value"})
        /// </summary>
        /// <param name="str"></param>
        public virtual void SendCustomTelemetry(string str)
        {
            try
            {
                KeyValuePair<string, object> kv = JsonConvert.DeserializeObject<KeyValuePair<string, object>>(str);
                _tMsg.AddCustomKeyValue(kv.Key, kv.Value);
            }
            catch (Exception ex) { SyncProMethods.LogException(this, ex); }
        }

        protected virtual string GetDefaultManifestUrl()
        {
            return string.Format("{0}{1}/manifest?access_token={2}", MANIFEST_URI_PREFIX, this._deviceUuid, this._deviceAccessKey);
        }

        //OnEvents
        /// <summary>
        /// On Configurations update, trigger the event handler, and notify S+ module 
        /// </summary>
        protected virtual void OnConfigurationsUpdate(SyncProDeviceConfig config)
        {
            ConfigurationsUpdateHandler handler = ConfigUpdate;
            if (handler != null)
                handler(this, config);
        }

        /// <summary>
        /// On deviceinfo update, trigger the event handler, and notify S+ module 
        /// </summary>
        protected virtual void OnDeviceInfoUpdate(DeviceInfo info)
        {
            DeviceInfoUpdateHandler handler = DeviceInfoUpdate;
            if (handler != null)
                handler(this, info);
        }

    }

    public class ThreeSeriesControlSystem : SyncProDevice
    {
        private const string LOCALHOST = "127.0.0.1";
        private string _sshUsername, _sshPassword;        //This holds the user name and password needed for SSH commands

        public ThreeSeriesControlSystem()
        {
            this._tMsg.AddCommonKeyValue("status", "online");   //Control system is always online

            //Create basic Configurations classes
            this.deviceConfig.networkProperties = new NetworkProperties();
            this.deviceConfig.generalProperties = new GeneralDeviceProperties();
            this.deviceConfig.authenticationProperties = new AuthenticationProperties();

            //These are the defaults for ssh
            _sshUsername = "crestron";
            _sshPassword = "";
        }

        /// <summary>
        /// This will be called by S+ when the program starts. It allows the device to run some initialiozation if required.
        /// </summary>
        public override void InitDevice(string uuid, string accessKey)
        {
            base.InitDevice(uuid, accessKey);
        }

        protected override void GetCurrentDeviceValues()
        {
            //Update General Configurations
            this.GetCurrentGeneralConfig();

            //Update network Configurations
            this.GetCurrentNetworkConfig();
        }

        /// <summary>
        /// This methods reads some local parameters and set them to the device's Configurations
        /// </summary>
        private void GetCurrentGeneralConfig()
        {
            this.deviceConfig.generalProperties.fwVersion = InitialParametersClass.FirmwareVersion;
            this.deviceConfig.generalProperties.programIDTag = InitialParametersClass.ProgramIDTag;
        }

        /// <summary>
        /// This methods reads the local ethernet parameters and set them to the device's Configurations
        /// </summary>
        private void GetCurrentNetworkConfig()
        {
            try
            {
                short adapterId = this.deviceConfig.networkProperties.adapterId = CrestronEthernetHelper.GetAdapterdIdForSpecifiedAdapterType(EthernetAdapterType.EthernetLANAdapter);              //Todo: Fix that to support Configurations of more than one adapaterID

                //Number of interfaces
                this.deviceConfig.networkProperties.numberOfEthernetInterfaces = InitialParametersClass.NumberOfEthernetInterfaces;

                //Mac Address
                this.deviceConfig.networkProperties.macAddress = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_MAC_ADDRESS, adapterId);

                //DHCP
                this.deviceConfig.networkProperties.dhcp = (CrestronEthernetHelper.GetEthernetParameter(
                    CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CURRENT_DHCP_STATE, adapterId).Equals("ON", StringComparison.OrdinalIgnoreCase)) ? true : false;

                //Webserver status
                this.deviceConfig.networkProperties.webServer = (CrestronEthernetHelper.GetEthernetParameter(
                    CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_WEBSERVER_STATUS, adapterId).Equals("ON", StringComparison.OrdinalIgnoreCase)) ? true : false;

                //Static IP Info
                this.deviceConfig.networkProperties.staticIpAddress = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_STATIC_IPADDRESS, adapterId);
                this.deviceConfig.networkProperties.staticNetMask = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_STATIC_IPMASK, adapterId);
                this.deviceConfig.networkProperties.staticDefRouter = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_STATIC_ROUTER, adapterId);

                //HostName
                this.deviceConfig.networkProperties.hostName = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_HOSTNAME, adapterId);

                //Domain Name
                this.deviceConfig.networkProperties.domainName = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_DOMAIN_NAME, adapterId);

                //Ports
                this.deviceConfig.networkProperties.cipPort = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CIP_PORT, adapterId);
                this.deviceConfig.networkProperties.securedCipPort = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_SECURE_CIP_PORT, adapterId);
                this.deviceConfig.networkProperties.ctpPort = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CTP_PORT, adapterId);
                this.deviceConfig.networkProperties.securedCtpPort = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_SECURE_CTP_PORT, adapterId);
                this.deviceConfig.networkProperties.webPort = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_WEB_PORT, adapterId);
                this.deviceConfig.networkProperties.securedWebPort = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_SECURE_WEB_PORT, adapterId);

                //Authenitcation
                this.deviceConfig.networkProperties.isAuthEnabled = InitialParametersClass.IsAuthenticationEnabled;


                //SSL
                this.deviceConfig.networkProperties.sslCertificate = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_SSL_TYPE, adapterId);

                //DNS Servers
                this.deviceConfig.networkProperties.dnsServers =
                    CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_DNS_SERVER, adapterId).Split(',');

                //DNS server comes in the following format - x.x.x.x (static/DHCP) - Since we force the data to be in ipv4, we remove the () section
                int i;
                foreach (string ds in this.deviceConfig.networkProperties.dnsServers)
                {
                    i = ds.IndexOf('(');
                    if (i > 0)
                        ds.Substring(0, i).Trim();
                }
            }
            catch (Exception ex) { SyncProMethods.LogException(this, ex); }
        }

        public override void ApplyCommand(ServerResponses.GetCommandResponse command)
        {
            try
            {
                if (command != null && command.status == "pending")
                {
                    switch (command.name)
                    {
                        case "reboot":
                            //Update on command done - for a reboot we update bfore we rebooted
                            SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/command", RequestType.Post, _deviceAccessKey, "{\"status\":\"done\"}", "application/JSON");
                            CrestronConsole.ConsoleCommandResponse("reboot\n");
                            break;
                        case "dump":
                            SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/command", RequestType.Post, _deviceAccessKey, "{\"status\":\"in_progress\"}", "application/JSON");
                            ReportDeviceDump();
                            //Update on command done
                            SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/command", RequestType.Post, _deviceAccessKey, "{\"status\":\"done\"}", "application/JSON");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ GetAndApplyCommands", ex);
            }
        }

        /// <summary>
        /// Collect device's dump and reports it
        /// </summary>
        public override void ReportDeviceDump()
        {
            string dump = string.Format("UTC date and time: {0}\n\n", DateTime.UtcNow);
            List<string> commands = new List<string>();
            try
            {
                if (_deviceUuid != null && _deviceAccessKey != null)
                {
                    commands.Add("time\n");
                    commands.Add("timezone\n");
                    commands.Add("progreg\n");
                    commands.Add("ipconfig /all\n");
                    commands.Add("showhw\n");
                    commands.Add("showlicense\n");
                    commands.Add("ver -v\n");
                    commands.Add("puf -results\n");
                    commands.Add("cpuload\n");
                    commands.Add("ramfree\n");
                    commands.Add("ipt -p:all -t\n");
                    commands.Add("reportcresnet\n");
                    commands.Add("err\n");

                    foreach (string cmd in commands)
                    {
                        string res = "";
                        CrestronConsole.SendControlSystemCommand(cmd, ref res);
                        dump += res;
                    }

                    //Report dump to server 

                    string response = SyncProMethods.HttpsJsonRequest(this._activeServerUrl + _deviceUuid + "/dump", RequestType.Post, _deviceAccessKey, dump, "text/plain");

                    //convert response to JSON
                    ServerResponses.SendDumpResponse sr = JsonConvert.DeserializeObject<ServerResponses.SendDumpResponse>(response);
                    if (_debugMode) CrestronConsole.PrintLine("Send Dump response - {0}:{1}", sr.success, sr.error);

                }
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProDevice \ ReportDeviceDump", ex);
            }


        }

        /// <summary>
        /// This will be implemented later, when the get space info API call will be
        /// </summary>
        /// <param name="lon"></param>
        /// <param name="lat"></param>
        private void ApplyLocation(double lon, double lat)
        {
            //CrestronEnvironment.Latitude = this.spaceInfo.latitude;
            //CrestronEnvironment.Longitude = this.spaceInfo.longtitude;
        }


        /// <summary>
        /// Sets the date and time of the control system
        /// </summary>
        /// <param name="dt"></param>
        private void ApplyTimeAndDate(DateTime dt)
        {
            CrestronEnvironment.SetTimeAndDate(
                (ushort)dt.Hour,
                (ushort)dt.Minute,
                (ushort)dt.Second,
                (ushort)dt.Month,
                (ushort)dt.Day,
                (ushort)dt.Year);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        /// <returns>If true - requires a reboot; false - does not require a reboot</returns>
        private bool ApplyNewDeviceConfigurations(SyncProDeviceConfig config)
        {
            bool requireReboot = false;
            if (config != null)
            {
                //Apply configuratin updates to the control system
                //Space Info

                //Location Update
                //ApplyLocation(this.spaceInfo.longtitude, this.spaceInfo.latitude);

                //Time and Date
                //ApplyTimeAndDate(this.spaceInfo.dateTime);

                //Todo: Update Time zone from list - CrestronEnvironment.SetTimeZone();

                //Apply authentication Configurations
                //ApplyAuthConfig(config.authenticationProperties);

                //Apply Network configuraitons - at the end, as it requires a reboot.
                requireReboot = ApplyNetworkConfig(config.networkProperties);

                //And then, reboot
                //Todo: Reboot only if needed
                
                return requireReboot;
            }
            return requireReboot;
        }

        /// <summary>
        /// Some read only values on the server that are reported by the device, can get overridden the first time with null. So we load them back from the control system
        /// </summary>
        /// <param name="config"></param>
        protected override void OverrideReadOnlyFields(SyncProDeviceConfig config)
        {
            this.deviceConfig.generalProperties.fwVersion = InitialParametersClass.FirmwareVersion;
            this.deviceConfig.generalProperties.programIDTag = InitialParametersClass.ProgramIDTag;
            this.deviceConfig.networkProperties.macAddress = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_MAC_ADDRESS, 0);
        }

        /// <summary>
        /// This is a simple method to send command over SSH
        /// </summary>
        /// <param name="host"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="command"></param>
        private void SendSshCommands(string host, string[] commands)
        {
            SshClient client = new SshClient(host, _sshUsername, _sshPassword);
            client.Connect();
            SshCommand sshCmd;
            foreach (string cmd in commands)
                sshCmd = client.RunCommand(cmd);
            client.Disconnect();
        }

        private void ApplyAuthConfig(AuthenticationProperties config)
        {
            if (config != null)
            {
                if (Authentication.Enabled)
                {
                    //Auth is currently enabled, try to get token with last known user
                    Authentication.UserToken uToken = Authentication.GetAuthenticationToken(this.deviceConfig.authenticationProperties.username,
                        this.deviceConfig.authenticationProperties.password);

                    if (Authentication.Enabled)
                    {
                        //Updating user information
                        //TODO:Not currently supported
                        //Maybe we need to remove users and then add the new one?

                    }
                    else
                    {
                        ////Disabling auth
                        //SendSshCommand(LOCALHOST, "auth off\n");
                        //SendSshCommand(LOCALHOST, _sshUsername + "\n");
                        //SendSshCommand(LOCALHOST, _sshPassword + "\n");
                    }
                }
                else
                {
                    //Auth is currently disabled
                    if (config.authEnabled)
                        //We re-enable auth
                        if (config.password == config.verifyPassword)
                        {
                            //Update local user and password
                            _sshUsername = (config.username == null) ? "crestron" : config.username;
                            _sshPassword = (config.password == null) ? "crestron" : config.password;

                            //SendSshCommand(LOCALHOST, "auth on\n");
                            //SendSshCommand(LOCALHOST, _sshUsername + "\n");
                            //SendSshCommand(LOCALHOST, _sshPassword + "\n");


                        }
                }
            }
        }

        /// <summary>
        /// After a new Configurations file was downloaded from the server, this method is called to apply it locally.
        /// </summary>
        /// <param name="config"></param>
        private bool ApplyNetworkConfig(NetworkProperties config)
        {
            //DHCP
            if (config.dhcp)
                CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_DHCP_STATE, config.adapterId, "ON");
            else
                CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_DHCP_STATE, config.adapterId, "OFF");

            //Webserver
            if (config.webServer != null)
            {
                if ((bool)config.webServer)
                    CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_WEBSERVER_STATE, config.adapterId, "ON");
                else
                    CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_WEBSERVER_STATE, config.adapterId, "OFF");
            }

            //Static IP Info
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_STATIC_IPADDRESS, config.adapterId, config.staticIpAddress);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_STATIC_IPMASK, config.adapterId, config.staticNetMask);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_STATIC_DEFROUTER, config.adapterId, config.staticDefRouter);

            //HostName
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_HOSTNAME, config.adapterId, config.hostName);

            //Domain Name
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_DOMAINNAME, config.adapterId, config.domainName);

            //Ports
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_CIP_PORT, config.adapterId, config.cipPort);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_SECURE_CIP_PORT, config.adapterId, config.securedCipPort);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_CTP_PORT, config.adapterId, config.ctpPort);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_SECURE_CTP_PORT, config.adapterId, config.securedCtpPort);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_WEB_PORT, config.adapterId, config.webPort);
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_SECURE_WEB_PORT, config.adapterId, config.securedWebPort);

            //SSL
            CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.SET_SSL_STATE, config.adapterId, config.sslCertificate);

            //DNS Server
            string[] localDnsServers = CrestronEthernetHelper.GetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_DNS_SERVER, config.adapterId).Split(',');

            //Remove current servers
            foreach (string ds in localDnsServers)
                CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.REMOVE_DNS_SERVER, config.adapterId, ds);

            //Add new servers
            if (config.dnsServers != null)
                foreach (string ds in config.dnsServers)
                    CrestronEthernetHelper.SetEthernetParameter(CrestronEthernetHelper.ETHERNET_PARAMETER_TO_SET.ADD_DNS_SERVER, config.adapterId, ds);

            return true;
        }

        /// <summary>
        /// To configure manifest for a control system, we need to connect to the local host over SSH,
        /// as the SendControlSystemCommand only allows programmer level commands.
        /// </summary>
        /// <param name="info"></param>
        protected override void OnDeviceInfoUpdate(DeviceInfo info)
        {
            base.OnDeviceInfoUpdate(info);
            try
            {
                if (info != null && info.custom != null & info.custom.crestron != null & info.custom.crestron.manifest != null)
                {
                    string auManifestUrlCmd = string.Format("aumanifesturl {0}\n", info.custom.crestron.manifest);
                    SendSshCommands(LOCALHOST, new string[] { auManifestUrlCmd, "auchecknow\n" });
                }
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"ThreeSeriesControlSystem \ OnDeviceInfoUpdate", ex);
            }
            //SshClient client = new SshClient(new ConnectionInfo("127.0.0.1",22,"admin",AuthenticationMethod

        }

        protected override void OnConfigurationsUpdate(SyncProDeviceConfig config)
        {
            string res = "";
            base.OnConfigurationsUpdate(config); //This is not really needed for control systems, but we leave if for consistancy 

            if (ApplyNewDeviceConfigurations(config))    //For control systemm, we first apply the new Configurationss 
                CrestronConsole.SendControlSystemCommand("Reboot\n", ref res);
        }
    }

    public class AirMedia : SyncProDevice
    {
        private Dictionary<short, string> resolutions;

        //Default ctor is a must
        public AirMedia()
        {
            if (this.deviceConfig != null)
                this.deviceConfig.airMediaProperties = new AirMedia200300DeviceConfig();
        }

        //Display Control
        public void SetAutoInputRoutingMode(int autoInputRouting)
        {
            this.deviceConfig.airMediaProperties.autoInputRouting =
                (autoInputRouting == 0) ? false : true;
        }

        public short GetAutoInputRoutingMode()
        {
            return (short)
                (this.deviceConfig.airMediaProperties.autoInputRouting ? 1 : 0);
        }

        //HDMI IN
        public void SetHdmiInHdcpSupport(short hdcpSupport)
        {
            this.deviceConfig.airMediaProperties.hdminInHdcpSupport =
                (hdcpSupport == 0) ? false : true;
        }

        public short GetHdmiInHdcpSupport()
        {
            return (short)
                (this.deviceConfig.airMediaProperties.hdminInHdcpSupport ? 1 : 0);
        }

        //AirMedia
        /// <summary>
        /// 
        /// </summary>
        /// <param name="mode">code mode, wehre 0=disable, 1=Random, 2=fixed</param>
        public void SetAirMediaLoginCodeMode(short mode)
        {
            switch (mode)
            {
                case 0:
                    this.deviceConfig.airMediaProperties.loginCodeMode = "Disabled";
                    break;
                case 1:
                    this.deviceConfig.airMediaProperties.loginCodeMode = "Random";
                    break;
                case 2:
                    this.deviceConfig.airMediaProperties.loginCodeMode = "Fixed";
                    break;
            }
        }

        /// <summary>
        /// Retuns the code mode 
        /// </summary>
        /// <returns>0 = Disabled; 1=Random; 2=Fixed</returns>
        public short GetAirMediaLoginCodeMode()
        {
            if (this.deviceConfig.airMediaProperties.loginCodeMode.CompareTo("Disabled") == 0)
                return 0;
            if (this.deviceConfig.airMediaProperties.loginCodeMode.CompareTo("Random") == 0)
                return 1;
            if (this.deviceConfig.airMediaProperties.loginCodeMode.CompareTo("Fixed") == 0)
                return 2;

            return 0;
        }

        public void SetDisplayCodeMode(short mode)
        {
            this.deviceConfig.airMediaProperties.displayCode =
                (mode == 0) ? false : true;
        }

        public void SetDisplayConnectionOptionsEnabled(short mode)
        {
            this.deviceConfig.airMediaProperties.displayConnectionOptions =
               (mode == 0) ? false : true;
        }

        public void SetDisplayConnectionOptionsMode(short mode)
        {
            switch (mode)
            {
                case 1:
                    this.deviceConfig.airMediaProperties.displayConnectionOptionsMode = "IP Address";
                    break;
                case 2:
                    this.deviceConfig.airMediaProperties.displayConnectionOptionsMode = "Hostname";
                    break;
                case 3:
                    this.deviceConfig.airMediaProperties.displayConnectionOptionsMode = "Hostname and Domain";
                    break;
                case 4:
                    this.deviceConfig.airMediaProperties.displayConnectionOptionsMode = "Custom URL";
                    break;
                default:
                    this.deviceConfig.airMediaProperties.displayConnectionOptionsMode = "IP Address";
                    break;
            }

        }

        public short GetDisplayConnectionOptionMode()
        {
            string mode = this.deviceConfig.airMediaProperties.displayConnectionOptionsMode;
            if (mode.CompareTo("IP Address") == 0)
                return 1;
            if (mode.CompareTo("Hostname") == 0)
                return 2;
            if (mode.CompareTo("Hostname and Domain") == 0)
                return 3;
            if (mode.CompareTo("Custom URL") == 0)
                return 4;

            return 1; //Default
        }

        public short GetDisplayCodeEnableMode()
        {
            return (short)
                (this.deviceConfig.airMediaProperties.displayCode ? 1 : 0);
        }

        public short GetDisplayConnectionOptionsEnableMode()
        {
            return (short)
                (this.deviceConfig.airMediaProperties.displayConnectionOptions ? 1 : 0);
        }
    }

    public class Space : SyncProDevice
    {
        public Space() { }
    }

    /// <summary>
    /// Occupancy sensor class, inherits from the SyncProDevice class and extends it for occupancy sensors
    /// </summary>
    public class OccupancySensor : SyncProDevice
    {
        /// <summary>
        /// SIMPL+ can only execute the default constructor. If you have variables that require initialization, please
        /// use an Initialize method
        /// </summary>
        public OccupancySensor()
        {
            this.deviceConfig.occupancyProperties = new OccupancySensorDeviceConfig();
        }

        /// <summary>
        /// Sets the timeout value, if it was changed from SIMPL.
        /// </summary>
        /// <param name="timeout"></param>
        public void SetTimeout(short timeout)
        {
            deviceConfig.occupancyProperties.timeout = timeout;
            base.SetDeviceConfigurations();
        }

        public void SetWhenVacatedMode(string mode)
        {
            if (mode == "Or" || mode == "And")
                deviceConfig.occupancyProperties.whenVacatedMode = mode;
            base.SetDeviceConfigurations();
        }
    }

    public class BasicTouchScreen : SyncProDevice
    {
        public BasicTouchScreen()
        {
            this.deviceConfig.userInterfaceProperties = new BasicUserInterfaceDeviceConfig();
            this.deviceConfig.networkProperties = new NetworkProperties();
            this.deviceConfig.userInterfaceProperties.hardButtons = new HardButtons();
        }
    }

    public class TouchScreen : SyncProDevice
    {
        #region Delegates
        public delegate void UiApplicationsUpdateHandler(object sender, SimplPlusDataClasses.AppsObject apps);
        public delegate void UiHomepageShortcutsUpdateHandler(object sender, SimplPlusDataClasses.ActionableButtonsObject buttons);
        public delegate void UiApplicationActivitiesUpdateHandler(object sender, SimplPlusDataClasses.ActionableButtonsObject buttons);
        #endregion

        #region Events
        //This event is being triggerd when the list of apps changes
        public event UiApplicationsUpdateHandler AppsUpdate;

        //This event is being triggered when the list of home page shotcuts is being updated
        public event UiHomepageShortcutsUpdateHandler HomepageShortcutsUpdate;

        //This event is called when the user selectes a new app. It's goal is to refresh the current selected app 
        //activitis list
        public event UiApplicationActivitiesUpdateHandler AppSelected;
        #endregion

        #region Properties
        public UiApp currentApp { get; set; }
        #endregion

        public TouchScreen()
        {
            if (this.deviceConfig != null)
                this.deviceConfig.userInterfaceProperties = new BasicUserInterfaceDeviceConfig();
        }

        public void SelectApp(short i)
        {
            OnAppSelection(i);
        }

        /// <summary>
        /// Overrides the base class by updating on the new applications as well.
        /// This class also call the base class to notify on the other Configurations updates
        /// </summary>
        /// <param name="config"></param>
        protected override void OnConfigurationsUpdate(SyncProDeviceConfig config)
        {
            base.OnConfigurationsUpdate(config);

            UiApplicationsUpdateHandler appsHandler = AppsUpdate;
            UiHomepageShortcutsUpdateHandler homepageShortcutsHandler = HomepageShortcutsUpdate;

            if (appsHandler != null)
                appsHandler(this, new SimplPlusDataClasses.AppsObject(this.deviceConfig.uiProperties.applications));

            if (homepageShortcutsHandler != null)
                homepageShortcutsHandler(this, new SimplPlusDataClasses.ActionableButtonsObject(this.deviceConfig.uiProperties.homePageShortcuts));


        }

        private void OnAppSelection(int i)
        {
            UiApplicationActivitiesUpdateHandler activitiesHandler = AppSelected;
            currentApp = this.deviceConfig.uiProperties.applications[i];

            if (activitiesHandler != null)
                activitiesHandler(this, new SimplPlusDataClasses.ActionableButtonsObject(currentApp.activities));
        }
    }

    /// <summary>
    /// This class extends a custom device class with object.
    /// </summary>
    public class CustomDeviceObject : CustomDevice
    {
        private string _objName;
        private int _index;

        public CustomDeviceObject()
        {

        }

        public void InitObject(string name, short isArray, short i)
        {
            this._objName = name;
            if (isArray > 0)        //This is an object in an array
                this._index = i;
            else
                this._index = -1;
        }

    }

    public class CustomDevice : SyncProDevice
    {


        public delegate void CustomConfigurationsUpdateHandler(object sender, CustomDeviceConfig config);
        public event CustomConfigurationsUpdateHandler CustomConfigUpdate;

        public CustomDeviceConfig customProperties;

        public CustomDevice()
        {
            customProperties = new CustomDeviceConfig();
        }

        public override void SetDeviceConfigurations()
        {
            SetCustomConfigurations(this.customProperties);
        }

        public override void InitDevice(string uuid, string accessKey)
        {
            base.InitDevice(uuid, accessKey);

        }

        /// <summary>
        /// Adds a new object
        /// </summary>
        /// <param name="objName"></param>
        private void AddObject(string objName)
        {
            //Todo: Implemenet
            //this.customProperties.Add(objName, new List<KeyValuePair<string, object>>());
        }

        /// <summary>
        /// This is used by S+ to add the key names when initializing.
        /// This is called after the device was already initialized, so if there was a local file, we read it first.
        /// </summary>
        /// <param name="keyName"></param>
        public void AddNewKeyName(string keyName)
        {
            if (keyName != "")
                if (!this.customProperties.properties.ContainsKey(keyName))
                    this.customProperties.properties.Add(keyName, null);
        }

        private void AddOrUpdateKvPair(string keyName, object value)
        {
            if (keyName != null && keyName != "")
            {
                if (this.customProperties.properties.ContainsKey(keyName))
                    this.customProperties.properties[keyName] = value;
                else
                    this.customProperties.properties.Add(keyName, value);

                //Finally - update the server on the new config.
                this.SetCustomConfigurations(this.customProperties);
            }
        }

        /// <summary>
        /// Adds a new string field
        /// </summary>
        /// <param name="keyName"></param>
        /// <param name="value"></param>
        public void AddOrUpdateStringField(string keyName, string value)
        {
            AddOrUpdateKvPair(keyName, value);
        }

        /// <summary>
        /// Add a new short\integer field
        /// </summary>
        /// <param name="keyName"></param>
        /// <param name="value"></param>
        public void AddOrUpdateShortField(string keyName, short value)
        {
            AddOrUpdateKvPair(keyName, value);
        }

        /// <summary>
        /// Add a new boolean field
        /// </summary>
        /// <param name="keyName"></param>
        /// <param name="value"></param>
        public void AddOrUpdateBooleanField(string keyName, short value)
        {
            AddOrUpdateKvPair(keyName, (value == 0) ? false : true);
        }

        /// <summary>
        /// This overrides the OnConfigurationsUpdate method so that S+
        /// will only get the custom Configurations (and not all other config objects as null)
        /// </summary>
        /// <param name="config"></param>
        protected override void OnConfigurationsUpdate(SyncProDeviceConfig config)
        {
            CustomConfigurationsUpdateHandler handler = CustomConfigUpdate;
            if (handler != null)
            {
                customProperties.GenerateArraysFromDictionary();    //first, we generate the arrays for S+
                handler(this, customProperties);
            }
        }
    }

}
