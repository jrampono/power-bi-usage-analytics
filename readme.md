# Power BI Foundations Fast Start Code Components - Usage Tracking 
## Introduction 
Power BI is a very popular business tool and in many organisations we see an rapid and exponential growth in its adoption and use. Growth and adoption is a great thing but without some degree of guidance & structure things can get out of control. So how do we achieve a bit of control and governance over Power BI? Well in my opinion one of the first pre-requisites to control is "visability". If you can quickly and easily see and analyse what is happening in your enterprise Power BI environment then you can take action to encourage the good things that are happening and discourage the bad. Some of the questions that would be very useful to answer are: 
1. Who is creating content, 
2. Who is viewing content, 
3. What is the most popular content, 
4. How is content being shared?
5. What are the trends associated with all of the above?

Straight "out of the box" the Power BI service provides quite a lot of useful information relating to this. See the two links below for a quick review of these features:

[https://docs.microsoft.com/en-us/power-bi/service-usage-metrics](https://docs.microsoft.com/en-us/power-bi/service-usage-metrics)

[https://docs.microsoft.com/en-us/power-bi/service-admin-portal#usage-metrics](https://docs.microsoft.com/en-us/power-bi/service-admin-portal#usage-metrics)

But what if you want to go beyond these standard features? What if you want to extended on these features to provide a comprehensive view of what is happening and integrate this into your planning and governance processes? Well, if that is you then you are in luck because that is exactly what this series of posts will be about. Over the course of 3 or 4 articles I intend to guide you through the process of implementing a full usage tracking and analysis solution for Power BI. The goal of the series will be to build out the architecture presented in the image below.

![](https://lh3.googleusercontent.com/7S_DAK9uhzx_HjsWAQnZIwoalhEmhSZlbsnEKw-aPUGk8QjxwWiDu_INFzJ3oKTwcoDPKBqI8RqA1A_tl3DtsbCtOW63dYX36rsTtdBVYG5__pwD9YhhgKvg2Rvq4V-KtK1M3hZuEZCXm7Be19_26R6tXxu4KW4jnIW5lxLyk00EMs-In6L7MvL4pOfQ-tRBoagd6L5Wv5Px2ZNmhIWCoamlxugFtqCWwboamAw74_kJ6D0KMaoT7GtE0VrPX5x_TgAf5D70ekL4hkDHtDh0Djt-Cj4ZVUg0qhpAGQMASIdu56rdqgL1JMy8oeO7GzcRelEPplKDInA71mMKFcJw3_EZD3tfAs_8zbq59r0Prp26gUDsbzAgZPHg4HYvAMyS_si_Pi11FzXMSBfpfvPGj7oRQamxHLm9eNjyddCBiQ2L1DdFv-C8yiI-66BF6U_3RpN2g2uQi5qR0uylPgxmvSz2S6Q571-oc92pgru9mXwYb8keDPLpaSg37Q9ZZdxyb_9QUyl0yaD5B0ouBaXzdVf3v3ytOexlj724MY6E_NVWKBqb1HeV06S6Mtl4JVvU2Pct-PSafeyUEhKeh8p9U0XU9LoCRn0j8lQI7YfkBerrra6R1WQhETk74UVCgFz4THIedmAGRJbG5d1VUTqcb4yN2oJ1DsyZARbjqmZDHETUFdZnuaPZNgak8YnSpTI4NBHmoEsrS9RmiCD5Dy7wn3oLaw=w2090-h975-no)

Information will be extracted from both the O365 Management API and the Power BI Admin API by a set of Azure Functions. This information will be loaded in a semi- structured format (JSON) into an Azure Blob Storage container. From there a subset of the information will be loaded into an Azure SQL Database. A rigid structure will be applied to the data as well as a series of business rules and validation procedures. A Power BI OLAP cube will then be built on top of this as well as a series of rich Power BI reports. I'll provide you with all of the template code to do this as well as step by step instructions. Once you have deployed the solution you are free to then extend it so that it meets the unique needs of your organisation. 

As an added bonus it useful to note that the architectural pattern applied here can be re-used to do other very useful things. Its a fairly typical and popular pattern that you can use for a wide variety of reporting and analytical applications.

You'll be pleased to know that I won't tackle this all at once. This first article will deal with the first phase of the architecture only. I've highlighted what will be covered using the red over-lay in the image below. So this article will look at implementing the Azure Functions that will access the APIs, extract the relevant data and upload that data into a Blob storage container. 

![](https://lh3.googleusercontent.com/XG35dQVxVhT5uy_GQa6v4aw8xYg1m-J9MUwV37QEYPhv0VNWY7kMA678ic3y0oA_mYSLdTHZuG5BfeONAWcx1QwjVI58Qlh2qLUe_KYepdMsd7VPQ30Ss2qNJEzeZZd0-yyxGgQ5V18=w2827-h1240-no)

## A quick note on authentication, configuration and deployment
You should consider all of the code provided to you as sample, non-production code. I don't provide any guarantees or warranties of any kind and this is a guide is a learning tool rather than a production deployment guide. ...

Azure functions can be executed locally using an IDE like "VS Code" or remotely within the Azure service. For local development and debugging it is essential that you understand how to configure local execution. 

**Data and Authentication Flows - Local Debugging**
When running the functions in VS code you will authenticate with cloud services using two Azure Service Principal Accounts. The "localdev_serviceprincipal" will be used to access AzureKeyVault, The Office Management API and Azure Blob storage. The details required to authenticate the localdev_serviceprincipal will be stored within a local.settings.json that is stored on your local computer.

The "cloud_serviceprincipal" will be used to access the Power BI REST API. Note that the cloud_serviceprincipal will do this by using permissions "delegated" from Azure Active Directory user account which we will refer to as the Power BI Master User. The details required to authenticate the cloud_serviceprincipal will be stored within and accessed from an Azure Key Vault. See the image below for a visual representation of the data and authentication flows.

![](https://lh3.googleusercontent.com/ZCUOQWbI0kbcPZkKU-U_87xnUOqKpoOdoy65zidvJMWI1x0EiqowqmoOiEpLhgvh2WIV9Gfek3Rht3jExGzchzPx3UgiTTVT2GN9VIGQFNRqGo92C7CWkVPbqIvZf31zDUQoJIwqTcE=w1372-h1003-no)

Once deployed to the cloud the the localdev_serviceprincipal is no longer used. Instead the managed service identity associated with the Azure Functions instance is now used to access the Office 365 Management API, Azure Key Vault and Azure Blob storage. Note the the cloud_serviceprincipal is still used to provide access to the Power BI Admin API's. Note that the authentication behaviour exhibited by the application is controlled by the UseMSI setting. For cloud deployment this is found in the Azure Function's application settings. For local development this is found in "local.settings.json". When UseMSI is "True" then the application will use the cloud based settings when set to "False" it will use the local development settings. Therefore, this value should be "True" in the Azure Function's application settings and false in "local.settings.json" on your local machine. The figure below depicts the data and authentication flows when the function is cloud deployed. 

**Data and Authentication Flows - Cloud Deployment**
![](https://lh3.googleusercontent.com/MyJz-NCOOgyZu9JQTaFwFbYBS-jYsQrrk8wTca2Gn-bNlrPgzP09_wxGw4WyxnxgebV-66Ea915b828Z2cKO2VuW8_sG9-R8fr3eMWyMxbuZ9hP95fHso4iKQxMd8UdtIqVFKKRBuRE=w1370-h1005-no)


## Repository Structure
The source control repository contains a number of sub-folders. For the purposes of this article you only need to look at the "FunctionApp" sub-folder. This folder contains a set of Azure Functions that will gather Power BI Meta data and usage data. The current function list includes:

1. **GetPowerBiMetaData** - This function provides the functionality to fetch meta data relating to Power BI Workspaces, Groups, Datasets and Reports. It will also write the collected data into an Azure Blob storage container. See the comments within "GetPowerBIMetaData.cs" to get an understanding of the various methods and parameters.

2. **GetPowerBiUsageStats** - This function will get the O365 audit data from the O365 Api and write it to an Azure Blob storage container. See the comments at the top of "GetPowerBIMetaData.cs" to get an understanding of the various methods and parameters.

**Note**: There is also a **Shared.cs** file that contains functionality that is used by both GetPowerBiMetaData & GetPowerBiUsageStats. 

## Detailed Instructions  for Development and Deployment 

### **Step One:**  Create Pre-requisite Azure Services 
#### **A.** Function App
Create a Function App in Azure and provision a System Assigned Managed Service Identity. I have not provided a script to do this but you can view the documentation at the link below:

[https://docs.microsoft.com/en-us/azure/app-service/overview-managed-identity](https://docs.microsoft.com/en-us/azure/app-service/overview-managed-identity)


#### **B.** Storage Account
Create a storage account and related blob container that will be used to provide an initial storage area for the Power BI meta data and audit log information. 

#### **C.** Create Power BI Master User
Create a Power BI "Master User Account" that does NOT have MFA enabled. This user should be a Power BI Admin @ the 0365 tenant level. For additional security you can add an Azure Function to automatically rotate this account's password on a regular basis.  

#### **D.** Key Vault:
Create an Azure Key Vault and create the following secrets: 
|SecretKey|SecretValue|
|-----|----|
|ApplicationId|Empty String
|AuthenticationKey|Empty String|
|MasterUserName|<mark>**{Power BI Master UserName From C. above}**<mark>|
|MasterUserPassword|<mark>**{Power BI Master UserPassword From C. above}**<mark>|

**Note!! Replace items in curly brackets with your own values.**


## **Step Two:**  Connect to Azure using an Administrator account. 
I am going to guide you through many of the remaining steps with the help of some Powershell scripts. In order to execute these scripts you will first need to login to Azure. To do this launch Powershell ISE and copy the following code block into the script. Execute the script and follow the login prompts (there will be two sets of login prompts). Please note that I am suggesting that you write this in Powershell ISE but you can use whatever Powershell execution environment that you prefer. 

```Powershell
#Connect to Azure with Appropriate Credential 
Connect-AzureAD
Connect-AzureRmAccount
```
 
## **Step Three:**  Set Deployment Environment. 
This step is simply going to set the deisred target environment (eg. Production or Development). Replace the script contents in Powershell ISE with the following commands. Edit based on the environment that you would like to deploy to and execute. 

```Powershell
#Determine if deployment is for for Development or Production (Uncomment second line for production deployment)
$Environment = "Development"
#$Environment = "Production"
```

## **Step Four:**  Create the required Service Principals. 
If you would like the required Service Principals to be created via Powershell script then replace the script contents in Powershell ISE with the following commands. Feel free to rename the service principals by updating the -DisplayName paramters accordingly. 

```Powershell
#####Create Service Principals###
$localdev_serviceprincipal = New-AzureRmADServicePrincipal -DisplayName "localdev_serviceprincipal"
$cloud_serviceprincipal = New-AzureRmADServicePrincipal -DisplayName "cloud_serviceprincipal"
$localdev_serviceprincipal_objectid = $localdev_serviceprincipal.Id 
$cloud_serviceprincipal_objectid = $cloud_serviceprincipal.Id 
```

Alternatively, if you would like to create these yourself via another method then simply run the script block below to set the related variables with the appropriate values. 

**Note!! Replace items in curly brakets with your own values (Do not keep the curly brackets).**
```Powershell
$localdev_serviceprincipal_objectid = "{localdev_serviceprincipal_objectid}"
$cloud_serviceprincipal_objectid = "{cloud_serviceprincipal_objectid}"
```

Now that you have the required Service Principal information go back to your key vault and add in the values for the "cloud_serviceprincipal". To get the AuthenticationKey you will need to generate a value via the portal. Please see this link for details: [https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal#get-application-id-and-authentication-key]()

**Note!! Replace items in curly brackets with your own values (Do not keep the curly brackets).**
|SecretKey|SecretValue|
|-----|----|
|ApplicationId|<mark>{cloud_serviceprincipal_objectid}<mark>
|AuthenticationKey|<mark>{cloud_serviceprincipal_secret}|<mark>

## **Step Five:** Set other required variables


Sample Storage Account Scope: 
"/subscriptions/**{Your-subscription-id}**/resourceGroups/**{Your-Resource-Group}**/providers/Microsoft.Storage/storageAccounts/**{Your-StorageAccount-Name}**/blobServices/default/containers/**{Your-Container-Name}**"

**Note!! Replace items in curly brackets with your own values (Do not keep the curly brackets).**

```Powershell
$msi_objectid = "{Your Azure Function App's MSI objectid}" 
$storageaccountscope = "{Scope of your storage account}"
$keyvaultname = "{Your Key Vault Name}"
```

## **Step Six:** Set O365 Management API Permissions
Your Service principal OR MSI will need read access to the Activity Feed in the Office 365 Management API. To grant the appropriate permissions use the Powershell script block below:
```PowerShell
#Get O365 Management API Service
$o365 = Get-AzureADServicePrincipal -Filter "appId eq 'c5393580-f805-4401-95e8-94b7a6ef2fc2'"

#grant access to Activity Feed Read o365 api role
if($Environment -eq "Development")
{
    $o365_User_Object_Id = $localdev_serviceprincipal_objectid
}
else 
{
    $o365_User_Object_Id = $msi_objectid   
}
ForEach ($i In $o365.AppRoles) {
    if ($i.Value -eq "ActivityFeed.Read")
    {
        New-AzureADServiceAppRoleAssignment -Id $i.Id -PrincipalId $o365_User_Object_Id -ResourceId $o365.ObjectId -ObjectId $o365_User_Object_Id    
    }
}

```

Note, if you wish to review permissions use the following code block:

```PowerShell
#Get Assignments
$assignments = Get-AzureADServiceAppRoleAssignedTo -ObjectId $pbi_User_Object_Id
$assignments
```

If you wish to REMOVE ALL assignments use this code block. **Note you don't need to run this!!!. It is just there in case you wish to remove assignments!!!.***
```PowerShell
#Remove Assignments
ForEach ($i In $assignments) {
    Remove-AzureADServiceAppRoleAssignment -AppRoleAssignmentId $i.ObjectId -ObjectId $pbi_User_Object_Id    
}
```


## **Step Seven:** Set Key Vault Access Rights
```PowerShell
#Grant Get right on Key Vault secrets to the local dev service principal or msi
if($Environment -eq "Development")
{
    $keyvault_User_Object_Id = $localdev_serviceprincipal_objectid
}
else 
{
    $keyvault_User_Object_Id = $msi_objectid   
}
Set-AzureRmKeyVaultAccessPolicy -VaultName $keyvaultname -ObjectId $keyvault_User_Object_Id -PermissionsToSecrets Get

```


## **Step Eight:** Set Storage Account Rights
The MSI and Service Principal will also need the ability to write data into the relevant blob storage account. I used the blob data contributor role but you can custom craft the permissions if you desire additional granularity. Run the Powershell Script block below to grant these rights to your Service Account or MSI. 

```PowerShell
#Grant Contributor Rights to Storage Account
$StorageAccountAccessList = New-Object Collections.Generic.List[string]
if($Environment -eq "Development")
{
    $StorageAccountAccessList.Add($localdev_serviceprincipal_objectid)        
}
else 
{
    $StorageAccountAccessList.Add($msi_objectid)     
}

$role = Get-AzureRmRoleDefinition | Where-Object {$_.name -eq 'Storage Blob Data Contributor'}
forEach ($i in $StorageAccountAccessList)
{    
    # Add user to role
    New-AzureRmRoleAssignment -RoleDefinitionId $role.Id -Scope $storageaccountscope -ObjectId $i
}  
```


## **Step Nine:** Set Power BI API Rights
I spent a considerable amount of time trying to get an MSI to work with the Power BI API's but it does not seem to be supported yet. I suspect that it could be made to working by using a user assigned identity rather than a system assigned one but I have not yet tested this theory. As a result the current solution uses a Service Principal to access the Power BI API's. The details of this service principal are stored in the Key Vault. 

The service principal needs to be given a high level admin role to allow it to read information from the Power BI API. Grant this role using the Powershell script below.

```PowerShell
#Get Power BI Service Reference
$pbi = Get-AzureADServicePrincipal -Filter "appId eq '00000009-0000-0000-c000-000000000000'"

#grant access to Power BI ***Note always the cloud SP****
if($Environment -eq "Development")
{
    $pbi_User_Object_Id = $cloud_serviceprincipal_objectid
}
else 
{
    $pbi_User_Object_Id = $cloud_serviceprincipal_objectid
}
ForEach ($i In $pbi.AppRoles) {
        New-AzureADServiceAppRoleAssignment -Id $i.Id -PrincipalId $pbi_User_Object_Id -ResourceId $pbi.ObjectId -ObjectId $pbi_User_Object_Id            
}
```

Next, the service principal needs to be specifically granted the rights to impersonate the Power BI master user account and read from Power BI as though it was the master user. The fastest way to do this is to navigate to the following link: [https://developer.microsoft.com/en-us/graph/graph-explorer#](). Login using an admin user. Then select the "POST" method and insert "https://graph.microsoft.com/beta/oauth2PermissionGrants/" into the URL box (this is the URL box to the right of the method selector). Paste the following JSON into the "Request body" section. **NOTE!!! You need to replace values below that are in curly brackets with your own values ("clientid" & "principalid")). Also update "expiryTime" as required**. The client ID is the objectid of your Service Prinicpal's application (find this in the "Enterprise Applications" section of the Azure Portal). 

Once you have done this press the "Run Query" button. You should get a success response. 

```Json
        {
            
            "clientId": "{object id of your cloud_serviceprincipal}",
            "consentType": "Principal",
            "expiryTime": "2019-11-11T08:31:03.2504886Z",
            "id": "MY5uVG9wYka7S9dRzdAgFEkow96MyU9EkXjrxHeDNQy2LZYlsUzHTZHLnTXQv3-l",
            "principalId": "{object_id of your power bi master user}",
            "resourceId": "dec32849-c98c-444f-9178-ebc47783350c",
            "scope": "App.Read.All Capacity.Read.All Capacity.ReadWrite.All Content.Create Dashboard.Read.All Dashboard.ReadWrite.All Dataflow.Read.All Dataflow.ReadWrite.All Dataset.Read.All Dataset.ReadWrite.All Gateway.Read.All Gateway.ReadWrite.All Report.Read.All Report.ReadWrite.All StorageAccount.Read.All StorageAccount.ReadWrite.All Tenant.Read.All Tenant.ReadWrite.All Workspace.Read.All Workspace.ReadWrite.All",
            "startTime": "0001-01-01T00:00:00Z"
        }
```

The final step is to add your master user to your application. To do this find your service principal in the "Enterprise Applications" section of the Azure portal. Next select "Users & Groups" and select "Add user".

**ADD Screenshot !!!**


## **Step Ten:** Create your local development environment & set your application settings


### Development Environment
To begin development or deployment first clone this repository to your local development machine and open the  **"FunctionApp"** folder using Visual Studio Code. 

### Required App Settings 
For local development you will need to create a "local.settings.json" file and paste in the following JSON. **Note!! Replace items in curly brackets with your own values. The final file should NOT include curly brackets.**

```Json
{
    "IsEncrypted": false,
    "Values": {
        "TenantId": "{Azure TenantId}", 
        "ApplicationId": "{The LocalDev Service Principal Application ID (Required for Local Dev Only)}",
        "AuthenticationKey": "{The LocalDev Service Principal Authentication Key (Local Dev Only)}",
        "KeyVault": "{Name of the Key Vault used to store sensitive information}",
        "Domain": "{The domain name of the Azure Active Directory that you are using}",
        "UseMSI": false
    }
}
```

When deploying to the cloud, by default the local settings file is not published. Therefore, you will need to manually set the appropriate settings in the cloud once you have published the function. See the animation below for a quick run through of this.  

![](https://lh3.googleusercontent.com/Mw0_USYIoe6q9DNqfNGLmihN_jp3H1wP8G7KzLTxkcAbUhr2_xboG0WiBkbdJShcgyXO2UFEpiQCx8MTszwv3MnSRUK2lUMPtvyr3Ku6QyMQOby6SHoCoITBIlZ2mAxqlGXIRPIJc6d8XWpCVTwXz9UsFPzWdzSrZoE6xoaaR2JK-yP9nvflaomY6p6bPW1eGEnpxEylPCMYMaL5YdP14wy22EPyjggcBlouIk4Ro754ttIfO-1ihAERhqDnL_LJY7RqBSdpr2AsSOS6NneCmNtv8G3UsVwyPeo_Oo3-5EWdsFAlU1FlagN39LB-nrz-mv4ERzOOhfl3D_ktPmal4unsRKs3G2xnbxfwN02ySfN0cp2qUI93qekV3K3OLfhQT8ilrS3DSKKgon3EKRsNNZ9md5UBH29Cj3zSIMXCh7eOHk2s4irCH0iyZjUW6E3KtV74nzVM20FGmtyyBB2NiQWjJCJdSIuG9CD2tfyO8MxTJfkR1al8Mdpmrf-M-IKmyDFgIaWcdASWjg6gZIapCF2p7dXhfnQrapB7SWRBgQJ23yB_qCQkiElBleO1PHuD3s4xtLmyKC-a8CS5iOWIeNHPQg0u49XVCeHZGP4v044PLVnP1Kh_4ibtX4jZRaKEI9xRJcMLJqpiIbs6nKdPvEx4w9odtm2tArSOAUMFuWEhXZZU1am27BhtWW-ht_XLyzYyG7Sjsr8FwR0RSxsnh-YDHg=w812-h465-no)

Note that you only need to set the following Application Settings: 
```Json
    {
        "TenantId": "{Azure TenantId}", 
        "KeyVault": "{Name of the Key Vault used to store sensitive information}",
        "Domain": "{The domain name of the Azure Active Directory that you are using}",
        "UseMSI: true
    }
```

## **Step Eleven:** Testing





**GetPowerBiUsageStats**


```Curl
curl -X POST \
  http://{}:7071/api/GetPowerBIMetaData \
  -H 'x-functions-key: {Your Azure Function Key}' \
  -d '{
	"Action":"GetLogsAndUploadToBlob",
	"BlobStorageAccountName":"{Your Blob Storage Account Name}",
	"BlobStorageContainerName":"{Your Blob Storage Container Name}",
	"BlobStorageFolderPath":"{Your Blob Storage Folder Path}"
}'

```

1. Call GetPowerBIMetaData with Action of "GetWorkspaces"

```Curl
curl -X POST \
  http://{}:7071/api/GetPowerBIMetaData \
  -H 'x-functions-key: {Your Azure Function Key}' \
  -d '{
	"Action":"GetLogsAndUploadToBlob",
	"BlobStorageAccountName":"{Your Blob Storage Account Name}",
	"BlobStorageContainerName":"{Your Blob Storage Container Name}",
	"BlobStorageFolderPath":"{Your Blob Storage Folder Path}"
}'
```

2. For Each Workspace Call GetPowerBIMetaData with Action of "GetItemsInWorkspace"
```Curl
curl -X POST \
  http://{}:7071/api/GetItemsInWorkspace \
  -H 'x-functions-key: {Your Azure Function Key}' \
  -d '{
	"Action":"GetLogsAndUploadToBlob",
    "WorkspaceId": {The id of the current workspace in your foreach loop} 
	"BlobStorageAccountName":"{Your Blob Storage Account Name}",
	"BlobStorageContainerName":"{Your Blob Storage Container Name}",
	"BlobStorageFolderPath":"{Your Blob Storage Folder Path}"
}'
```

**GetPowerBiMetaData** 
```Curl
curl -X POST \
  http://{}:7071/api/GetPowerBIMetaData \
  -H 'x-functions-key: {Your Azure Function Key}' \
  -d '{
	"Action":"GetWorkspaces",
	"BlobStorageAccountName":"{Your Blob Storage Account Name}",
	"BlobStorageContainerName":"{Your Blob Storage Container Name}",
	"BlobStorageFolderPath":"{Your Blob Storage Folder Path}"
}'

```

## **Step Twelve:** Clean Up

- Remove service principal (it is only required for development and local debugging)




