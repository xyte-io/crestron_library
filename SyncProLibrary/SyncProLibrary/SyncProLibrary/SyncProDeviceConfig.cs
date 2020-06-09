using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SyncProLibrary
{
    /// <summary>
    /// Configurations class for all devices. Each Configurations file is constructed out of three sections - general and network (common for most devices) and sprcific device Configurations.
    /// </summary>
    public class SyncProDeviceConfig
    {
        /// <summary>
        /// Default constructor. Sets the default values for version and last updated
        /// </summary>
        public SyncProDeviceConfig()
        {
            version = 1;
        }

        //Configurations version
        public int version { get; set; }
        public DateTime last_updated { get; set; }

        //General Properties
        public GeneralDeviceProperties generalProperties { get; set; }

        //Network Properties
        public NetworkProperties networkProperties { get; set; }

        //Authentication
        public AuthenticationProperties authenticationProperties { get; set; }

        //Specific Device Properties - Differ from one device to another
        //Occupancy Sensor
        public OccupancySensorDeviceConfig occupancyProperties { get; set; }

        //Touch Screens
        public UserInterfaceDeviceConfig uiProperties { get; set; }

        public BasicUserInterfaceDeviceConfig userInterfaceProperties { get; set; }

        //AirMedia
        public AirMedia200300DeviceConfig airMediaProperties { get; set; }

        //Custom Devices
        //public CustomDeviceConfig customDeviceProperties { get; set; }

    }

    #region Configurations classes

    /// <summary>
    /// These Configurations are common for most devices
    /// </summary>
    public class GeneralDeviceProperties
    {
        public string fwVersion { get; set; }
        public string programIDTag { get; set; }
    }

    /// <summary>
    /// These are networking config. While not identical, they are 
    /// </summary>
    public class NetworkProperties
    {
        public string protocol { get; set; }
        public string remoteHostname { get; set; }
        public ushort remotePort { get; set; }

        //3Series
        public int numberOfEthernetInterfaces { get; set; }
        public short adapterId { get; set; }
        public bool dhcp { get; set; }
        public bool? webServer { get; set; }
        public bool? isAuthEnabled { get; set; }

        public string ipAddress { get; set; }
        public string macAddress { get; set; }
        public string staticIpAddress { get; set; }
        public string staticNetMask { get; set; }
        public string staticDefRouter { get; set; }
        public string hostName { get; set; }//
        public string domainName { get; set; }
        public string cipPort { get; set; }
        public string securedCipPort { get; set; }
        public string ctpPort { get; set; }
        public string securedCtpPort { get; set; }
        public string webPort { get; set; }
        public string securedWebPort { get; set; }
        public string sslCertificate { get; set; } //Self, CA, None
        public string[] dnsServers { get; set; }
    }

    /// <summary>
    /// Occupancy Sensor Configurations
    /// </summary>
    public class OccupancySensorDeviceConfig
    {
        public short timeout { get; set; }
        public string whenVacatedMode { get; set; }

    }

    public class BasicUserInterfaceDeviceConfig
    {
        public HardButtons hardButtons { get; set; }
    }

    public class HardButtons
    {
        public short powerButton { get; set; }
        public short homeButton { get; set; }
        public short lightsButton { get; set; }
        public short upButton { get; set; }
        public short downButton { get; set; }
    }

    public class AuthenticationProperties
    {
        public bool authEnabled { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public string verifyPassword { get; set; }
    }

    public class AirMedia200300DeviceConfig
    {
        public bool autoInputRouting { get; set; }
        public bool hdminInHdcpSupport { get; set; }
        public string hdmiOutResolution { get; set; }

        public string loginCodeMode { get; set; }   //0-Disabled; 1-Random; 2-Fixed
        public short loginCode { get; set; }       //4 digits code - 1000-9999
        public bool displayCode { get; set; }
        public bool displayConnectionOptions { get; set; }
        public string displayConnectionOptionsMode { get; set; } //1-IP Address; 2-Hostname; 3-Hostname+Domain; 4-Custom URL
        public string customUrl { get; set; }

        //Todo:Complete HDBaseT and DigitalSignage
        public string digtialSignageState { get; set; }
        public string digitalSignageUrl { get; set; }
    }

    /// <summary>
    /// This is a simple class to be able to trasnfer data to S+
    /// </summary>
    public class CustomKeyStrValue
    {
        public string keyName { get; set; }
        public string value { get; set; }

        public CustomKeyStrValue() { }

        public CustomKeyStrValue(string name, string value)
        {
            this.keyName = name;
            this.value = value;
        }

    }

    /// <summary>
    /// This is a simple class to be able to trasnfer data to S+
    /// </summary>
    public class CustomKeyShortValue
    {
        public string keyName { get; set; }
        public short value { get; set; }

        public CustomKeyShortValue() { }

        public CustomKeyShortValue(string name, short value)
        {
            this.keyName = name;
            this.value = value;
        }
    }

    public class CustomDeviceConfig
    {
        //This is a dictionary of the custom data. Each item has it's key (string), and value. a value can be a string, integer, boolean or other object.
        public Dictionary<string, Object> properties;

        //these are arrays of serial, analog and digitals, to transfer to S+
        public CustomKeyStrValue[] serials;
        public CustomKeyShortValue[] analogs;
        public CustomKeyShortValue[] digitals;
        public short serialsCount, analogsCount, digitalsCount; 

        /// <summary>
        /// Default constructor
        /// </summary>
        public CustomDeviceConfig()
        {
            this.properties = new Dictionary<string, object>();
        }

        /// <summary>
        /// This method builds three arrays from the digtionary, based on their type
        /// </summary>
        public void GenerateArraysFromDictionary()
        {
            List<CustomKeyStrValue>  stringsList = new List<CustomKeyStrValue>();
            List<CustomKeyShortValue>  shortsList = new List<CustomKeyShortValue>();
            List<CustomKeyShortValue>  booleansList = new List<CustomKeyShortValue>();       //Bool list is also of type short, sor S+

            foreach (KeyValuePair<string, Object> kv in properties)
            {
                if (kv.Value.GetType() == typeof(string))
                    stringsList.Add(new CustomKeyStrValue(kv.Key, (string)kv.Value));
                else if (kv.Value.GetType() == typeof(short))
                    shortsList.Add(new CustomKeyShortValue(kv.Key, (short)kv.Value));
                else if (kv.Value.GetType() == typeof(bool))
                {
                    if ((bool)kv.Value)
                        shortsList.Add(new CustomKeyShortValue(kv.Key, 1));   //True
                    else
                        shortsList.Add(new CustomKeyShortValue(kv.Key, 0));   //False
                }
            }

            this.serials = stringsList.ToArray();
            this.serialsCount = (short)stringsList.Count();
            this.analogs = shortsList.ToArray();
            this.analogsCount = (short)shortsList.Count();
            this.digitals = booleansList.ToArray();
            this.digitalsCount = (short)booleansList.Count();
            
        } 
    }

    public class UserInterfaceDeviceConfig
    {
        //Touch Screen UI Configurations 
        public List<UiActionableButton> homePageShortcuts { get; set; }
        public short numberOfHomePageShortcuts { get; set; } //This is simply the list size

        public List<UiApp> applications { get; set; }
        public short numberOfApps { get; set; } //This is simply the list size

        /// <summary>
        /// Returns the app in location i in the apps list
        /// </summary>
        /// <param name="i"></param>
        public UiApp GetAppInfo(short i)
        {
            if (applications != null) return applications[i];
            else return null;
        }
    }

    /// <summary>
    /// This is a button with its actions. This is used for homepage items
    /// and activities' buttons
    /// </summary>
    public class UiActionableButton
    {
        public uiButton button { get; set; }
        public List<DeviceAction> actions { get; set; }
        //List of actions / presets
    }

    /// <summary>
    /// Application of the UI. Defines the button properties as well as the activities
    /// </summary>
    public class UiApp
    {
        public uiButton button { get; set; }
        public List<UiActionableButton> activities { get; set; }
    }

    //Generic UI Button
    public class uiButton
    {
        public string title { get; set; }
        public string icon { get; set; }
        public string activeIcon { get; set; }
    }

    /// <summary>
    /// A list of action that can be taken in sequence
    /// </summary>
    public class Preset
    {
        public List<DeviceAction> actions { get; set; }

        //Invokes all actions in the preset
        public void Invoke()
        {
            foreach (DeviceAction da in actions)
            {
                da.Invoke();
            }
        }
    }

    /// <summary>
    /// This class represents an action taken on a specific device
    /// </summary>
    public class DeviceAction
    {
        public string deviceId { get; set; }
        public string actionName { get; set; }

        public void Invoke()
        {
            //Todo: Invoke the action
        }
    }

    #endregion
}
