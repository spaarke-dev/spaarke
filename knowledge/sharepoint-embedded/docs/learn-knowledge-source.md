---
source: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/declarative-agent/sharepoint-embedded-knowledge-source
fetched: 2026-05-14
---

# Set up SharePoint Embedded as a knowledge source in Microsoft Foundry | Microsoft Learn

> **Note**: This functionality is currently in preview.

This article helps you configure [Microsoft Foundry Agent Service](https://learn.microsoft.com/en-us/azure/foundry/agents/overview) with a [SharePoint knowledge source (Preview)](https://learn.microsoft.com/en-us/azure/search/agentic-knowledge-source-how-to-sharepoint-remote) configured for SharePoint Embedded (SPE).

## Prerequisites

- You have set up an SPE app with at least one container. To get started, learn more at [SharePoint Embedded Overview](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/overview).
- You have at least one Copilot license on your tenant. This requirement is for the preview period only. Will switch to metered billing once we transition out of preview.

## Set up the SharePoint knowledge source for SharePoint Embedded

The SharePoint knowledge source must be configured with the `remoteSharePointParameters.containerTypeId` pointing to your application's container type. For more information, see the [SharePoint knowledge source properties](https://learn.microsoft.com/en-us/azure/search/agentic-knowledge-source-how-to-sharepoint-remote#source-specific-properties).

## Grant Foundry access to a container type

You also need to grant Microsoft Foundry the necessary application permission to access your container type. You can do this by updating the container type registration in your consuming tenants as shown below. Replace `{fileStorageContainerTypeId}` with your container type ID. The container type's owning application must call this API on consuming tenants.

```http
PUT /storage/fileStorage/containerTypeRegistrations/{fileStorageContainerTypeId}/applicationPermissionGrants/880da380-985e-4198-81b9-e05b1cc53158
Content-Type: application/json

{
  "delegatedPermissions": ["readContent"],
  "applicationPermissions": ["none"]
}
```

> **Tip**: This may also be done during initial container type registration using the [Create container type registration](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertyperegistrations) endpoint.

---

## GAP: Missing Foundry SharePoint knowledge source JSON example

This Learn page references the configuration at a high level only. The full `remoteSharePointParameters` JSON shape (a SharePoint knowledge source definition with `containerTypeId`) lives on the linked [agentic-knowledge-source-how-to-sharepoint-remote](https://learn.microsoft.com/en-us/azure/search/agentic-knowledge-source-how-to-sharepoint-remote) page. Curate that page on the next refresh if Foundry integration becomes a primary path.
