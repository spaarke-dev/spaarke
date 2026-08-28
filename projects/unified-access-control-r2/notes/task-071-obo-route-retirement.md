# Task 071 — Retire the drive-keyed OBO file routes

> **Executed**: 2026-08-26 · **Rigor**: FULL · Phase 0c Secure Documents — Wave 1
> **Outcome**: 4 routes DELETED · 3 routes ESCALATED (kept, unchanged) · 2 sibling routes FLAGGED (out of write scope)

---

## 0. Severity framing (do not overstate)

These routes were **latent, not exploitable at HEAD**. They are OBO, so SharePoint Embedded evaluates
the caller's own container permission and denies without one — and under the broker-only decision
(`SECURE-DOCUMENTS-BUILD-PLAN.md` §1) no user is ever granted one. They were a **bypass by
construction**, not a live hole: for anyone who *did* hold a container ACL they routed around every
per-document gate task 002 added.

`DocumentAuthorizationFilter.ExtractResourceId`'s `containerId`/`driveId`/`itemId` fallback is
**inert and fail-closed** — a driveId is not an `sprk_documents` GUID, so `RetrievePrincipalAccess`
returns None and the request denies. That is *not* a finding. It is, however, exactly why the filter
could not simply be bolted onto a drive-keyed route: it cannot authorize one at all.

---

## 1. Ground-truth correction to the POML

The POML's background table enumerates **5** routes. `Api/OBOEndpoints.cs` actually maps **7**. The
two it omits are the chunked-upload pair (`POST .../upload-session`, `PUT /api/obo/upload-session/chunk`).

Two *additional* ungated drive-keyed OBO routes live in `Api/DocumentVersionEndpoints.cs`, which the
POML lists as `role="reference"` (not `modify`) — see §5.

Complete mapped `/api/obo/` surface (grep: `"/api/obo/` across `src/server/api/Sprk.Bff.Api/**/*.cs`):

| File | Route |
|---|---|
| `OBOEndpoints.cs:16` | `GET /api/obo/containers/{id}/children` |
| `OBOEndpoints.cs:55` | `PUT /api/obo/containers/{id}/files/{*path}` |
| `OBOEndpoints.cs:106` | `POST /api/obo/drives/{driveId}/upload-session` |
| `OBOEndpoints.cs:141` | `PUT /api/obo/upload-session/chunk` |
| `OBOEndpoints.cs:205` | `PATCH /api/obo/drives/{driveId}/items/{itemId}` |
| `OBOEndpoints.cs:245` | `GET /api/obo/drives/{driveId}/items/{itemId}/content` |
| `OBOEndpoints.cs:315` | `DELETE /api/obo/drives/{driveId}/items/{itemId}` |
| `DocumentVersionEndpoints.cs:44` | `GET /api/obo/drives/{driveId}/items/{itemId}/versions` |
| `DocumentVersionEndpoints.cs:84` | `GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content` |

---

## 2. Caller inventory — grep-evidenced, not asserted

### The distinction the POML conflates

**Compose does NOT call these HTTP routes.** `ComposeService.cs:1483` calls
`_spe.UploadSmallAsUserAsync(...)` — the **in-process `ISpeFileOperations` facade method**, not
`PUT /api/obo/containers/{id}/files/{*path}`. Same for `ProjectPreFillService.cs:304`,
`MatterPreFillService.cs:334`, `ChatWordExportEndpoints.cs:154`, `ChatDocumentEndpoints.cs:1157`.
Deleting a **route** cannot break a **facade** caller. Compose is unaffected by every deletion below.

`DocumentVersionEndpoints.cs` likewise does not call OBOEndpoints — it is a sibling route file that
happens to share the `/api/obo/drives/...` prefix and resolves through `ISpeFileOperations` directly.

### Per-route table

| # | Route | Production callers | Verdict |
|---|---|---|---|
| 1 | `GET /api/obo/containers/{id}/children` | **ZERO**. No `src/client/**` or `src/solutions/**` caller. Facade `ListChildrenAsUserAsync` is consumed *only* by this route (`SpeFileStore.cs:225/230` → `DriveItemOperations.cs:330` → `OBOEndpoints.cs:36`). Self-referential `nextLink` at `DriveItemOperations.cs:398`. Tests only. | **DELETE** |
| 2 | `PUT /api/obo/containers/{id}/files/{*path}` | **LIVE — 11 call sites** (see §2.1) | **ESCALATE** |
| 3 | `POST /api/obo/drives/{driveId}/upload-session` | **ZERO reachable** (see §2.2) | **ESCALATE with #2** |
| 4 | `PUT /api/obo/upload-session/chunk` | **ZERO**. `UploadOperation.uploadChunk` (`UploadOperation.ts:131`) PUTs to `session.uploadUrl` — the **Graph** URL — never this BFF route. | **ESCALATE with #2** |
| 5 | `PATCH /api/obo/drives/{driveId}/items/{itemId}` | **ZERO**. No `.ts`/`.tsx` caller anywhere. Facade `UpdateItemAsUserAsync` consumed only at `OBOEndpoints.cs:226`. | **DELETE** |
| 6 | `GET /api/obo/drives/{driveId}/items/{itemId}/content` | **ZERO invocations.** Two dead library methods target it: `SdapClient/DownloadOperation.ts:16` (via `SdapApiClient.downloadFile`) and `document-upload/SdapApiClient.ts:134` (`downloadFile`). Grep for `.downloadFile(` across `src/**/*.{ts,tsx}` → only a JSDoc example (`SdapApiClient.ts:38`) and a `toBeDefined()` existence assertion. | **DELETE** |
| 7 | `DELETE /api/obo/drives/{driveId}/items/{itemId}` | **ZERO invocations.** `SdapClient/DeleteOperation.ts:16` (0 callers) and `document-upload/SdapApiClient.ts:175`, reached only from `replaceFile` (`:207`) — and `.replaceFile(` has **0** invocations. | **DELETE** |

### 2.1 Route #2 — the live caller set (11 sites)

Via `EntityCreationService.uploadFilesToSpe` (`EntityCreationService.ts:493`):

| Caller | Line |
|---|---|
| `Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectWizard.tsx` | 712 |
| `Spaarke.UI.Components/src/components/CreateEventWizard/CreateEventWizard.tsx` | 401 |
| `Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts` | 339 |
| `Spaarke.UI.Components/src/components/CreateInvoiceWizard/invoiceService.ts` | 314 |
| `Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts` | 545 |
| `Spaarke.UI.Components/src/components/EmailComposer/createXrmEmailComposeHandlers.ts` | 255 |
| `Spaarke.AI.Widgets/src/widgets/workspace/CreateAnalysisWizardWidget.tsx` | 778 |
| `src/solutions/LegalWorkspace/src/components/CreateProject/ProjectWizardDialog.tsx` | 191 |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` | 239 |

Via the second, independent client (`document-upload/SdapApiClient.ts:101`):

| Chain | Evidence |
|---|---|
| `DocumentUploadWizard/src/services/uploadOrchestrator.ts:195` → `MultiFileUploadService.uploadFiles` (`:54`) → `FileUploadService.uploadFile` (`:54`) → `SdapApiClient.uploadFile` (`:101`) | live |

Via `Spaarke.SdapClient` (`UploadOperation.ts:27`, `uploadSmall`) — reached from
`SdapApiClient.uploadFile` (`:121`) for files < 4 MB. `EntityCreationService.ts:177` **does**
construct this client, but uses it only for `indexFile`; no production `.uploadFile(` invocation on it.

### 2.2 Why #3 and #4 are unreachable today

`SdapApiClient.uploadFile` (`Spaarke.SdapClient/src/SdapApiClient.ts:110-125`) switches to
`uploadOp.uploadChunked` at ≥ 4 MB. `uploadChunked` → `createUploadSession` (`UploadOperation.ts:94`)
first calls `GET /api/obo/containers/{containerId}/drive` (`:98`) — **a route that is not mapped
anywhere in the BFF** (grep confirms: only a comment reference in `SpeFileStoreDtos.cs:15`). The
chunked path therefore fails at its first hop before ever reaching `POST .../upload-session`. It is
dead code, and it was dead before this task.

### 2.3 Test callers (all outside `tests/Spaarke.ArchTests/`)

| File | Routes exercised |
|---|---|
| `tests/integration/contract/ListingEndpointsContractTests.cs` | #1 (9 calls) |
| `tests/integration/Spe.Integration.Tests/SystemIntegrationTests.cs:89` | #1 |
| `tests/unit/Sprk.Bff.Api.Tests/CorsAndAuthTests.cs:28` | #1 |
| `tests/unit/Sprk.Bff.Api.Tests/FileOperationsTests.cs` | #5, #6, #7 |
| `tests/unit/Sprk.Bff.Api.Tests/UploadEndpointsTests.cs` | #3, #4 (kept) |
| `src/client/shared/Spaarke.SdapClient/src/__tests__/SdapApiClient.test.ts` | #6, #7 existence assertions |

---

## 3. Per-route decisions

### DELETED — 4 routes

Routes **1, 5, 6, 7**. These **read or mutate content that already exists**, so a per-document
decision is well-defined and a gated document-id-keyed equivalent already ships:

| Deleted route | Gated equivalent that already exists |
|---|---|
| `GET .../items/{itemId}/content` | `FileAccessEndpoints.cs` — 8 routes, each `.AddDocumentAuthorizationFilter("read")` (`:57,69,83,94,103,128,153,173`) |
| `DELETE .../items/{itemId}` | `DocumentOperationsEndpoints.cs:88` — `.AddDocumentAuthorizationFilter("delete")` |
| `PATCH .../items/{itemId}` | `DocumentOperationsEndpoints.cs:43,55,71` — `.AddDocumentAuthorizationFilter("write")` |
| `GET /containers/{id}/children` | no client ever used it; enumeration for real consumers goes through the Dataverse-backed document list (`DataverseDocumentsEndpoints.cs:163` gated `"read"`) |

Deleting beats gating: a gated drive-keyed route still invites *"why not just grant the user container
access?"* — the question broker-only exists to foreclose.

Route #1 also carried an independent **error-OPEN** defect: it converted a Graph 404 into
`200 {"items":[]}` (documented at `projects/spaarke-auth-v4-dataverse-MI/notes/decisions/031-obo-verification-dev.md:331`
and `notes/lessons-learned.md:59`). Deletion closes that too.

### ESCALATED — 3 routes kept unchanged

Routes **2, 3, 4** — the OBO **upload** capability. See §4.

---

## 4. 🔔 ESCALATION — POML `<escalation><trigger>` FIRED

> *"If a shipped feature genuinely requires drive-keyed access and no document row exists for the item
> — e.g. transient or working files with no `sprk_document` — STOP. That is a modelling gap, and
> inventing an authorization answer for unmodelled content is how bypasses get built."*

**The trigger applies to the upload trio, and the match is exact.**

- **The feature is shipped and live**: 11 call sites across 7 `Create*Wizard` surfaces, the
  `EmailComposer`, and `DocumentUploadWizard`.
- **No document row exists at the moment of authorization.** The ordering is
  `uploadFilesToSpe` → *then* `createDocumentRecords`. The bytes land in SPE **before** any
  `sprk_document` is created. There is nothing for `RetrievePrincipalAccess` to answer about.
- **The authorization object is therefore the container / owning record, not a document.** That is
  precisely the seam Wave 2 is building: task **075** (record-aware container resolver) and task
  **076** (route every call site through it — the build plan's *"7 client sites"* are the same
  `uploadFilesToSpe` sites inventoried in §2.1). Task **073** independently owns the app-only twin
  `PUT /api/containers/{containerId}/files/{*path}`.

**What I did NOT do, deliberately:**

- Did **not** delete these routes — that is 11 live call sites and a production outage.
- Did **not** bolt `DocumentAuthorizationFilter` onto them — `ExtractResourceId` would return the
  **container id** for `{id}` and deny 100 % of uploads (inert fallback, §0). Every wizard breaks.
- Did **not** invent a container-scoped check — the POML's own constraint forbids it
  (*"A container-scoped check is NOT sufficient and is the thing being fixed"*), and designing the
  record-aware resolver here would pre-empt and fork task 075.
- Did **not** grant SPE container permissions. `GrantMembershipAsync` remains at **zero callers**
  (verified §6).

**Recommended split** (the honest scope boundary):

| Route | Owner |
|---|---|
| `PUT /api/obo/containers/{id}/files/{*path}` | **075 + 076** — gate against the *owning record* via the record-aware container resolver, at the same time the resolver replaces the BU cascade. Coordinate with **073**, which gates the app-only twin: the two container-upload routes should end up behind **one** decision, not two. |
| `POST /api/obo/drives/{driveId}/upload-session` + `PUT /api/obo/upload-session/chunk` | **075 + 076**, as the large-file variant of the same capability. Retiring the large-file half while keeping the small-file half would leave an inconsistent upload surface. Note these are **dead today** (§2.2) — a follow-up may simply delete them once 076 settles the upload contract, which is cheaper than gating them. |

Task **074**'s ArchTest must therefore expect a **named waiver** for these three routes, not a filter.

---

## 5. Flagged, out of write scope

`Api/DocumentVersionEndpoints.cs` maps two more **ungated drive-keyed OBO routes** (`:44`, `:84`) that
are in the **same bypass class**. Its own header comment states the architectural claim this task
rejects:

> *"Per-document authorization is enforced by SharePoint Embedded itself under the user's delegated
> permission"* (`DocumentVersionEndpoints.cs:22-26`)

Under broker-only that claim is exactly the bypass-by-construction pattern. These routes have a **live
caller** (`src/solutions/AllDocuments/src/versionHistory.ts:81` → `VersionHistoryModal`), and unlike
the upload trio they read **existing** content, so a per-document gate **is** well-defined for them —
they should be gated, not deleted.

The POML lists this file as `role="reference"`, and the parent session's write scope for this task is
`OBOEndpoints.cs` + direct callers + tests. **Not touched.** Recommend a follow-up task, or fold into 074's waiver review.

Also noted, not fixed:

- `GET /api/obo/containers/{id}/drive` — called by `UploadOperation.ts:98`, **never mapped**. Dead client call.
- `GET /api/obo/drives/{driveId}/items/{itemId}` — called by `SdapApiClient.getFileMetadata` (`:161`), **never mapped**. Dead client call.
- `ListChildrenAsUserAsync` / `UpdateItemAsUserAsync` / `DownloadFileWithRangeAsUserAsync` /
  `DeleteItemAsUserAsync` facade methods now have zero route consumers. Left in place: they sit on
  `SpeFileStore` / `DriveItemOperations` and removing them would reach into `ISpeFileOperations`,
  which dozens of Compose tests mock. Dead-code cleanup, not a bypass. Follow-up.
- `DriveItemOperations.cs:398` emits a `nextLink` pointing at the deleted `/children` route. Now
  unreachable (its only caller was that route). Follow-up with the facade cleanup.

---

## 6. Acceptance criteria

| Criterion | Status |
|---|---|
| No route in OBOEndpoints reaches SPE content keyed by drive/item without a per-document decision | ⚠️ **Partial + escalated.** All 4 read/mutate routes deleted. The 3 remaining are **create** routes where no document exists to decide about — escalated per the POML trigger rather than answered wrongly. |
| Caller inventory evidenced (grep), not asserted | ✅ §2, per-route, file:line |
| Every former caller works — Compose and document versions specifically verified | ✅ Compose calls the **facade**, not the routes (`ComposeService.cs:1483`); document versions live in a different file with its own routes, untouched. Zero production callers existed for any deleted route. |
| DELETE and PATCH covered by the same standard as the read path | ✅ Both deleted, same standard as the deleted read path |
| Resolution failure denies; no fallback to container permission | ✅ Vacuous for deleted routes. No new authorization code introduced, so no new fallback. |
| `GrantMembershipAsync` still has zero callers | ✅ verified §6 |

---

## 6a. 🔔 CROSS-TASK BREAKAGE — task 074's ArchTest, which I must not edit

Task **074**'s `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` hard-codes the registration
count of the file this task modifies:

```csharp
// RouteAuthorizationGuardTests.cs:897-900
Assert.True(
    ScanFile("Api/OBOEndpoints.cs").Count == 7,
    "Expected 7 registrations in OBOEndpoints.cs — the file where AddDocumentAuthorizationFilter "
    + "appears zero times. A drop here would make finding #2 invisible.");
```

`OBOEndpoints.cs` now has **3** registrations, so this assertion fails. Verified empirically, not
predicted:

```
Failed Task 074: the scanner reads the real governed files and finds every registration in them
  Expected 7 registrations in OBOEndpoints.cs — ...
Failed! - Failed: 1, Passed: 9, Skipped: 0, Total: 10
```

`tests/Spaarke.ArchTests/**` is outside this task's write scope (task 074 is executing concurrently),
so **I did not edit it.** The owner of 074 must apply four changes:

| # | Change | Why |
|---|---|---|
| 1 | `Count == 7` → `Count == 3` (line 898) | 4 routes deleted |
| 2 | **Delete** the 4 now-dead `Pending`/"071" waivers: `GET .../containers/{id}/children`, `PATCH .../items/{itemId}`, `GET .../items/{itemId}/content`, `DELETE .../items/{itemId}` | the routes no longer exist. `NoWaiverIsStale` does **not** catch these — it only flags a waiver whose route became *gated*, and a deleted route is never scanned. So they are dead entries that make the outstanding-work list overstate itself. Consider extending `NoWaiverIsStale` to also flag a waiver whose route is absent from the scan entirely. |
| 3 | **Re-point** the 3 surviving upload waivers (`PUT /api/obo/containers/{id}/files/{*path}`, `POST /api/obo/drives/{driveId}/upload-session`, `PUT /api/obo/upload-session/chunk`) from `OwningTask "071"` to **"075/076"** (coordinate with 073) | 071 is complete and deliberately does NOT gate them (§4). Leaving them owned by 071 makes them look like unfinished 071 work rather than Wave 2 scope. |
| 4 | Update the `GovernedFile("Api/OBOEndpoints.cs", …)` reason string | it currently reads *"drive/container-keyed read, PATCH, DELETE and enumerate — finding #2"*; all four of those are gone. The file is now an upload-only surface. |

Not affected: `Detector_NegativeControl_FiresOnEachHistoricalMiss` uses `ScanText` with synthetic
source rather than the real file, so its `GET /api/obo/drives/{driveId}/items/{itemId}/content` case
still passes and correctly preserves the historical proof. Leave it.

Also note 074's own waiver list already carries the two `DocumentVersionEndpoints` routes flagged in
§5 as `Pending`/"071". **071 did not gate them** (out of write scope) — they need a new owner.

---

## 6b. Verification results

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ **Build succeeded**, 0 errors, 7 warnings (all pre-existing `CS0618` on `DemoProvisioningOptions`, unrelated) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | ✅ succeeded, 0 errors |
| New regression tests | ✅ **5/5 pass**, incl. the positive control `SurvivingOboUploadRoute_WithoutBearer_Returns401` (proves the four 404s mean *route absent*, not *fixture 404s everything*) |
| `CorsAndAuthTests` | ✅ 1 pass + 1 pre-existing skip |
| `Spe.Integration.Tests/SystemIntegrationTests` | ✅ 6 pass + 1 pre-existing skip |
| `Spaarke.ArchTests` route guard | ⚠️ **9/10 pass, 1 fail** — task 074's hard-coded count, §6a. Not mine to fix. |
| Publish size (compressed, `deploy/api-publish/`, 215 files) | ✅ **45.05 MB incl. PDBs** / 44.14 MB excl. Build-plan baseline is 45.05 MB incl. PDBs → **0.00 MB delta**. Ceiling 60 MB. (Deleting route registrations does not move a compressed publish; expected.) |
| `dotnet list package --vulnerable --include-transitive` | ✅ no vulnerable packages, any severity, across all 6 projects. No package graph change. |
| `GrantMembershipAsync` callers | ✅ **zero** — only its own definition (`SpeContainerMembershipService.cs:59`) + 2 doc-comment mentions |
| **Full BFF suite** `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | ✅ **11084 passed / 0 failed / 82 skipped** (all 82 skips pre-existing) |

---

## 6c. Step 9.5 quality gates

### `code-review`

**Quality direction — measurably improved.**

| File | Lines | Branches | Mapped routes | Signal |
|---|---|---|---|---|
| `Api/OBOEndpoints.cs` | 356 → **224** (−37%) | 36 → **21** (−42%) | 7 → **3** | ✅ Improved |
| `SystemIntegrationTests.cs` | 193 → 201 (+8 comment lines) | 15 → 15 | — | Neutral |
| `CorsAndAuthTests.cs` | 32 → 41 (+9 comment lines) | 0 → 1 | — | Neutral |
| `OboDriveKeyedRouteRetirementTests.cs` | new, 130 | 4 | — | New file |

The 224 remaining lines include a 33-line class-doc header, so executable code shrank further than the
line count shows.

**AI code-smell scan** — 5/5 clean: no interface-with-single-impl, no try/catch-log-rethrow (surviving
catch blocks return `ProblemDetails` and are unchanged), no null-checks on non-nullable, no
code-restating comments (every added comment states *why*, incl. the "do NOT re-add" instruction), and
`MapOBOEndpoints` retains one responsibility.

**One finding, fixed during review**: the four route-absence tests were named two-part
(`{Subject}_{Expected}`) while the positive control was three-part, violating ADR-038 **B13**
(scenario+expected required) and inconsistent within one file. Renamed to
`Retired…Route_WhenRequested_Returns404NotRouted`. Re-verified: 5/5 pass.

**Two accepted-with-rationale observations** (not defects):
- The 33-line class-doc header is long for a 3-route file. Kept because the "do NOT re-add these / here
  is the gated equivalent" instruction has to sit at the point of temptation, not only in a notes file.
- The inline `// DELETED …` tombstone block duplicates git history. Kept because it carries the *remedy*
  (which gated endpoint to use instead), which `git log` does not surface to someone editing the file.

### `adr-check`

| ADR | Verdict |
|---|---|
| ADR-001 Minimal API | ✅ preserved; no controller, no `[FunctionName]` |
| ADR-003 fail-closed | ✅ vacuous for deleted routes; **no new authorization code, therefore no new fallback**. The constraint's "resolution failure denies" is satisfied by there being no resolution path to fail. |
| ADR-007 Graph isolation | ✅ no `Microsoft.Graph` type anywhere in scope |
| ADR-008 endpoint-filter auth | ⚠️ **Warning, documented exception (§6.5 Path A)** — see below |
| ADR-009 Redis-first | ✅ no `IMemoryCache` |
| ADR-010 DI minimalism | ✅ no new interface, no new registration |
| ADR-013 AI facade | ✅ no `IOpenAiClient` / `IPlaybookService` |
| ADR-028 / A4 secret-free | ✅ no `.WithClientSecret`; no auth-path change |
| ADR-038 testing | ✅ new test at protected KEEP path `tests/integration/regression/**`; negative + positive controls; names a concrete regression; no banned shape (`Mock<HttpMessageHandler>`, DI-registration, ctor-null, `Stopwatch`) |

**ADR-008 tension — CLAUDE.md §6.5 Path A (project-scoped exception), documented at the point of
decision, not deferred.** The three surviving routes carry only
`.RequireRateLimiting(...).RequireAuthorization()` — no per-resource filter. Reasoning:

- **Not introduced by this change.** Pre-existing state; those three registrations are byte-identical to
  HEAD. This task strictly *reduced* the ungated surface from 7 routes to 3.
- **Path C (comply) was considered and rejected on evidence**, not convenience: attaching
  `DocumentAuthorizationFilter` makes `ExtractResourceId` return the **container id**, which is not an
  `sprk_documents` GUID, so `RetrievePrincipalAccess` returns None and every upload denies. That breaks
  7 `Create*Wizard` surfaces + `EmailComposer` + `DocumentUploadWizard`. Complying with the letter of
  ADR-008 here produces a strictly worse outcome than a documented exception — the §6.5 anti-pattern
  *"ADR says no, so I'll write worse code to comply"*.
- **Path B (amend ADR-008) is not warranted.** ADR-008 is correct in general; the gap is that a *create*
  route has no pre-existing resource to key a per-resource filter on. That is a modelling gap in the
  container/record seam, not a defect in ADR-008.
- **Exception scope is bounded and owned**: exactly 3 routes, owned by tasks 075 + 076 (with 073),
  enumerated as named `Pending` waivers in task 074's ArchTest, and documented in the source itself
  (`OBOEndpoints.cs` class summary) so it cannot be silently forgotten.

**Verified pre-existing, not attributable to this task**: `SystemIntegrationTests.cs:187` uses
`Stopwatch` (ADR-038-banned in tests). Present at HEAD as line 179, inside the already-`[Skip]`'d
`ApiPerformance_MeetsResponseTimeRequirements`. My diff to that file is +9/−1 at line 89. Reported for
coverage per the skill's coverage-first contract; out of scope to fix here.

⚠️ **Side effect worth flagging**: the publish-size check writes to `deploy/api-publish/`, which
`.claude/constraints/azure-deployment.md` mandates as the only permitted publish location. That path
is shared with the concurrently-executing main session; my publish overwrote whatever was there. No
deploy was performed.

---

## 7. BFF hygiene (root CLAUDE.md §10)

**Placement Justification**: no new code, no new endpoint, no new service, no new DI registration, no
new package. This task **removes** 4 endpoints from `Sprk.Bff.Api`. Placement question is moot in the
additive direction; the removal keeps the BFF the single decision point by deleting surface that
routed around it.

- **Publish size**: see §8 — expected flat-to-down.
- **New HIGH CVE**: none possible; no package graph change.
- **Test obligation (§F)**: tests referencing deleted routes updated in the same change (§8).
