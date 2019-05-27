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

namespace MS.Function
{
    public static class GetPowerBIUsageStats
    {
        /* Function to get Power BI Usage Stats from the 0365 Audit Logs 

           ***********Important Note****************
           - O365 Audit Log subscriptions are User Specific! So you will need to run create subscription once for EACH authentication mechanism (Service Principal (DEV) / MSI (PROD)) 

           Parmeters to pass in: 
            a) Action: 
                -> CreateSubscription: Creates initial subscription to get logs. Run this once only manually (not to be included in your daily schedule). No Additional Parameters required for this action.
                -> GetLogsAndUploadToBlob: Gets the logs from the subscription and uploads to blob. Creates a file per contentid. This runs the whole process in one function call. Only use if volumes in the tenant are small.Additional Parameters required for this action.                        
                    BlobStorageAccountName: Name of the blob storage account to upload the data into. 
                    BlobStorageContainerName: Name of the Container to upload the data into.
                    BlobStorageFolderPath: Path to Folder within the blob storage container. 
                -> GetLogContentURIs: Only gets the content URI's available for the last 7 days. Use as first step for enterprise deployments. No Additional Parameters required for this action.
                -> UploadContentToBlob: Gets the content for a specific contentid and uploads to the specifed blob.  Use as second step for enterprise deployments. Additional Parameters required for this action:
                    ContentId: The id of the content. This will have been returned in the list object that you receive from GetLogContentURIs.
                    ContentUri: The URI of the content. This will have been returned in the list object that you receive from GetLogContentURIs.
                    BlobStorageAccountName: Name of the blob storage account to upload the data into. 
                    BlobStorageContainerName: Name of the Container to upload the data into.
                    BlobStorageFolderPath: Path to Folder within the blob storage container. 
            
        */
        [FunctionName("GetPowerBIUsageStats")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("GetPowerBIUsageStats has been triggered..");                        
            Shared.InitializeLog(log);  

            //Get office.com token (Uses Raw Rest Call)                  
            var TokenRequest = Shared.Azure.AuthenticateAsyncViaRest(Shared.GlobalConfigs.GetBoolConfig("UseMSI"), "https://manage.office.com","https://login.microsoftonline.com/"+Shared.GlobalConfigs.GetStringConfig("Domain")+"/oauth2/token",  Shared.GlobalConfigs.GetStringConfig("ApplicationId"), Shared.GlobalConfigs.GetStringConfig("AuthenticationKey"),null, null,null, "client_credentials"); 
            log.LogInformation("Inital office.com token retrieved..");                      
            //Determine Action 
            //Todo Add descriptive return information if Action not there. 
            string reqbody = new StreamReader(req.Body).ReadToEndAsync().Result; 
            string action = Shared.GlobalConfigs.GetStringRequestParam("Action", req, reqbody).ToString();
            string BlobStorageAccountName; 
            string BlobStorageContainerName;
            string BlobStorageFolderPath;
            
            BlobStorageAccountName = Shared.GlobalConfigs.GetStringRequestParam("BlobStorageAccountName", req,reqbody).ToString();
            BlobStorageContainerName = Shared.GlobalConfigs.GetStringRequestParam("BlobStorageContainerName", req, reqbody).ToString();
            BlobStorageFolderPath = Shared.GlobalConfigs.GetStringRequestParam("BlobStorageFolderPath",req, reqbody).ToString() + "/" + DateTime.Today.ToString("yyyyMMdd");
            
            string resource = null;
            //if(Helpers.GlobalConfigs.UseMSI) {                
                resource = "https://vault.azure.net";
              //  }     

            Shared.log.LogInformation("Getting Key Vault Token");   
            var KeyVaultToken = Shared.Azure.AuthenticateAsyncViaRest(Shared.GlobalConfigs.GetBoolConfig("UseMSI"),resource, "https://login.microsoftonline.com/"+Shared.GlobalConfigs.GetStringConfig("TenantId")+"/oauth2/token", Shared.GlobalConfigs.GetStringConfig("ApplicationId"), Shared.GlobalConfigs.GetStringConfig("AuthenticationKey"), null, null,"https://vault.azure.net/.default","client_credentials");       

            TokenCredential StorageToken = new TokenCredential(Shared.Azure.AzureSDK.GetAzureRestApiToken(string.Format("https://storage.azure.com/",BlobStorageAccountName), Shared.GlobalConfigs.GetBoolConfig("UseMSI")));

            List<ContentURI> ContentURIs = new List<ContentURI>();
            switch (action)
            {
                case "CreateSubscription":
                    SetUpSubscription(TokenRequest);
                    return (ActionResult)new OkObjectResult("Subscription Successfully Setup");
                case "GetLogsAndUploadToBlob":
                    ContentURIs = GetContentURIs(TokenRequest);
                    //Upload Content to Blob                   
                    foreach(var URI in ContentURIs)
                    {
                        GetContentAndUploadToBlob(URI, TokenRequest, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, StorageToken);
                    }
                    return (ActionResult)new OkObjectResult("Succeeded");
                case "GetLogContentURIs":
                    ContentURIs = GetContentURIs(TokenRequest);
                    return (ActionResult)new OkObjectResult(ContentURIs);
                case "UploadContentToBlob":
                    ContentURI contenturi = new ContentURI();
                    contenturi.contentUri = Shared.GlobalConfigs.GetStringRequestParam("ContentUri", req, reqbody).ToString();
                    contenturi.contentId = Shared.GlobalConfigs.GetStringRequestParam("ContentId", req, reqbody).ToString();
                    GetContentAndUploadToBlob(contenturi,TokenRequest, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, StorageToken);
                    if (contenturi.UploadSuccess) 
                        {return (ActionResult)new OkObjectResult(contenturi);}
                    else 
                        {return (ActionResult)new BadRequestObjectResult(contenturi);}
                default:
                    var logmessage = "Appropriate Action Parameter was not provided";
                    Shared.LogErrors(new Exception(logmessage));
                    return new BadRequestObjectResult(logmessage); 
            }
            
        }

        public static void GetContentAndUploadToBlob(ContentURI URI, string TokenRequest, string BlobStorageAccountName, string BlobStorageContainerName, string BlobStorageFolderPath, TokenCredential StorageToken)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    //Get The Content
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenRequest.ToString());
                    var query = System.Web.HttpUtility.ParseQueryString(String.Empty);                                    
                    string queryString = URI.contentUri; 
                    var result = client.GetAsync(queryString).Result;
                    Shared.log.LogInformation("GetContentAndUploadToBlob - Content Retrieved");
                    HttpContent content = result.Content;
                    //Upload To Blob
                    Shared.Azure.Storage.UploadContentToBlob(content.ReadAsStringAsync().Result, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, URI.contentId, StorageToken);                    
                    URI.UploadSuccess = true;
                    Shared.log.LogInformation("GetContentAndUploadToBlob - Upload of file completed");
                }
                Shared.log.LogInformation("GetContentAndUploadToBlob - Function Completed");
            }
            catch (Exception e)
            {
                Shared.LogErrors(new Exception("GetContentAndUploadToBlob Failed:"));
                Shared.LogErrors(e);
                URI.UploadSuccess = false;
            }
        }

        public class ContentURI
        {
            public string contentUri {get; set;}
            public string contentId {get;set;}
            public bool UploadSuccess {get;set;}

            public ContentURI(string contentUri, string contentId)
            {
                this.contentId = contentUri; 
                this.contentId = contentId;                 
                this.UploadSuccess = false;
            }

            public ContentURI()
            {
                this.UploadSuccess = false;
            }
        }
        public static List<ContentURI> GetContentURIs(string TokenRequest)
        {
            //We will get all for last 7 days
            var dates = new List<DateTime>();
            for (var dt = DateTime.Today.AddDays(-6); dt <= DateTime.Today; dt = dt.AddDays(1))
            {
                dates.Add(dt);
            }

            
            List<ContentURI> retvar = new List<ContentURI>();
            foreach(DateTime dt in dates)
            {
                try
                {
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenRequest.ToString());
                        var query = System.Web.HttpUtility.ParseQueryString(String.Empty);                
                        string queryString = string.Format("https://manage.office.com/api/v1.0/{0}/activity/feed/subscriptions/content?contentType=Audit.General&startTime={1}&endTime={2}",Shared.GlobalConfigs.GetStringConfig("TenantId"),dt.ToString("yyyy-MM-dd"), dt.AddDays(1).ToString("yyyy-MM-dd"))+"&PublisherIdentifier=" + Shared.GlobalConfigs.GetStringConfig("TenantId"); 
                        Shared.log.LogInformation("Getting Content URI's. Querystring: " + queryString);
                        var result = client.GetAsync(queryString).Result;
                        if(result.StatusCode == System.Net.HttpStatusCode.OK)
                        {                    
                            var content = result.Content.ReadAsStringAsync().Result;                    
                            {              
                                Newtonsoft.Json.Linq.JArray jarray = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JArray>(content);
                                foreach(var j in jarray)
                                {
                                    ContentURI item = new ContentURI();
                                    item.contentId = j["contentId"].ToString();
                                    item.contentUri = j["contentUri"].ToString();
                                    retvar.Add(item); 
                                }
                            }
                            Shared.log.LogInformation("GetContentURI's - Function Completed");
                        }
                        else 
                        {
                            throw new Exception("GetContentURI's request has an invalid status code. StatusCode: " + result.StatusCode.ToString() + "; ReasonPhrase: " + result.ReasonPhrase);
                        }

                        

                    }                
                }
                catch (Exception e)
                {
                    Shared.LogErrors(new Exception("GetContentURI's Failed:"));
                    Shared.LogErrors(e);                    
                }
            }
            return retvar;

        }

        // Used to set up initial Subscription
        public static void SetUpSubscription(string TokenRequest)
        {   
            /* Create Subscription  - Do this once only*/              
            using (var client = new System.Net.Http.HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenRequest.ToString());

                var query = System.Web.HttpUtility.ParseQueryString(String.Empty);
                //query["$top"] = "10";
                //query["$filter"] = "loggedByService	eq ";
                string queryString = "https://manage.office.com/api/v1.0/"+Shared.GlobalConfigs.GetStringConfig("TenantId")+"/activity/feed/subscriptions/start?contentType=Audit.General&PublisherIdentifier=" + Shared.GlobalConfigs.GetStringConfig(("TenantId"));                            
                var result = client.PostAsync(queryString, null).Result;
                var content = result.Content.ReadAsStringAsync().Result;
                
            }

        }

        public static void ListSubscriptions(string TokenRequest)
        {   
            using (var client = new System.Net.Http.HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenRequest.ToString());
                var query = System.Web.HttpUtility.ParseQueryString(String.Empty);
                string queryString = "https://manage.office.com/api/v1.0/"+Shared.GlobalConfigs.GetStringConfig("TenantId")+"/activity/feed/subscriptions/list";
                var result = client.GetAsync(queryString).Result;
                var content = result.Content.ReadAsStringAsync().Result;
                
            }

        }

       
         
         //Deprecated as using "Shared Class Now"
         public static class Helpers2
        {

            private static ILogger log {get; set;}

            public static void SetLog(ILogger log)
            {
                Helpers2.log = log;
            }

            //Standardised Method for Getting Auth Token - Will work with MSI & Service Principal. Uses raw HTTP calls so no auth library dependency
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
            public static class GlobalConfigs
            {   
                public static string TenantId = System.Environment.GetEnvironmentVariable("TenantId", EnvironmentVariableTarget.Process);
                public static bool UseMSI = System.Convert.ToBoolean(System.Environment.GetEnvironmentVariable("UseMSI", EnvironmentVariableTarget.Process));
                public static string ApplicationId = System.Environment.GetEnvironmentVariable("ApplicationId", EnvironmentVariableTarget.Process);
                public static string AuthenticationKey = System.Environment.GetEnvironmentVariable("AuthenticationKey", EnvironmentVariableTarget.Process);
                public static string Domain = System.Environment.GetEnvironmentVariable("Domain", EnvironmentVariableTarget.Process);
            
            }

            public class KeyVaultConfigs
            {

                //public static string GetMasterUserName(string KeyVaultToken, string KeyVault){ return GetKeyVaultSecret("MasterUserName",KeyVault,KeyVaultToken);} 
                //public static string GetMasterUserPassword(string KeyVaultToken, string KeyVault){ return GetKeyVaultSecret("MasterUserPassword",KeyVault,KeyVaultToken);}                 

            }

            //Gets KeyVault Secret
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
                        var error = "GetKeyVaultSecret Failed..";
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

            public static void LogErrors(System.Exception e)
            {
                log.LogError(e.Message.ToString());
                if(e.StackTrace != null)
                {
                    log.LogError(e.StackTrace.ToString());
                }
            }

            
            public static class AzureSDK
            {
                //Gets RestAPI Token for various Azure Resources using the SDK Helper Classes
                public static string GetAzureRestApiToken(string ServiceURI, bool UseMSI)
                {
                    if (UseMSI == true)
                    {
                        
                        var tokenProvider = new AzureServiceTokenProvider();
                        //https://management.azure.com/                    
                        return tokenProvider.GetAccessTokenAsync(ServiceURI).Result;
                        
                    }
                    else
                    {       

                        var context = new AuthenticationContext("https://login.windows.net/" + Helpers2.GlobalConfigs.TenantId);
                        ClientCredential cc = new ClientCredential(Helpers2.GlobalConfigs.ApplicationId, Helpers2.GlobalConfigs.AuthenticationKey);
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
                        creds = f.FromServicePrincipal(Helpers2.GlobalConfigs.ApplicationId, Helpers2.GlobalConfigs.AuthenticationKey, Helpers2.GlobalConfigs.TenantId, AzureEnvironment.AzureGlobalCloud);

                    }

                    return creds;
                }
            }
        }
    }
}
