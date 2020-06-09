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

namespace SyncProLibrary
{
    /// <summary>
    /// Common methods
    /// </summary>
    public static class SyncProMethods
    {
        #region Error log
        public static void LogException(Object obj, Exception ex)
        {
            if (obj is string)
                ErrorLog.Error("{0}: >>> Exception at {1} with ex = {2}; stack = {3}", DateTime.Now, obj.ToString(), ex.Message, ex.StackTrace);
            else
                ErrorLog.Error("{0}: >>> Exception at {1} with ex = {2}; stack = {3}", DateTime.Now, obj.GetType(), ex.Message, ex.StackTrace);
        }

        public static void LogError(object obj, string str)
        {
            ErrorLog.Error("{0}: >>> Error at {1}:{2}", DateTime.Now, obj.GetType(), str);
        }

        public static void LogNotice(object obj, string str)
        {
            ErrorLog.Notice("{0}: >>> Notice at {1}:{2}", DateTime.Now, obj.GetType(), str);
        }
        #endregion

        #region WebMethods
        internal static string HttpsJsonRequest(string uri, Crestron.SimplSharp.Net.Https.RequestType requestType, string key, string msg, string contentType)
        {
            HttpsClient client = new HttpsClient();
            client.HostVerification = false;
            client.PeerVerification = false;

            try
            {
                HttpsClientRequest aRequest = new HttpsClientRequest();
                //HttpsClientRequest aRequest = new HttpsClientRequest();

                string aUrl = uri;
                aRequest.Url.Parse(aUrl);
                aRequest.Encoding = Encoding.UTF8;
                aRequest.RequestType = requestType;
                aRequest.Header.ContentType = contentType;
                aRequest.ContentString = msg;
                aRequest.Header.SetHeaderValue("Authorization", key);

                HttpsClientResponse myResponse = client.Dispatch(aRequest);

             return myResponse.ContentString;
            }

            catch (Exception ex)
            {
                LogException(@"SyncProMethods \ GenericHttpsRequest", ex);
                return null;
            }
        }
        #endregion

        #region File System methods
        /// <summary>
        /// Creates directory to hold all Configurations files.
        /// </summary>
        /// <returns></returns>
        public static DirectoryInfo CreateConfigurationsFilesDirectory()
        {
            string path = Directory.GetApplicationRootDirectory() + Path.DirectorySeparatorChar + "NVRAM" + Path.DirectorySeparatorChar + "Configurations" + Path.DirectorySeparatorChar;
            try
            {
                if (!Directory.Exists(path))
                    return Crestron.SimplSharp.CrestronIO.Directory.CreateDirectory(path);

                return new DirectoryInfo(path);
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"FileOperations \ CreateConfigurationsDirectories failes with - ", ex);
                return null;
            }
        }

        /// <summary>
        /// Reads Json binary file and returns it as a string.
        /// If the file cannot be found, the method returns null.
        /// </summary>
        /// <param name="fullFilePath"></param>
        /// <returns></returns>
        public static string ReadJsonFromFile(string fullFilePath)
        {
            string result = null;
            try
            {
                JsonSerializer jsonSerializer = new JsonSerializer();

                if (File.Exists(fullFilePath))
                {
                    BsonReader bReader = new BsonReader(new BinaryReader(fullFilePath));
                    result = jsonSerializer.Deserialize<object>(bReader).ToString();
                    bReader.Close();
                }
                else return null; 

            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProMethods \ ReadJsonFromFile - Failed to read file with exception - ", ex);
                return null;
            }
            return result;
        }

        /// <summary>
        /// Write Json object to file
        /// </summary>
        /// <param name="fullFilePath"></param>
        /// <param name="jsonObject"></param>
        public static void WriteJsonToFile(string fullFilePath, object jsonObject)
        {
            try
            {
                JsonSerializer jsonSerializer = new JsonSerializer();
                BsonWriter bWriter = new BsonWriter(new BinaryWriter(new FileStream(fullFilePath, FileMode.Create, FileAccess.Write)));
                jsonSerializer.Serialize(bWriter, jsonObject);
                bWriter.Close();
            }
            catch (Exception ex)
            {
                SyncProMethods.LogException(@"SyncProMethods \ WriteJsonToFile - Failed to read file with exception - ", ex);
            }
        }
        #endregion
    }
}