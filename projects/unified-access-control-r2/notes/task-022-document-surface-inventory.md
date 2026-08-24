# Task 022 — document-surface authorization inventory

> In progress, started 2026-08-24. Rigor FULL, `opus` @ `xhigh`, directional steps.
> **The inventory is a deliverable**, not a means: task 002 gated 4 members of this class, and the
> review estimated the class at "~15". The real count is **22 routes across 4 files**.

---

## The mechanism

`AddDocumentAuthorizationFilter("<operation>")` ([`Api/Filters/DocumentAuthorizationFilter.cs`](../../../src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs)) is the extension point. It pulls the caller from `ClaimTypes.NameIdentifier`, resolves the resource from route values (`id` → `documentId` → `containerId` → `driveId` → `itemId` → `resourceId`, first match wins), and calls `AuthorizationService.AuthorizeAsync`.

Two properties worth recording because the gates depend on them:

- **It fails closed.** `OperationAccessPolicy.GetRequiredRights` **throws** on an unknown operation string, and the filter's `catch (Exception)` returns 500. So a typo'd operation denies rather than allows.
- **That is also a trap.** The file's own header records the prior incident: an unmapped operation produced `"unknown_operation"` for *every* caller — an unconditional 403 on the finance surface, the Office save path, and three document read routes. **New operation keys must be added to `OperationAccessPolicy` BEFORE a filter is attached with them.**

### Available bare-string operation keys today

`read` · `finance.read` · `finance.confirm` · `entity.associate_document`

**There is no bare `write` or `delete` key.** Every write/delete gate in this task therefore requires a new key with the per-key rationale comment this table uses (which resource is authorized, and why that specific right — the `entity.associate_document` entry is the model: `AppendTo`, not `Write`, because attaching a document to a matter does not modify the matter).

---

## The 22 routes

### ✅ Gated (4)

| Route | File | Operation | Gated by |
|---|---|---|---|
| `GET /api/documents/{documentId}/content` | FileAccessEndpoints.cs:58 | `read` | task 002 |
| `GET /api/documents/{documentId}/download` | FileAccessEndpoints.cs:125 | `read` | task 002 |
| `GET /api/documents/{documentId}/eml-render` | FileAccessEndpoints.cs:145 | `read` | email-r5 task 010 |
| `GET /api/v1/documents/{id}/download` | DataverseDocumentsEndpoints.cs:443 | `read` | pre-existing |

### 🛑 Gated in form only — no authorization decision (1)

| Route | File | Finding |
|---|---|---|
| `POST /api/documents/bulk-download` | DocumentsBulkEndpoints.cs:43 | **C1** |

`BulkDownloadAuthorizationFilter.InvokeAsync` confirmed verbatim: reads a `tid`/tenant claim (401 if absent), extracts the `BulkDownloadRequest`, logs `"Bulk download authorization granted: tenant={TenantId}, requestedCount={Count}"`, and returns `await next(context)`. **No per-document decision at any point.** 500 GUIDs per request, streamed app-only, and failures are listed in a `_FAILED.txt` manifest while zipping continues — so one call both exfiltrates and enumerates.

### 🛑 Ungated — destroy (2, both Critical)

| Route | File | Finding | Note |
|---|---|---|---|
| `DELETE /api/documents/{documentId}` | DocumentOperationsEndpoints.cs:57 | **C2** | `DeleteAsync(Guid documentId, string correlationId, CancellationToken)` — **no user identity parameter at all**. Destroys the Dataverse row and the SPE file app-only. Reachable from a shipped client hook. |
| `DELETE /api/v1/documents/{id}` | DataverseDocumentsEndpoints.cs:230 | **C3** | Second app-only destroy path, while its `/download` sibling *is* gated. |

### 🔴 Ungated — mutate / disclose (8)

| Route | File | Finding |
|---|---|---|
| `GET /api/v1/documents/{id}` | DataverseDocumentsEndpoints.cs:108 | **H2** — returns `GraphDriveId`/`GraphItemId`, the exact pointers C1 and C2 consume |
| `PUT /api/v1/documents/{id}` | DataverseDocumentsEndpoints.cs:163 | **H2** — app-only tamper by GUID |
| `POST /api/documents/{documentId}/checkout` | DocumentOperationsEndpoints.cs:30 | **H3** — returns an **editable** URL |
| `POST /api/documents/{documentId}/checkin` | DocumentOperationsEndpoints.cs:39 | **H3** |
| `POST /api/documents/{documentId}/discard` | DocumentOperationsEndpoints.cs:48 | **H3** |
| `GET /api/documents/{documentId}/checkout-status` | DocumentOperationsEndpoints.cs:66 | **H3** |
| `POST /api/documents/{documentId}/analyze` | DocumentOperationsEndpoints.cs:74 | **H3** |
| `POST /api/documents/{documentId}/share-link` | FileAccessEndpoints.cs:89 | **H4** → owned by task 012 (constraint), not gated here |

### 🟠 Ungated — URL-minting reads (4)

These mint URLs that **outlive the request**, which is why the original investigation flagged them separately (open item 6). The `preview-url` route is also the one C1's justifying comment cites as its model.

| Route | File |
|---|---|
| `GET /api/documents/{documentId}/preview-url` | FileAccessEndpoints.cs:33 |
| `GET /api/documents/{documentId}/preview` | FileAccessEndpoints.cs:44 |
| `GET /api/documents/{documentId}/office` | FileAccessEndpoints.cs:68 |
| `GET /api/documents/{documentId}/view-url` | FileAccessEndpoints.cs:100 |
| `GET /api/documents/{documentId}/open-links` | FileAccessEndpoints.cs:76 |

### ⚪ Different shape — no caller-supplied document id (3)

Not part of the per-document class; listed so the inventory is provably complete rather than silently truncated. These need collection-level scoping, which is Phase 1 evaluator work.

| Route | File | Shape |
|---|---|---|
| `POST /api/v1/documents/` | DataverseDocumentsEndpoints.cs:30 | create — no id yet |
| `GET /api/v1/documents/` | DataverseDocumentsEndpoints.cs:447 | list — needs collection scoping |
| `GET /api/v1/containers/{containerId}/documents` | DataverseDocumentsEndpoints.cs:520 | list by container |

---

## Progress

### ✅ H5 — fixed

`ExternalDataService.GetProjectsAsync` ordered the accessible-projects query by `sprk_name`, which does not exist on `sprk_project` — the display name is `sprk_projectname`, and the `$select` on the line above already had it right. Dataverse answered 400, `GetCollectionAsync` caught it and returned an empty list, so **the external SPA rendered "you have no grants" for every caller who had grants.**

This is the **sixth** instance of the stale-column class in this project. The reason it survived every prior review: the `$select` and the `$orderby` are on adjacent lines and disagree, so eyeballing the select gives a false all-clear. A repo-wide grep confirms no other `sprk_projects` query orders by `sprk_name`.

### Remaining, in order

1. **Add the missing `OperationAccessPolicy` keys** before attaching any write/delete gate — no bare `write`/`delete` key exists, and an unmapped operation is an unconditional 403 (the trap this file's header already documents).
2. **C2 + C3** — the two destroy paths. C2 needs caller identity threaded into `DocumentCheckoutService.DeleteAsync`, which currently has no identity parameter, so this is a signature change with call-site fallout.
3. **C1** — bulk. Needs a per-item decision *plus* an explicit partial-failure contract: the `_FAILED.txt` manifest currently distinguishes "denied" from "missing", which is an enumeration oracle even once per-item authorization exists.
4. **H2, H3** — mutate/disclose, per-route operations.
5. **The four URL-minting reads** — these outlive the request; decide whether `read` is the right operation or whether minting deserves its own key.
6. **The six OBO read routes** — decide whether OBO alone suffices or a record check is still required, and record the reasoning either way (POML escalation trigger if a check is needed that does not exist).
7. Tests per gated route + perturbation for each gate.
