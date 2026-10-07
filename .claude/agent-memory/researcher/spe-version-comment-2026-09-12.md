---
name: spe-version-comment-2026-09-12
description: Does SharePoint Embedded support a READABLE version comment on a new file version? Verdict WRITE-ONLY via checkout/PUT/checkin; no Graph read-back (CSDL-verified v1.0+beta). For Word add-in r1 save path (ReplaceFileContentAsUserAsync).
metadata:
  type: project
---

# SPE version comment (2026-09-12)

**Question**: Can the BFF attach a readable comment to the new SPE version it writes via
`PUT /drives/{d}/items/{i}/content` (OBO)? Owner rule: if yes, add it to upload; if no, job record is enough for r1.

**Verdict**: NO for "readable version comment" (documented). WRITE-ONLY via check-in.
- `checkin` action takes `comment` ("A check-in comment that is associated with the version") + `checkInAs`; SPE-supported
  (Graph ref page has the SPE FileStorageContainer.Selected note; the SPE Power Platform connector explicitly lists
  "Check in file ... in a SharePoint Embedded container" with the comment param).
- READ-BACK: none. Graph CSDL (`/$metadata`, v1.0 AND beta, pulled 2026-09-12): `baseItemVersion` = lastModifiedBy,
  lastModifiedDateTime, publication; `driveItemVersion` adds content,size; `listItemVersion` adds nav `fields`;
  `publicationFacet` = checkedOutBy, level, versionId. No property name matching *checkin*comment* anywhere in either schema.
- `_CheckinComment` via `listItem/versions?$expand=fields` = undocumented; community report says it returns the SAME value
  across versions (item-level, not per-version). Unverified for SPE.
- PUT content / upload session carry no comment (`driveItemUploadableProperties.description` = OneDrive Personal only).
- No require-checkout / minor-version setting on SPE container or container-type settings in Graph CSDL
  (container: isItemVersioningEnabled, itemMajorVersionLimit, isOcrEnabled [+ beta itemDefaultSensitivityLabelId]).
  The connector's "Update container" lists `itemMinorVersionLimit` and checkInAs "published or minor" — NOT in Graph CSDL; treat as connector-only/unverified.

**Repo facts found (2026-09-12)**: `DocumentCheckoutService` checkout/checkin is Dataverse-only (sprk_fileversion statuscode + dates);
it never calls Graph checkout/checkin. `CheckInAsync(comment)` passes comment to `UpdateFileVersionCheckInAsync`, which DROPS it
(payload = sprk_checkedindate + statuscode only); comment only echoed in the response `VersionComment`.

**Sources**: graph/api/driveitem-checkin (v1.0+beta); graph/api/resources/driveitemversion (v1.0+beta); resources/listitemversion;
resources/publicationfacet; driveitem-list-versions ("OneDrive doesn't preserve the complete metadata for previous versions");
listitem-list-versions (/sites paths only); driveitem-put-content (app-only can't replace sensitivity-labelled content);
connectors/sharepointembedded; sharepoint/dev/embedded/build/container-metadata; build/configure-authentication-authorization
(ManageContent = WriteContent + app-only discard checkout); graph.microsoft.com/{v1.0,beta}/$metadata.

**Open questions**: live probe — does checkout→PUT→checkin on an SPE item succeed OBO, and does `_CheckinComment` on
`/drives/{d}/items/{i}/listItem/versions/{v}?$expand=fields` vary per version? Does a fields PATCH after PUT mint an extra version?
Related: [[spe-wopi-coauthoring-lock-423-2026-07-30]] (checkout/co-auth 423), [[spe-dedup-content-identity-2026-07]] (versions API, custom columns).
