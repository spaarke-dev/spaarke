# Task 076 — record-keyed upload contract (option C)

> **Status**: in progress. Step 0 verified, Step 1 (this design) written before code.
> **Date**: 2026-08-27

---

## Step 0 — the three §1 facts, verified first-hand on the merged branch

| POML claim | Verification | Result |
|---|---|---|
| `Api/UploadEndpoints.cs` is gone (073) | `ls` | ✅ absent |
| 075's resolver is present | `find` | ✅ `Infrastructure/Dataverse/RecordContainerResolver.cs` + `SecureContainerDecision.cs` + `Services/Communication/Engine/CommunicationContainerResolver.cs` |
| `GET /api/obo/containers/{id}/drive` mapped nowhere | grep of `src/server/**` | ✅ **three comments, zero `Map*` calls** (`DocumentsEndpoints.cs:16`, `UploadSessionManager.cs:105`, `SpeFileStoreDtos.cs:15`) |

The third fact clears escalation trigger 3: the chunked OBO pair is dead, so **delete** is correct
and **convert** would be giving a dead path a new contract.

**Client upload call sites re-grepped** (the POML says every count in this project has been wrong at
least once — this one was right): exactly three, matching U1/U2/U3.

| # | Site | Route |
|---|---|---|
| U1 | `Spaarke.UI.Components/src/services/EntityCreationService.ts:493` | `PUT /api/obo/containers/{id}/files/{name}` |
| U2 | `Spaarke.UI.Components/src/services/document-upload/SdapApiClient.ts:101` | same |
| U3 | `Spaarke.SdapClient/src/operations/UploadOperation.ts:27` | same |
| — | `UploadOperation.ts:98` | `GET …/drive` — **dead**, 404 |

---

## 🔴 A fact the POML did not measure — and it changes the shape of the work

### The finding

**Option (C) requires a server-side resolution of the caller's business-unit container, and that
capability does not exist. INV-7 says it deliberately does not exist.**

The POML's §1 measures the work as *"one route converted and two deleted"*. That is the complete
account of the **route** surface, and it is accurate. It is not the complete account of the
**contract**, because of what the server needs in order to answer the question the client stops
answering.

075's resolver signature is:

```csharp
ResolveForRecordAsync(string entityLogicalName, Guid recordId, string? nonSecureFallbackContainerId, ct)
```

That third parameter is load-bearing. `SecureContainerDecision.Decide` routes a **secure** record to
its own container and everything else to `nonSecureFallbackContainerId` — and deliberately never
consults a non-secure record's own stamped column:

> *"A non-secure record's OWN stamped container is deliberately never consulted — only the fallback.
> Reading it would silently redirect content for any record carrying a stale stamp, and stale stamps
> demonstrably exist because the creation wizard's BU cascade writes that column today."*

Today the client computes that fallback. Verified — **every** upload site passes a BU-derived value:

| Caller | What it passes |
|---|---|
| `LegalWorkspace/CreateProject/ProjectWizardDialog.tsx:121` | `getSpeContainerIdFromBusinessUnit(webApi)` |
| `LegalWorkspace/CreateMatter/WizardDialog.tsx:135` | same |
| `LegalWorkspace/CreateEvent/EventWizardDialog.tsx:103` | same |
| `SmartTodo/SmartTodoApp.tsx:643` | same |
| `EmailComposer/createXrmEmailComposeHandlers.ts:255` | `bu.containerId` |
| `CreateProjectWizard.tsx:712`, `CreateEventWizard.tsx:401`, `CreateAnalysisWizardWidget.tsx:778` | `context.speContainerId` — resolved from the BU at wizard-open (this is finding **F-9**) |

Under (C) the client stops sending it. The bytes still have to land somewhere for a **non-secure**
record, and acceptance criterion 4 says *"Non-secure upload behaviour is unchanged."* So the server
must resolve the acting caller's BU container itself.

**It cannot today.** Grepped `src/server/api/Sprk.Bff.Api/**` for `sprk_containerid`: every hit is
provisioning (writing a project's own container), 075's resolver (consuming a fallback it is given),
or a doc comment. There is no server-side `user → businessunit → sprk_containerid` chain.

And that absence is deliberate. **design.md:450, INV-7**:

> *"Acting user's BU → `businessunit.sprk_containerid` … **The BFF deliberately does not resolve this
> server-side (INV-7)**"*

### Why I am proceeding rather than stopping

The POML forbids the smaller alternative in terms that leave no room:

> *"A route that accepts BOTH a record and a container is a failed implementation of this task."*

and the escalation trigger names it directly:

> *"Do NOT reintroduce a client-supplied container 'just for that one path' — that is option (B)
> arriving through the back door, and (B) was rejected."*

So there is exactly one permitted reading, and it is: **the server resolves the fallback too.** This
is not a pivot away from (C) — it is what (C) *means* once the third parameter is accounted for. The
owner's decision that "the client stops deciding" is only true if the server can decide the whole
question.

### Why 075's three INV-7 reasons do not block this

075 §4 defended client-side resolution with three reasons, all written the same day. Each was aimed
at a **client-facing recordId→containerId HTTP endpoint** — which option (C) does not add. Taking
them in turn:

| 075's reason | Does it bite under (C)? |
|---|---|
| 1. *"A recordId → containerId HTTP endpoint is a new disclosure primitive"* — cites task 070 (stopped emitting `driveId`/`speFileId`) and task 081 (a container-resolving route that leaked cross-tenant) | **No — inverted.** Under (C) the container id never leaves the server. The route consumes it internally and returns file metadata. (C) is *strictly better* on this axis than (A), which keeps container ids flowing to clients. |
| 2. *"It would need a record-scoped authorization filter that does not exist"* | **No.** `Api/Filters/EntityAccessFilter.cs` authorizes `(entityType, recordId)` via `CallerRecordAccessProbe` and already covers `sprk_matter` / `sprk_project` / `sprk_invoice` / `account` / `contact`. The POML's §11 constraint names it as the thing to extend. This is what clears escalation trigger 2. |
| 3. *"The BFF is not reachable from every client surface"* — a BFF-dependent resolver would fail closed on every upload when the BFF is down | **No — not on this path.** The upload **is** a BFF call (`PUT /api/obo/…`). If the BFF is unreachable the upload fails regardless; resolving the container inside that same request adds no new dependency. Reason 3 is correct for the *non-upload* container consumers (the wizard's `sprk_containerid` stamp, navigation URLs), which this task does not move. |

So INV-7 is **narrowed, not abolished**: BU resolution stays client-side for record *stamping* and
for the non-upload consumers; it moves server-side for the *upload* path only. That is the smallest
change consistent with (C), and it is recorded here as a **CLAUDE.md §6.5 path A** deviation
(project-scoped exception, INV-7 is a project design invariant rather than an ADR).

### 🔔 Owner: this is the one thing to confirm

The deviation is narrow and defensible, but INV-7 was reaffirmed in writing on 2026-08-27 by 075. If
the intent was that the BFF *never* resolves a BU container under any circumstances, then (C) is not
implementable as written and the contract needs a different shape — which is an owner decision, not
an implementation detail. Flagged here rather than absorbed silently.

---

## Step 1 — the route contract

```
PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}
```

| Concern | Resolution |
|---|---|
| **Authorization** | `EntityAccessFilter` (extended) on `(entityLogicalName, recordId)` via `CallerRecordAccessProbe`. ADR-008: a filter, not handler code. A caller without access to the record is denied **before** any container resolution or Graph call. |
| **Container** | `RecordContainerResolver.ResolveForRecordAsync(entity, recordId, fallback, ct)` where `fallback` is the caller's BU container resolved server-side (new — see above). |
| **Secure record, has container** | Resolves to the record's own container. |
| **Secure record, no container** | `SdapProblemException("secure_record_container_missing", 409)` — fail closed and loud. **No fallback.** The client surfaces the operator-actionable message; it must not retry into a shared container. |
| **Non-secure record** | The caller's BU container — behaviour unchanged from today. |
| **Record does not exist** | `container_record_not_found` (404). |
| **Ambiguous / indeterminate ownership** | 409, per 075's contract. Not softened here. |

The authorization key and the container are now the same value by construction: both derive from
`(entityLogicalName, recordId)`, and no code path lets them disagree.

---

## Deploy ordering — the outage risk (POML §5)

**The client and the BFF MUST ship together.** The upload contract changes on both sides:

- BFF ahead of client → the client still calls `PUT /api/obo/containers/{id}/files/…`, which no
  longer exists → **404 on every upload**.
- Client ahead of BFF → the client calls `PUT /api/obo/records/{entity}/{id}/files/…`, which does
  not exist yet → **404 on every upload**.

There is no compatibility window and no feature flag. This must be stated in the PR description.
It is the single most likely way this task causes an outage.

**Merge prerequisites, both satisfied**: 073 (deletes `UploadEndpoints.cs`, the app-only twin) and
075 (the resolver) are **both on master** as of this task's start.
