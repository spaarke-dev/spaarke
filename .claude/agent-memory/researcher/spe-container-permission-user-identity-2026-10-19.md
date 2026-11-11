---
name: spe-container-permission-user-identity
description: Which fields carry user identity on SPE container permission list (grantedToV2.user id/email/UPN), guest email vs #EXT# UPN, SDK v5 Identity typing
metadata:
  type: reference
---

## 2026-10-19: SPE container permission user identity fields
**Question**: On GET /storage/fileStorage/containers/{id}/permissions, where is user identity (oid, UPN, email), what for B2B guests, and are email/UPN typed in Graph C# SDK v5?
**Findings**: Only documented example (filestoragecontainer-list-permissions, v1.0) shows `grantedToV2.user` = {id, userPrincipalName, displayName, email}; id is a GUID (consistent with Entra oid; docs never say "oid"). Declared type is sharePointIdentitySet whose `user` is plain `identity` (displayName,id,tenantId,thumbnails only) so email/UPN are NOT typed: in SDK v5 `Identity` has only DisplayName/Id/OdataType/AdditionalData/BackingStore -> email/UPN land in `AdditionalData` (SDK source verified). `grantedTo` is deprecated (docs: migrating to V2); container example shows only grantedToV2. Guest email-vs-UPN: UNDOCUMENTED anywhere.
**Sources**: learn.microsoft.com/graph/api/filestoragecontainer-list-permissions; /resources/permission; /resources/sharepointidentityset; /resources/identity; github.com/microsoftgraph/msgraph-sdk-dotnet Models/Identity.cs
**Open questions**: guest email/UPN values need live probe; `includeAllContainerUsers=true` param exists (item-level-only identities).
