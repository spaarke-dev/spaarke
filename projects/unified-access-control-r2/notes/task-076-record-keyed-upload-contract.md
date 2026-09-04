# Task 076 — record-keyed upload contract (option C)

> **Status**: server half SHIPPED. Client cutover **ESCALATED** — see §5.
> **Dates**: designed 2026-08-27 · server half + escalation 2026-08-28
> **Corrections applied 2026-08-28**: the route-contract table's two container rows, and the INV-7
> section, were both **wrong** in the 2026-08-27 draft. Corrected in place, with the error kept visible
> in §2 rather than quietly overwritten — the wrong version was cited downstream.

---

## Step 0 — the three §1 facts, verified first-hand on the merged branch

| POML claim | Verification | Result |
|---|---|---|
| `Api/UploadEndpoints.cs` is gone (073) | `ls` | ✅ absent |
| 075's resolver is present | `find` | ✅ `Infrastructure/Dataverse/RecordContainerResolver.cs` + `SecureContainerDecision.cs` + `Services/Communication/Engine/CommunicationContainerResolver.cs` |
| `GET /api/obo/containers/{id}/drive` mapped nowhere | grep of `src/server/**` | ✅ **three comments, zero `Map*` calls** (`DocumentsEndpoints.cs:16`, `UploadSessionManager.cs:105`, `SpeFileStoreDtos.cs:15`) |

The third fact cleared escalation trigger 3, so the chunked OBO pair was **deleted** rather than given a
new contract. That deletion landed in the earlier half of this task (commit `ed5d9e776`).

---

## 1 — The route contract (CORRECTED)

```
PUT  /api/obo/records/{entityLogicalName}/{recordId:guid}/files/{*path}
POST /api/obo/records/{entityLogicalName}/{recordId:guid}/upload-session?path=…&conflictBehavior=…
```

| Concern | Resolution |
|---|---|
| **Authorization** | `RecordRouteAccessAuthorizationFilter` (new, `Api/Filters/`) on `(entityLogicalName, recordId)` via the existing `CallerRecordAccessProbe`, demanding the existing `entity.associate_document` right (`AccessRights.AppendTo`). ADR-008: a filter, not handler code. Denied **before** any container resolution or Graph call. |
| **Container** | `RecordContainerResolver.ResolveForRecordAsync(entityLogicalName, recordId, ct)` — the **two-argument** overload. **No caller-supplied fallback is passed, and there is no parameter through which one could be.** |
| **Secure record, has container** | Its own container. |
| **Secure record, no container** | `secure_record_container_missing` (409) — fail closed and loud. **No fallback.** The business-unit read is deliberately never reached for a secure record, so the fail-closed path cannot acquire a usable fallback. |
| **Non-secure record** | ~~The caller's BU container~~ → **the RECORD's own `owningbusinessunit` → `businessunit.sprk_containerid`.** |
| **Business unit has no container** | `Unresolved` → the route returns 409. An upload cannot "skip" the way an ingest path can. |
| **Record does not exist** | `container_record_not_found` (404). |
| **Ambiguous / indeterminate ownership** | 409, per 075's contract. Not softened. |
| **Entity logical name not authorizable** | **403 `entity_type_not_authorizable`.** A miss in the shared map DENIES; it is never a pass-through. |

The authorization key and the container are the same value by construction: both derive from
`(entityLogicalName, recordId)`, and no code path lets them disagree.

### The §11 decision on the filter — extend, do not add a fourth map

The codebase already had **three** logical/short-name → entity-set maps before this task, which is
itself over the §11 line: `EntityAccessFilter.EntitySetByType`,
`SemanticSearchAuthorizationFilter.AuthorizableEntitySets`, and `RecordSearchAuthorizationFilter`'s
dynamically-built one. A fourth was not acceptable.

So `EntityAccessFilter` gained `internal static bool TryResolveEntitySet(...)` and the new filter reads
**that** table. The new filter declares no map, no probe, and no `OperationAccessPolicy` key of its own.

A separate filter TYPE was still necessary, and this is the ≤2-sentence answer the POML's §11 constraint
asks for: `EntityAccessFilter` reads its caller id from `HttpContext.Items[OfficeAuthFilter.UserIdKey]`
(401 without it — the OBO upload routes carry no `OfficeAuthFilter`), reads its target from a
deserialized Office `SaveRequest` body (the upload routes' body is raw bytes), and **calls `next()` when
it finds no target** — a deliberate fail-OPEN that is right for Office and catastrophic on an upload.
The mechanism is shared; the three input/failure behaviours are not.

---

## 2 — INV-7, corrected. The client sites were in BREACH of it, not compliant with it.

**The 2026-08-27 draft of this note claimed INV-7 "deliberately does not exist server-side" and framed
server-side BU resolution as a `§6.5` path-A deviation from it. That was wrong**, and it is corrected
here rather than deleted because the wrong version was cited while it stood.

The real INV-7 is **`projects/spaarke-multi-container-multi-index-r1/design.md:82-88`**:

> **INV-7 — Resolution chain (canonical order).** For any record needing a container/index:
> 1. **Record's own field** (if set) — wins
> 2. **Parent's BU's field** (for Documents: parent record's BU; for Matters etc.: own BU) — cascading default
> 3. **Tenant-level default** (server fallback, defined in BFF config) — last resort

INV-7 therefore **already specifies, line for line, the model task 076 implements**. It never prohibited
server-side resolution — clause 3 explicitly names *a server fallback in BFF config* — and it explicitly
sources the business unit from the **parent record**, not the acting user.

**Consequence**: the seven client sites resolving `getUserId() → systemuser.businessunitid →
businessunit.sprk_containerid` were **violating INV-7 clause 2**, because they used the *acting user's*
business unit rather than the *record's*. Task 076 brings the code into line with INV-7. It is not a
deviation from it, and it needs no `§6.5` exception. (`Communication:ArchiveContainerId` is INV-7 clause
3 — which is why the ingest paths pass it *in* as a fallback rather than reading it as the decision.)

**Where the "client-side" misreading came from**, worth recording so it is not re-derived: INV-7's next
sentence says the chain *"is implemented at create-time (plugins + wizard)"* — a statement about WHERE
the chain runs. That project's `CLAUDE.md` forbids Dataverse plugins, so "plugins + wizard" collapsed to
"wizard", and `SaveComposeDocumentRequest.ContainerId`'s comment came to read *"the resolver stays in the
wizards"*. A note about implementation **location** got cited downstream as a limit on **capability**.

⚠️ **"INV-7" is an overloaded label** — at least four unrelated invariants in this repo carry that
number (ADR-028's single-`PublicClientApplication` rule, SpaarkeAi's `buildBffApiUrl` rule, this
resolution chain, and more). **Always cite the source project.**

---

## 3 — The seven server-side Communication sites, classified

The POML's §3 hypothesis — *"these are server-side ingest, no owning record exists when bytes move, so
`Communication:ArchiveContainerId` is correct and they need no change"* — is **partially wrong**. Two of
the seven are genuine byte-writes with the `sprk_communication` id already in scope.

Line numbers verified first-hand; **five differ from the POML's §3 table**, noted per row.

| # | Site (verified) | POML said | Bytes move? | Owning record in scope? | Classification |
|---|---|---|---|---|---|
| 1 | `CommunicationService.cs:461` `ArchiveExistingAttachmentsAsync` | 460 | No — Dataverse rows only | Yes | **ALREADY CORRECT — no change.** Pointer *recording*: the bytes already sit at `(sprk_graphdriveid, sprk_graphitemid)` and :476 records where. Routing it would stamp a container the item is not in → dangling pointer, 404 on download. |
| 2 | `CommunicationService.cs:1259` `SendAsync` step 6 | 1259 ✅ | No — Dataverse rows only | Yes | **NEEDS A DIFFERENT FIX — not routing, and NOT 076's.** The drive id is a guess for attachments whose bytes were never written here; the correct value is the *source document's* own `GraphDriveId`, which `DownloadAndBuildAttachmentsAsync` reads and discards at :2422. A container resolver would produce a *different* wrong pointer. Filed as follow-up. |
| 3 | `CommunicationService.cs:1573` `SendAsUserAsync` step 6 | 1574 | No | Yes | Byte-identical to #2. Same disposition. |
| 4 | `CommunicationService.cs:2053` `ArchiveToSpeAsync` | 2054 | **YES** (`UploadSmallAsync`, :2066) | Yes (param, :2035) | 🔧 **ROUTED — the real gap.** The outbound/on-demand `.eml` twin of the inbound one 075 already fixed. A `.eml` is the FULL message body. Three callers, all with `communicationId`: `ArchiveExistingAsync:295`, `SendAsync:1240`, `SendAsUserAsync:1554`. |
| 5 | `CommunicationService.cs:2145` `FetchEmlAttachmentsForEmbedAsync` | 2146 | No — download only (:2156) | Yes | **ALREADY CORRECT — no change.** Read-side *lookup*: names where to FIND existing bytes. Routing it would look in the wrong container, 404, and the catch at :2175 would silently drop the attachment from the archived `.eml`. |
| 6 | `CommunicationService.cs:2367` (comment) | 2368 | No | n/a | **CORRECT BY CONSTRUCTION — comment claim VERIFIED TRUE.** Read 2342-2572 in full: the only `driveId` assignment is :2422 `docRecord.GraphDriveId` and a missing one **throws** (`ATTACHMENT_MISSING_SPE_REF`, :2426). No `ArchiveContainerId` expression exists in the method. ⚠️ But its *rationale* at :2364-2366 — *"Containers in Spaarke are per Business Unit (not per matter)"* — is **stale and contradicts INV-7 clause 1**. Left alone (out of scope) but flagged: a reader could cite it to justify a wrong decision. |
| 7 | `MessageAttachmentMaterializer.cs:114` `MaterializeAsync` | 114 ✅ | **YES** (`UploadSmallAsync`, :130) | Yes (`request.CommunicationId`, :259) | 🔧 **ROUTED.** The messaging-channel twin of the email inbound-attachment path 075 fixed. No production caller today (registered-but-unwired), so zero live blast radius — but leaving it plants exactly the bug 075 removed. |

**Net: 3 need no change · 1 correct by construction · 2 routed here · 2 handed off.**

### Site 7 detail — the override order is load-bearing

`request.DriveId` **used to win** over the archive container. If the resolver had been added only "below"
that override, the isolation fix would have been **caller-bypassable** — any caller supplying a drive id
would silently reinstate the defect. So `request.DriveId ?? ArchiveContainerId` is now passed *in* as the
INV-7 clause-3 fallback, and a secure regarding's own container beats it. Non-secure behaviour is
byte-identical to before.

Both routed sites **refuse** rather than fall back when the resolver is unavailable: an absent isolation
seam that degrades to "use the shared container" is the CLAUDE.md §10 F.1 anti-pattern in its most
damaging form.

---

## 4 — What shipped

| Deliverable | State |
|---|---|
| Record-keyed `PUT …/files/{*path}`, gated | ✅ |
| Record-keyed `POST …/upload-session`, gated — **restores >= 4 MiB uploads server-side** | ✅ |
| `RecordRouteAccessAuthorizationFilter`, reusing `EntityAccessFilter`'s map + `CallerRecordAccessProbe` | ✅ |
| Communication sites 4 + 7 routed | ✅ |
| ArchTest registration-count pin 1 → 3, with the reason | ✅ |
| Tests: 6 gate + 5 two-arg-overload, perturbation-checked | ✅ |
| Client cutover (3 upload clients + ~20 suppliers) | ❌ **ESCALATED — §5** |
| Delete W1/W2 client `sprk_containerid` writes | ❌ **BLOCKED on §5** |
| Delete the container-keyed route + its waiver | ❌ **BLOCKED on §5** |

### Test coverage gap found in the earlier half

**No test covered the two-argument overload.** All 14 pre-existing `ResolveForRecordAsync` calls in
`RecordContainerResolverTests.cs` pass `nonSecureFallbackContainerId` explicitly, so the record's-own-BU
derivation added by the earlier half of 076 — and the load-bearing fact that the BU read is **skipped**
for a secure record — were entirely unpinned. Closed by
`tests/integration/auth/UnifiedAccessControl/RecordKeyedUploadAuthorizationTests.cs`.

### Perturbation check (POML step 9) — both breaks reddened the right tests

| Perturbation | Result |
|---|---|
| A — rights check disabled (`if (false && …)`) | ✅ **exactly 2 FAIL**: `…NoRightsOnTheOwningRecord…`, `…HoldsOnlyRead…`. 9 pass. |
| B — unmapped-entity branch `return await next(context)` | ✅ **exactly 1 FAIL**: `…EntityTypeIsNotAuthorizable…`. 10 pass. |
| Restored | ✅ 45/45 across all three UAC container suites |

---

## 5 — ~~🔔 ESCALATION~~ ✅ ANSWERED 2026-08-28. Route BUILT 2026-09-03.

> 🔴 **THIS SECTION WAS STALE FOR FIVE DAYS AND CAUSED A WRONG STATUS REPORT.** It presents an open
> escalation asking the owner to choose option 1, 2 or 3. **The owner answered on 2026-08-28** — in
> [`SESSION-STATUS-2026-08-28.md`](SESSION-STATUS-2026-08-28.md) §6.5 Q1 — and the answer is **none of
> those three**:
>
> > **Q1 → acting user's BU, but the SERVER derives it. No upload ticket needed.**
>
> The resolution order the owner settled:
>
> ```
> record exists + secure    -> the record's OWN sprk_containerid, or FAIL CLOSED
> record exists, non-secure -> the RECORD's owningbusinessunit -> sprk_containerid
> NO record yet             -> the ACTING USER's businessunitid -> sprk_containerid  (server-derived)
> server-side ingest        -> Communication:ArchiveContainerId
> ```
>
> The invariant survives because *the user's BU container is the correct **VALUE*** ≠ ***the CLIENT**
> should send a container id*. The server reads Dataverse and derives it.
>
> **✅ The route is BUILT** — `PUT /api/obo/me/files/{*path}` (`756e089cb`), using
> `RecordContainerResolver.ResolveForActingUserAsync`, the same resolver ComposeService already uses for
> the matter-less draft. No container parameter; typed 403 for an unresolvable caller; 409 for a BU with
> no container; secure content can never reach it.
>
> **What remains for 076 is the CLIENT CUTOVER**, not a decision. Nothing below is a live question.
> This note was written independently of SESSION-STATUS-2026-08-28 and never reconciled with it — do not
> re-derive an open escalation from it.

### Original text (superseded, kept for the reasoning)

## 5 — ~~🔔 ESCALATION~~: three client upload paths have no owning record

**The POML's first escalation trigger has fired.** It reads:

> *"If an upload path genuinely has NO owning record at the moment the bytes move — so `(entity,
> recordId)` cannot be supplied — STOP and surface it. … Do NOT reintroduce a client-supplied container
> 'just for that one path' — that is option (B) arriving through the back door."*

Three do, verified first-hand:

| Path | Evidence |
|---|---|
| **EmailComposer local attachment** — `createXrmEmailComposeHandlers.ts:255` | `onUploadLocalAttachment(file)` has no record. The `sprk_document` is created **after** the upload and **deliberately unassociated**; the code's own comment: *"the email may have no persisted regarding yet"*. |
| **Analysis wizard standalone document** — `CreateAnalysisWizardWidget.tsx:778` | Uploads, then `createDocumentRecords('', '', '', …)` (empty entity set / id / nav-prop → standalone). The `sprk_analysis` row is created later. |
| **DocumentUploadWizard "skip associate"** — `DocumentUploadWizardDialog.tsx:238-242` | `effectiveParentEntityType`/`Id` are `""` by design; the container falls back to `buContainerIdRef.current`. The user explicitly declined a parent. |

The other 6 of 9 `uploadFilesToSpe` call sites DO create the record first and are cleanly convertible.

**Why the server half shipped anyway**: the record-keyed routes are correct and additive under every
resolution of this question, and they are what the other 6 sites will move onto. Nothing about them
changes depending on how the three gaps are resolved.

**Why the client did NOT cut over**: changing `uploadFilesToSpe`'s signature to `(entityLogicalName,
recordId)` leaves those three with no callable upload path, and deleting the container-keyed route breaks
them outright. Giving the record-keyed routes a container parameter for their benefit is option (B).
There is no fourth move that is not a silent regression.

### Two further blockers surfaced while verifying, both owner-visible

**(a) ✅ RESOLVED 2026-09-03 — no longer a blocker. See the correction below.**

> **Closed by item 7 (`f85796f70`) plus one factual correction.** `sprk_workassignment` and `sprk_event`
> are now IN `EntityAccessFilter.EntitySetByType` — added with their lookup columns verified present on
> `sprk_document` against live Dataverse metadata, so the record-keyed route resolves rather than denies
> for both. The behaviour-change concern below was real but points the SAFE way: these types previously
> **400'd** at the Office endpoint, so adding them converts "rejected outright" into "allowed if the
> caller is authorized". Nothing became more permissive; a new capability arrived already gated.
>
> 🔴 **And the third entity was never an upload target.** This note listed `sprk_todo` alongside the
> other two. Verified 2026-09-03: there is **no `uploadFilesToSpe` call site for `sprk_todo`** anywhere,
> and `CreateTodoWizard/TodoWizardDialog.tsx` has no upload path at all. It is also unmappable —
> `sprk_document` has no `sprk_todo` lookup column — so a document could not be filed to a to-do even if
> one were uploaded. It is deliberately absent from the map. Do not re-derive "todo is a live upload
> target" from this file's history.
>
> **What this means for 076**: blocker (a) is gone and needs no separate task. The ONLY thing still
> blocking the client cutover is the owner decision in "Options for the owner" below.

**(a) — ORIGINAL TEXT, superseded, kept for the reasoning: Three upload target entities are not in the shared map.** `sprk_workassignment`
(`workAssignmentService.ts:545`), `sprk_event` (`CreateEventWizard.tsx:401`) and `sprk_todo` are live
upload targets absent from `EntityAccessFilter.EntitySetByType`, so the new route would **deny** them.
Adding them is §11-clean (extending the existing table, not a fourth) — but that table is **shared with
the Office save path**, where the additions would turn a current 400 into a real authorization check.
That is a behaviour change to a shipped surface and belongs to whoever owns it, not to a silent edit here.

**(b) The client needs the resulting `driveId` BACK.** `createDocumentRecords` writes
`sprk_graphdriveid: containerId` (`EntityCreationService.ts:621`) and `indexFile()` needs
`ISpeFileMetadata.driveId`. So the brief's *"the container id never leaves the server"* cannot hold
literally while the client still creates the `sprk_document` row. **This is not option (B)**: accepting a
container in the REQUEST lets the caller choose where bytes go (the vulnerability); returning the one the
SERVER chose tells the caller where they landed (a fact it must record). The routes therefore return the
server's chosen drive id. Eliminating even that means moving `sprk_document` creation server-side across
9 wizard call sites — a separate project.

### Options for the owner

| | Option | Cost | Consequence |
|---|---|---|---|
| **1** | **Create the record before the bytes** in all three paths (persist the email draft / create `sprk_analysis` first / require a parent) | 3 wizard reorderings + map additions per (a) | Cleanest. The container-keyed route and its waiver both die. Changes user-visible wizard flow. |
| **2** | **Server-issued upload ticket** — a gated `POST /api/obo/upload-tickets` mints a short-lived server-side container binding for parentless content | New endpoint + state | Keeps the flows; the client still never names a container. Effectively a new contract shape. |
| **3** | **Accept a bounded parentless route** — keep the container-keyed route permanently for the three, gated by *something* | Low | ⚠️ Effectively option (B) with a boundary. Not recommended: it preserves the shape 073 deleted from the app-only twin. |

**Recommendation: option 1**, path by path, with option 2 only if a flow genuinely cannot persist a
parent first. ~~Either way the map additions in (a) are a prerequisite and should be their own task.~~
**Superseded 2026-09-03: the (a) map additions are DONE (`f85796f70`) and needed no separate task.**

🔔 **THIS IS THE ONLY OPEN QUESTION IN 076.** The server half shipped, the resolver is wired, blockers
(a) and (b) are both closed. The client cutover — and with it deleting the container-keyed route and its
three Pending waivers — cannot proceed until the owner picks 1, 2 or 3 for the three parentless paths.
A partial cutover (the 6 clean sites only) does NOT help: it leaves the container-keyed route alive for
the other three, which is option 3 by default — the one option this note recommends against.

---

## 6 — DEPLOY ORDERING (unchanged, and now narrower)

Because the client did **not** cut over, this specific merge is **additive and safe to deploy alone** —
two new routes, no existing contract changed. The three clients keep working.

**But the moment the client cutover lands, the ship-together obligation is absolute:**

- BFF ahead of client → the client still calls `PUT /api/obo/containers/{id}/files/…`; once that route is
  deleted → **404 on every upload**.
- Client ahead of BFF → the client calls `PUT /api/obo/records/{entity}/{id}/files/…` → **404 on every
  upload**.

No compatibility window, no feature flag. State this in the cutover PR description. It is the single most
likely way this work causes an outage.

**Merge prerequisites**, both satisfied: 073 (deleted `UploadEndpoints.cs`) and 075 (the resolver).

---

## 7 — Placement Justification (CLAUDE.md §10, `.claude/constraints/bff-extensions.md`)

**In the BFF, and it could not be anywhere else.** The two new routes are the OBO upload surface: they
need the caller's delegated token for the Graph write and the Dataverse `RetrievePrincipalAccess`
question. Both already live in `Sprk.Bff.Api`.

- **New endpoints**: 2. Both extend the existing `OBOEndpoints` group; no new file, no new group.
- **New services**: 1 filter type (`RecordRouteAccessAuthorizationFilter`). It adds no probe, no map,
  no `OperationAccessPolicy` key, and no interface — ADR-010 concrete, instantiated per-request by its
  own extension method exactly as `DocumentAuthorizationFilter` is.
- **New DI registrations**: **0.** `CallerRecordAccessProbe` and `RecordContainerResolver` are both
  already registered unconditionally (`ExternalAccessModule.cs:110`, `Program.cs:63`), so there is no
  §10 F.1 asymmetric-registration question to answer.
- **New packages**: 0. **CRUD→AI dependencies**: 0.
- **Test obligation (§10 bullet 6)**: 11 new tests + 3 existing suites repaired.
