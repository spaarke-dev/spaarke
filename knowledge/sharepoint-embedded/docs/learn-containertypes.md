---
source: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/containertypes
fetched: 2026-05-14
note: Supplemental — captured to fill the gap left by the 404 on the containers/ concept URL listed in the directive.
---

# Create new SharePoint Embedded container types | Microsoft Learn

A container type is a SharePoint Embedded resource that defines the relationship, access privileges, and billing accountability between a SharePoint Embedded application and a set of containers. Also, the container type defines behaviors on the set of containers.

Each container type is strongly coupled with one SharePoint Embedded application, which is referred to as the owning application. The owning application developer is responsible for creating and managing their container types. SharePoint Embedded mandates a 1:1 relationship between the owning application and a container type.

A container type is represented on each container instance as an immutable property (ContainerTypeID) and is used across the entire SharePoint Embedded ecosystem, including:

- **Access authorization**: A SharePoint Embedded application must be associated with a container type to get access to container instances of that type. Once associated, the application has access to all container instances of that type. The actual access privilege is determined by the application-ContainerTypeID permission setting. The owning application by default has full access privilege to all container instances of the container type it's strongly coupled with.
- **Easy exploration**: Container types can be created for trial purposes, allowing developers to explore SharePoint Embedded application development and assess its features for free.
- **Billing**: Container types for nontrial purposes are billable and must be created with an Azure Subscription.
- **Configurable behaviors**: Container type defines selected behaviors for all container instances of that type.

> **Notes**:
> 1. You must specify the purpose of the container type you're creating at creation time. A container type set for trial purposes can't be converted for production; or vice versa.
> 2. Standard and passthrough container types can't be converted once created. If you want to convert a standard container type to passthrough billing or vice versa, you must delete and re-create the container type.

## Tenant requirements

- An active instance of SharePoint is required in your Microsoft 365 tenant.
- Users who authenticate into SharePoint Embedded container types and containers must be in Microsoft Entra ID (Members and Guests)
- A Microsoft Entra ID app registration needs to be configured for container type management.

## Creating container types

SharePoint Embedded has two different container types you can create.

1. Trial container type. Uses the `trial` billing classification.
2. Standard container type. Uses the `standard` or `directToCustomer` billing classification.

To create a container type, your Microsoft Entra ID application needs to have the `FileStorageContainerType.Manage.All` application permission on the owning tenant. Your Microsoft Entra ID application needs to call the [Create fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes) endpoint on behalf of a SharePoint Embedded Administrator:

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypes
Content-Type: application/json

{
  "name": "{ContainerTypeName}",
  "owningAppId": "{ApplicationId}",
  "billingClassification": "{BillingClassification}",
  "settings": {
    ...
  }
}
```

Replace:

- `{ContainerTypeName}` with a user-friendly name.
- `{ApplicationId}` with the ID of your application.
- `{BillingClassification}` with either `trial`, `standard`, or `directToCustomer`.

## Trial container type

A container type can be created for trial/development purposes and isn't linked to any Azure billing profile. For trial container types, the developer tenant is the same as the consuming tenant. Each developer can have only one container type with `trial` billing classification in their tenant at a time. The trial container type is valid for up to 30 days but can be removed at any time within this period.

You can easily set up a trial container type using the [SharePoint Embedded Visual Studio Code extension](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/spembedded-for-vscode).

Restrictions applied to trial container types:

- The tenant can have up to five containers of the container type.
- Each container has up to 1 GB of storage space.
- The container type expires after 30 days.
- The developer must permanently delete all containers of an existing container type in trial status to create a new container type for trial.
- The container type is restricted to work in the developer tenant. It can't be deployed in other consuming tenants.

## Standard container types (nontrial)

A standard container type can be used in production environments. Each tenant can have 25 container types at a time.

### Billing models

- **Standard billing** — All consumption-based charges are directly billed to the tenant who owns or develops the application. The admin in the developer tenant must establish a valid billing profile when creating a standard container type.
- **Passthrough billing** (`directToCustomer`) — Consumption-based charges are billed directly to the tenant registered to use the SharePoint Embedded application (consuming tenant). Admins in the developer tenant don't need to set up an Azure billing profile.

### Set the billing profile (standard)

For standard billing container types, the developer tenant Global Administrator needs to:

- Create an Azure subscription in their tenancy
- Create a resource group attached to the Azure subscription

After creating the container type with `standard` billing classification, attach a billing profile to the container type using SharePoint Online Management Shell:

```powershell
Add-SPOContainerTypeBilling -ContainerTypeId <ContainerTypeId> -AzureSubscriptionId <AzureSubscriptionId> -ResourceGroup <ResourceGroup> -Region <Region>
```

Every container type must have an owning application. A single owning app can only own one container type at a time. An Azure subscription can be attached to any number of container types.

## Configuring container types

The Developer Admin may apply configuration when calling the [Create fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertypes) endpoint. Alternatively, they may call the [Update fileStorageContainerType](https://learn.microsoft.com/en-us/graph/api/filestoragecontainertype-update) endpoint to reconfigure an existing container type.

> **Important**: Updating settings on a container type may take up to **24 hours** for the new values to be replicated on all consuming tenants. If a consuming tenant applied overrides on container type settings, the new values aren't applied and the overrides remain in place.

## Registering container types

To create and interact with containers, you must register the container type within the Consuming Tenant. The owning application defines the permissions for the container type by invoking the [Create fileStorageContainerTypeRegistration](https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertyperegistrations) endpoint.

## Deleting container types

The Developer Admin can only delete trial container types in their tenant. Deletion of standard container types is not yet supported. To delete a container type, you must first remove all containers of that container type, including from the deleted container collection.
