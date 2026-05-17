---
source: https://learn.microsoft.com/en-us/azure/search/search-howto-index-sharepoint-online
fetched: 2026-05-14
---

# SharePoint in Microsoft 365 Indexer - Azure AI Search

> **Important**: The SharePoint in Microsoft 365 indexer is in **preview**. It's offered "as-is" under Supplemental Terms of Use and supported on a best-effort basis only. Preview features aren't recommended for production workloads and aren't guaranteed to become generally available.
>
> Fill out the preview registration form (`https://aka.ms/azure-cognitive-search/indexer-preview`). All requests are approved automatically. After you fill out the form, use a preview REST API to index your content.

This article explains how to configure a search indexer to index documents stored in SharePoint document libraries for full-text search in Azure AI Search.

In Azure AI Search, an indexer extracts searchable data and metadata from a data source. The SharePoint in Microsoft 365 indexer provides the following functionality:

- Indexes files and metadata from one or more document libraries.
- Indexes incrementally, picking up just the new and changed files and metadata.
- Detects deleted content automatically. Document deletion in the library is picked up on the next indexer run, and the corresponding search document is removed from the index.
- Extracts text and normalized images from indexed documents automatically. Optionally, you can add a skillset for deeper AI enrichment, such as OCR or entity recognition.
- Supports document **basic access control lists (ACL) ingestion** in preview during initial document sync. It also supports full data set incremental data sync.
- Supports **Microsoft Purview sensitivity label ingestion and honoring at query time** (preview).

## Prerequisites

- Azure AI Search, Basic pricing tier or higher.
- SharePoint in Microsoft 365 cloud service (OneDrive isn't a supported data source).
- Files in a document library.
- Visual Studio Code with the REST Client extension for setting up and running the indexer pipeline.

## Supported document formats

CSV, EML, EPUB, GZ, HTML, JSON, KML, Markdown, Microsoft Office formats (DOCX/DOC/DOCM, XLSX/XLS/XLSM, PPTX/PPT/PPTM, MSG, XML), Open Document formats (ODT, ODS, ODP), PDF, plain text, RTF, XML, ZIP.

## Limitations and considerations

- The indexer can index content from supported document formats in a document library. There's no indexer support for SharePoint lists, .ASPX site content, or OneNote notebook files. Furthermore, indexing sub-sites recursively from a specific site isn't supported.
- Incremental indexing limitations:
    - Renaming a SharePoint folder breaks incremental indexing. A renamed folder is treated as new content.
    - Microsoft 365 processes that update SharePoint file system metadata can trigger incremental indexing, even if there are no other changes to content.
- Security limitations:
    - No support for private endpoints. Secure network configuration must be enabled via a firewall.
    - No support for tenants with Microsoft Entra ID Conditional Access enabled.
    - No support for user-encrypted files and password-protected ZIP files. However, encrypted content is allowed if it's protected by Microsoft Purview sensitivity labels and if the configuration to preserve and honor those labels (preview) is enabled.
    - Limited support for document-level access permissions. A basic level of ACL sync is currently in preview.

Considerations:

- To build a custom Copilot or RAG app that interacts with SharePoint data using Azure AI Search, Microsoft recommends using the **remote SharePoint knowledge source**. This knowledge source uses the Copilot Retrieval API to query textual content directly from SharePoint in Microsoft 365, returning results to the agentic retrieval engine for merging, ranking, and response formulation. There's no search index used by this knowledge source, and only textual content is queried. Azure AI Search doesn't replicate data. It enforces the SharePoint permission model by returning only the results that each user is authorized to see.
- If you need to create a custom Copilot/RAG application or AI agent to chat with SharePoint data in production environments, consider first building it directly via Microsoft Copilot Studio.

## Configure the SharePoint in Microsoft 365 indexer

To set up the SharePoint in Microsoft 365 indexer, use a preview REST API.

### Step 1 (Optional): Enable a system-assigned managed identity

Enable a system-assigned managed identity to automatically detect the tenant in which the search service is provisioned.

### Step 2: Decide which permissions the indexer requires

The SharePoint in Microsoft 365 indexer supports both **delegated and application** permissions.

- **Application permissions (recommended)**, where the indexer runs under the identity of the SharePoint tenant with access to all sites and files. The indexer requires a client secret. The indexer also requires tenant admin approval before it can index content. This permission type is the only one that supports basic ACL preservation (preview) configuration. Delegated permissions can't be used for ACL sync.
- **Delegated permissions**, where the indexer runs under the identity of the user or app sending the request. Data access is limited to the sites and files to which the caller has access. To support delegated permissions, the indexer requires a device code prompt to sign in on behalf of the user. User-delegated permissions enforce token expiration every 75 minutes. This configuration is only recommended for small testing operations.

### Step 3: Create a Microsoft Entra application registration

Create the application registration in the same tenant as Azure AI Search.

1. Sign in to the Azure portal.
2. Search for or navigate to Microsoft Entra ID, then select Add > App registration.
3. Select + New registration:
    - Enter a name for your app.
    - Select **Single tenant**.
    - Skip the URI designation step. No redirect URI required.
    - Select Register.
4. On the navigation pane under Manage, select **API permissions**, then **Add a permission**, then **Microsoft Graph**.
    - If your indexer uses application API permissions, choose **Application** permissions:
        - For standard indexing: `Files.Read.All`, `Sites.Read.All`.
        - If you're enabling content indexing and basic ACL sync (preview): `Files.Read.All`, `Sites.FullControl.All`.
        - If you need to limit ACL sync to specific sites: `Sites.Selected` (then grant the application full control only for those selected sites).
    - If the indexer is using delegated API permissions, select Delegated permissions and then select `Delegated - Files.Read.All`, `Delegated - Sites.Read.All`, and `Delegated - User.Read`.
5. Give admin consent. Tenant admin consent is required when using application API permissions.
6. Select the Authentication tab.
7. Set **Allow public client flows** to Yes, then select Save.
8. Select + Add a platform, then Mobile and desktop applications, then check `https://login.microsoftonline.com/common/oauth2/nativeclient`, then Configure.

#### Authentication for application API permissions

To authenticate the Microsoft Entra application with application permissions, the indexer uses either a **client secret** or a **secretless configuration** (managed identity with federated credential).

### Step 4: Create data source

A data source specifies which data to index, credentials, and policies to efficiently identify changes in the data. Multiple indexers in the same search service can use the same data source.

Required properties:

- **name**: unique name within your search service.
- **type**: must be `"sharepoint"` (case-sensitive).
- **credentials**: SharePoint endpoint and authentication method.
- **container**: which document library to index.

```http
POST https://[service name].search.windows.net/datasources?api-version=2025-11-01-preview
Content-Type: application/json
api-key: [admin key]

{
    "name" : "sharepoint-datasource",
    "type" : "sharepoint",
    "credentials" : { "connectionString" : "[connection-string]" },
    "container" : { "name" : "defaultSiteLibrary", "query" : null }
}
```

For user-assigned managed identity, add an `"identity"` block:

```http
{
    "name" : "sharepoint-datasource",
    "type" : "sharepoint",
    "credentials" : { "connectionString" : "[connection-string]" },
    "container" : { "name" : "defaultSiteLibrary", "query" : null },
    "identity": {
      "@odata.type": "#Microsoft.Azure.Search.DataUserAssignedIdentity",
      "userAssignedIdentity": "/subscriptions/[Azure subscription ID]/resourceGroups/[resource-group]/providers/Microsoft.ManagedIdentity/userAssignedIdentities/[user-assigned managed identity]"
    }
}
```

#### Connection string format

- **Delegated API permissions**: `SharePointOnlineEndpoint=[SharePoint site url];ApplicationId=[Azure AD App ID];TenantId=[SharePoint site tenant id]`
- **Application API permissions with application secret**: `SharePointOnlineEndpoint=[SharePoint site url];ApplicationId=[Azure AD App ID];ApplicationSecret=[Azure AD App client secret];TenantId=[SharePoint site tenant id]`
- **Application API permissions with secretless (managed identity)**: `SharePointOnlineEndpoint=[SharePoint site url];ApplicationId=[Azure AD App ID];FederatedCredentialObjectId=[selected managed identity object (principal) ID];TenantId=[SharePoint site tenant id]`

### Step 5: Create an index

```http
POST https://[service name].search.windows.net/indexes?api-version=2025-11-01-preview
Content-Type: application/json
api-key: [admin key]

{
    "name" : "sharepoint-index",
    "fields": [
        { "name": "id", "type": "Edm.String", "key": true, "searchable": false },
        { "name": "metadata_spo_item_name", "type": "Edm.String", "searchable": true },
        { "name": "metadata_spo_item_path", "type": "Edm.String" },
        { "name": "metadata_spo_item_content_type", "type": "Edm.String", "filterable": true, "facetable": true },
        { "name": "metadata_spo_item_last_modified", "type": "Edm.DateTimeOffset", "sortable": true },
        { "name": "metadata_spo_item_size", "type": "Edm.Int64" },
        { "name": "content", "type": "Edm.String", "searchable": true }
    ]
}
```

> Important: Only `metadata_spo_site_library_item_id` may be used as the key field in an index populated by the SharePoint in Microsoft 365 indexer.

### Step 6: Create an indexer

```http
POST https://[service name].search.windows.net/indexers?api-version=2025-11-01-preview
Content-Type: application/json
api-key: [admin key]

{
    "name" : "sharepoint-indexer",
    "dataSourceName" : "sharepoint-datasource",
    "targetIndexName" : "sharepoint-index",
    "parameters": {
        "configuration": {
            "indexedFileNameExtensions" : ".pdf, .docx",
            "excludedFileNameExtensions" : ".png, .jpg",
            "dataToExtract": "contentAndMetadata"
        }
    },
    "schedule" : { },
    "fieldMappings" : [
        { "sourceFieldName" : "metadata_spo_site_library_item_id",
          "targetFieldName" : "id",
          "mappingFunction" : { "name" : "base64Encode" }
        }
    ]
}
```

For delegated permissions, the initial request returns a `transientFailure` containing a device login URL and code. Visit `https://microsoft.com/devicelogin` and enter the code within 10 minutes.

### Step 7: Check the indexer status

```http
GET https://[service name].search.windows.net/indexers/sharepoint-indexer/status?api-version=2025-11-01-preview
```

## Indexing document metadata

If you're indexing document metadata (`"dataToExtract": "contentAndMetadata"`), the following metadata is available to index:

| Identifier | Type | Description |
| --- | --- | --- |
| metadata_spo_site_library_item_id | Edm.String | Combination key uniquely identifying an item in a document library. |
| metadata_spo_site_id | Edm.String | SharePoint site ID. |
| metadata_spo_library_id | Edm.String | Document library ID. |
| metadata_spo_item_id | Edm.String | Item ID in the library. |
| metadata_spo_item_last_modified | Edm.DateTimeOffset | Last modified UTC date/time. |
| metadata_spo_item_name | Edm.String | Name of the item. |
| metadata_spo_item_size | Edm.Int64 | Size in bytes. |
| metadata_spo_item_content_type | Edm.String | Content type. |
| metadata_spo_item_extension | Edm.String | Extension. |
| metadata_spo_item_weburi | Edm.String | URI of the item. |
| metadata_spo_item_path | Edm.String | Combination of parent path and item name. |

## Include or exclude by file type

```http
{
    "parameters" : {
        "configuration" : {
            "indexedFileNameExtensions" : ".pdf, .docx",
            "excludedFileNameExtensions" : ".png, .jpeg"
        }
    }
}
```

## Controlling which documents are indexed

The `"container"` section has `"name"` and `"query"` properties.

`name` must be one of:

- `defaultSiteLibrary`: Index all content from the site's default document library.
- `allSiteLibraries`: Index all content from all document libraries in a site.
- `useQuery`: Only index the content defined in `query`.

`query` keywords:

- `includeLibrariesInSite`: Index content from all libraries under the specified site.
- `includeLibrary`: Index all content from this library.
- `excludeLibrary`: Don't index content from this library.
- `includeFolder`: Index content from a specific folder and its subfolders (recursive).
- `excludeFolder`: Don't index content from a specific folder and its subfolders.
- `additionalColumns`: Index custom columns from the document library (comma-separated, use `\\,` to escape).

## Handling errors

To continue indexing when an unsupported content type is encountered:

```http
"parameters" : { "configuration" : { "failOnUnsupportedContentType" : false } }
```

To ignore unprocessable documents:

```http
"parameters" : { "configuration" : { "failOnUnprocessableDocument" : false } }
```

To index storage metadata only for oversized documents:

```http
"parameters" : { "configuration" : { "indexStorageMetadataOnlyForOversizedDocuments" : true } }
```

Tolerate a number of errors:

```http
"parameters" : { "maxFailedItems" : 10, "maxFailedItemsPerBatch" : 10 }
```
