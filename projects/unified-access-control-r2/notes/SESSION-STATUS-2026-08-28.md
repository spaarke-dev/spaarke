# Consolidated status — 2026-08-28 session (083 · 076 · 078 · 012 + test-suite repair)

> **Purpose**: one place to review and resolve the open questions. Supersedes the scattered agent
> reports. Companion evidence: [`task-083-sink-inventory.md`](task-083-sink-inventory.md) (the manual
> sweep) and the guard itself, `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`
> (the authoritative inventory — Rule A asserts discovered-set == declared-set both directions).

---

## 1. What landed (merged to `work/unified-access-control-r2`, not yet to master)

| # | Item | State |
|---|---|---|
| 1 | **Local test suite repaired** | ✅ **Full local run: 10,907 tests, 0 failed, 4.64 min.** Root cause found. |
| 2 | **The inverted SPE write-sink guard** | ✅ 25 sites, all classified. Found a live hole no inventory had. |
| 3 | **076 server half** — 2 new record-keyed routes | ✅ Additive; safe to deploy alone. Client cutover ESCALATED. |
| 4 | **`design.md` INV-7 correction** (083 step 7) | ✅ Acceptance criterion met. |
| 5 | **012** — one new test, zero production change | ✅ Analysis done; FR-11 gap ESCALATED. |
| 6 | Upstream: PR #860 + #862 | ✅ merged in; local `master` ref had been stale. |
| 7 | **078** | ⏳ agent still running |

Publish **45.12 MB** compressed incl. PDBs / 44.19 excl. (Release, framework-dependent linux-x64,
`Compress-Archive -CompressionLevel Optimal`) — **+0.16 MB** vs the 44.96 baseline, ceiling 60. No
vulnerable packages. ArchTests **130 passed / 6 failed**, all six the known pre-existing
`Sprk.Provisioning.ControlPlane` baseline (PR #847 fixes exactly those).

---

## 2. The defect class, re-measured — 12 client-supplied sinks across 5 surfaces

**Shape**: the client names an SPE container or drive; the server writes bytes into it. The
authorization decision keys on something *other* than that container, so the two can disagree. SPE
permissions are **additive-only** — content written into a shared container **can never be retracted**.

The guard's output (authoritative; the manual sweep's table is superseded):

| Surface | Sites | Status |
|---|---|---|
| `Api/DocumentsEndpoints.cs:65`, `:122` | 2 | **DELETE** — decided, unblocked |
| `Api/OBOEndpoints.cs:72` (legacy route) | 1 | 076 shipped the replacement; legacy retained pending **Q1** |
| **`Api/SpeAdmin/ContainerItemEndpoints.cs:633`, `:924`, `:1067`** | **3** | 🔴 **LIVE, unblocked, no owner** → **Q2** |
| **`Services/Office/OfficeStorageUploader.cs:54`** + workers `:646`, `:1205` | **3** | 🔴 **LIVE, unblocked, no owner** → **Q2** |
| `Services/Compose/ComposeService.cs:442`, `:1484`, `:1515` | 3 | 🛑 behind PR #806 (issue #858) |

Plus 6 `ServerDerivedRecord` (correct), 5 `ServerDerivedConfig` (correct for server ingest), 2 `Dead`.

### The two live surfaces, precisely

**Office save (`POST /api/office/save`)** — the sharpest finding of the session. It *does* carry
`.AddEntityAccessFilter()`, authorizing the caller against `SaveRequest.TargetEntity` — but the container
comes from `SaveRequest.ContainerId`, **a different client-supplied field on the same body**. Authorized
against one thing, writing into another. That is **option (B)**, which 083 explicitly rejected, live in
shipped code, on an **app-only MI write** (so no SPE ACL backstop). Additionally `TargetEntity` is
*optional*, and `EntityAccessFilter.cs:148-159` calls `next(context)` when it is absent — so the write can
run on baseline authentication alone. Today's clients never populate `ContainerId`, so **the hole is the
contract, not the traffic**: any authenticated caller who writes the JSON picks the container.
⚠️ `RouteAuthorizationGuardTests` scores this route as **gated** — which is why two guards asking
different questions was necessary.

**SpeAdmin container items** — three routes (`upload`, `delete`, and `POST …/folders`) take the container
from the client route and build an app-only Graph client from the **`configId` the client also supplies**.
They are mapped on the **root app**, not inside the `/api/spe` admin group, so they inherit **neither**
the admin-role filter **nor** the tenant-scope filter; bare `.RequireAuthorization()` only. Their primary
defect is a **missing admin gate**, which is broader than container selection.

### Why four prior recounts missed both

`RouteAuthorizationGuardTests` governs a **hand-maintained census of 12 files**, and neither
`Api/Office/OfficeEndpoints.cs` nor `Api/SpeAdmin/ContainerItemEndpoints.cs` is in it. **The forcing
function could only find holes in files someone had already thought to list.** Fixed: the new guard
discovers sinks by scanning the whole tree, so incompleteness is now a build failure.

---

## 3. 🔴 OPEN QUESTIONS

### Q1 — 076: three upload paths have no owning record. What is the contract? *(the real design question)*

The POML's own escalation trigger, fired verbatim. Three live client paths upload bytes **before any
owning record exists**, so `(entity, recordId)` cannot be supplied:

| Path | Why |
|---|---|
| EmailComposer local attachment (`createXrmEmailComposeHandlers.ts:255`) | `sprk_document` created *after* upload, **deliberately unassociated** — *"the email may have no persisted regarding yet"* |
| Analysis wizard standalone doc (`CreateAnalysisWizardWidget.tsx:778`) | `createDocumentRecords('', '', '', …)` — standalone; `sprk_analysis` comes later |
| DocumentUploadWizard skip-associate (`DocumentUploadWizardDialog.tsx:238-242`) | `parentEntityType`/`Id` are `""` **by design** |

The other 6 of 9 sites create the record first and convert cleanly.

| Option | Assessment |
|---|---|
| **(1) Server-issued upload ticket** — server mints a short-lived token bound to a container **it** chose; client uploads against the ticket; record associated afterwards | The POML's own suggested shape. Preserves the invariant (client never names a container) without forcing wizard reordering. New surface (ticket mint + redeem). **Recommended.** |
| **(2) Create the `sprk_document` row server-side FIRST**, then upload keyed on it | Cleanest model — every write has a record. But inverts the ordering of 3 shipped wizards and changes user-visible flow on failure paths. |
| **(3) Keep the legacy container-keyed route alive for these three** | ❌ **This is option (B) through the back door** and violates 083's binding constraint. Listed only to be rejected explicitly. |

**Blocked on**: your call between (1) and (2). Until then the legacy ungated route stays alive, waiver
re-pointed to `076-ESCALATED` (correctly *kept*, not deleted — deleting it would have made Rule A fail on
a genuinely ungated route).

### Q2 — 083 re-plan: how to sequence the 6 live sites on Office + SpeAdmin

Your standing directive settles *who*: **we fix them here** — "trying to offload to other projects is
very risky because they lack the context", and 083's acceptance criteria forbid handing any row to another
project. What is open is packaging.

⚠️ **Task 084 and 085 do not exist** — this project has 080–083 and 086–089. New tasks would take those
numbers.

| Option | Assessment |
|---|---|
| **(a) Two new tasks — 084 Office, 085 SpeAdmin** — 083 closes with the deletions + row 8 + the guard | Smaller PRs, independent review, each surface gets its own owner-visible acceptance criteria. **Recommended.** |
| **(b) Absorb both into 083** | One PR spanning Office add-ins, SpeAdmin, Documents, Chat and Compose — large blast radius for one merge. |
| **(c) SpeAdmin's admin-gate fix separately from its container fix** | Defensible: the missing admin gate is a *different, broader* defect that happens to also be a container hole. But it is the same 3 routes and the same file, so splitting doubles the merge cost. |

Note: `Infrastructure/Graph/SpeAdminGraphService.cs` is contended by **PR #859** (spe-admin-r2);
`ContainerItemEndpoints.cs` and `Api/SpeAdminEndpoints.cs` are **not** touched by any open PR, so the
endpoint-mapping fix is unblocked.

### Q3 — 012 (FR-11): accept the residual risk, or build track-and-revoke?

Current state is a well-reasoned **third thing**, not either of FR-11's two options:

| Requirement | State |
|---|---|
| No **permanent** link | ✅ closed *structurally* — `[Range]`-validated ceilings + `ValidateOnStart`; an operator **cannot** configure an unbounded lifetime |
| Not anonymous **by default** | ✅ closed — explicit per-call opt-in, `Share` right required, 7-day cap, Warning audit, tenant off-switch |
| **Revocable** | 🔲 **open** — an SPE link survives Dataverse revocation, so lifetime is the *only* revocation this route has |

**Residual risk, stated exactly**: a ≤7-day window in which an anyone-with-the-link URL to one document
exists and cannot be retracted, even if that document's access is revoked. Minting requires `Share` on
that specific document and is audited at Warning with the caller's identity.

| Option | Assessment |
|---|---|
| **(a) Accept + document the bounded residual risk** | The exposure is bounded, gated, audited and operator-switchable. Cheapest, and defensible on the record. |
| **(b) Build track-and-revoke** | The literal FR-11 fix: persist the Graph permission id, add a revoke endpoint calling Graph permission-delete, wire into the existing revoke-all path (010/017). **New BFF surface** — needs §11's three questions + §10 Placement Justification. |
| **(c) Narrow the request** — only ask for anonymous when recipients are actually external | Shrinks minting from *every* Link attachment to *external sends only*. **Not free**: `onResolveShareLink` is typed `(documentId) => Promise<string \| null>` with **no recipient context**, so it means changing a shared `Spaarke.UI.Components` contract. Complements (a) or (b). |

⚠️ Correction to the agent's report: disabling anonymous does **not** break the send — the client does
`if (!resp.ok) return null` and falls back to the internal URL. It makes external sharing
**non-functional for external recipients** — a product regression, not an outage. There is exactly **one**
sender in the codebase (`createXrmEmailComposeHandlers.ts:373`), and it requests anonymous
**unconditionally on every call**.

**Recommendation**: **do not mark 012 ✅.** POML status `completed-with-escalation`.

### Q4 — widening the shared entity-set map (blocks part of Q1)

`sprk_workassignment`, `sprk_event` and `sprk_todo` are **live upload targets absent from
`EntityAccessFilter.EntitySetByType`**, so 076's new route would **deny** them. That map is **shared with
the Office save path**, so widening it changes a shipped surface. Decide alongside Q1/Q2 — do not widen
silently.

### Q5 — confirm the refined invariant (low risk, needs an explicit nod)

The client **needs the resulting `driveId` back** (for `sprk_graphdriveid` and `indexFile()`), so *"the
container id never leaves the server"* cannot hold literally until `sprk_document` creation moves
server-side. The distinction the 076 agent drew, which I believe is correct: **returning the server's
CHOSEN id is not option (B); accepting one in the request is the vulnerability.** Confirm and the wording
gets recorded as the invariant.

---

## 4. Corrections this session made to prior claims — read before trusting any older note

| Claim | Correction |
|---|---|
| "Rows 4/5 are LIVE app-only holes, do first" (POML + my own reporting) | **Wrong.** "app-only" describes only the outbound Graph leg; the routes require a caller token. Unexploitable today only by **value-space disjointness** (a `b!…` drive id is not a GUID, so `sprk_documents({driveId})` 403s) — luck, not design. Still DELETE. |
| "Rows 7/8 are instances of this class" | **No** — both resolve from config, not client input. They violate the *settled model* by ignoring the session's owning record. |
| "Row 7 has zero callers" | **Not supportable from source** — `ChatWordExportEndpoints` **is** mapped (`Infrastructure/DI/EndpointMappingExtensions.cs:268`). Only client *usage* is unattested. Do not delete on that basis. |
| "INV-7 has no technical basis" (my first correction) | **Too weak.** INV-7 (`spaarke-multi-container-multi-index-r1/design.md:82-88`) **already prescribes** *record's own field → parent record's BU → server config fallback* — the owner's exact model. The seven client sites were **in breach of** it, and `design.md` cited it as the reason to leave them that way. ⚠️ **Four unrelated invariants in this repo are numbered "INV-7"** — always cite the source project. |
| "The 313 fake hostnames cause the 100s hang" | **Wrong** — 6 of 7 answer in <0.6s; nothing blackholes. The hang was in credential acquisition, upstream of any endpoint. **Do not sweep them**; counter-risk: `CorsModule.cs:93` allows any `*.dynamics.com` origin, so a blind `.invalid` rewrite would flip CORS semantics. |
| "`TryResolveParentEntitySet` is reusable" (076 + 083 POMLs) | Renamed in task 077 → **`SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet`** (`:192`). And there are **three** logical-name→entity-set maps on different key spaces. |
| "The 7 Communication sites need no change" (my hypothesis) | **Partially wrong** — 2 of 7 are real byte writes and were routed. |
| "W1 is one write; ~12 suppliers; W2 at `src/dataverse/webresources/…`" | **W1 is FIVE writes** (4 bypass the helper); **~20 suppliers**; W2's real path is `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js:295-309` and is partly load-bearing. 5 of 7 Communication line numbers were off by one. |
| The manual sweep's S14 (SpeAdmin) | Wrong three ways — wrong lines, wrong sink name, and **missed a third live write** (`CreateFolderForConfigAsync:633`). |

**The pattern is now established beyond doubt: every count asserted in this project has been wrong at
least once — including the census of sink *names*.** That is the argument for the guard over any inventory.

---

## 5. The local test suite — answered and fixed

**What "not a usable signal" meant**: ~5 of ~11,000 tests failed locally but passed in CI on the same
commit, and the failing *set moved between runs* — so a local red could not distinguish "I broke
something" from "this machine again."

**Root cause** (found, not guessed): `Program.cs:48` registers one singleton `TokenCredential` as a real
`DefaultAzureCredential`, and **no fixture replaces it**. On a developer machine with az CLI + Az.Accounts
+ VS IdentityService caches present, one *failed* token acquisition costs **~6.0 s and is not cached** — so
every retry re-pays it. On a CI runner none of those credentials exist and the identical call fails in
**~0 ms**. That asymmetry *is* the divergence; machine load decides which test crosses the 100 s
`HttpClient` default first, which is why the set moved.

The specific escape: `ChatSessionManager.GetSessionAsync` is a three-tier lookup (Redis hot → **Cosmos
warm** → Dataverse cold). `ComposeSupersedeFixture` doubled hot and cold and left the **warm** tier real.
Four of five tests create their session first and hit the hot tier;
`Supersede_WhenSessionUnknown_Returns404` asks for a random GUID **by design** — a guaranteed miss — so it
alone fell through to a live `CosmosClient`.

| | Before | After |
|---|---|---|
| The hanging test | FAIL @ 2 m 6 s | PASS @ **14 ms** |
| Worst of the flaky set | 24 s / 21 s / 21 s | **2 s** |
| 3 consecutive runs | set moved | **62/62 identical, 69/69/70 s** |
| Full local suite | ~5 failures | **10,907 tests, 0 failed, 4.64 min** |

**Honest limits**: the guard intercepts only `IHttpClientFactory` clients (Cosmos direct-mode TCP and
`new HttpClient()` are covered indirectly, via the credential layer); ~59 other fixtures still leave the
Cosmos warm tier real — now harmless at 0 ms, but not hermetic; scoped to one test assembly by design so
the live-Azure `Spe.Integration.Tests` are unaffected; **not yet verified against real CI**.

---

## 6. Housekeeping / smaller items found, no decision needed

- `UploadSessionManager.cs:103-104` claims its methods are *"LIVE via OBOEndpoints.cs:119/172"* — **stale**
  since 076 deleted those routes. `UploadChunkAsUserAsync` is dead too.
- Stale doc comments claiming `HostContext` drives container resolution: `ChatWordExportEndpoints.cs:19`
  and `:119`, `ChatDocumentEndpoints.cs:1020`. It does not.
- `WorkingDocumentService.cs:172` reads the matter's **stamped** `sprk_containerid` directly — stale-stamp
  redirect risk; candidate for `RecordContainerResolver`.
- `MessageAttachmentMaterializer` and `EmailAttachmentProcessor` have **dead write paths** with
  caller-supplied `DriveId` override fields that invite a future hole. Delete the field or the class.
- 083's POML §6 says *extend* `RouteAuthorizationGuardTests.cs`; I deliberately overrode that with a new
  file (two concurrent editors). Reconcile the POML + `notes/task-074-*.md` when 083 closes.
- `ADR-010` interface ceiling moved 153 → 156 in the pre-existing baseline failures.
- ⚠️ **Process lesson**: `gh pr view --json files` **caps at 100 silently**. And a diff against a
  long-lived local `master` ref nearly made an already-merged upstream fix (PR #860) look like agent scope
  creep — **fetch before trusting such a diff**.

---

## 6.5. ✅ OWNER ANSWERS — 2026-08-28. These resolve Q1–Q5. Implement to these.

### Q1 → acting user's BU, but the SERVER derives it. No upload ticket needed.

Owner: *"the current is to use the client-derived container id — meaning the user's business unit…
most of this is most always going to be correct because record/document owner is the team the user is
assigned to… If that is true, then user's business unit container id is correct."*

**Accepted for the three no-record paths only, with the mechanism changed.** The distinction that must
not be lost:

> the user's BU container is the correct **VALUE** ≠ the **CLIENT** should send a container id

The server already reads Dataverse and can resolve the acting user's BU itself. That accepts the answer
while preserving the invariant, and it means **option (1)'s upload ticket is NOT required.** Exposure is
bounded: a caller can only write into their own BU's container, which they are entitled to anyway.

**The resolution order to implement:**
```
record exists + secure    -> the record's OWN sprk_containerid, or FAIL CLOSED (never any fallback)
record exists, non-secure -> the RECORD's owningbusinessunit -> businessunit.sprk_containerid   [built, 076]
NO record yet             -> the ACTING USER's businessunitid -> businessunit.sprk_containerid   [NEW]
                             server-derived, never client-supplied
server-side ingest        -> Communication:ArchiveContainerId
```

⚠️ **Correction the owner should carry forward** (verified live against Dataverse 2026-08-27): *"the user
isn't going to have access to the record/document if they are not in the team"* does **not** hold in the
case that matters most. **Users sit in the Operations subtree while secure records are owned in
`Secure Projects`**, so for a secure record the acting user's BU is provably the WRONG container. Access
also arrives via sharing (POA), role depth and the user hierarchy — not only team membership. And the root
`Spaarke` BU **shares its container with `Spaarke Business Unit 1`**, so a BU container is not itself an
isolation boundary. Hence acting-user BU is admissible **only** where no record exists, and **never** for
secure content.

### Q2 → yes, create tasks 084 (Office) and 085 (SpeAdmin)

Owner: *"yes can create 084, 085 for these new tasks."* 083 closes with the deletions + row 8 + the guard.

### Q3 → accept the bounded residual risk. Container-level revocation is the policy.

Owner: *"for SPE there is not file-level permission — it is only container level; we will handle revocation
of direct SPE link file access at the container level for both internal and external users; the secondary
level is app permission revocation since file access is through Document record as the front door."*

**Accepted → option (a): accept + document.** But record the gap explicitly, because it is an exception to
the model just described rather than something the model covers:

🔴 **An anonymous share link escapes BOTH named controls.** It requires **no container membership** (that is
what anonymous means) and it **does not go through the Document record front door**. So container-level
revocation does **not** invalidate an already-minted link, and neither does app-permission revocation. The
**≤7-day expiry remains the only revocation this route has.** That is precisely why `ShareLinkOptions`
`[Range]`-validates the ceiling — an operator must not be able to configure an unbounded one.

Residual risk, stated for the record: a ≤7-day window in which an anyone-with-the-link URL to one document
exists and cannot be retracted, even after that document's access is revoked. Minting requires
`AccessRights.Share` on that specific document and is audited at Warning with the caller's identity.
**012 → `completed-with-escalation`, NOT ✅.** Track-and-revoke (Q3 option b) stays available as
separately-scoped follow-on if the residual window is later judged too wide.

### Q4 → yes, add them. It is file access.

Owner: *"access in terms of file access? if that is the access then yes these need to be included."*
Confirmed: an entity type absent from `EntityAccessFilter.EntitySetByType` makes the record-keyed upload
route **DENY** (fail closed). So `sprk_workassignment`, `sprk_event` and `sprk_todo` uploads would be
rejected outright. Add all three **with a test per newly-admitted type**, and note the map is shared with
the Office save path — so 084 must re-verify that surface after the widening.

### Q5 → follows Q1

Returning the server's **CHOSEN** container/drive id to the client is fine and necessary (the client needs
`driveId` for `sprk_graphdriveid` + `indexFile()`). **Accepting one in the request is the vulnerability.**
Recorded as the invariant wording.

---

## 6.6. Where the SPE folders come from (owner question, 2026-08-28)

The owner observed unexplained folders in SPE Admin — `communications`, `emails`, `exports`, **"New Word
Document from Word Web Add In 8"**, **"Word Document Office Add In 3"**. Two mechanisms:

**Explicit** — `POST /api/spe/containers/{id}/folders` → `CreateFolderAsync`
(`Infrastructure/Graph/SpeAdminGraphService.cs:977`, admin "New Folder" button; the third live site in
row 10).

**Implicit — this is what the owner is seeing.** Graph creates intermediate folders automatically when a
file is uploaded to a *path*. Confirmed sites:

| Folder | Created by |
|---|---|
| `exports` | `Api/Ai/ChatWordExportEndpoints.cs:152` — `uploadPath = $"exports/{request.Filename}"` |
| `chat-uploads` | `Api/Ai/ChatDocumentEndpoints.cs:1158` |
| `communications`, `emails` | the Communication archive path (`ArchiveContainerId`) |
| **document-title-named folders** | ~~the Office add-in save path passing a document title as `folderPath`~~ → **REFUTED. See the correction below.** |

### 🔴 CORRECTION (2026-08-29) — the document-title folder inference was WRONG, twice over

This section originally concluded: *"the Office save path is passing a document title as `folderPath`,
creating one folder per saved document"*, flagged as "a strong inference … task 084 must confirm it at the
payload's source". It was confirmed at the source and **refuted**:

- `SaveRequest.FolderPath` was **client-supplied and always null**. No producer under `src/client/**` ever
  set it (zero hits for `folderPath` there) and no server code constructed one. It has since been deleted.
- No BFF sink named a folder after a document's own title. The dead plumbing could not have produced the
  observed folders.

**The actual cause: an unsanitized filename became the SPE upload path verbatim.** The Office add-in's
free-text "Document Name" box let a user type a date:

```
"New Word Document from Word Web Add In 8/24/2026"
   → folder "…Add In 8"  →  folder "24"  →  extension-less file "2026"
```

The trailing `8` in the folder name is the **month**, not a truncated title. Two production `sprk_document`
rows account for both observed folders, written by the BFF service identities. Control case:
`Examiner's Report 8-24-2026` — same user, same day, hyphens instead of slashes — minted nothing.

The **email** branch already sanitized; the **document** branch did not, for as long as the feature existed.
That asymmetry was the whole bug. Fixed by **task 084** (2026-08-29), which sanitizes at 14 sites and
consolidates seven duplicate sanitizers onto `Infrastructure/Graph/SpeUploadPath`.

⚠️ **A second refuted claim from the same investigation**: the plan's Phase 0 proposed confirming folder
provenance via `GET /api/spe/audit`. That rests on a false premise — `SpeAuditService` is **write-only**, the
Office path logs nothing, and the table has **0 rows**.

⚠️ **And a third**: the upload is **app-only**, which is why SPE Admin showed no human creator. That absence
was read as evidence of an external cause (Word Online writing directly to the container). It was evidence of
an app-only write — i.e. of us.

**The lesson**: three confident inferences about this surface, each internally coherent, all wrong, all
surviving until someone read the source. The folders were ours the whole time.

**These folders are row 9 made visible** — the observable footprint of the same Office save path that takes
its container from a client-supplied body field. Worth stating in 084's justification: the defect already
has visible operational consequences, not just theoretical ones.

---

## 7. Recommended resolution, in one line each

| Q | Recommendation |
|---|---|
| **Q1** | Option **(1) server-issued upload ticket** — preserves the invariant without reordering three shipped wizards. |
| **Q2** | Option **(a)** — new tasks **084** (Office) and **085** (SpeAdmin); 083 closes with the deletions + row 8 + the guard. |
| **Q3** | **(a) accept + document** the bounded residual risk now; keep **(b) track-and-revoke** as a separately-scoped follow-on. Do **not** mark 012 ✅. |
| **Q4** | Widen the map **explicitly**, as part of whichever task lands Q1 — with a test per newly-admitted entity. |
| **Q5** | Confirm: returning the server's **chosen** container/drive id is not option (B); accepting one in the request is. |
