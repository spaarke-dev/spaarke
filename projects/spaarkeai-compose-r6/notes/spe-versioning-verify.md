# SPE Versioning Verify + Documents Version-API Inventory (Task 002)

> Phase 0 gate (spec Assumptions + Dependencies/Prerequisites). Underwrites the render-on-save
> "version history is the safety net" claim (ADR-049 Path-B amendment) and de-risks FR-07 before
> any endpoint is built. **Code-evidence inventory only** — see "Human/Live Verification Gate"
> below for what remains to be confirmed against a deployed environment.

🔒 RIGOR LEVEL: STANDARD
📋 REASON: Verification + inventory producing a notes artifact only; no production code change (root §8 table).

---

## 1. Verdict: is SPE versioning append-only? — YES, by code evidence

A Compose save never issues a delete or destructive overwrite call against SPE. Every save path
that persists to an **existing** drive item calls `ReplaceFileContentAsUserAsync`, which is
documented and implemented as a Graph `PUT .../content` — the exact Graph semantics that mint a
**new version** and retain prior ones, never an in-place byte overwrite.

### Save-path trace

1. **`ComposeService.SaveAsync`** — `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:642`
   Entry point. Resolves the baseline, applies the operation log (surgical patch — pre-render-on-save
   architecture, still current as of task 002), then persists.

2. **Persist — existing item (normal save)** —
   `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:1030`
   ```csharp
   var replaced = await _spe.ReplaceFileContentAsUserAsync(
           httpContext, request.DriveId, request.DocumentSpeId!, contentStream, cancellationToken)
       .ConfigureAwait(false);
   ```

3. **Persist — transient-key dedup replace-in-place** —
   `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:963`
   (same primitive, different caller — the create-on-save dedup branch that reuses an existing
   SPE item instead of minting a new one)

4. **Facade** — `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs:239`
   (`ReplaceFileContentAsUserAsync` delegates to `_uploadManager.ReplaceFileContentAsUserAsync`)

5. **Implementation + Graph call** —
   `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs:368-428`
   - Doc comment (`:368-372`), **explicit and load-bearing**:
     > "Replace the content of an existing drive-item by itemId (OBO flow). PUTs the stream to the
     > drive-item's `/content` endpoint, **committing a new SPE version**. Used by document editors
     > (Compose R1) that saved content back to an item they had already opened."
   - Actual Graph call (`:399-406`):
     ```csharp
     var saved = await graphClient.Drives[driveId].Items[itemId].Content
         .PutAsync(content, requestConfiguration => { ... If-Match ... }, cancellationToken: ct);
     ```

### Why this is append-only, not destructive, by Graph/SPE contract

- The call targets the driveItem's `/content` **stream resource**, not a delete + recreate. Graph's
  documented behavior for `PUT /drives/{id}/items/{id}/content` on a versioning-enabled
  drive/container is to **create a new version** of the item and retain the previous version(s) —
  this is standard SharePoint/OneDrive/SPE versioning semantics (the same mechanism `GetFileVersionsAsync`
  below reads back from).
- There is no call anywhere on the Compose save path to a version-delete, version-purge, or
  destructive-replace Graph endpoint. Grep across `Services/Compose/**` and
  `Infrastructure/Graph/**` for delete/purge-shaped calls on `/versions` found none reachable
  from `SaveAsync`.
- The optional `If-Match` header (`UploadSessionManager.cs:397-406`, FR-24/Spike 7 G-1) is an
  **optimistic-concurrency precondition** on the current version, not a version-pruning
  mechanism — a stale `If-Match` causes the PUT to 412/fail closed (see `EtagPreconditionFailedException`,
  `UploadSessionManager.cs:306-317` for the analogous create-path 412 mapping), it does not cause data loss.

**Caveat (not a contradiction of the verdict, but load-bearing for FR-07):** whether SPE actually
*performs* this versioning is a **per-container-type, admin-configurable setting**, not a hardcoded
platform guarantee — see §4 below. The code path is append-only *when versioning is enabled for the
container type in use*; Compose does not itself force-enable it on save.

---

## 2. Documents Version-API Inventory

| # | API / Primitive | File:Line | Auth Path | Scope | List or Download | Exposed to end users today? |
|---|---|---|---|---|---|---|
| 1 | `GET /api/spe/containers/{id}/items/{itemId}/versions` (endpoint registration) | `src/server/api/Sprk.Bff.Api/Api/ContainerItemEndpoints.cs:48` | **App-only** (config-scoped Graph client via `GetClientForConfigAsync`, client secret from Key Vault) | Admin / config-scoped (any item in the configured container type, not user-permission-checked at the item level) | **LIST** | Yes — but as an **admin/SPE-admin surface**, not a user-facing Documents feature |
| 2 | `GetFileVersionsAsync` (Graph call) | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs:1876-1924` | App-only (same client as #1) | Admin / config-scoped | **LIST** (returns `SpeFileVersionSummary[]`: id, lastModifiedDateTime, lastModifiedBy, size — oldest-first) | Backs #1 |
| 3 | `GetFileVersionsForConfigAsync` (config-resolving wrapper) | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs:1420-1426` | App-only | Admin / config-scoped | **LIST** | Backs #1 via `ContainerItemEndpoints.GetFileVersions` handler (`:321-351`) |
| 4 | `DownloadFileVersionAsUserAsync` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs:842-904` | **OBO** (user token exchange via `_factory.ForUserAsync(ctx, ct)`) | User-document scoped (the caller's own delegated permissions on the item) | **DOWNLOAD** (specific version's `/versions/{versionId}/content`, returns `Stream?`) | **No** — Compose-internal only; consumed by `ComposeService.ResolveSaveBaselineAsync` (task 002/FR-06 "re-fetch load-time SPE baseline" path), not exposed via any public endpoint |
| 5 | `GetCurrentVersionIdAsUserAsync` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs:906-959` | **OBO** | User-document scoped | **LIST** (fetches full `/versions` collection, picks newest by `LastModifiedDateTime`, returns just the current version id) | **No** — Compose-internal; used to stamp `BaselineVersionId`, not exposed as a general "list versions for me" call |

### Reading the table for FR-07

- The **only** version-*list* surface that runs on the **OBO** path today is #5
  (`GetCurrentVersionIdAsUserAsync`), and it is deliberately narrow — it returns one id (the
  current version), not the full chronological list a "Documents version-history" UX needs.
- The **only** full version-list surface (#1/#2/#3) is **app-only + config-scoped**, i.e. it
  authenticates as the SPE-admin app registration, not as the calling user. It is unsuitable for
  a user-facing "list my document's versions" feature as-is: it does not check the calling user's
  permission on the specific item (permission checking today happens implicitly by putting this
  behind `RequireAuthorization()` + the SpeAdmin surface's own access model, not per-item OBO
  delegation), and it authenticates as an app, not the user.
- The **download-a-specific-version** primitive FR-07 needs already exists on the **correct**
  (OBO) auth path — #4, `DownloadFileVersionAsUserAsync` — but is Compose-internal and unexposed
  through any endpoint.

---

## 3. What FR-07 must ADD vs REUSE

| Capability FR-07 needs | Status | Action |
|---|---|---|
| **List a document's versions, OBO (user-permission-scoped)** | **MISSING.** No existing primitive does this. #5 (`GetCurrentVersionIdAsUserAsync`) fetches the full `/versions` collection via OBO internally but only returns the single current-version id; it is not shaped as a list API and is not exposed. #1-3 list all versions but are app-only/admin-scoped, not OBO. | **ADD**: a new OBO version-list primitive (straightforward — same Graph call shape as `GetCurrentVersionIdAsUserAsync`, but return the full mapped `SpeFileVersionSummary[]` instead of just the newest id) + a new endpoint exposing it, in the `Api/ContainerItemEndpoints.cs` / `DocumentsEndpoints.cs` surface per spec Affected Areas. |
| **Open/download a specific prior version, OBO, exact bytes** | **EXISTS, unexposed.** `DownloadFileVersionAsUserAsync` (`DriveItemOperations.cs:842`) already does exactly this — OBO auth, targets `/versions/{versionId}/content`, returns the raw `Stream?`. | **REUSE**: wire this existing primitive to a new endpoint (no new Graph-calling code needed for the "open" half). |
| **Restore / branch-from a prior version** | Out of scope for R6 per spec (FR-07 acceptance + Owner Clarifications: "Open/view prior versions (read-only)... restore & branch-from are explicitly out of scope (fast-follow)"). | **NOT BUILT** — confirmed out of scope, no gap to close for R6. |
| **App-only admin version-list endpoint** (`ContainerItemEndpoints.cs:48`) | Pre-existing, unrelated surface (SpeAdmin tooling). | **NO CHANGE** — stays as the admin surface; FR-07's new OBO endpoint is additive, not a replacement. |

**Net new surface for FR-07** = one new OBO list-versions primitive (small — mirrors the existing
`GetCurrentVersionIdAsUserAsync` Graph call shape) + one new OBO list endpoint + one new OBO
open-prior-version endpoint (thin wrapper over the already-existing `DownloadFileVersionAsUserAsync`)
+ the Documents-surface client affordance (`AllDocuments/src/App.tsx` or a new entry point, per
spec §11 New Components table).

---

## 4. SPE Container Versioning Enablement — Config Evidence

Versioning is a **per-container-type, admin-configurable Graph/SPE setting**, exposed through
Spaarke's own container-type-settings admin surface:

- **Request DTO**: `src/server/api/Sprk.Bff.Api/Models/SpeAdmin/UpdateContainerTypeSettingsRequest.cs:34-48`
  ```csharp
  /// Whether versioning is enabled for files in containers of this type.
  /// When true, SharePoint Embedded retains previous versions of modified files.
  /// Null means "do not change the current versioning setting".
  [JsonPropertyName("isVersioningEnabled")]
  public bool? IsVersioningEnabled { get; init; }

  /// Maximum number of major versions to retain for each file.
  /// Only relevant when IsVersioningEnabled is true.
  [JsonPropertyName("majorVersionLimit")]
  public int? MajorVersionLimit { get; init; }
  ```
- **Response/config DTOs** carry the resolved state as `isItemVersioningEnabled` /
  `itemMajorVersionLimit` (`Models/SpeAdmin/ConfigDtos.cs:120-124`, `:208-212`, `:271-275`), backed
  by Dataverse fields `sprk_itemversioningenabled` / `sprk_itemmajorversionlimit`
  (`ConfigDtos.cs:342-346`).
- **Applied to Graph** via `UpdateContainerTypeSettingsForConfigAsync`, invoked from
  `PUT /api/spe/containertypes/{typeId}/settings`
  (`src/server/api/Sprk.Bff.Api/Endpoints/SpeAdmin/ContainerTypeSettingsEndpoints.cs:154-161`),
  which forwards `request.IsVersioningEnabled` + `request.MajorVersionLimit` straight through to
  the Graph container-type settings call.

**Implication**: the append-only verdict in §1 holds **conditionally** on the container type in
use having `isVersioningEnabled = true` (with a non-trivial `majorVersionLimit`). This is an
admin/tenant-provisioning fact, not something the Compose save path enforces or can verify at
save time. Nothing in the Compose save path checks this setting before calling
`ReplaceFileContentAsUserAsync` — if an operator ever flips a container type's versioning off,
saves would keep succeeding (Graph still accepts the PUT) but the safety-net premise (prior
versions retrievable) would silently stop holding for that container type. This is not a
code-level risk for R6 (out of scope to add a runtime check) but is worth flagging as an
operational assumption: **the container type(s) backing Compose documents in each environment
must have versioning enabled with an adequate major-version limit.**

---

## 5. Human / Live-Verification Gate (NOT performed by this task — by design)

Per task instructions, this task does **not** attempt to authenticate to or hit any live
Azure/SPE environment. The following remains an explicit **human verify step** before FR-07 is
built, and this report is written so that check is a formality, not an investigation:

- [ ] **Live confirmation**: in the actual deployed Documents surface / SPE container backing
  Compose, save a document twice (producing v3 → v4), then open v3 via the existing app-only
  `GET /api/spe/containers/{id}/items/{itemId}/versions` endpoint (or Graph Explorer) and confirm
  its bytes are unchanged/intact after v4 exists — i.e. exercise exactly the "open v3 after v4"
  scenario from spec FR-07's acceptance criterion, using the already-existing admin list endpoint
  + a manual download, since no OBO list endpoint exists yet to do this end-to-end as a normal user.
- [ ] **Confirm the container type(s) actually used by Compose documents** have
  `isVersioningEnabled = true` with a `majorVersionLimit` large enough not to silently prune the
  version the safety net depends on (see §4) — this is a config/data check, not a code check.

Everything else in this report (the append-only code trace, the API inventory, the FR-07
ADD-vs-REUSE table) is evidence-complete from code and does not require live access.

---

## Acceptance Criteria Check (per task 002 POML)

| Criterion | Met? | Where |
|---|---|---|
| Confirms with cited evidence that a Compose save appends a new SPE version and leaves prior versions byte-retrievable | ✅ | §1 (save-path trace + Graph PUT `/content` semantics + doc-comment citation) |
| Inventories `ContainerItemEndpoints.cs:48` and `DownloadFileVersionAsUserAsync:842` with file:line, auth path, scope | ✅ | §2 table (rows 1 and 4), plus the full supporting chain (rows 2, 3, 5) |
| States explicitly what FR-07 must ADD vs REUSE | ✅ | §3 table |
| Negative: no production code, endpoint, or DI registration modified; only the notes file is written | ✅ | This task performed Read/Grep only; no Edit/Write calls against source |

---

*Written by task-execute (STANDARD rigor) for spaarkeai-compose-r6 task 002. No production code
touched. Sole write: this file.*
