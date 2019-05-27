using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Services.AppAuthentication;
using System.Linq;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Collections.Generic;
using System.Dynamic;
using System.Net.Http;
using Microsoft.Azure.KeyVault;


namespace MS.Function
{
    public class Shared
        {
            public static ILogger log {get; set;}
            public static void InitializeLog(ILogger log)
            {
                Shared.log = log;
            }


            public static class Azure
            {
                public static string AuthenticateAsyncViaRest( bool UseMSI, string ResourceUrl = null, string AuthorityUrl = null, string ClientId = null, string ClientSecret = null, string Username = null, string Password = null, string Scope = null, string GrantType = null)
            {
                HttpResponseMessage result = new HttpResponseMessage();
                if (UseMSI == true)
                {   
                    log.LogInformation("AuthenticateAsyncViaRest is using MSI");                
                    using (var client = new HttpClient())
                    {                        
                        client.DefaultRequestHeaders.Add("Secret", Environment.GetEnvironmentVariable("MSI_SECRET"));
                        result = client.GetAsync(String.Format("{0}/?resource={1}&api-version={2}", Environment.GetEnvironmentVariable("MSI_ENDPOINT"), ResourceUrl, "2017-09-01")).Result;
                    }
                    
                }
                else 
                {
                    log.LogInformation("AuthenticateAsyncViaRest is using Service Principal");
                    var oauthEndpoint = new Uri(AuthorityUrl);                

                    using (var client = new HttpClient())
                    {
                        List<KeyValuePair<string,string>> body = new List<KeyValuePair<string,string>>();
                        
                        if(ResourceUrl != null) {body.Add(new KeyValuePair<string, string>("resource", ResourceUrl));}
                        if(ClientId != null) {body.Add(new KeyValuePair<string, string>("client_id", ClientId));}
                        if(ClientSecret != null) {body.Add(new KeyValuePair<string, string>("client_secret", ClientSecret));}
                        if(GrantType != null) {body.Add(new KeyValuePair<string, string>("grant_type", GrantType));}
                        if(Username != null) {body.Add(new KeyValuePair<string, string>("username", Username));}
                        if(Password != null) {body.Add(new KeyValuePair<string, string>("password", Password));}
                        if(Scope != null) {body.Add(new KeyValuePair<string, string>("scope", Scope));}
                        
                        result = client.PostAsync(oauthEndpoint, new FormUrlEncodedContent(body)).Result;                        
                    }
                }
                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var content = result.Content.ReadAsStringAsync().Result;
                    var definition = new {access_token = ""};                
                    var jobject = JsonConvert.DeserializeAnonymousType(content, definition);
                    return (jobject.access_token);
                }
                else 
                {
                    var error = "AuthenticateAsyncViaRest Failed..";
                    try
                    {
                        var content = result.Content.ReadAsStringAsync().Result;
                        error = error + content;
                    }
                    catch 
                    {

                    }
                    finally
                    {
                    log.LogError(error);
                    throw new Exception (error);
                    }
                }
        }

                public static class AzureSDK
                {
                    //Gets RestAPI Token for various Azure Resources using the SDK Helper Classes
                    
                    public static string GetAzureRestApiToken(string ServiceURI, bool UseMSI)
                    {
                        if (UseMSI)
                        {
                            return GetAzureRestApiToken(ServiceURI, UseMSI, null,null);
                        }
                        else 
                        {
                            //By Default Use Local SP Credentials
                            return GetAzureRestApiToken(ServiceURI, UseMSI, Shared.GlobalConfigs.GetStringConfig("ApplicationId"), Shared.GlobalConfigs.GetStringConfig("AuthenticationKey"));
                        }
                    }

                    public static string GetAzureRestApiToken(string ServiceURI, bool UseMSI, string ApplicationId, string AuthenticationKey)
                    {
                        if (UseMSI == true)
                        {
                            
                            var tokenProvider = new AzureServiceTokenProvider();
                            //https://management.azure.com/                    
                            return tokenProvider.GetAccessTokenAsync(ServiceURI).Result;
                            
                        }
                        else
                        {       

                            var context = new AuthenticationContext("https://login.windows.net/" + Shared.GlobalConfigs.GetStringConfig("TenantId"));
                            ClientCredential cc = new ClientCredential(ApplicationId, AuthenticationKey);
                            AuthenticationResult result = context.AcquireTokenAsync(ServiceURI, cc).Result;
                            return result.AccessToken;
                        }
                    }

                    //Gets AzureCredentials Object Using SDK Helper Classes 
                    public static AzureCredentials GetAzureCreds(bool UseMSI)
                    {
                        //MSI Login
                        AzureCredentialsFactory f = new AzureCredentialsFactory();
                        var msi = new MSILoginInformation(MSIResourceType.AppService);
                        AzureCredentials creds;


                        if (UseMSI == true)
                        {
                            //MSI
                            creds = f.FromMSI(msi, AzureEnvironment.AzureGlobalCloud);
                        }
                        else
                        {
                            //Service Principal
                            creds = f.FromServicePrincipal(Shared.GlobalConfigs.GetStringConfig("ApplicationId"), Shared.GlobalConfigs.GetStringConfig("AuthenticationKey"), Shared.GlobalConfigs.GetStringConfig("TenantId"), AzureEnvironment.AzureGlobalCloud);

                        }

                        return creds;
                    }
                }
            
                public static class Storage
                {
                    public static void UploadContentToBlob(string content, string BlobStorageAccountName, string BlobStorageContainerName, string BlobStorageFolderPath, string TargetFileName, TokenCredential tokenCredential)
                    {
                        try
                        {
                            using (var client = new HttpClient())
                            {                    
                                //Write Content to blob
                                //Need to make sure that MSI / Service Principal has write priveledges to blob storage account OR that you use SAS URI fetched from Key Vault
                                using (var storageclient = new HttpClient())
                                {                                
                                    
                                    StorageCredentials storageCredentials = new StorageCredentials(tokenCredential);
                                    CloudStorageAccount storageAccount = new CloudStorageAccount(storageCredentials, BlobStorageAccountName, "core.windows.net",true);

                                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                                    CloudBlobContainer container = blobClient.GetContainerReference(BlobStorageContainerName);
                                    
                                    CloudBlobDirectory directory = container.GetDirectoryReference(BlobStorageFolderPath);
                                    CloudBlockBlob blob = directory.GetBlockBlobReference(string.Format("{0}.json", TargetFileName));

                                    blob.UploadTextAsync(content).Wait();                        
                                }
                            }
                        }
                        catch (Exception e)
                        {                           
                            Shared.LogErrors(e);                            
                            Shared.LogErrors(new Exception("UploadContentToBlob Failed:"));
                            throw e;            

                        }
                    }
                }
            
            }

            public static class GlobalConfigs
            {
                
                public static string GetStringRequestParam(string Name,  HttpRequest req, string reqbody)
                {
                    try
                    {
                        
                        string ret;
                        
                        if(req.Method == HttpMethod.Get.ToString())
                        {
                            ret = req.Query[Name].ToString();                        
                        }
                        else 
                        {                                       
                            dynamic parsed = JsonConvert.DeserializeObject(reqbody);
                            ret = parsed[Name].Value.ToString();                        
                        }
                        return ret;
                    }
                    catch (Exception e)
                    {
                        Shared.LogErrors(new Exception ("Could not bind input parameter " + Name + " using the request querystring nor the request body."));
                        throw e;
                    }
                }


                public static string GetStringConfig(string ConfigName)
                {
                    return (string)GetConfig(ConfigName);
                }
                public static bool GetBoolConfig(string ConfigName)
                {
                    return System.Convert.ToBoolean(GetConfig(ConfigName));
                }
                
                private static Object GetConfig(string ConfigName)
                {
                    Object Ret;
                    try {
                         Ret = System.Environment.GetEnvironmentVariable(ConfigName, EnvironmentVariableTarget.Process);
                    }
                    catch (Exception e) {
                        Shared.LogErrors(new Exception ("Could not find global config " + ConfigName));
                        throw (e);
                    }

                    return Ret;
                }

                public static string KeyVaultToken {get; set;}
                public static void SetKeyVaultToken()
                {
                    string resource = null;
                    resource = "https://vault.azure.net";

                    Shared.log.LogInformation("Getting Key Vault Token");  
                    //Always Set Using Local Dev SP or MSI 
                    KeyVaultToken = Shared.Azure.AuthenticateAsyncViaRest(Shared.GlobalConfigs.GetBoolConfig("UseMSI"),resource, "https://login.microsoftonline.com/"+Shared.GlobalConfigs.GetStringConfig("TenantId")+"/oauth2/token", Shared.GlobalConfigs.GetStringConfig("ApplicationId"), Shared.GlobalConfigs.GetStringConfig("AuthenticationKey"), null, null,"https://vault.azure.net/.default","client_credentials"); 
                }

                public static string GetKeyVaultSecret(string Secret, string VaultName, string KeyVaultToken)
            {
                
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", KeyVaultToken);                                        
                    string queryString = string.Format("https://{0}.vault.azure.net/secrets/{1}?api-version=7.0", VaultName, Secret); 
                    var result = client.GetAsync(queryString).Result;

                    if (result.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var content = result.Content.ReadAsStringAsync().Result;
                        var definition = new {value = ""};                
                        var jobject = JsonConvert.DeserializeAnonymousType(content, definition);
                        return (jobject.value);
                    }
                    else 
                    {
                        var error = "GetKeyVaultSecret Failed - Secret Name: " + Secret;
                        try
                        {
                            var content = result.Content.ReadAsStringAsync().Result;
                            error = error + content;
                        }
                        catch 
                        {

                        }
                        finally
                        {
                        log.LogError(error);
                        throw new Exception (error);
                        }
                    }                   
                }
            }

               
            }

            //ToDo: Consider Caching Configs so we dont fetch from key vault every time.
            
            public static void LogErrors(System.Exception e)
            {
                log.LogError(e.Message.ToString());
                if(e.StackTrace != null)
                {
                    log.LogError(e.StackTrace.ToString());
                }
            }

            public static void LogInformation(string Message)
            {
                log.LogInformation(Message);
            }

            
           
        
            
        }
    

}