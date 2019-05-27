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
    public static class GetPowerBIGroupsAsAdmin
    {
        /* Function to get Power BI Meta Data From Admin API  
        Parmeters to pass in: 
            a) Action: 
                -> GetWorkspaces: Gets all group workpaces from across the organisation and returns them as a json object. Also uploads the object as a json file into blob storage. Additional Parameters required for this action:
                    BlobStorageAccountName: Name of the blob storage account to upload the data into. 
                    BlobStorageContainerName: Name of the Container to upload the data into.
                    BlobStorageFolderPath: Path to Folder within the blob storage container.                       
                -> GetItemsInWorkspace: Gets Reports, Datasets and Dashboards in a specific Workspace and uploads them as files into Blob storage. Additional Parameters required for this action:
                    WorkspaceId: The id of the workspace to get items from (get this from "GetWorkspaces" above)
                    BlobStorageAccountName: Name of the blob storage account to upload the data into. 
                    BlobStorageContainerName: Name of the Container to upload the data into.
                    BlobStorageFolderPath: Path to Folder within the blob storage container. 
                -> GetWorkspacesAndDetails: Executes both GetWorkspaces and then GetItemsInWorkspace for each of the returned workspaces. This method is NOT intdended for use in large environments and the function may time out.                   
                    BlobStorageAccountName: Name of the blob storage account to upload the data into. 
                    BlobStorageContainerName: Name of the Container to upload the data into.
                    BlobStorageFolderPath: Path to Folder within the blob storage container. 

                
        */
        [FunctionName("GetPowerBIMetaData")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            try 
            {
            log.LogInformation("GetPowerBIMetaData has been triggered..");                        
            Shared.InitializeLog(log);                       

            string action; 
            string BlobStorageAccountName; 
            string BlobStorageContainerName;
            string BlobStorageFolderPath;     
            
            string reqbody = new StreamReader(req.Body).ReadToEndAsync().Result; 
            action = Shared.GlobalConfigs.GetStringRequestParam("Action", req, reqbody);
            BlobStorageAccountName = Shared.GlobalConfigs.GetStringRequestParam("BlobStorageAccountName", req, reqbody).ToString();
            BlobStorageContainerName = Shared.GlobalConfigs.GetStringRequestParam("BlobStorageContainerName", req, reqbody).ToString();
            BlobStorageFolderPath = Shared.GlobalConfigs.GetStringRequestParam("BlobStorageFolderPath", req, reqbody).ToString() + "/" + DateTime.Today.ToString("yyyyMMdd");
            
            string resource = null;
            //if(Helpers.GlobalConfigs.UseMSI) {                
                resource = "https://vault.azure.net";
              //  }     

            Shared.log.LogInformation("Getting Key Vault Token");   
            var KeyVaultToken = Shared.Azure.AuthenticateAsyncViaRest(Shared.GlobalConfigs.GetBoolConfig("UseMSI"),resource, "https://login.microsoftonline.com/"+Shared.GlobalConfigs.GetStringConfig("TenantId")+"/oauth2/token", Shared.GlobalConfigs.GetStringConfig("ApplicationId"), Shared.GlobalConfigs.GetStringConfig("AuthenticationKey"), null, null,"https://vault.azure.net/.default","client_credentials");                       
        
            Shared.log.LogInformation("Getting Power BI Token");             
            string CloudServicePrincipalAppId =  Shared.GlobalConfigs.GetKeyVaultSecret("ApplicationId", Shared.GlobalConfigs.GetStringConfig("KeyVault"), KeyVaultToken);
            string CloudServicePrincipalAuthKey =  Shared.GlobalConfigs.GetKeyVaultSecret("AuthenticationKey", Shared.GlobalConfigs.GetStringConfig("KeyVault"), KeyVaultToken);
            //Auth with both Service Credentials and User Creds - Does not look like admin apis support service principal only yet therefore using master user approach. 
            var PowerBIToken = Shared.Azure.AuthenticateAsyncViaRest(false, "https://analysis.windows.net/powerbi/api","https://login.microsoftonline.com/common/oauth2/token", CloudServicePrincipalAppId, CloudServicePrincipalAuthKey, Shared.GlobalConfigs.GetKeyVaultSecret("MasterUserName", Shared.GlobalConfigs.GetStringConfig("KeyVault"), KeyVaultToken ), Shared.GlobalConfigs.GetKeyVaultSecret("MasterUserPassword", Shared.GlobalConfigs.GetStringConfig("KeyVault"), KeyVaultToken),"openid","password");    
            
            //Get Azure Token Using Azure SDK rather than Raw REST Calls                 
            TokenCredential StorageToken = new TokenCredential(Shared.Azure.AzureSDK.GetAzureRestApiToken(string.Format("https://storage.azure.com/",BlobStorageAccountName), Shared.GlobalConfigs.GetBoolConfig("UseMSI")));

            var query = System.Web.HttpUtility.ParseQueryString(String.Empty);

            switch (action)
            {
                case "GetWorkspacesAndDetails":                            
                    Newtonsoft.Json.Linq.JObject workspaces = GetWorkspaces(req, PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, StorageToken);                    
                    Shared.log.LogInformation("Retrieved Workspace List");  
                    foreach(var w in workspaces["value"])
                    {                         
                        GetAllItemsInWorkspace(req,PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath,  w["id"].ToString(), StorageToken); 
                    }
                    return (ActionResult)new OkObjectResult("Succeeded");                
                case "GetWorkspaces":
                    Newtonsoft.Json.Linq.JObject workspaces1 = GetWorkspaces(req, PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, StorageToken);
                    return (ActionResult)new OkObjectResult(workspaces1);
                case "GetItemsInWorkspace":  
                    Newtonsoft.Json.Linq.JObject retobj = new Newtonsoft.Json.Linq.JObject();                  
                    string groupid = Shared.GlobalConfigs.GetStringRequestParam("WorkspaceId", req, reqbody).ToString(); 
                    try 
                    {                        
                        GetAllItemsInWorkspace(req,PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, groupid, StorageToken);   
                        retobj = new Newtonsoft.Json.Linq.JObject( 
                                new Newtonsoft.Json.Linq.JProperty("WorkspaceId", groupid),
                                new Newtonsoft.Json.Linq.JProperty("Result", "Success")
                            );
                    }
                    catch (Exception e)
                    {
                        Shared.LogErrors(e);                       
                        Shared.LogErrors(new Exception ("Fetching Items for Workspace " + groupid + " Failed."));                       
                        retobj = new Newtonsoft.Json.Linq.JObject( 
                                new Newtonsoft.Json.Linq.JProperty("WorkspaceId", groupid),
                                new Newtonsoft.Json.Linq.JProperty("Result", "Failure")
                            );
                        
                    }                    
                    return (ActionResult)new OkObjectResult(retobj);                
                default:
                    var logmessage = "Appropriate Action Parameter was not provided";
                    Shared.LogErrors(new Exception(logmessage));
                    return new BadRequestObjectResult(logmessage); 
            }
            }
            catch (Exception e)
            {
                Shared.LogErrors(e);    
                var logmessage = "GetPowerBIMetaData Unhandled Exception";
                Shared.LogErrors(new Exception(logmessage));                            
                return new BadRequestObjectResult(logmessage);
            }
            

                
        }

        public static Newtonsoft.Json.Linq.JObject GetWorkspaces(HttpRequest req, string PowerBIToken, string BlobStorageAccountName, string BlobStorageContainerName, string BlobStorageFolderPath, TokenCredential StorageToken){
            var query = System.Web.HttpUtility.ParseQueryString(String.Empty);
            query["$top"] = "5000";
            query["expand"] = "users";    
            Shared.log.LogInformation("GetWorkspaces - Started");             
            Newtonsoft.Json.Linq.JObject workspaces = GetAdminArtefactsGeneric(PowerBIToken, "groups", query);
            Shared.Azure.Storage.UploadContentToBlob(Newtonsoft.Json.JsonConvert.SerializeObject(workspaces), BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, "groups", StorageToken); 
            Shared.log.LogInformation("GetWorkspaces - Completed");
            return workspaces;            
        }

        public static void GetAllItemsInWorkspace(HttpRequest req, string PowerBIToken, string BlobStorageAccountName, string BlobStorageContainerName, string BlobStorageFolderPath, string groupid, TokenCredential StorageToken){
            Shared.log.LogInformation("Getting workspace details: " + groupid); 
            var query = System.Web.HttpUtility.ParseQueryString(String.Empty);                                
            query["$top"] = "5000";            
            //Iterate through by group                    
            {                
                GetItemsInWorkspace(groupid, PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, "reports", query, StorageToken);
                GetItemsInWorkspace(groupid, PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, "datasets", query,StorageToken);
                GetItemsInWorkspace(groupid, PowerBIToken, BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, "dashboards", query, StorageToken);
            }      
        }

        public static void GetItemsInWorkspace(string groupid, string PowerBIToken, string BlobStorageAccountName, string BlobStorageContainerName, string BlobStorageFolderPath, string Artefact, System.Collections.Specialized.NameValueCollection Query, TokenCredential StorageToken){
            Shared.log.LogInformation("Getting workspace " + Artefact + " details.");     
            var GroupArtefact = "groups/" + groupid + "/" + Artefact;
            var content = GetAdminArtefactsGeneric(PowerBIToken,GroupArtefact, Query);
            foreach(Newtonsoft.Json.Linq.JObject o in content["value"])
            {
                o.Add("WorkspaceId", groupid);
            }                 
            Shared.Azure.Storage.UploadContentToBlob(Newtonsoft.Json.JsonConvert.SerializeObject(content), BlobStorageAccountName, BlobStorageContainerName, BlobStorageFolderPath, GroupArtefact.Replace("/","_"), StorageToken);
        }

        public static Newtonsoft.Json.Linq.JObject GetAdminArtefactsGeneric(string PowerBIToken, string Artefact, System.Collections.Specialized.NameValueCollection Query) 
        {
            try
            {
                //Get Groups 
                using (var client = new HttpClient())
                {
                    
                    Shared.log.LogInformation("GetAdminArtefactsGeneric - " + Artefact + " - " + "Started");  
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", PowerBIToken);                
                    //TODO: Add loop for clients with more than 5000 workspaces
                    string queryString = "";
                    if (Query.Count == 0)
                    {
                        queryString = "https://api.powerbi.com/v1.0/myorg/admin/" + Artefact; 
                    }
                    else 
                    {
                        queryString = "https://api.powerbi.com/v1.0/myorg/admin/" + Artefact+ "?" + Query.ToString(); 
                    }
                    

                    var result = client.GetAsync(queryString).Result;  
                    if (result.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        var content = result.Content.ReadAsStringAsync().Result;  
                        return Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(content);
                    }      
                    else 
                    {
                        throw new Exception ("PBI Api Request Failed. Status Code " + result.StatusCode.ToString() + "; Reason:  " + result.ReasonPhrase); 
                    }                        
                   
                }
            }
            catch (Exception e)
            {
                Shared.LogErrors(e);    
                Shared.LogErrors(new Exception("GetAdminArtefactsGeneric Failed"));
                throw e;                          
            }

        }

      
        




        }
}
