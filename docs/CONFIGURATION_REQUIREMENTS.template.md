# Configuration Requirements for Spaarke File Management Solution

**Document Purpose:** Complete configuration checklist for operational deployment
**Target Audience:** DevOps, System Administrators, Solution Architects
**Last Updated:** September 29, 2025

---

## Overview

This document outlines ALL configuration requirements needed to make the Spaarke file management solution fully operational. Missing any of these configurations will prevent the solution from functioning correctly.

⚠️ **SECURITY NOTE:** This is a template file. Replace all `[PLACEHOLDER]` values with actual values in your local copy. Never commit actual secrets to version control.

---

## 1. Dataverse Environment Configuration

### **1.1 Environment Details**
**Required Information:**
```json
{
  "dataverse": {
    "environmentUrl": "[YOUR-DATAVERSE-ENVIRONMENT-URL]",
    "apiUrl": "[YOUR-DATAVERSE-API-URL]",
    "tenantId": "[YOUR-AZURE-TENANT-ID]",
    "environmentId": "[YOUR-DATAVERSE-ENVIRONMENT-ID]",
    "organizationId": "[YOUR-DATAVERSE-ORG-ID]"
  }
}
```

**How to Obtain:**
- Environment URL: Power Platform Admin Center → Environments → Select Environment → Settings
- Tenant ID: Azure Portal → Azure Active Directory → Properties
- Environment ID: Power Platform Admin Center → Environment Details
- Organization ID: Dataverse → Developer Resources

### **1.2 Entity Schema Names and Field Types**
**Required for Code Generation:**
```csharp
public static class EntityNames
{
    public const string Document = "sprk_document";           // Logical name
    public const string Container = "sprk_container";         // Logical name
    public const string DocumentDisplayName = "Spaarke Document";
    public const string ContainerDisplayName = "Spaarke Container";
}

public static class FieldNames
{
    // sprk_document fields
    public const string DocumentId = "sprk_documentid";
    public const string DocumentName = "sprk_name";
    public const string ContainerReference = "sprk_containerid";
    public const string HasFile = "sprk_hasfile";             // Two Options (bool)
    public const string FileName = "sprk_filename";
    public const string FileSize = "sprk_filesize";           // Whole Number (long)
    public const string MimeType = "sprk_mimetype";
    public const string GraphItemId = "sprk_graphitemid";
    public const string GraphDriveId = "sprk_graphdriveid";
    public const string StateCode = "statecode";               // Choice (OptionSetValue)
    public const string StatusCode = "statuscode";               // Choice (OptionSetValue)

    // sprk_container fields
    public const string ContainerId = "sprk_containerid";
    public const string ContainerName = "sprk_name";
    public const string SpeContainerId = "sprk_specontainerid";
    public const string DocumentCount = "sprk_documentcount"; // Whole Number (int)
    public const string DriveId = "sprk_driveid";
}
```

### **1.3 Complete Field Type Specifications**

**sprk_document Entity Fields:**
```json
{
  "fields": [
    {
      "schemaName": "sprk_documentname",
      "displayName": "Document Name",
      "type": "String",
      "maxLength": 850,
      "required": true,
      "codeType": "string"
    },
    {
      "schemaName": "sprk_containerid",
      "displayName": "Container Reference",
      "type": "Lookup",
      "target": "sprk_container",
      "required": true,
      "codeType": "EntityReference"
    },
    {
      "schemaName": "sprk_hasfile",
      "displayName": "Has File",
      "type": "TwoOptions",
      "defaultValue": false,
      "trueLabel": "Yes",
      "falseLabel": "No",
      "codeType": "bool?"
    },
    {
      "schemaName": "sprk_filename",
      "displayName": "File Name",
      "type": "String",
      "maxLength": 255,
      "required": false,
      "codeType": "string"
    },
    {
      "schemaName": "sprk_filesize",
      "displayName": "File Size (bytes)",
      "type": "BigInt",
      "minValue": 0,
      "maxValue": 2147483647,
      "codeType": "long?"
    },
    {
      "schemaName": "sprk_mimetype",
      "displayName": "MIME Type",
      "type": "String",
      "maxLength": 100,
      "codeType": "string"
    },
    {
      "schemaName": "sprk_graphitemid",
      "displayName": "Graph Item ID",
      "type": "String",
      "maxLength": 1000,
      "codeType": "string"
    },
    {
      "schemaName": "sprk_graphdriveid",
      "displayName": "Graph Drive ID",
      "type": "String",
      "maxLength": 1000,
      "codeType": "string"
    },
    {
      "schemaName": "statecode",
      "displayName": "Status",
      "type": "Choice",
      "optionSet": "statecide",
      "defaultValue": 0,
      "codeType": "OptionSetValue"
    },
    {
      "schemaName": "statuscode",
      "displayName": "Status Reason",
      "type": "Choice",
      "optionSet": "statuscode",
      "defaultValue": 1,
      "codeType": "OptionSetValue"
    }
  ]
}
```

**sprk_container Entity Fields:**
```json
{
  "fields": [
    {
      "schemaName": "sprk_containername",
      "displayName": "Container Name",
      "type": "String",
      "maxLength": 850,
      "required": true,
      "codeType": "string"
    },
    {
      "schemaName": "sprk_containerid",
      "displayName": "Container ID",
      "type": "String",
      "maxLength": 1000,
      "required": true,
      "codeType": "string"
    },
    {
      "schemaName": "sprk_documentcount",
      "displayName": "Document Count",
      "type": "WholeNumber",
      "minValue": 0,
      "maxValue": 2147483647,
      "defaultValue": 0,
      "codeType": "int?"
    },
    {
      "schemaName": "sprk_driveid",
      "displayName": "Drive ID",
      "type": "String",
      "maxLength": 1000,
      "codeType": "string"
    }
  ]
}
```

### **1.4 Option Set Configuration**
**Document Status Option Set (`statecode`):**
```csharp
public enum StateCode
{
    Active = 0,
    Inactive = 1
}
```

**Option Set Values:**
```json
{
  "optionSetName": "statecode",
  "displayName": "Status",
  "options": [
    {"value": 0, "label": "Active", "color": "#0078D4"},
    {"value": 1, "label": "Inactive", "color": "#D13438"}
  ]
}
```

**Document Status Reason Option Set (`statuscode`):**
```csharp
public enum StatusCode
{
    Draft = 1,
    Processing = 421500002,
    Active = 421500001,
    Error = 2
}
```

**Option Set Values:**
```json
{
  "optionSetName": "statuscode",
  "displayName": "Status Reason",
  "options": [
    {"value": 1, "label": "Draft", "color": "#0078D4"},
    {"value": 421500002, "label": "Processing", "color": "#FF8C00"},
    {"value": 421500001, "label": "Active", "color": "#107C10"},
    {"value": 2, "label": "Error", "color": "#D13438"}
  ]
}
```

### **1.4 Security Roles**
**Required Roles to Create:**
- `Spaarke Document User` - Read/Write documents they own
- `Spaarke Document Manager` - Full CRUD on all documents
- `Spaarke Container Admin` - Manage containers and all documents
- `Spaarke System Administrator` - Full administrative access

**Permission Matrix Required:**
```yaml
Entities:
  sprk_document:
    DocumentUser: [Create, Read Own, Write Own, Delete Own]
    DocumentManager: [Create, Read All, Write All, Delete All]
    ContainerAdmin: [Create, Read All, Write All, Delete All]

  sprk_container:
    DocumentUser: [Read All]
    DocumentManager: [Read All]
    ContainerAdmin: [Create, Read All, Write All, Delete All]
```

---

## 2. Azure Active Directory / Entra ID Configuration

### **2.1 Application Registrations**
**Required App Registrations:**

**BFF API Application:**
```json
{
  "name": "Spaarke-BFF-API",
  "clientId": "[YOUR-BFF-API-CLIENT-ID]",
  "tenantId": "[YOUR-AZURE-TENANT-ID]",
  "redirectUris": [
    "https://spaarke-bff.azurewebsites.net/signin-oidc",
    "https://localhost:7001/signin-oidc"
  ],
  "apiPermissions": [
    {
      "api": "Microsoft Graph",
      "permissions": [
        "Files.ReadWrite.All",
        "Sites.ReadWrite.All"
      ]
    },
    {
      "api": "Dataverse",
      "permissions": [
        "user_impersonation"
      ]
    }
  ],
  "clientSecrets": [
    {
      "description": "BFF API Client Secret",
      "value": "[GENERATED-SECRET]",
      "expires": "[DATE]"
    }
  ]
}
```

**Power Platform Application:**
```json
{
  "name": "Spaarke-PowerPlatform",
  "clientId": "[YOUR-POWER-PLATFORM-CLIENT-ID]",
  "tenantId": "[YOUR-AZURE-TENANT-ID]",
  "redirectUris": [
    "[YOUR-DATAVERSE-ENVIRONMENT-URL]",
    "https://make.powerapps.com/environments/[YOUR-ENVIRONMENT-ID]"
  ],
  "apiPermissions": [
    {
      "api": "Spaarke-BFF-API",
      "permissions": ["access_as_user"]
    }
  ]
}
```

### **2.2 Managed Identity Configuration**
**User-Assigned Managed Identity (UAMI):**
```json
{
  "name": "spaarke-bff-identity",
  "resourceGroup": "[YOUR-RESOURCE-GROUP]",
  "subscription": "[YOUR-SUBSCRIPTION-ID]",
  "clientId": "[UAMI-CLIENT-ID]",
  "principalId": "[UAMI-PRINCIPAL-ID]",
  "permissions": [
    {
      "resource": "SharePoint Embedded",
      "role": "Container Administrator"
    },
    {
      "resource": "Key Vault",
      "permissions": ["Get", "List"]
    }
  ]
}
```

---

## 3. SharePoint Embedded Configuration

### **3.1 Container Type Configuration**
**Required Container Type:**
```json
{
  "containerTypeId": "[YOUR-SPE-CONTAINER-TYPE-ID]",
  "displayName": "[YOUR-CONTAINER-TYPE-NAME]",
  "description": "[YOUR-CONTAINER-TYPE-DESCRIPTION]",
  "permissions": {
    "defaultAccess": "none",
    "adminAccess": "[UAMI-CLIENT-ID]"
  }
}
```

**How to Create:**
- SharePoint Admin Center → SharePoint Embedded → Container Types
- Create new container type with above settings
- Note the generated Container Type ID for configuration

### **3.2 Application Registration for SPE**
**Additional SPE-Specific Permissions:**
```json
{
  "apiPermissions": [
    {
      "api": "SharePoint",
      "permissions": [
        "Container.Selected",
        "FileStorageContainer.Selected"
      ]
    }
  ]
}
```

---

## 4. Azure Service Bus Configuration

### **4.1 Service Bus Namespace**
**Required Configuration:**
```json
{
  "namespaceName": "[YOUR-SERVICEBUS-NAMESPACE]",
  "resourceGroup": "[YOUR-RESOURCE-GROUP]",
  "location": "[YOUR-AZURE-REGION]",
  "sku": "Standard",
  "connectionString": "[YOUR-SERVICEBUS-CONNECTION-STRING]"
}
```

### **4.2 Queues and Topics**
**Required Service Bus Entities:**
```yaml
Queues:
  document-events:
    maxDeliveryCount: 10
    lockDuration: "PT5M"
    defaultMessageTimeToLive: "P1D"
    deadLetteringOnMessageExpiration: true

  document-jobs:
    maxDeliveryCount: 5
    lockDuration: "PT10M"
    defaultMessageTimeToLive: "P7D"
    deadLetteringOnMessageExpiration: true

Topics:
  document-notifications:
    subscriptions:
      - audit-logging
      - user-notifications
      - system-monitoring
```

### **4.3 Access Policies**
**Required Access Policies:**
```json
{
  "policies": [
    {
      "name": "BFF-API-Access",
      "permissions": ["Send", "Listen"],
      "primaryKey": "[GENERATED-KEY]",
      "secondaryKey": "[GENERATED-KEY]"
    },
    {
      "name": "Plugin-Send-Only",
      "permissions": ["Send"],
      "primaryKey": "[GENERATED-KEY]",
      "secondaryKey": "[GENERATED-KEY]"
    }
  ]
}
```

---

## 5. Azure Key Vault Configuration

### **5.1 Key Vault Instance**
**Key Vault Configuration:**
```json
{
  "keyVaultName": "[YOUR-KEYVAULT-NAME]",
  "resourceGroup": "[YOUR-RESOURCE-GROUP]",
  "location": "[YOUR-AZURE-REGION]",
  "accessModel": "Role-Based Access Control (RBAC)",
  "keyVaultUri": "https://[YOUR-KEYVAULT-NAME].vault.azure.net/",
  "rbacRoles": [
    {
      "principalId": "[UAMI-PRINCIPAL-ID]",
      "principalType": "User-Assigned Managed Identity",
      "role": "Key Vault Secrets User",
      "permissions": ["Get", "List"]
    },
    {
      "principalId": "[DEVELOPER-OBJECT-ID]",
      "principalType": "User",
      "role": "Key Vault Secrets Officer",
      "permissions": ["Get", "List", "Set", "Delete", "Backup", "Restore"]
    }
  ]
}
```

### **5.2 Required Secrets**
**Essential Secrets (Start with these):**
```yaml
Essential:
  UAMI-ClientId:
    value: "[UAMI-CLIENT-ID]"
    description: "Managed Identity Client ID for authentication"

  SPE-ContainerTypeId:
    value: "[SPE-CONTAINER-TYPE-ID]"
    description: "SharePoint Embedded Container Type ID"

  ServiceBus-ConnectionString:
    value: "[SERVICEBUS-CONNECTION-STRING]"
    description: "Azure Service Bus connection string"

Optional-For-Later:
  BFF-API-ClientSecret:
    value: "[BFF-API-CLIENT-SECRET]"
    description: "Azure AD App Registration secret for BFF API"

  Dataverse-ConnectionString:
    value: "[DATAVERSE-CONNECTION-STRING]"
    description: "Alternative: AuthType=Office365;Url=[DATAVERSE-URL];Username=[USERNAME];Password=[PASSWORD]"
    note: "Prefer managed identity authentication over connection strings"
```

---

## 6. Application Configuration Files

### **6.1 BFF API Configuration**
**appsettings.json Structure:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "[DATABASE-CONNECTION-IF-NEEDED]",
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/ServiceBus-ConnectionString)",
    "Dataverse": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/Dataverse-ConnectionString)"
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "[YOUR-AZURE-TENANT-ID]",
    "ClientId": "[YOUR-BFF-API-CLIENT-ID]",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/BFF-API-ClientSecret)"
  },
  "Graph": {
    "TenantId": "[YOUR-AZURE-TENANT-ID]",
    "ClientId": "[YOUR-BFF-API-CLIENT-ID]",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/Graph-API-ClientSecret)",
    "ContainerTypeId": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/SPE-ContainerTypeId)"
  },
  "Dataverse": {
    "ServiceUrl": "[YOUR-DATAVERSE-ENVIRONMENT-URL]",
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/Dataverse-ConnectionString)"
  },
  "ServiceBus": {
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/ServiceBus-ConnectionString)",
    "DocumentEventsQueue": "document-events",
    "DocumentJobsQueue": "document-jobs"
  },
  "ManagedIdentity": {
    "ClientId": "@Microsoft.KeyVault(SecretUri=https://[YOUR-KEYVAULT].vault.azure.net/secrets/UAMI-ClientId)"
  },
  "Cors": {
    "AllowedOrigins": "[YOUR-DATAVERSE-URL],[YOUR-POWER-APPS-MAKER-URL]"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Spaarke": "Debug"
    }
  }
}
```

---

## SECURITY GUIDELINES

### **🔒 Protecting Sensitive Configuration**

1. **Never commit actual secrets to version control**
2. **Use this template and create local copies with real values**
3. **Store secrets in Azure Key Vault**
4. **Use managed identity when possible**
5. **Rotate secrets regularly**

### **📁 File Handling**
- Copy this template to `CONFIGURATION_REQUIREMENTS.md` for local use
- Replace all `[PLACEHOLDER]` values with actual values
- The actual file with real values is in `.gitignore`
- Use Key Vault references in application configuration

---

This configuration template ensures all required infrastructure, security, and application settings are properly configured for a successful deployment of the Spaarke file management solution while maintaining security best practices.