# Current Task State — `unified-access-control-r2`

> **Last Updated**: 2026-08-28 — **FIVE PARALLEL AGENTS IN FLIGHT.** Orchestration state below is the
> thing to recover; everything after §083 is prior context that has not changed.
> **Recovery**: read "Quick Recovery", then "§AGENTS IN FLIGHT", then §083, then §076.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **State** | Worktree on `work/unified-access-control-r2` at `babf5f7ee` — 1 commit ahead of master (the design.md INV-7 correction). Five agents working in **isolated git worktrees**, each committing to its own branch. |
| **Next Action** | 🔴 **READ [`notes/SESSION-STATUS-2026-08-28.md`](notes/SESSION-STATUS-2026-08-28.md) FIRST — §6.5 holds the owner's answers to Q1–Q5 and is the implementation contract.** Then: (1) create tasks **084** (Office save — row 9) and **085** (SpeAdmin container items — row 10, THREE routes incl. `POST …/folders`); (2) implement Q1's no-record branch — server-derived acting-user BU, never client-supplied; (3) widen `EntityAccessFilter.EntitySetByType` with `sprk_workassignment`/`sprk_event`/`sprk_todo` + a test each, then re-verify the Office save surface (shared map); (4) 083 closes with the DELETEs (rows 4/5) + row 8 conversion + the landed guard; (5) set 012 to `completed-with-escalation`, **not ✅**. |
| **All six agents DONE, all branches merged** | test-suite repair · sink guard · 076 server half · 078 complete · 012 analysis · plus upstream #860/#862. 14+ commits ahead of master, zero conflicts. |
| **Owner directive this session** | Run 083 **and** 012/076/078 (CI-coordination scope), parallel where possible; and **fix** the unreliable local test suite rather than working around it. |

### ⚠️ MERGE HAZARD — `current-task.md` itself

Every agent invokes `task-execute`, which rewrites **this file** in its own worktree. Four or five
divergent versions of a scratch state file will conflict on merge. **Resolution: keep MINE, discard
theirs** (`git checkout --ours` on this path) — the orchestrator's copy is authoritative. Do not spend
time merging them.

### ⚠️ SHARED FILE — `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`

Three tasks edit it, each owning ONE far-apart waiver entry, so the hunks merge cleanly:

| Lines | Waiver | Owner |
|---|---|---|
| ~234, ~239 | `PUT /api/drives/{driveId}/upload`, `DELETE /api/drives/{driveId}/items/{itemId}` (both `UNOWNED`) | task **083** (main session) |
| ~274 | `PUT /api/obo/containers/{id}/files/{*path}` (`075/076`) | task **076** agent |
| ~306 | `GET /api/v1/containers/{containerId}/documents` (`078`) | task **078** agent |

Each was instructed: delete, never convert to `Permanent`; no reordering or reformatting (that is what
would collide). Also in that file: a registration-count pin on `Api/OBOEndpoints.cs` that 076 must update
with a *reason*, not just a matching number.

---

## §AGENTS IN FLIGHT (2026-08-28)

| Agent | Task | Model | Isolation | Deliverable |
|---|---|---|---|---|
| `sweep-083` | 083 steps 1–3 | **fable** | main worktree, **READ-ONLY** | Trace rows 7/8 · caller-grep rows 4/5 · the app-only contract decision · sweep for unlisted sinks |
| `task-076` | 076 remainder | opus | worktree branch | Route conversion + >=4 MiB upload-session + client cutover + W1/W2 + 7 Communication sites + waiver |
| `task-078` | 078 | opus | worktree branch | Authorize `GET /api/v1/containers/{containerId}/documents` + waiver |
| `task-012` | 012 | sonnet | worktree branch | Gap analysis first — the work may already be done by task 072 |
| `test-signal` | local-suite repair | opus | worktree branch | Make outbound HTTP in tests fail fast + name the escaping URL |

**Why 083's edits stay in the main session**: the POML is `parallel-safe:false` and its reason is real —
concurrent agents on one authorization surface produce silent lost writes. Only the *read-only*
investigation was parallelised.

**Model-tier gate (CLAUDE.md §8.5)**: 083 is `<model-tier>fable</model-tier>`. Session is Opus 5, so the
judgment-critical part was dispatched to an actual **Fable** subagent rather than arguing that opus and
fable are the same escalation class.

### 🔴 CONFLICT-CHECK RESULT — corrects the POML's blocking claim

Paginated (`gh api --paginate`, because `gh pr view --json files` **caps at 100 silently**) across all 10
open non-dependabot PRs. Only **#806** overlaps, and it touches MORE than the POML recorded:

| 083 row | File | #806 | Verdict |
|---|---|---|---|
| 4, 5 | `DocumentsEndpoints.cs` | absent | ✅ unblocked |
| 8 | `Api/Ai/ChatDocumentEndpoints.cs` | modified, **0 line changes** | ✅ effectively unblocked |
| 7 | `Api/Ai/ChatWordExportEndpoints.cs` | **5+/9−** | ⚠️ soft, 14 lines |
| 6 | 3 Compose files | `ComposeEndpoints.cs` **46+/2671−**, updated today | 🛑 hard blocked |

`RouteAuthorizationGuardTests.cs` is clean across every open PR. **#847 does NOT touch it** (it fixes the
6 `Sprk.Provisioning.ControlPlane.Core` ArchTest failures, which are the known not-ours master baseline).

### ✅ DONE THIS SESSION (committed at `babf5f7ee`)

`design.md`'s INV-7 claim corrected — **083 step 7 / acceptance criterion met.** And the finding is
stronger than the POML's: **INV-7 was misread, and it already mandates our model.** Source
(`spaarke-multi-container-multi-index-r1/design.md:82-88`) reads *record's own field → parent record's
BU → tenant default (server fallback in BFF config)* — line for line the owner's model. So the seven
client sites were **in breach of** INV-7, and our design.md cited INV-7 as the reason to leave them that
way. The "stays in the wizards" phrasing came from INV-7's next line, *"implemented at create-time
(plugins + wizard)"* — about WHERE the chain runs; that project's CLAUDE.md bans plugins, so it collapsed
to "wizard". ⚠️ **"INV-7" is an overloaded label** — four unrelated invariants share the number; always
cite the source project.

### 🔎 STALE POML CLAIM RESOLVED (both 076 and 083 cite it)

`TryResolveParentEntitySet` **does not exist**. The real symbol is
`SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet` (`:192`, `internal static`, with
`AuthorizableEntitySets` at `:144`). Worse: there are **THREE** logical-name→entity-set maps and §11 says
a second is already a review failure —

| Map | Keys on |
|---|---|
| `EntityAccessFilter.EntitySetByType:98` (private) | LOGICAL names (`account`, `sprk_matter`) |
| `SemanticSearchAuthorizationFilter.AuthorizableEntitySets:144` (internal) | SHORT names (`matter`) |
| `RecordSearchAuthorizationFilter:246` | built dynamically |

Different key spaces, so not interchangeable. **Decision passed to the 076 agent**: the new route keys on
a LOGICAL name (that is what `ResolveForRecordAsync` takes), so **extend `EntityAccessFilter`** — a fourth
map is an automatic review failure. Caveat found: `EntityAccessFilter` today reads its target from an
Office `SaveRequest` **body** and leans on `OfficeAuthFilter` for the user id, so the route-keyed variant
must take route values and must not depend on `OfficeAuthFilter`.
`CallerRecordAccessProbe.GetCallerRightsAsync` (`:205`) needs the **plural** entity set and fail-closes to
`AccessRights.None`.

### 🔴 ESCALATION FIRED + OWNER DECISION (2026-08-28) — read `notes/task-083-sink-inventory.md`

083's escalation trigger 3 fired (">~3 unlisted instances → STOP and re-plan"). **Owner chose: "widen the
guard first, then re-plan."** So the guard is the instrument, not the last step — its discovered list
supersedes every inventory including §2's and the notes file's.

**Full findings + evidence: [`notes/task-083-sink-inventory.md`](notes/task-083-sink-inventory.md).** Headlines:

- **Rows 4/5 are NOT live holes** — my earlier reporting was wrong. "app-only" describes only the outbound
  Graph leg; the routes require a caller token, so trigger 2 cannot fire. Unexploitable today only by
  **value-space disjointness** (a `b!…` drive id is not a GUID, so `sprk_documents({driveId})` 403s) —
  luck, not design. **DELETE both.**
- **Rows 7/8 are not this class** — config-derived, but record-blind. Row 7 has **zero callers** → DELETE.
  Row 8 is live (`SprkChat.tsx:2014`) → CONVERT via the session's `HostContext`.
- **ROW 9 (new, LIVE)**: `POST /api/office/save` — `SaveRequest.ContainerId` from the client BODY, **MI**
  write, gated on `TargetEntity` (a *different* value), and `TargetEntity` is **optional** —
  `EntityAccessFilter.cs:148-159` returns `next(context)` when absent. Verified in the main session.
- **ROW 10 (new, LIVE)**: SpeAdmin container items — mapped on the **root app**, not the `/api/spe` admin
  group, so no admin-role filter and no tenant-scope filter; `configId` is a bearer capability.
- **Root cause of four missed recounts**: the ArchTest census is a hand-maintained list of **12 files**,
  and both live rows' files are absent from it.

**Sixth agent dispatched**: `sink-guard` (opus, isolated worktree) building
`tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs` — a **NEW file** (zero conflict with
the two in-flight waiver edits) that INVERTS the census: scans every BFF `.cs` for SPE write sinks and
fails on any unclassified site, so incompleteness becomes a build failure. Its report must include every
delta vs the S1–S23 table **in both directions** — anything it finds that the manual sweep missed is the
most valuable output.

### Next actions, in order

1. **Wait for `sink-guard`'s discovered list** → that gives the true count, which is the re-plan input.
2. Re-plan: file **084** for the live rows (9, 10) — executed HERE, per the owner's standing "no
   offloading" directive; acceptance criteria forbid handing any row to another project.
3. 083 lands: DELETE rows 4, 5, 7 · CONVERT row 8 · the widened guard · `design.md` INV-7 ✅ done.
4. Merge the five agent branches BY BRANCH NAME. Keep MY `current-task.md`; discard theirs.
5. Row 6 (Compose, now **three** sinks not one: `ComposeService.cs:1482/1484`, `:1515`, `:442`) stays
   behind PR #806.

### Prior verified baseline (unchanged)

build **0/0** · ArchTests **121 pass / 6 fail** (not ours; PR #847 fixes exactly those) · publish
**45.11 MB** compressed incl. PDBs (+0.15 vs 44.96, ceiling 60) · CVE clean · PR **#861** open as draft,
CI was fully green (23 success / 1 neutral / 0 failures).

### ⚠️ The local test suite is NOT trustworthy on this machine — CI is

Local runs show **5 failures that do not exist in CI** (`Tier 2 / Full Unit Tests` = SUCCESS on the exact
same SHA). Established, not assumed:

1. Reverting `RecordContainerResolver.cs` to its pre-076 state reproduces them identically → not mine.
2. The failing **set moves between runs** (`SearchItems` dropped out; `ScopePersonas` and
   `EndpointAuthorizationCharacterization` appeared). A deterministic break does not move.
3. All take **~100 s** and die with `TaskCanceledException` / *"The client aborted the request"* on an
   in-memory `WebApplicationFactory` client — a timeout signature, not an assertion failure.

**Root cause, partly found and partly NOT — do not repeat the dead end.** Proven: **5 of 6 "fake" test
hostnames resolve to LIVE Microsoft Azure IPs** via wildcard DNS (`test.crm.dynamics.com` →
`13.64.177.224`, `test.search.windows.net` → `20.191.59.83`, plus `test.openai.azure.com`,
`test.documents.azure.com`, `test.vault.azure.net`; only `test.servicebus.windows.net` is NXDOMAIN). So a
stray outbound call in a test opens a **real TCP connection to Azure** and hangs to the 100 s default
instead of failing fast. **313 occurrences across 62 test files** + 3 in
`Sprk.Provisioning.ControlPlane.Tests`.

**But I DISPROVED that as the cause of the specific hang** — rewriting those hostnames to `.invalid` in
`ComposeSupersedeEndpointContractTests.cs` and re-running left it at **2 m 6 s, still failing** (edit was
reverted). Likely because the config is set in more than one place. So:
- The hostname hazard is **real and worth fixing** (a latent 100 s trap on every stray call) — but it is
  **test hygiene, not a blocker**: CI is green.
- ⚠️ Changing those hostnames carries a real risk: URL-shape validation may depend on the genuine
  `.dynamics.com` / `.azure.com` suffixes. Probe one file before sweeping 313.
- The **actual** cause of `Supersede_WhenSessionUnknown_Returns404` hanging is **still unknown**. Next
  diagnostic step: trace what the unknown-session path calls outbound
  (`ChatEndpoints.cs:270` → `SupersedeComposeOutputAsync`, ~`:1530`).

---

## 🔴 §083 — THE NEXT TASK. Owner-directed, and it supersedes finishing 076 first.

**Owner decision 2026-08-27, verbatim intent**: *"this is turning into a critical issue and trying to
offload to other projects is very risky because they lack the context… we need to address the full extent
of this issue here."*

**The defect class**: the client names an SPE container; the server writes bytes into it. SPE permissions
are **additive-only**, so one survivor puts secure content in a shared container **permanently**.

**At least 5 instances, 2 of them LIVE. Two rows are UNTRACED — tracing them is step 1.**

| # | Path | Status |
|---|---|---|
| 1–2 | app-only container route · chunked OBO pair | ✅ deleted (073, 076) |
| 3 | `PUT /api/obo/containers/{id}/files/{*path}` | 🔄 **076 mid-conversion** (see §076) |
| **4** | **`PUT /api/drives/{driveId}/upload`** — **app-only MI**, `canwritefiles` policy only | 🔴 **LIVE. DO FIRST.** |
| **5** | **`DELETE /api/drives/{driveId}/items/{itemId}`** — **a DESTROY**, same gating | 🔴 **LIVE** |
| 6 | Compose create-on-save `ContainerId` | 🔲 issue **#858**, sequenced behind PR #806 |
| 7–8 | `ChatWordExportEndpoints.cs:154` · `ChatDocumentEndpoints.cs:1160` | ⚠️ **UNTRACED** |

**Why 4 and 5 outrank Compose**: they write **app-only (MI)**, and 073's whole finding was that app-only
needs **no container ACL** — so unlike every OBO row these are **live holes, not latent bypasses**. Both
survived 073 only because they live in `DocumentsEndpoints.cs`, outside its file scope. **Neither is
blocked by any open PR.**

**The hard sequencing block, verified**: PR **#806** modifies `IComposeService.cs`, `ComposeEndpoints.cs`
**and** `ComposeService.cs`. Row 6 waits on it. ⚠️ **`gh pr view --json files` CAPS AT 100 SILENTLY** — it
under-reported #806 (**352** actual) and #843 (**178**). **Always `gh api --paginate` for overlap checks
in this repo.**

**Issue #858 ownership was CORRECTED** — it originally read as a handoff to compose-r8; the comment
(`#858#issuecomment-5453509522`) now states UAC-r2 owns the fix, compose-r8 must NOT start it, and the only
ask of them is notification when #806 clears.

**082 is a DIFFERENT concern** — caller-*identity* claim reads, not container selection. I initially
advised folding them together; that was wrong. Keep them separate.

---

## §076 — PARTIALLY DONE AND ON MASTER. Finish it inside 083 or before it.

### ✅ Wave A is fully merged — all 6 branches, BY BRANCH NAME

011 · 013 · 015 · 018 · 020 · 081, zero conflicts. **ArchTest edit #13 applied** (the last of the 13;
081's census comment flipped to past tense only after 081's code was in the tree).

Each merged test class was verified to **execute**, not merely compile — and that surfaced a
handoff error worth keeping: **`tests/integration/auth/**` compiles into `Sprk.Bff.Api.Tests`**
(csproj:101), NOT `Spe.Integration.Tests` as the prior handoff said. Counts: FetchXmlGuardSelfJoin 26 ·
WorkforceEmailNoHijack 45 · MembershipPagingCharacterization 18 · SpeRevokeMatcher 31 ·
ScopeInjectorBound 22 · AuditEnrichmentMiddleware 8 · ExternalModuleDataContract 8 ·
StandingGrantRuntimeUnionSeam 2 · ExternalScopeCharacterization 6.

Two stale claims the merges created were repaired: the 081 census comment, and
`OfficeAuthFilter.cs`'s consumer list (018 deleted `OfficeDocumentAccessFilter`, one of the three it
named — and the list was **already** incomplete, omitting **nine direct handler reads** in
`OfficeEndpoints.cs`).

---

## 🔴 §076 — IN PROGRESS. Read this before touching anything.

### The owner-approved model change (2026-08-27) — this supersedes the POML

Option (C) said "the client stops deciding", but 075's resolver takes a
`nonSecureFallbackContainerId` **the client supplied** — so (C) was unreachable without the server
deriving it. Owner directed: **derive it from the record's own owning business unit.**

**What was wrong**: every client upload site resolved `getUserId() → systemuser.businessunitid →
businessunit.sprk_containerid` — *the person uploading, not the thing uploaded to*. Worse for
isolation: users sit in the Operations subtree while secure records are owned in `Secure Projects`,
so acting-user resolution writes secure content into the general **Operations** container.

**INV-7 has no technical basis** — traced. `design.md:450` states it as a constraint; its only
concrete form is a comment on `SaveComposeDocumentRequest.ContainerId` (`IComposeService.cs:743-751`)
saying *"the resolver stays in the wizards"* — a **scope boundary** from
`spaarke-multi-container-multi-index`, cited downstream as a constraint. `design.md:450` still needs
correcting.

**Verified live against Dataverse** (do not re-derive): `owningbusinessunit` populated on every
`sprk_project` row · `businessunit.sprk_containerid` populated on 3 of 6 BUs · the **`Secure Project`
BU has NO container** (correct — secure records use their own) · the **root `Spaarke` BU SHARES its
container with `Spaarke Business Unit 1`** · `sprk_issecure` is **NULL on 5 of 10 rows** — the
"ABSENT is not FALSE" case, live.

### ✅ Done in 076

| Step | State |
|---|---|
| **0** verify the three §1 facts | ✅ all three confirmed first-hand. `UploadEndpoints.cs` gone · 075's resolver present · `GET /api/obo/containers/{id}/drive` mapped NOWHERE (3 comments, 0 `Map*`) — this cleared escalation trigger 3 |
| **1** design note | ✅ [`notes/task-076-record-keyed-upload-contract.md`](notes/task-076-record-keyed-upload-contract.md) |
| **3** delete the dead chunked pair | ✅ `ed5d9e776` — both routes + dead client + 2 waivers; OBO registration pin **3 → 1** |
| **model change** server-side BU resolution | ✅ `4d375b420` — `RecordContainerResolver` now derives the fallback from `owningbusinessunit` |

### 🔲 Remaining in 076

1. **Step 2 — convert the live route.** `PUT /api/obo/containers/{id}/files/{*path}` →
   `PUT /api/obo/records/{entity}/{recordId}/files/{*path}`, authorized by
   **`CallerRecordAccessProbe.GetCallerRightsAsync(bearerToken, entitySet, recordId, ct)`** (fail-closed
   to `AccessRights.None`) via an endpoint filter per ADR-008. Needs a logical-name → entity-SET map —
   **reuse `SemanticSearchAuthorizationFilter`'s `TryResolveParentEntitySet`** (task 080 made it
   `internal` for exactly this); do NOT write a second one (§11).
2. **NEW — the >4 MB fix (owner-directed).** `POST /api/obo/records/{entity}/{recordId}/upload-session`
   → authorize record, resolve container, create the Graph session, return `uploadUrl`. **Client chunks
   directly to Graph's `uploadUrl`** — that part already worked, and the deleted BFF chunk-relay route
   stays deleted (nothing ever called it; proxying bytes through the BFF is worse).
3. **Step 4** — cut over U1 `EntityCreationService.ts:493`, U2 `SdapApiClient.ts:101`,
   U3 `UploadOperation.ts:27` to `(entity, recordId)`.
4. **Step 5** — classify all 12 container suppliers; unclassified = survivor. Note
   `NavigationService.ts:354-362`, `WorkspaceGrid.tsx:535-537`, `sprk_analysis_commands.js:58` feed
   **reads/navigation, not uploads** — those are NOT this task's to delete.
5. **Step 6** — delete W1 (`EntityCreationService.ts:327` `applyDefaultContainerId`, via
   `applyUserBuDefaults:374`) and W2 (`DocumentUploadWizard/sprk_subgrid_commands.js`).
6. **Step 7** — route the 7 server-side Communication sites (`CommunicationService.cs:460/1259/1574/
   2054/2146`, `MessageAttachmentMaterializer.cs:114`, and verify `:2368`'s "no longer used" comment).
7. **Step 8** — delete the LAST OBO waiver once the route is gated. **Never** convert it to Permanent.
8. **Steps 9–11** — tests (incl. the no-access-caller deny case, which has no prior coverage at all),
   absence-grep, build/publish/CVE.

### ⚠️ 075 built a CLIENT-side resolver that option (C) makes dead

`Spaarke.UI.Components/src/services/RecordContainerResolver.ts` — its header says *"Task 076 routes
the ~8 client call sites onto this module"*, which is **option (A)'s design**. Under (C) no client
resolves a container. It currently has **zero production importers** (only its own test + the
barrel). Decide explicitly in Step 5: delete it, or keep `decideContainer` alone for the
fixture-parity pin (`tests/fixtures/secure-container-decision-table.json` drives BOTH halves — check
before deleting, or the C# half's parity test loses its counterpart).

### 🔴 Deploy ordering — the outage risk

**Client + BFF MUST ship together.** No compatibility window, no feature flag: BFF-first 404s every
upload, client-first 404s every upload. Must be in the PR description.

### Filed, not fixed — hand to compose-r8 (PR #806)

[`notes/finding-compose-create-on-save-client-named-container.md`](notes/finding-compose-create-on-save-client-named-container.md)
— Compose create-on-save writes into a **client-named container**, the same shape 076 removes from
uploads. Root cause is a contract gap: `SaveComposeDocumentRequest` carries **no parent-record key**
(all 16 properties enumerated). But the owning record IS known one step earlier
(`LoadComposeDocumentRequest.MatterId` + ADR-040 session binding) and isn't threaded to save. Not
exploitable today (OBO, no user holds a container ACL). `ComposeEndpoints.cs` IS governed as
`Scope.HandlerAuthorized`, so it is visible and classified. **ADR-049 surface under active
development — handover, not a drive-by edit.**

### 🔴 STILL OWED — a regression test whose gap is PROVEN

`SemanticSearchAuthorizationFilter` + `RecordSearchAuthorizationFilter` **and their handlers** were
fixed to `CallerResolution.ResolveObjectId`, but nothing guards them. Perturbation-proven twice:
restore the broken read and **45 dedicated authorization tests stay green**. Write a principal in
production's MAPPED shape (schema-URI `oid` + *divergent* `NameIdentifier` `sub`) asserting the
**oid** reaches the authorization decision.

### 🔴 STILL OWED — a regression test whose gap is PROVEN, not suspected

`SemanticSearchAuthorizationFilter` + `RecordSearchAuthorizationFilter` **and their handlers** were fixed
to `CallerResolution.ResolveObjectId`, but nothing guards them. Perturbation-proven twice: restore the
broken read and **45 dedicated authorization tests in `Spe.Integration.Tests` stay green**.
**Write**: a principal in production's MAPPED shape (schema-URI `oid` + *divergent* `NameIdentifier` `sub`)
asserting the **oid** reaches the authorization decision.

### The session's most instructive find (don't lose the lesson)

**#840's `CallerIdentityGuardTests.Rule1` — now blocking — caught three surviving
`FindFirst("oid") ?? NameIdentifier` reads in our files**, two feeding *per-row authorization*
(`SemanticSearchEndpoints.cs:653` → `:569`, `RecordSearchEndpoints.cs:130` → `:280`). My earlier fix had
covered the **filters** only, and `SemanticSearchEndpoints.cs:650` documents the invariant —
*"Mirrors the filter's extraction so both halves… identify the caller identically"* — so fixing one half
**silently broke the mirror**. A mechanical ratchet caught what review did not.

### Decisions NOT to re-litigate

- **076 → option (C)**, record-keyed upload contract. Deps **073 + 075** (both on master). Tier **opus**.
  Creates a **client + BFF ship-together** obligation. Status restored to `pending`.
- **P2 (parent–child) is ours entirely** — no split.
- **082 narrowed** — #840 built the ratchet; keep the §11 four-primitive question + classify-by-sink.
- **Do not harden the ingest catch** — see the 047 residual below.
- **A18 is RETRACTED by A19** — §4c was right; my merge was wrong.


### The three read-first documents

1. **[`notes/wave2-parallel-merge-plan.md`](notes/wave2-parallel-merge-plan.md)** — the integration
   checklist. §§A1–A17 cover Wave A. 13 ArchTest edits, census 111→110, 8+ follow-ups.
2. **[`notes/coordination-compose-r8-2026-08-27.md`](notes/coordination-compose-r8-2026-08-27.md)** —
   cross-project contract, **DELIVERED** (PR #832 + #806 comments + their worktree). Carries
   **Amendment 1** (we own P2 entirely) and **Amendment 2** (076 → option C).
3. **[`notes/response-from-spaarkeai-compose-r8-2026-08-27.md`](notes/response-from-spaarkeai-compose-r8-2026-08-27.md)**
   — their reply, **accepted in full**. Their §4 warns our census would not have caught either of their two
   disclosures (id-space defects with no claim read at all) and offers two extra rules. Their §5 hands over
   `WorkspaceLayoutService`: three breaks, and *"the claim fix alone would have converted a disclosure into
   an outage"* — FR-01's shape on a third surface.

### 🔴 NEXT: WORK ITEMS (owner-approved 2026-08-27)

1. ~~Deliver the coordination doc~~ ✅ **DONE** — PR #832 + #806 comments, plus the full doc in their
   worktree with a provenance header. They replied "accepted in full" within 9 minutes and merged #832
   within 49.
2. ~~081 hardening~~ ✅ **DONE** (`1a77288b0` + `41cb87310`). **P12 is the deliverable**: invert the branch
   ordering with the conjunction intact → 19 tests still green, so execution order is provably no longer
   the saving function. ⚠️ Retro-check returned **NO** as instructed — my risk model conflated input-shape
   with source-edit risk; see merge plan §A17.
3. **File the parent-fallback task** (new Phase 0c) — **now OURS entirely** (owner: no split, P2 is ours).
   Filter-level, **Type 1 scoped** (terms 2–4 are what give contacts parent access, so "ask Dataverse about
   the parent" returns nothing for Types 2/3), applies the parent's **vetoes** (§6.1 — pre-veto leaks Secure
   through children), states the two-parent rule (§6.2), records that it does **not** cover orphans (§6.3).
   **Also file the separate orphan task** — orphans are the dominant case.
4. **Two Dataverse measurements** (minutes, gate several decisions):
   **(a)** depth of `prvReadsprk_Document` per role — the census in `design.md:544` covers only
   `prvReadsprk_Project`/`_Matter`, so this is unmeasured; **(b)** the business unit of the
   `# mi-bff-api-dev` application user. Together they decide whether FR-01's 403 is MI-ownership or a
   `RetrievePrincipalAccess` failure (both return a byte-identical fail-closed 403), and whether §5.2's BU
   restructure would break every MI-owned record.
5. **MERGE — the gate is open.** ~~#832~~ ✅ merged. ~~master~~ ✅ merged. ~~076 decision~~ ✅ option **C**.
   Remaining: **merge the 10 worktrees** → 13 ArchTest edits + census **111→110** → **task 082** (narrow it,
   see below) → **047** live validation. Also pull **050/052** forward (**050 has NO deps**) and decide
   whether **030** starts now, since all of Phase 1 sits behind it.

### Decisions made this session (do not re-litigate)

| Decision | Outcome |
|---|---|
| **076 resolution point** | **Option (C)** — record-keyed upload contract; routes take `(entity, recordId)`, server resolves, **client stops deciding**. (A) was rejected: it leaves two keys for one decision and F-9 proves they already diverge. Scope re-measured — the note's "spans three tasks" was **stale**, 073 already deletes the overlap; ~3 OBO routes remain. **076's POML still needs rewriting to C.** |
| **P2 ownership** | **UAC-r2 owns it ENTIRELY** — model, spec corrections, AND implementation. No split (loses context and attention). compose-r8 will **not** build a fallback; retracted on #806. |
| **Task 082 scope** | Largely **superseded** — compose-r8's PR **#840** did the tail sweep (41 sites/37 files) and built `tests/Spaarke.ArchTests/CallerIdentityGuardTests.cs`. **Narrow 082** to the §11 four-primitive question + a **classify-by-SINK** audit, and add their two rules: a `Guid.TryParse` whose failure path drops a security predicate, and any caller-id vs `ownerid`/`owninguser`/`createdby` comparison without oid→systemuserid translation. |

### ⚠️ Standing hazard: a CONCURRENT SESSION is committing to this branch

Commits `ef1da3bd4`, `57191820b`, `973f9a459` were **not** mine — another session handled the compose-r8
correspondence and captured my uncommitted amendments (+66 lines) in its own commit. Nothing was lost, but
**two sessions writing one tree** is the lost-writes hazard documented for sub-agents, at session level.
Before touching `RouteAuthorizationGuardTests.cs` (13 pending edits, single file), check `git log` for
foreign commits.

### Files modified this session (all committed + pushed through `314adad96`)

- `notes/coordination-compose-r8-2026-08-27.md` — **new**, the cross-project contract
- `tasks/082-caller-identity-primitive-census.poml` — **new**, the §11 ratchet
- `notes/wave2-parallel-merge-plan.md` — §§A1–A16 (Wave A findings)
- `spec.md` — FR-17 corrections (FR-25→NFR-03; both dead filters; the A-23 always-deny retraction)
- `.claude/FAILURE-MODES.md` — **G-12** (stale assembly behind a truthful "up-to-date" build)
- `.claude/constraints/azure-deployment.md` — publish-size five-field convention made binding
- `.claude/CHANGELOG.md` — entries for both `.claude/` changes
- `src/.../Membership/IIdentityNormalizationService.cs` — removed the load-bearing false security claim
- `tasks/{024,043,025}-*.poml` — carry-forward constraints from 020/015/011
- `tasks/TASK-INDEX.md` — Wave A → 🔄, task 082 filed

### Critical context

**Every agent worktree was cut from `master`, not this branch** (`isolation: worktree` uses the repo's
default checkout). Verified harmless for Wave A — none of the 12 target files differ between trees — but
agents cannot see task 074's guard and their test baselines are not ours. **Verify the base on every future
dispatch.** Only 081 reset onto the project branch.

**The batch's transferable lesson (AP-8 + G-12):** a green suite proves the code does what its tests say,
never that the tests say the right rule. This wave found tests **pinning a defect as the contract** (015),
a double **collapsing three entities into one** (020), a **method name asserting a security property it
does not provide** (013's `ExtractVerifiedEmail`), and — only visible from the orchestrator position —
**three agents reporting incoherent publish sizes while each was individually correct**.

---

## 🟢 CI IS GREEN — the Router gate was repaired and the fix is ON MASTER (2026-08-27)

`CI / Router` had **never** succeeded on this branch (17 runs, 0 successes). Commit `f695ce38f` fixed
three defects and the gate is now **green**, verified as a real green rather than a docs-only skip:
all 15 jobs ran, including all four Tier 1 blocking jobs, and **Tier 2 `Full Unit Tests` ran to
completion** instead of dying at the old 6-minute wall — the first time CI has actually executed the
full unit suite on this branch.

All three fixes are **content-verified live on `origin/master`** (not merely commit-ancestry):
`ci-tier2-advisory.yml:243` `timeout-minutes: 30` · only `workflow_call:` at `:23` (the self-colliding
`pull_request:` trigger is gone) · `ci-router.yml:274` builds an adjudication set that excludes tier2,
with `allowed-skips: tier1` at `:294`. They reached master via the auth-v4 → master chain, not our PR.

**`SDAP CI` is still red, and it is NOT ours** — one failing job, `Tenant Isolation (I1–I5)`, failing
identically on master at `74ee9b6b1` (FR-28/I1, FR-29/I2, FR-32/I5). **PR #828 already fixes exactly
those three.** Do not file a duplicate. Note the job calls itself *merge-blocking* while master is red
on it, so repo-wide it currently gates nothing.

**CI coordination**: another agent owns `sdap-ci.yml` + `scripts/ci/classify-and-retry.ps1` (PRs #829,
#830). **Zero file overlap** with ours — verified. Do NOT touch `.github/workflows/**` from this
project without re-checking. Useful thing to pass them: the Router now excludes tier2 **by
construction**, so retry logic on the advisory tier cannot redden the gate however it concludes.

---

## 🟠 IN FLIGHT — WAVE A: 6 PARALLEL AGENTS IN SEPARATE WORKTREES (dispatched 2026-08-27)

**Work is NOT all in this worktree right now.** Six `task-execute` agents dispatched with
`isolation: worktree`, each with its own checkout and commits. Selected for **fully disjoint
modify-sets** — that disjointness is the safety property, not the POMLs' `∥-safe` flag.

| Agent | Task | Model | Exclusive modify-set |
|---|---|---|---|
| `uac-081` | **081** classify the caller | opus/xhigh | `Endpoints/Diagnostics/TenantContainerResolverEndpoint.cs`, `Infrastructure/Logging/AuditEnrichmentMiddleware.cs`, NEW `Spaarke.Core/Auth/` primitive |
| `uac-011` | **011** same-entity self-join | sonnet/high | `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` |
| `uac-013` | **013** workforce `oid` no-hijack | sonnet/high | `Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs`, `Services/Ai/Membership/IdentityNormalizationService.cs` |
| `uac-015` | **015** membership paging | sonnet/high | `Services/Ai/Membership/MembershipResolverService.cs`, `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` |
| `uac-018` | **018** dead filter + `in`-clause bound | sonnet/high | `Api/ExternalAccess/AccessibleRecordSetAuthorizationFilter.cs`, `Api/ExternalAccess/Tier2ScopeFilterInjector.cs` |
| `uac-020` | **020** org-grant SPE cleanup | sonnet/high | `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs`, `Dtos/RevokeAccessResponse.cs`, `tests/.../SpeRevokeMatcherTests.cs` |

**Pre-dispatch conflict check PASSED** (Step 0.5 hot-path, all six touch BFF): zero overlap with every
open PR (#806/#828/#829/#830/#636/#526) **and** zero overlap with the three unmerged worktree branches
(073 `dd3e38f6d`, 079 `8185c8fcc`, 075 `3289844`).

### Held back from Wave A — with reasons (do NOT dispatch these blind)

| Task | Why held |
|---|---|
| **012** | Contends `FileAccessEndpoints.cs`, which **072 just rewrote** (share-link now gated on `Share`, bounded expiry, anonymous opt-in). Its POML predates 072 — **re-scope before running**, part of it may already be satisfied |
| **024**, **025** | Both contend `SpeRevokeMatcherTests.cs` + `RevokeExternalAccessEndpoint.cs` with 020 |
| **028** | Contends `AccessibleRecordSetService.cs` with 015 |
| **029** | Contends `ExternalProjectDataEndpoints.cs` + `ExternalAccessModule.cs` with 028; `spec.md` with 023 |
| **023** | Contends `spec.md` with 029 |
| **027** | Modifies `ci-tier2-advisory.yml` — **the file we just fixed**, on the CI hot path with another agent live. Needs coordination, not parallelism |
| **076** | 🔔 escalated — owner decision outstanding |
| **078** | Deps on 075 (unmerged) and gated on 047 |
| **047** | Operator-driven — needs a live deploy + real secure project; not autonomous-safe |
| **026** | ∥-safe and free, but held so the main session stays clear for escalations + the merge |
| **030/031/040** | Edit `.claude/**` → main-session-only (root §3) |

**Why worktrees rather than shared-worktree parallelism** (the POMLs' `∥-safe:true` does not cover
these): sub-agents share ONE worktree by default, so concurrent edits to a shared file are **lost
writes, not git conflicts** — and 073 + 079 BOTH need waivers deleted from
`RouteAuthorizationGuardTests.cs`, while either may want a new `OperationAccessPolicy` key. Concurrent
`dotnet build` in one worktree also contends on `bin`/`obj`.

**MAIN-SESSION-OWNED files — every agent (both batches) was told NOT to touch these and to report
needed changes instead:** `Spaarke.Core/Auth/OperationAccessPolicy.cs` ·
`Api/Filters/DocumentAuthorizationFilter.cs` · `Infrastructure/Graph/SpeFileStore.cs` ·
`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` · `current-task.md` ·
`tasks/TASK-INDEX.md` · `spec.md` · **`.github/workflows/**` (CI hot path, another agent is live)**.
Same boundary pattern as the `.claude/` rule (root §3).

### ⛔ READ THE MERGE PLAN FIRST: [`notes/wave2-parallel-merge-plan.md`](notes/wave2-parallel-merge-plan.md)

That file is the complete integration checklist — worktree inventory, the 12 ArchTest edits, a
must-fix false-PASS vector in a new guard, 8 follow-ups to file, and the verification sequence.
**Nothing is merged yet.** Batch status: **073 ✅ shipped, both gates returned** (`dd3e38f6d`) ·
**079 ✅ shipped, ⛔ NEITHER GATE RAN — both owed on the combined diff** (`8185c8fcc`) ·
**075 ✅ shipped, gate PASSED after 4 rounds / 10 defects / 0 in round 4** (`3289844`) ·
**076 🔔 ESCALATED, not implemented — owner decision outstanding.**

⚠️ **CORRECTED 2026-08-27 — task 074's guard is NOT "currently +5 red".** It is green at HEAD here:
`Api/UploadEndpoints.cs` still exists in this worktree, and CI's Tier 1 arch-tests job passes. The +5
is the **post-merge** state that appears the moment 073's deletion lands. The merge plan's sequencing
(§2 edits applied in the same tranche as 073, census last) is correct either way — only the tense was
wrong. Cause is still known to the line: the `GovernedFile` entry for the deleted
`Api/UploadEndpoints.cs`, whose `ScanFile` does an unguarded `File.ReadAllText`, accounts for 4 of 5.

**Merge-back obligations when they report:**
1. Apply each reported `OperationAccessPolicy` key centrally (073 and 079 may both want one).
2. Delete the now-stale Pending waivers: 073 owns **4** (`PUT /api/containers/{id}/files/{*path}`,
   `POST /api/containers/{id}/upload`, `PUT /api/upload-session/chunk`, `PUT /api/drives/{id}/upload`);
   079 owns **2** (versions list + prior-version content). Only delete the ones actually gated —
   `NoWaiverIsStale` fires on a waived route that became gated, and a waiver for a route that no longer
   exists is worse than noise.
3. Re-run the full suite + ArchTests in THIS worktree after merging — each agent verified only its own
   worktree, so nothing has yet tested the combination.
4. Expect 073 to possibly come back **blocked on 075's seam** — its waivers are tagged "073/075/076"
   jointly. That is a correct outcome, not a failure; it was told not to duplicate or stub the mapping.

**Baselines the agents were given** (so their numbers are comparable): full suite **11,172 / 0 / 82** ·
ArchTests **9 known master failures** (FR-27 ×2, FR-28, FR-29, FR-32, FR-F1, FR-F2, ADR-010,
ServiceBusClientGuard) · publish **45.08 MB** compressed incl. PDBs, ceiling 60.

---

## 🔴 START HERE

**081 is UNBLOCKED and rewritten to option B.** The POML, the decision record
([`notes/task-081-tenant-diagnostic-BLOCKED.md`](notes/task-081-tenant-diagnostic-BLOCKED.md) — filename
is historical, it is no longer blocked) and `TASK-INDEX.md` are all consistent as of 2026-08-26. **Next
action is 072 or Wave 2 (075 → 076)**, not 081 bookkeeping.

**⚠️ Two "verified facts" recorded in the previous version of this block were WRONG.** Corrected in the
decision record's §Corrections; do not carry them forward:
- ❌ *"zero reads of `idtyp`/`appid` as claims in `src/server`"* — **false.**
  `Infrastructure/Logging/AuditEnrichmentMiddleware.cs` reads `appid`/`azp` (:102-104) and `idtyp` (:132).
- ❌ *"`Sprk.Bff.Api/CLAUDE.md` falsely claims `AuditEnrichmentMiddleware` enriches with `appid`"* —
  **false, that doc is correct.** No doc fix needed there.

This **improved** 081: `IsOnBehalfOfFlow` (:129-145) already classifies caller kind — it is just a
`private static` method in a logging middleware, so unreachable, answering a logging question rather than
an authorization one, with no tests and no other consumers. So 081 is *promote and extend ONE classifier*
(CLAUDE.md §11 reuse), not *write a new one*, and its acceptance criteria require exactly one classifier
to exist afterwards.

**The three things carried into the POML that must not be re-litigated:**
1. **Placement is binding** — the primitive goes in `src/server/shared/Spaarke.Core/Auth/`.
   `Spaarke.Core` cannot reference BFF `Infrastructure/**` (`LayerDependencyTests`), so a BFF-side
   primitive is unreachable by the evaluator in `Spaarke.Core/Auth/AuthorizationService.cs` and gets
   rebuilt — the trap that shrank task 032. Verified: no new package reference needed (`ClaimsPrincipal`
   is BCL).
2. **User principals DENY outright**, not `tid`-match. A provisioning diagnostic has no end-user use
   case; this gets option C's "no user reach" without C's credential downgrade.
3. **The trap**: `appid`/`azp` is present in *delegated* tokens too — it names the client app, not the
   caller kind. `allowedAppIds.Contains(appId)` alone lets a human on the L2 app registration name any
   tenant. Gate = positive app-only determination **∧** allow-list. Absence ⇒ indeterminate ⇒ deny.
   Empty **or** absent allow-list ⇒ deny everyone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | ✅ **072 COMPLETE.** Phase 0c: **070 ✅ 071 ✅ 072 ✅ 074 ✅ 077 ✅ 080 ✅** · **081 🔲 READY** (option B — see START HERE) · 073 · 075 · 076 · 078 · 079 filed |
| **Next Action** | **073** (authorize container upload — Wave 1, `opus`/`high`, `∥-safe:true`) **or Wave 2 (075 → 076)**. Note 078 depends on 075, so Wave 2 unblocks it |
| **⚠️ 072 deploy ordering** | **BFF + client must ship together.** An older client posts `{}` → binds to organization scope → emailed links silently stop opening for **external** recipients, no error signal. See `notes/task-072-gate-share-link.md` §7 |
| **Commits** | `d6d156ac1` 080 · `4c51eed7e` CI + census · `f857fdc07` 077 · `9a0823996` handoff · `7b8ac54e2` 081→option B · `bb1e442ea` 072. Push 7b8ac54e2 + bb1e442ea |
| **⚠️ PR head ≠ your SHA** | `ce7a88718` is a `github-actions[bot]` auto-format commit that landed on top. **Always check the PR head SHA, not the one you pushed** — bot commits move it, and their workflow runs park at `action_required` until approved (`gh api -X POST .../actions/runs/{id}/approve`) |
| **Step** | Between tasks. Working tree clean. **PR #825 open as DRAFT** |
| **CI on #825** | ✅ **ASSESSED + RESOLVED.** 51 check-runs. `Changed-Surface Integration Smoke` **PASSED** (first run ever on this branch). Two failures, **neither ours** — see the CI block below. Master merged (285 commits, 0 conflicts) |

### ✅ CI assessed and resolved 2026-08-26 — 3 findings, none of them regressions

**PR #825 needed a close+reopen to get CI at all.** The `pull_request` event produced **zero** runs on
creation (`check-runs` total = 0) despite: no `draft` gating anywhere, Actions enabled
(`allowed_actions: all`), all workflows `active`, `mergeStateStatus: CLEAN`, and `pull_request` firing
normally for other branches. Reopening fired it (51 check-runs). **This is a SECOND, independent way this
branch ends up with no CI** — the first was structural (no PR ⇒ no triggers). Both look identical from
the outside: a branch that appears tested because local runs are green.

| CI failure | Verdict |
|---|---|
| `Tier 1 / Arch Tests` — census `expected 109, found 111` | **Our forcing function working.** CI tests the MERGE with master; master added 2 route-registering files. **Fixed**: census → 111 with both files classified inline |
| `Tenant Isolation (I1–I5)` | **Pre-existing red on master** — master's own latest `SDAP CI` run 32969447565 fails this identical job |

**Proved no regressions the honest way**: ran the full ArchTests in a throwaway worktree at pristine
`origin/master` → **9 failures**; same suite on this branch → **9 failures**, `comm` diff of the sorted
names is **empty both directions**. Master is red; we add nothing. (FR-27 ×2, FR-28, FR-29, FR-32, FR-F1,
FR-F2, ADR-010, ServiceBusClientGuard — all master's, none ours. Worth telling whoever owns them.)

### 🔴 TWO NEW FINDINGS from the CI work

1. **`Auth Smoke` has NEVER fired for any authorization filter change.** Its path filter used
   `**/Authorization*.cs`, which anchors at the START of the filename — and all **17** real filters are
   named `<Subject>AuthorizationFilter.cs` (`DocumentAuthorizationFilter`,
   `SemanticSearchAuthorizationFilter`, …). The glob matched **zero** of them. **FIXED** in
   `ci-tier1-blocking.yml`: added `Api/Filters/**`, leading-wildcard `**/*Authorization*.cs`,
   `**/*Auth*Filter*.cs`, `Spaarke.Core/Auth/**`, `*AccessDataSource*.cs`. Same failure shape as the
   original vulnerability: a gate that LOOKS like it covers auth while covering none of the auth code.
2. **Task 081 FILED — cross-tenant read** in master's new
   `Endpoints/Diagnostics/TenantContainerResolverEndpoint.cs`. It takes `tenantId` from the QUERY STRING
   and treats the caller's JWT `tid` as a mere *fallback*, passing the caller-supplied value straight to
   `ITenantContainerResolver.ResolveAsync` with no match check. Tenant A can resolve tenant B's SPE
   container id; the 400-vs-200 "tenant not served by this stamp" split is also a tenant-enumeration
   oracle. **Third hole the 074 forcing function has produced** (after 077, 078) and the first from being
   *made to classify* a new file rather than a rule firing directly.
| **080 gates** | Step 9.5 ran as mechanical ADR checks on the diff: no new `.WithClientSecret` (ADR-028 A4) · no `Microsoft.Graph` outside Infrastructure (007) · no `IMemoryCache` (009) · no new interface (010) · no ADR-038 banned test shapes · both new `Results.Problem` sites carry error codes. **One accepted gap**: the new "Caller context not available" 500 has no error code, matching its two existing siblings in the same file — coding one of three identical 500s is worse than either consistent option |
| **Gates so far** | Unit **11,084 / 0** (82 skip, unchanged vs Wave 1) · Integration SemanticSearch **81/81** · ArchTests **79/79** · code-page jest `useSemanticSearch` **48/48** · publish **43.76 MB** (ceiling 60) · CVE **clean**. **Only Step 9.5 (`code-review` + `adr-check`) remains** |
| **⚠️ NO OPEN PR** | **#812 is MERGED.** This branch needs a NEW PR — not yet opened. Nothing blocks it |
| **Next Action** | Run `/code-review` + `/adr-check` on the 6 modified files (listed below), then commit 080. Then **072**, or **Wave 2 (075 → 076)**. 073/077/078/079 filed and ready |

### 🆕 CI FINDING 2026-08-26 — no CI had run on this branch at all

`gh run list` showed **zero runs** for `8ce4b7cac`, `53c665abb`, `c5143a776` — including the Wave 1
security commit. Cause: `ci-router.yml` triggers only on `pull_request:[master]` / `push:[master]` /
`merge_group`; `sdap-ci.yml` needs a PR; `ci-tier1-blocking.yml` is `workflow_call` + `workflow_dispatch`
only. **With no open PR, a push to this branch fires nothing.**

- Dispatched tier 1 manually → **run 32983649044 = SUCCESS**, the first green CI on this branch.
  Arch Tests (incl. the 4 newly-binding facts) ✅ · Classify ✅ · Compile ✅
- ⚠️ **`Changed-Surface Integration Smoke` and `Auth Smoke` both SKIPPED** — they are gated to
  `pull_request` events. The classifier *did* identify `Spe.Integration.Tests.SemanticSearch` as changed.
  **Opening a PR is the only way to run them.** Do not report those two as verified in CI until then.
- The `binary-tickling-yeti` plan (tier2 timeout 6→20, Router tier2-exclusion, tier2 self-collision) is
  **already applied and committed** — verified present in both workflow files. Nothing left there.

### Task 080 — files modified (all uncommitted)

| File | Change |
|---|---|
| `Api/Filters/SemanticSearchAuthorizationFilter.cs` | `scope=all` permitted w/ `RequiresPerRowParentAuthorization`; allow-list made `internal` + `TryResolveParentEntitySet` |
| `Api/Ai/SemanticSearchEndpoints.cs` | Row-level parent authorization (lazy, distinct-parent, budgeted); `/count` **refuses** `scope=all` |
| `hooks/useSemanticSearch.ts` | Entity fragment w/o record id degrades to cross-record — in `search()` AND `loadMore()` |
| `services/targetEntityNormalize.ts` | Blank-label fallback warns instead of silently widening |
| `SemanticSearchAuthorizationTests.cs` | +19 cross-record cases; reconciled the stale `Search_ScopeAll_Returns403` |
| `SemanticSearchIntegrationTests.cs` | 3 scope=all tests 403→200; **new** `Count_ScopeAll_Returns_403` |
| `notes/task-080-cross-record-search.md` | NEW — premise corrections, paging contract, perturbation table |

**Perturbation-verified on two independent mechanisms** (disjoint failure sets): neutralizing the access
check reddens **9** tests; neutralizing fail-closed parent resolution reddens **5**. Full table in the notes.

### ⛔ Do NOT re-derive these — task 080 corrected the POML's premises

1. **The dropdown's `matter`/`project`/`invoice` rows never hit `/api/ai/search`.** `deriveSearchDomain`
   routes them to `useRecordSearch` → **`/api/ai/search/records`**, which is **task 077's still-open hole**.
   080 does not make the page safe on its own.
2. **The main broken path was not a dropdown row.** It was `hasUserInitiatedSearch` dropping the launch
   scope to tenant-wide the moment the user types a query (`App.tsx:473-474`) → `scope:'all'` → 403.
3. **"Supply the missing entityId" was the wrong fix.** Those rows have no record to point at;
   `SearchRequestFragment` omits `entityId` by design. The fix is degrading to filtered cross-record.
4. **The POML's feared paging hazard does not exist.** `SemanticSearchService.cs:189` sets
   `totalResults = results.Count`, so `hasMore` is already always false on this path. The real hazard is
   **over-filtering** — a short page that looks like "no matches". Hence the `PARTIAL_RESULTS` warning.
5. **`ValidEntityTypes` has no `workassignment`**, and `account`/`contact` are valid filter values with no
   authorizable-parent mapping (so their rows fail closed). Three disagreeing vocabularies — notes §0.4.
6. **Publish 43.76 MB is the clean baseline.** The apparent −1.29 MB vs task 070's 45.05 MB is
   measurement hygiene (this run `rm -rf`'d the output dir first), not a real shrink.
7. **The code page's jest suite has ~42 pre-existing failures** (`bundleIcon is not a function` +
   `SearchFlowIntegration`). Confirmed identical with my changes stashed. Not mine, worth its own task.

### ✅ ALL THREE OWNER DECISIONS RESOLVED 2026-08-26

1. **Spaarke DOES offer cross-record search.** → `scope=all` must be *filtered*, not refused. Filed as
   **task 080** (authorize the PAGE, not the corpus — no dependency on task 031). Task 070's refusal was
   a correct stop-gap on a **false premise**; 080 is the real answer. **080 also fixes the pre-existing
   missing-`entityId` defect**, without which the code page stays broken in every dropdown state.
2. **079 has no shipping dependency** — schedule it whenever.
3. **074's CI gate: FIXED.** ✅ See below. `ci-cd-unit-test-remediation-r1` is not active, so the
   ownership block is gone.

### ✅ 074 is now BLOCKING, not advisory

Four facts added to `.github/workflows/ci-tier1-blocking.yml`'s `arch-tests` filter:
`EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver` · `NoAuthorizationFilterIsDecorative` ·
`ScannerAccountsForEveryRegistrationInTheGovernedFiles` · `TheEndpointFileCensusIsPinned`.
Verified with the exact filter string: **4 selected, 4 pass, 440 ms** (budget <30 s).

- **`sdap-ci.yml` deliberately NOT touched** — it has `continue-on-error` at both job and step level so
  it can never fail a build, AND it is open in **PR #806**. The blocking tier was the right home anyway.
- Rule B (`NoAuthorizationFilterIsDecorative`) is **not redundant** with the main gate: the route that
  leaked the tenant's documents *had* a filter, so the main rule called it gated and four human sweeps
  agreed. Only Rule B catches that shape. Do not "simplify" the set down to one rule.
- `TheEndpointFileCensusIsPinned` is included on purpose despite being a drifting count — without it the
  other three simply would not govern a newly-added endpoint file. The drift IS the forcing function.
- Also discovered: the `auth-smoke` job **already blocks** on `SemanticSearchAuthorizationTests`
  (`ci-tier1-blocking.yml:428`), so task 070's negative tests were gating CI from the moment they landed.

### Prior owner-decision detail (kept for context)

**1. `scope=all` refusal breaks shipped UI — and the underlying question is bigger.**
The SemanticSearch **code page** is an enterprise search screen. Its dropdown (from `sprk_aisearchindex`
rows) maps to scope in [`targetEntityNormalize.ts:103-123`](../../src/client/code-pages/SemanticSearch/src/services/targetEntityNormalize.ts):
"All" row → `scope:'all'` (**now 403**); any other row → `scope:'entity'` + `entityType` but **no
`entityId`** (**now 400** — `entityId` only arrives as a URL param and [`App.tsx:270-272`](../../src/client/code-pages/SemanticSearch/src/App.tsx) has a TODO saying it isn't plumbed through). A blank
config label also falls back to `all` → 403. **So the whole page is broken, not one dropdown row.**

The real question: **does Spaarke offer cross-record search at all?** If yes — and a legal-ops product
surely does — then refusing outright is the wrong shape. **Recommended: authorize the PAGE of results,
not the corpus** — let `scope=all` through, run the search, authorize the 20–50 rows about to be
returned. O(page) not O(tenant), checks are cached, needs no dependency on task 031, and it reuses the
result-level mechanism 070 already built. ⚠️ **This retracts the earlier "remove the All affordance"
recommendation**, which was reasoning about a checkbox rather than a product capability.

**2. Does 079 go in this wave or the next?** It is independent of Wave 2 (reads existing content, so the
document exists) and has a live caller.

### Phase 0c status after Wave 1

| Done | Escalated into | Filed mid-wave | Not started |
|---|---|---|---|
| 070 071 074 | 071's upload trio → **073/075/076** | **077 078 079** | 072 073 075 076 |

Three of the six new tasks came from 074's forcing function or 071's caller inventory — **none** from a
human re-reading the route table. That is 074 earning its place, demonstrated not asserted.

### Corrections carried forward — do NOT re-derive the old versions

- **074 runs in CI but CANNOT FAIL it.** `sdap-ci.yml`'s `code-quality` job has `continue-on-error` at
  BOTH job and step level; the only blocking arch job selects 7 named facts by `--filter`. A one-line
  append fixes it, but `.github/workflows/**` belongs to `ci-cd-unit-test-remediation-r1`. **Advisory
  until they take it.** Do not claim the gate is binding.
- **Compose never called the OBO routes.** `ComposeService` uses the in-process `SpeFileStore` facade.
  The original POML's "do not break Compose" warning was aimed at a risk that did not exist.
- **OBOEndpoints had 7 routes, not 5.** Now 3 (the upload trio). 074's census asserts 3.
- **The upload trio's escalation is CORRECT, not unfinished.** Uploads CREATE content — no
  `sprk_document` exists at authorization time, so `ExtractResourceId` yields a container id and the
  document filter would deny **100% of uploads** across 9 wizards. Subject is the owning RECORD → 075/076.
- **`AccessibleRecordSetService` is NOT the workforce answer today.** It resolves ADR-034 membership, not
  Dataverse's real answer; that substitution is task **031**. Use `GetCallerRecordAccessAsync` (added by 070).
- **`CallerRecordAccessProbe` already existed** (task 008) and answers the same question — couldn't be
  extended (BFF-layer; `Spaarke.Core` can't reference it). Consequence: **task 032's scope shrinks.**
- **The Create Project wizard defect is a discarded return value, NOT step ordering.** Files stage in
  React state and move only on Finish; provisioning already runs first. `provisionSecureProject` returns
  the container id and [`CreateProjectWizard.tsx:700-704`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectWizard.tsx) throws it away, so secure files land in the shared BU
  container. **~2 lines. Fully written up in task 076's POML** — read it before touching the wizard.

### Known follow-ups recorded, not fixed (detail in `notes/task-070-gate-semantic-search.md`)

New auth tests sit outside the ADR-038 KEEP paths (move to `tests/integration/auth/**`) · error-path
denials cached for the full 60s TTL · two `LookupDataverseUserIdAsync` overloads whose first `string` is
a **token** in one and an **oid** in the other (and the 2-arg one logs it) · `useAiSummary.ts:114-126`
has required `driveId`/`itemId` never sent to the server · dead client methods in two shared-lib barrels
still target deleted routes (zero invocations) · `NoWaiverIsStale` doesn't catch waivers for DELETED routes.
| **New findings** | ⚠️ **077** (`POST /api/ai/search/records`) and **078** (`GET /api/v1/containers/{id}/documents`) — both **exploitable at HEAD**, both found by 074's ArchTest on its FIRST run. POMLs written, in TASK-INDEX |
| **Status** | **PR #812 is MERGED** — continued work needs a NEW PR. BFF **deployed to dev 2026-08-25** (45.05 MB, hash-verified, healthy). Branch is ~20 commits behind master — rebase at commit time |
| **Phase** | **Phase 0 — 14 of 20** (remaining **011 012 013 015 018 020**) · **Phase 0b — 4 of 12** (**021 ✅ 022 ✅ 045 ✅ 046 ✅** · remaining **047** 023–029) · **Phase 0c — 0 of 7** (070 🔄 071 🔄 074 🔄) |
| **Next Action** | Finish 070: (a) additive record-access seam, (b) rewrite `SemanticSearchAuthorizationFilter`, (c) result-level parent check in `SemanticSearchService`, (d) drop `driveId`/`speFileId` + route PCF through a document-id-keyed path, (e) tests + build + publish size + CVE |

### Task 070 — decisions made this session (do not re-derive)

**1. `scope=all` is REFUSED, not reduced.** Simpler, safer, and no legitimate caller was found. `default:`
(empty/unknown scope) DENIES. Both were `return new AuthorizationResult(true, null)` at HEAD.

**2. The canonical authorization seam could NOT be used as-is.**
`DataverseAccessDataSource.TryRetrievePrincipalAccessAsync:509` hard-codes the RPA target as
`sprk_documents({resourceId})`, so `AuthorizationService` can only authorize `sprk_document`. It cannot
answer "may this caller read this **matter**?", which is exactly what `scope=entity` needs.

**3. Chosen fix: an ADDITIVE record-access method, not a threaded entity-type parameter.**
Threading an entity type through `IAccessDataSource.GetUserAccessAsync` would touch ~10
`AuthorizationContext` construction sites, both `IAccessDataSource` implementations, AND
`CachedAccessDataSource`'s `(userId, resourceId)` cache key (which would otherwise let a document's
snapshot answer for a record of another type). That is a shared-authorization-surface refactor and does
not belong inside "gate one route". Instead: a new method alongside the existing one — existing call
sites UNCHANGED — using the SAME authority (`RetrievePrincipalAccess`, as the caller, over OBO).
This is the seam **072** and **Wave 3's parent-inheritance** will also need.

**4. `AccessibleRecordSetService` was NOT used for the workforce plane, deliberately.** The POML named it
as the extension point, but `ComposeForSystemUserAsync` resolves **ADR-034 membership**
(`sprk_assigned*` participation) — NOT Dataverse's real answer. Gating the MDA Matter form on that
would deny the document list to any user who can read the matter but is not an assigned participant,
on the flagship form. It would be reverted, which reopens the hole. Substituting Dataverse's real
answer for workforce is task **031**'s ADR-028 A2 amendment and has not landed. Contacts still route
through the accessible-record-set path.

**5. Parent-type allow-list, not string pluralization.** The entity-set name is resolved from an explicit
allow-list; an unrecognised `entityType` DENIES rather than being guessed at.

**6. Result-level authorization for the index path = parent-id equality check on each result.** Costs zero
extra round trips (the value is already on the result) and defends against AI-Search index staleness —
a document reparented in Dataverse but stale in the index. Satisfies the POML's "a filter expression is
not an authorization decision" constraint without a per-result Dataverse call. Hot-path round-trip
count: **1** (the parent check).

### ▶ START HERE — Phase 0c, Secure Documents

**The owner decision, recorded 2026-08-25**: the BFF is the **single access-decision point** for every document and every byte, for **both** workforce and external contacts. No user is ever granted an SPE container permission — `GrantMembershipAsync` stays at zero callers. The per-project container is **blast-radius containment**, not the live ACL.

**Why now**: **zero secure projects exist in any environment.** Build this before the first one and there is never a migration. That window closes the moment a real secure project is created.

**The coordination contract is [`SECURE-DOCUMENTS-BUILD-PLAN.md`](SECURE-DOCUMENTS-BUILD-PLAN.md)** — the decision, the three invariants, what each component is *for*, verified current state, the platform constraints, and the honest claim at the end of Wave 2. **Read it before executing any 07x task.**

| Wave | Tasks | Notes |
|---|---|---|
| **1 — close the holes** | 070 072 (serialize — shared auth surface) · 071 073 074 (`parallel-safe: true`) | **070 and 073 are exploitable at HEAD** |
| **2 — make the container real** | 075 → 076 (strict) | Can run concurrently with Wave 1 |

**074 is the highest-value task in both waves** — it makes ungated routes a build failure. Everything else closes a specific hole; 074 closes the way holes get added.

### The two findings that drive Phase 0c

**Exploitable now**: `POST /api/ai/search` returns allow for **every** scope including `default` and `scope=all` — any authenticated non-admin gets tenant-wide document names, AI summaries, TL;DRs, `driveId` and `speFileId`. It never touches SPE, so container permissions are irrelevant to it. And `PUT /api/containers/{containerId}/files/{*path}` takes the container id off the route and writes **app-only (MI)** — no container ACL needed.

**The structural one**: **nothing reads `sprk_project.sprk_containerid`.** Provisioning stamps it; every write resolves from the acting user's BU or a global archive. So secure documents land in **shared** containers — and SPE permissions are **additive-only** (*"you can't break inheritance on arbitrary files or folders"*, verified against Microsoft docs 2026-08-25), so **no per-item permission can ever retract that**. Per-project containers are the only mechanism, which makes task 075 the document guarantee.

⚠️ **Latent, not exploitable**: the `OBOEndpoints` drive-keyed routes (071) and `share-link` (072) are **OBO**, so SPE denies without a container ACL — and no user has one. They are bypasses by construction, not live holes. Do not overstate them.

### Corrections carried forward — do not re-derive the old versions

- **FR-29 delegation IS implemented** (`DelegationRuleFilter`, Write-on-record via OBO, fail-closed). This is *why* Manage Access silently fails: the server correctly 403s and **the UI swallows it**. UI defect only.
- **The contact document path is CORRECT** and is the **reference implementation** for Wave 3's inheritance — `ExternalProjectDataEndpoints` checks project access AND doc∈project before any SPE read.
- **`DocumentAuthorizationFilter.ExtractResourceId`'s container fallback is inert and fail-closed** — a driveId is not a document GUID, so it denies. Not a finding.
- **The isolation guarantee is "no ordinary human sits at or above the secure BU in the tree"** — NOT "reduce the depth". `Deep` is fine at a *sibling* BU; validated live.



### 🔔 OWNER DECISION REQUIRED — task 046 found that secure projects are NOT isolated

**Proven empirically, not inferred.** `Test User 1` — an ordinary non-admin user — **read a real
`sprk_issecure=true` project** owned by the `Secure Project` owner team, sitting in the `Secure Project`
BU. Cause: **`Spaarke Basic User` holds `prvReadsprk_Project` at `Deep` depth**, and `Deep` held at the
**root** BU reaches every descendant BU.

This is **design §5.2's blocking prerequisite, still unremediated** — not a new defect. §5.2 inferred it
from a depth census on 2026-08-20; task 046 exercised the whole mechanism against a real record. The
**negative control passed** (a `Basic`-depth principal WAS denied on the same record), which is what
establishes that BU containment works correctly *once no ordinary role holds `Deep` or `Global`*.

| Fix | Blast radius (measured live 2026-08-25) | Note |
|---|---|---|
| **A — BU restructure** (§5.2's already-decided direction): users out of root into an Operations BU; secure BU becomes a **sibling** | Larger — every user's BU changes; secure BU re-parented; BU-cascade container re-seeded | Durable; survives future role edits |
| **B — narrow the depth**: `Spaarke Basic User` `prvReadsprk_Project` `Deep`(4) → `Local`(2) | **ZERO today** — all 18 real projects and all 5 human users are in the root BU, so `Local` preserves current visibility exactly | One reversible edit, but a *role* guarantee, so a later role edit can silently undo it |

**Not applied by 046 on purpose** — editing an ordinary end-user role changes every user's effective
access. B closes the exposure now at near-zero risk while A is scheduled; they are not exclusive.
Detail: design §5.1a-2. **Do NOT "fix" it by removing `sprk_project` Read from ordinary roles** — a
share confers nothing without the entity privilege, so that would silently disable all sharing.

### ⚠️ What this does to task 047's claim

047 can validly conclude **"provisioning runs end-to-end"** — worth doing, since provisioning has never
succeeded in any environment. It **cannot** conclude "isolation works" until the decision above lands.
Keep those claims separate in the report.

### The one thing that needs the OPERATOR, not the agent

**Task 047 (live provisioning validation) needs the BFF deployed to dev.** The `Deploy BFF API`
workflow is **`disabled_manually`**, so that deploy is operator-driven. Sequence:
**~~046 (agent)~~ ✅ → deploy (operator) → 047 (agent).**

### What 046 configured in live dev (`spaarkedev1`) — already done, do not redo

| | |
|---|---|
| `Secure Project Owner` | `roleid e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`, in the `Secure Project` BU |
| Privileges | **exactly 1** — `prvReadsprk_Project` @ **User (`Basic`)** depth (hypothesis said 7 @ BU depth — wrong in both dimensions) |
| Held by | that one owner team; **0 users, 0 other teams** |
| `System Administrator` | **REMOVED** from the team; assignment re-proven *after* removal |
| Team members | **0** |
| Test artifacts | probe project deleted — 0 secure projects, 0 projects in the secure BU |

Runbook: [`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) ·
write-up: [`notes/task-046-secure-project-owner-role.md`](notes/task-046-secure-project-owner-role.md)

### Still open from 046

- **Child-entity ownership** — **18 Spaarke entities via 19 lookups** carry a project lookup (the POML
  said 3); `sprk_document` carries **two** (`sprk_project` *and* `sprk_relatedproject`, so a one-lookup
  check misses half the cases). **Nothing assigns children to the secure team**, so they are unisolated
  independently of the depth defect and would stay so after it is fixed. **Needs its own task** —
  extending task 021's assign is the wrong shape (children are created continuously, long after
  provisioning returns; this needs a create-time rule). Sequence with `spaarke-secure-project-r1`.
- **FR-28's share→read assertion is untestable** until the depth fix lands — every human with
  `sprk_project` Read holds `Deep`/`Global`, so no record exists that they cannot already read.

### Live Dataverse facts task 046 needs (verified 2026-08-25 — do NOT re-derive from docs)

| Fact | Value |
|---|---|
| Secure BU | **`Secure Project`** — SINGULAR — `d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`, parent = root `Spaarke`, created 2026-08-25 08:28 |
| Its default owner team | `Secure Project` — `daec0b6f-80a0-f111-aaac-000d3a99d1d7`, `teamtype=0` (Owner), `isdefault=Yes` |
| Team members | **ZERO** ✅ (design §5.1a requires this) |
| Team roles | **ONLY `System Administrator`** (`3980a53d-b0cf-3ded-37c8-4d4f9b94acef`) — 🔴 task 046 removes this |
| Roles matching `Secure%` | **NONE EXIST** — `Secure Project Owner` has never been created |
| Secure projects in dev | **ZERO — none has ever been provisioned** |
| `SP-*` per-project BUs | **NONE** — the retired mechanism never succeeded, so there is no legacy debris |
| Root BU `Spaarke`.`sprk_containerid` | `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` |
| `Secure Project` BU.`sprk_containerid` | **`null`** ✅ correct by design |
| Dev BFF app service | **`spaarke-bff-dev`** in `rg-spaarke-dev` (the e2e spec's `spe-api-dev-67e2xz` default is STALE) |
| `SharePointEmbedded__ContainerTypeId` | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` ✅ configured |
| `SecureProject__BusinessUnitName` | **NOT SET** → the endpoint uses the code default, which is why the singular/plural fix was load-bearing |

⚠️ **Three projects share that root-BU container id** (`Intellectual Asset Management System Patent`,
`Clarivate Plc Q3 2025 Earnings Disclosure`, `Test New Matter via Workspace`). That is the wizard's BU
cascade stamping SHARED storage onto projects — the mechanism behind both the 409 regression and design
§5.1c's isolation gap. **For task 047: assert INEQUALITY against every BU container, never presence of
a value** — a populated field is exactly the false positive.

### 🔴 Task 046's headline finding, restated so it is not lost

The owner team holds **`System Administrator`**. It is memberless so nothing is exposed *today*, but it
is one membership row from full admin rights on the BU that NFR-05 exists to guard, and review §D says
of this exact question *"None — and definitely NOT System Administrator."*

**Consequence**: task 021's escalation trigger for "the team lacks entity privileges" **cannot fire in
dev** — assignment succeeds because the team is omnipotent, not because it is correctly scoped. **A
green provisioning run in dev is NOT evidence the role is configured.**

⚠️ **046 treats design §5.1a's privilege list as a HYPOTHESIS, not a spec.** For a team that owns the
records, **User depth may suffice** and is tighter than the Business-Unit depth currently written down —
which would *narrow* NFR-05's exemption. Determine empirically; record the error that forced each
privilege you add.

---

## What 021 and 045 shipped (both on master)

**021 — provisioning matches design §5.1.** Resolves the ONE canonical BU **by name** from
`SecureProject:BusinessUnitName` (`$top=2`, fails closed on absent AND ambiguous, never falls back) →
assigns the project to that BU's **default owner team** and **reads the owner back to verify** →
creates the project's own SPE container → records it on `sprk_containerid`, **failing loudly with the
container id** if that write cannot land (ADR-003). Deleted: BU creation, account creation, both
rollbacks, three resolvers, the umbrella branch, and three response members. `sprk_externalaccount` —
the project's **CLIENT** lookup — is never written, pinned by a test.

**The live 409 regression is CLOSED.** The marker is now **ownership**, which only provisioning writes;
`sprk_containerid` was shared state, which was the whole bug.

**045 — auth-v4 integration.** `CallerRecordAccessProbe` ported off its own client secret onto
`OrderedCredentialClientProvider` (ADR-028 A4; FR-F1/FR-F2 pass with **no** allowlist or census entry).
Plus 5 Moq ctor sites, 6 fixtures needing `Graph:ManagedIdentity:Enabled`, and master's own 6 stale
tests. Full write-ups: [`notes/task-021-provisioning-stamping.md`](notes/task-021-provisioning-stamping.md)
and [`notes/ci-dark-and-authv4-integration-2026-08-25.md`](notes/ci-dark-and-authv4-integration-2026-08-25.md).

### ⚠️ What is NOT achieved yet — do not overstate this on master

- **No document isolation.** Nothing READS the project's `sprk_containerid` yet; that needs the three
  container-resolution strategies special-cased → project **`spaarke-secure-project-r1`** (design.md
  drafted, 4 open questions awaiting the owner).
- **No human can reach a secure project.** FR-28's explicit share (access teams, design §5.1b) is
  outstanding. The record is isolated but **unshared**. Still needs its own task.
- **OBO correctness is unproven.** No test performs a real exchange (P5 unreachable offline —
  `OrderedCredentialClientProvider` is `sealed`). Task **034** owns live verification.
- **Provisioning has never run successfully in ANY environment.** Task **047**.

---

## Four lessons that keep paying off — apply to every remaining task

**1. A misleading "it passed" now has FOUR causes, not two.** (a) test at the wrong level,
(b) perturbed code unreachable, (c) — task 021 — **a FAKE that ignores part of the contract**
(its fixture ignored `$top` and the discriminating `$filter` predicates, so two perturbations looked
"covered" by accident; *a fake is evidence only to the extent it refuses what Dataverse would refuse*),
and (d) — task 046 — **the platform answered from a STALE CACHE.** Dataverse's principal-privilege
cache lags role edits by ~one operation; an early 046 pass reported *"assignment allowed with zero
privileges"*, which taken at face value would have justified shipping a role that grants nothing.
**Defences**: re-probe until stable across ≥3 polls, and cross-check the `privilegeCount` reported in
any denial against the role's real privilege count. Run a zero-privilege control — if a role with no
privileges still allows the operation, every reading in that session is void.
*All four share one shape: the observation was real, but it was not an observation of the thing you
thought it was.*

**1b. Configuration-shaped assertions miss depth-shaped holes.** Task 046's headline finding —
ordinary users can read secure projects — is invisible to any check that enumerates roles "scoped to
the secure BU". `Spaarke Basic User` names that BU nowhere and reaches it anyway, via `Deep` at an
ancestor. **Reach is a property of depth held at an ancestor, not of the target.** Prefer the
empirical form: provision the record, attempt an impersonated read as a known non-admin, require
denial. Same shape as NFR-04's negative canary — **success where you expect denial is the signal.**

**2. Read the GATE, not a substitute — and check the gate EXISTS.** A conflicted PR produces **NO
gate, not a red one**: GitHub cannot compute `refs/pull/N/merge` and dispatches zero workflows. Two
pushes went unadjudicated while a local suite was green. **Verify a `github-actions` check suite exists
for the SHA** (`gh api repos/{owner}/{repo}/commits/{sha}/check-suites`) before claiming anything.
Related: master's Router can be green while a whole test project fails, because tier1 runs a
**changed-surface filtered subset** and tier2 (which runs everything) is **advisory**.

**3. A merge conflict is not the only way two branches collide.** Task 045 hit the same invisibility
pattern three times — a duplicated credential site, a duplicated stale-test repair, and a duplicated
`.csproj` glob that merged **textually clean and semantically broken** (`NETSDK1022`, whole test
project fails to build, no conflict to warn you). When merging a long-lived branch, check for
*semantic* duplicates, not just textual ones.

**4. Mocking at a seam proves the CALLER, never the CALLEE.** 045 found `CallerRecordAccessProbe` had
**zero** test coverage because every fixture substituted it — its precondition logic could be inverted,
opening the whole delegation gate, with the suite green.

---

## Verified baselines (as of `290d9ab79`, on master)

- **All 7 test projects: 11,715 passed / 0 failed** — `Sprk.Bff.Api.Tests` 11,075 ·
  `Spe.Integration.Tests` 372 · `Sprk.Bff.Api.IntegrationTests` 96 · `Spaarke.ArchTests` **69** ·
  `Spaarke.Scheduling.Tests` 46 · `Spaarke.Core.Tests` 45 · `RecordSyncJob.IsolatedTests` 12
- **Publish 43.75 MB** compressed incl. PDBs (ceiling 60). `--vulnerable` clean. BFF build 0 errors.
- **`Router = SUCCESS`**; main repo local master synced and rebuilt clean from that checkout.

**The suite gate is `dotnet test` at the root PLUS three projects it does not pick up:**

```
dotnet test -c Debug                                              # 4 projects
dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj
dotnet test tests/unit/Spaarke.Core.Tests/Spaarke.Core.Tests.csproj
dotnet test tests/unit/RecordSyncJob.IsolatedTests/RecordSyncJob.IsolatedTests.csproj
```

Running one project and reporting "full suite green" is how six tasks' worth of breakage was missed.

⚠️ `Sprk.Bff.Api.Tests` **silently vanishes from a root `dotnet test`** when it fails to BUILD (exit 1,
no `Failed!` line). If it is absent from the output, build it explicitly before believing anything.

---

## Recommended order

**046 → [operator deploy] → 047 → 025 → 023 → 029 → 028 → 024**, with **026 and 027 runnable any
time**. **026 is higher value than its position suggests** — it repairs
`secure-project-fields-schema.md`, the stale doc that CAUSED Critical findings C4/C5.

Also open: **Phase 0's 011, 012, 013, 015, 018, 020**, and a task still needed for **FR-28's access
teams** (design §5.1b).

---

## 🔔 Owner decisions waiting (read before the next task)

| # | Decision | Where |
|---|---|---|
| ~~D1~~ | ✅ **RESOLVED 2026-08-23** — ADR-028 A4 path A accepted; to be handled in the broader MI migration. Recorded in [`design.md` §9](design.md) | `notes/task-008-delegation-rule.md` §7 |
| ~~D2~~ | ✅ **RESOLVED 2026-08-23** — Write stays (Dataverse `CreateAccess` is an entity-level privilege, not a right on an existing record, so requiring it would deny everyone; Write is also exactly what the endpoint's own `UpdateAsync` needs). The underlying risk was **idempotency**, now closed by a 409 guard. No admin role introduced, per the owner's constraint | `notes/task-008-delegation-rule.md` §10.2 |
| **D3** | **Download enforcement vs `CanDownload`** (from 002/006): enforcement requires **Read**, the capability requires **Write**. Benign in effect but it IS the divergence FR-05 criterion 5 exists to prevent | `notes/task-002-download-authorization.md` §4 |

---

## Session summary — what was accomplished

Eleven Phase 0 tasks, all on PR #812. **FR-01 → FR-09 and FR-13 are closed**, plus part of FR-17 and
NFR-07.

| Task | What it closed |
|---|---|
| 001 | 62-test characterization suite; **first ever backfill** of the `tests/integration/auth/**` KEEP path |
| 002 | **R1's January-2026 attack scenario** — `/download` had no per-document filter; also closed `/content` |
| 003 | 4 missing `OperationAccessPolicy` keys + a source-scanning completeness gate |
| 004 | `AuthorizationService` evaluates **as the caller** |
| 005 | The `AccessRights.Read` ceiling — `RetrievePrincipalAccess` replaces a "can I read it → therefore Read" probe |
| 006 | `PermissionsEndpoints` caller-scoped; **FR-02's criterion closed** |
| **007** | **A-5 — grant expiry.** `sprk_expiresdate` was written and read NOWHERE; expired grants conferred access forever while the UI showed expiry as working |
| **008** | **A-6 — the delegation rule.** Six external-access mutations were behind bare `RequireAuthorization()`. **Unblocks task 065** |
| 010 | **A-11, ranked #1 of 13** — `/grant` upserts, `/revoke` sweeps every row on the logical key |
| 014 | Auth-mode segment in the cache key (`sp`/`obo`) |
| 019 | `LookupUserMembership` no longer sends `["*"]` |

### Method that keeps paying off — apply it to every remaining task

**Verify tests discriminate by breaking the fix and watching them fail.** Done on every task; it has
caught real gaps every time.

| Perturbation | Failures |
|---|---|
| Revert the single-doc token (006) → then the batch token | 2 → 3 |
| Transpose `AppendToAccess → Append` (005) | 4 of 15 |
| Remove the `/content` filter (002) | 2 of 17 |
| Drop `_sprk_contact_value eq null` (010) | 3 of 22 |
| Reduce revoke to the named row (010) | 2 of 22 |
| **Detach the delegation filter (008)** | **17 of 36** |
| **Weaken it to "any rights at all" (008)** | **8 of 36** |
| **Resolve revoke's target from the request body (008)** | **1 of 19** — the one test that isolates it |
| **Point the entity check back at `sprk_documents` (008 follow-up)** | **6 of 9** |
| **Disable the provisioning idempotency guard (008 follow-up)** | **4 of 5** |
| **Drop the expiry predicate (007)** | **2 of 11** |
| **Drop the `eq null` branch (007)** | **1 of 11** |
| **`ge` → `gt` on a Date Only column (007)** | **1 of 11** — the boundary-day test |
| **Ungroup the org disjunction (007)** | **1 of 11** |
| **Revert the `$select` to `_sprk_contactid_value` (016)** | **14 of 20** |
| **Restore the null-contact exclusion (016)** | **6 of 20** |
| **Rethrow instead of the typed enumeration response (016)** | **2 of 20** |
| **Ignore `failedCount`, always 200 (016)** | **2 of 20** |
| **Drop the unaddressable-row guard (016)** | **1 of 20** |
| **Match the SPE permission on the contact GUID again (017)** | **2** |
| **Restore false success on SPE no-match (017)** | **3** |
| **Report a Graph error as genuinely-absent (017)** | **2** |
| **Re-swallow SPE listing failures (017)** | **2** — *initially 0; see the lesson below* |
| **Ignore per-member SPE removal failures (017)** | **1** |
| **Restore the broken provisioning `$select` (review fix)** | **7 of 7** — and the same names passed **5 of 5** before the guard was ported |

**Capture failing-test identity with TRX**, not `-v q`:
`dotnet test … --logger "trx;LogFileName=t.trx"`, then parse `outcome="Failed"`.

---

## Full State (Detailed)

### Decisions made during the review (most recent)

| Decision | Rationale |
|---|---|
| **Fix the provisioning `$select` immediately, before synthesis** | It broke a shipped endpoint on the branch. Everything else in the findings is analysis; this was a live break |
| **Do NOT guess the `@odata.bind` nav-property casing** | Deferred to task 021 with a mandatory `$metadata` step. Nav props are case-sensitive and not derivable from the attribute name; a wrong one is accepted as an unknown property and the write silently does not happen — the exact class under review. No secure project exists in dev to read the casing back from |
| **Fix names AND the swallow together in 021** | Names alone leaves the next drift invisible; the swallow alone hard-blocks provisioning on names we know are wrong |
| **Port task 016's `$select`-validating fake to the provisioning fixture** | The guard already existed one directory over and was not carried across — which is precisely why 5 of 5 tests stayed green while the endpoint 500'd |
| **KEEP `GrantMembershipAsync`** (owner ruling) | Verified: one code occurrence repo-wide, no reflection path, unreachable from any endpoint, no other worktree or open PR references it |
| **Defuse task 009's POML now, not as a task** | It is a pending security task whose POML told the executor to flip a nonexistent characterization and named task 011's contended file. Under literal execution it would have WEAKENED the fail-closed gate it exists to strengthen |
| **File the review findings as a doc, propose tasks, do not create 7 POMLs unilaterally** | Seven tasks is a scope decision that belongs to the owner |

### Decisions made in task 017

| Decision | Rationale |
|---|---|
| **Delete the endpoint's forked matcher rather than fix it** | `SpeContainerMembershipService.RevokeMembershipAsync` already matched on email correctly and had **zero callers**. The endpoint had forked a working implementation and broken it — CLAUDE.md §11 says reuse, so the fork goes |
| **Keep the SPE removal path** (escalation did not fire) | Nothing in the codebase ADDS a container permission, so this is a cleanup path for legacy/admin ACLs — exactly the ones nothing else will clean. `NoPermissionFound` is therefore the healthy answer, not a problem |
| **4-state `SpeContainerOutcome`, not a bool** | ADR-003 requires distinguishing "confirmed absent" from "match failed". The old bool answered `true` for both, which is how A-13 hid |
| **"No email" → `Failed`, not `NoPermissionFound`** | Without the key an existing permission is unfindable. That is unknown, not absent — calling it absent would repeat A-13 in a new place |
| **Keep `SpeContainerMembershipRevoked`, made honest** | Existing readers get a correct value instead of a constant. Only the relic (`WebRoleRemoved`) was removed |
| **`GrantMembershipAsync` NOT deleted** | It is dead (zero callers) and H-8b says remove dead branches — but it defines the identity key the matcher must match. Documented with a "no callers by design / broker-only" header and **flagged for the owner** rather than silently deleting a public method |
| **`ListExternalMembersAsync` propagates** | An empty list must mean one thing. Catching everything and returning `[]` is what made "Graph unreachable" indistinguishable from "empty container" |
| **Per-member removal failures counted, loop not aborted** | Aborting leaves strictly MORE access in place. Same reasoning as task 016's deactivation sweep |
| **Org-grant SPE cleanup filed, not fixed** | No single grantee → no email. Needs org→members expansion (declined in 016 for cache too). Bounded: broker-only creates no member ACLs |

### Decisions made in task 016

| Decision | Rationale |
|---|---|
| **`_sprk_contact_value`, confirmed against live metadata** | Three sources agreed (live metadata, `ExternalParticipationService`, `ExternalGrantKey`); the solution's `views-schema.md` says `sprk_contactid` and is **stale**. There is no `sprk_contactid` attribute on the table at all, so the escalation trigger did not fire |
| **Drop the null-contact filter entirely** | A null contact IS the organization-grant discriminator. Requiring a contact was not a safety check — it silently excluded every org grant from closure |
| **An id-less row is a FAILURE, not a skip** | It cannot be PATCHed, so it cannot be deactivated. Skipping it quietly would leave an active grant behind a 200 — the same false-success shape, one layer down |
| **Partial deactivation now returns non-success (in-scope extension)** | Not in A-12; found while fixing it. The loop swallowed per-row errors and returned only the success count, so 2-of-5 revoked answered `200 OK`. Precedent one directory over: `ExternalGrantLifecycle.DeactivateAsync` (task 010) |
| **Continue-on-error is KEPT** | Aborting at the first failure leaves strictly MORE access standing. What changed is that failures are counted and reported, not that the sweep stops |
| **Steps 3–4 run before the failure is returned** | Both only ever REMOVE access, so running them makes a partial state strictly less open. Closure is idempotent, so "retry" is sound |
| **`ExternalAccessRow` `private` → `internal`** | The reason A-12 survived: no test could name `QueryAsync<ExternalAccessRow>`. ADR-038 §4 seam via `InternalsVisibleTo`; ban B8 (reflection) avoided |
| **The fake table validates the `$select`** | Load-bearing. A fake that ignored the projection would have gone green on the exact code that shipped A-12 |
| **SPE guard added but NOT tested** | `ListExternalMembersAsync` swallows everything and returns `[]`, so the guard cannot fire today. Documented as untestable-today rather than covered by a fake exception the service cannot throw — and filed on 017 |
| **Tests at `tests/integration/auth/**`, not the POML `<outputs>` unit path** | The `task-001` constraint is explicit; that path is deletion-protected, the unit path is not; every Phase 0 task so far landed there |

### Decisions made in task 007

| Decision | Rationale |
|---|---|
| **`ge`, not the POML's prescribed `gt`** | `sprk_expiresdate` is **DATE ONLY** (verified live). `gt` kills a grant at 00:00 ON its expiry date, silently shortening every dated grant by a day. "Access until 30 June" means 30 June works. FR-06's acceptance is an expiry **in the past**, which `ge` satisfies |
| **Bare `yyyy-MM-dd`, never a timestamp** | A datetime literal against a Date Only column risks a 400 — and a 400 here returns an EMPTY grant set, i.e. a silent total access outage, not a visible error |
| **`eq null` branch is mandatory** | OData `ge` excludes nulls; most grants have no expiry. Without it the predicate revokes every open-ended grant — an outage, not an expiry bug |
| **Revocation paths deliberately do NOT filter expiry** | `ExternalGrantLifecycle` (upsert + revoke sweep) and `ProjectClosureEndpoint`'s cascade must SEE expired rows — filtering there makes expired grants **unrevokable**. "Add it everywhere" was the obvious reading and would have introduced a new defect |
| **The display path got the predicate too** | `GetProjectContactIdsAsync` feeds a list whose contract says "active access". A participant list that disagrees with enforcement tells an operator someone still has access when they do not — that is how a revocation gets skipped |

### Decisions made in task 008

| Decision | Rationale |
|---|---|
| **Group-level filter, target resolved by bound request TYPE, default DENIES** | A seventh route added to `/api/v1/external-access` later is gated from its first request rather than inheriting A-6. Failure is loud and immediate — the right direction for an authorization default. Path strings would drift from five other files |
| **New `CallerRecordAccessProbe` instead of `AuthorizationService`** | `DataverseAccessDataSource` hard-codes `sprk_documents({id})` in BOTH its RPA target and its fallback probe → answers `None` for a project for EVERY caller. The filter would have denied universally |
| **Not `IDataverseUserClient`** (which is the right shape) | Twice-gated: compound AI gate + `ToolFramework:Enabled`. Six unconditional routes depending on it = §10 F.1 asymmetric registration, plus a CRUD→AI dependency |
| **OBO `WhoAmI()` for the principal** | RPA takes the principal as an ARGUMENT; an app-only version would carry the caller's identity as *data*, and a wrong id silently answers about the wrong person — the A-2 shape. Under OBO the identity is the *credential* |
| **No read-probe fallback** | A read proves Read; Read is not licence to grant. Consequence accepted: an RPA outage denies all six mutations rather than widening them |
| **`/revoke` follows the ROW's root, not the body's `projectId`** | Otherwise a caller with Write on any project of their choosing could revoke grants on a matter they cannot touch |
| **`/invite` now requires a resolvable root** | It provisions a CIAM identity. Contract narrowing; the only first-party caller already sends `projectId` as required |
| Mapper `internal` → `public` | Second production consumer in another assembly. The alternative — a second copy of the name→flag table — is exactly how an `AppendAccess`/`AppendToAccess` transposition gets introduced |

### Carried forward — read before ANY remaining task

| Item | Detail |
|---|---|
| **SEVEN test projects, not one** | See the process-failure box above. `dotnet test` at root covers 4; ArchTests / Core.Tests / RecordSyncJob.IsolatedTests need explicit invocation |
| **POML paths are unreliable** | Tasks 002/005/006/008/007/**016** all named test paths that do not exist or that a later constraint overrides — six of twelve. **Verify every path before acting on it** |
| **Publish size is COMPRESSED** | Raw bytes are ~137 MB, the ceiling is 60. Zip `deploy/api-publish/` before reporting. Measuring raw once produced a false "3× over ceiling" scare |
| **A fake that ignores the `$select` will go green on a broken projection** | Task 016 built a fake that rejects unknown columns; the provisioning fixture had none, so 5 of 5 tests passed while `/provision-project` 500'd. **When an endpoint reads Dataverse, its fake must validate the projection.** Now ported to both |
| **Verify EVERY column you add, not just the ones you came to fix** | The review found five stale-column instances; the fifth was introduced by the same session that fixed three. Fixing an instance of a class does not inoculate the next line you write |
| **Mocking at a seam proves the CALLER, never the CALLEE** | Task 017: re-swallowing listing failures passed EVERY endpoint test, because the closure tests substitute `RemoveAllExternalMembersAsync` at its seam and never reach `ListExternalMembersAsync`. The fix a binding constraint asked for was untested until a perturbation exposed it. **When a task's deliverable is "make X report failures", test X directly** |
| **A green local suite is NOT CI — read the gate, not the substitute** | This project reported "11,374 passed locally" as verification for six consecutive commits while `CI / Router` had never once rendered a verdict on the branch (17 runs, 0 successes). Local runs never execute Arch Tests, Changed-Surface Integration Smoke, Auth Smoke, Plugin Size or the Last-Reviewed stamp. **And when the gate is red for reasons that look unrelated to the diff, that is a finding to chase — not noise to route around.** It hid a repo-wide CI defect for weeks |
| **A check with only a happy-path test is not tested** | Task 009 hit the zero-failure perturbation TWICE. (a) Two guards denied the same case, so a status-code assertion could not tell them apart — deleting the A-7 fix left every test green; fixed by asserting WHICH guard denied. (b) The new work-assignment membership check had a positive test but no negative one — bypassing it entirely failed zero; fixed by adding the negative. **Pair every positive with a negative, and assert the distinguishing observable.** |
| **Check the `$orderby`, not just the `$select`** | H5's sixth stale-column instance sat one line below a CORRECT `$select`. Reading the select gave a false all-clear for months. Verify EVERY clause that names a column — select, filter, orderby, expand, and `@odata.bind`. |
| **A zero-failure perturbation has TWO causes — distinguish them** | Either the test is at the wrong level, **or the perturbed code is unreachable**. Task 022's `BulkDownloadAuthorizationFilter` catch inverted to fail-open broke 0 of 30 — not a coverage gap: `AuthorizeAsync` absorbs its own exceptions, so nothing reaches that catch. Proved with a two-factor experiment (force `AuthorizeAsync` to throw outside its try → 14 failures; do that AND invert the catch → 17; **the 3-test delta IS the guard's coverage**). Rewriting tests would have added coverage for a path that cannot execute. **Check reachability before "fix the test".** |
| **A doc comment asserting "enforcement happens elsewhere" is a claim to verify, not evidence** | Task 022 found four. `BulkDownloadAuthorizationFilter` said twice that per-document access was "enforced at Dataverse lookup time via the user's identity (same model as `preview-url`)" — the lookup is app-only, and `preview-url` had no authorization either, so the claim cited a route making the same empty claim. `/checkout` claimed OBO+PCF enforcement on an app-only path. But `share-link`'s identical-sounding claim was **TRUE** (`CreateSharingLinkAsUserAsync` really does call `ForUserAsync`). **Check the named mechanism — the pattern is valid, the instances vary.** |
| **State the blast radius you verified, not the one that sounds worse** | I nearly shipped "any authenticated caller could mint a url for any document by GUID" for the five URL-minting reads. They use OBO, so Graph already enforced SPE access; the gate is a second, narrowing boundary. Overstating a finding in a comment is the same defect as understating one — both mislead the next reader. |
| **A perturbation harness needs a clean-tree BASELINE and fresh mtimes** | Task 022's first sweep produced FAKE numbers. The harness restored files with `shutil.copy2`, which preserves the *backup's* mtime — older than the built DLL — so MSBuild skipped recompiling and some runs measured a **stale binary still carrying the previous perturbation**. It reported 3 failures where the truth was 1. Two fixes, both mandatory for any future harness here: `os.utime(f, None)` after restore, and a clean-tree baseline run that must be **0 failures** before the sweep. Without the baseline every count is measured against unknown noise. Caught only because an unexplained number was checked instead of accepted. |
| **A doc comment claiming "enforced elsewhere" is a finding, not evidence** | `/checkout`'s comment said "PCF controls button visibility based on Dataverse security profile / actual permissions enforced by Graph API via OBO". Both halves false: client-side button visibility is not enforcement, and the path is app-only so nothing downstream saw the caller. Sixth doc-comment-lies instance in this area. **When a comment explains why no check is needed, verify the mechanism it names actually runs on that route.** |
| **Distinguish "the gate needs X" from "the service lacks X"** | I recorded C2 as "NOT a filter attachment — needs a signature change with call-site fallout" because `DeleteAsync` takes no identity. Wrong: `DocumentAuthorizationFilter` reads identity from `HttpContext`. The missing parameter was a real observation (app-only destroy → no defence in depth) attached to the wrong conclusion (a blocker). It nearly cost a whole extra step. |
| **Do not attach an authorization filter before its operation key exists** | `OperationAccessPolicy.GetRequiredRights` throws on an unknown operation and the filter's catch returns 500 — fail-closed, but that means the route becomes an unconditional 403 for EVERY caller. Already happened once (finance surface + Office save + three document reads); the file's header records it. |
| **Do not push again while a CI run is in flight** | The 13 cancelled Router runs are self-inflicted: push cadence (13:39 → 13:53 → 15:06 → 15:09 → 15:24) outran a ~9-min Router with `cancel-in-progress: true`, so each push killed the previous verdict. **After the last push of a work session, wait for the gate before pushing again** — otherwise the branch accumulates commits that were never adjudicated |
| **Look for an existing correct implementation before fixing a broken one** | Task 017's bug was a FORK of working code that had zero callers. Grepping for the method name first turned a "patch the matcher" task into a deletion |
| **Frontend tests need `npm install` first** | `node_modules` is absent in a fresh worktree; `npm test` fails with "jest is not recognized". Use `npm install --legacy-peer-deps --no-audit --no-fund` (never `npm ci`, per root CLAUDE.md §12) |
| **Don't put backticked markdown in a bash-quoted Python heredoc** | Bash treats backticks as command substitution and silently mangles the text. Write the script to the scratchpad and run it as a file |
| **Schema docs lose to live metadata** | `views-schema.md` says `sprk_contactid`; the table has no such attribute. Two Phase 0 tasks (007 type, 016 name) turned on checking live metadata rather than trusting a doc |
| **Some POMLs are not valid XML** | `007` (and `017`) carry a raw `<` inside a constraint (`Mock<HttpMessageHandler>`), so `ET.parse` fails on them. Pre-existing; `scripts/Validate-TaskPoml.ps1` reports PASS because it is not a strict parse. Do not "fix" a POML on the strength of a parse error alone — check whether it predates you |
| **KEEP paths** | Access-control → `tests/integration/auth/**`; pure domain logic → `tests/unit/domain/**`. Both globbed into `Sprk.Bff.Api.Tests.csproj` |
| **Vacuity trap** | Offline, real auth dependencies fail closed, so "all denied" is true before AND after a fix. Substitute a double that CAN answer yes, then break the fix to prove the tests bite |
| **Shared-fixture write logs bleed across tests** | `IClassFixture` gives ONE fixture per class; a `ConcurrentBag` recording writes accumulates across every test in it. A "created nothing" assertion then fails on another test's residue — or, worse, passes on it. Reset from the test-class constructor (`ProvisionProjectTestFixture.Reset()`) |
| **Moq + generic methods** | `QueryAsync<T>` returning `Task<List<T>>` cannot be stubbed with a plain lambda when `T` is the handler's own private DTO. Use `new InvocationFunc(...)` + reflection over `invocation.Method.GetGenericArguments()`, returning the JSON wire shape so the handler's own `[JsonPropertyName]` bindings stay under test (this is what keeps `_sprk_securitybuid_value` honest) |
| **NEW (008): DI resolves BEFORE endpoint filters** | Minimal API binds a handler's DI arguments before the filter pipeline. `CiamUserProvisioningService` throws without `Ciam:Domain`, so `/invite*` answered 500 *before* the filter ran. Not a hole, but a 403-free assertion on such a route proves nothing. Test fixtures for this group need the CIAM keys |
| **Doc comments in this area lie** | Five cases now: `CachedAccessDataSource`; `DataverseAccessDataSource`'s "Dataverse enforces Write/Delete separately"; a task-001 test claiming 005 would flip it; `RetrievePrincipalAccess` documented as used with zero call sites; and the POML's claim that `provision-project` has no target record (it does) |
| **`/api/v1/external` fixture trap** | `AuthPolicies.ExternalCollaboration` pins `Ciam` + `Bearer`, bypassing `FakeAuthHandler` → 500. Use `ExternalCollaborationTestFixture` |
| **Bash cwd drift** | A bare `cd` persists across calls. Prefix with `cd /c/code_files/spaarke-wt-unified-access-control-r2` |
| **CI bot pushes** | A `dotnet format` bot auto-commits to the branch. **Pull/rebase before pushing** |
| **Own-coverage obligation** | Tasks **007, 012, 013, 015, 016, 017, 018** have no pinned baseline — each supplies its own tests |
| ~~`data-mutation` KEEP path~~ | ✅ **BACKFILLED 2026-08-23** — it was the last of the seven with no csproj glob. **All seven ADR-038 KEEP paths now compile** |

### CI posture — DECIDED 2026-08-24 (owner)

**Rely on `CI / Router`. Do NOT chase `SDAP CI`.**

`CI / Router` is the intended single composite gate (spec FR-A01) and is now **green** after the
2026-08-24 repair ([`notes/ci-router-gate-repair-2026-08-24.md`](notes/ci-router-gate-repair-2026-08-24.md),
[issue #813](https://github.com/spaarke-dev/spaarke/issues/813)) — two consecutive greens, tier2
unit tests running 24m / 23m32s against a 30m timeout.

`SDAP CI` remains **red on pre-existing latent flakes**, not on anything this project changed. The
repaired gate exposed a cluster of them: the classifier fails the build on any pass-1 failure not in
`tests/.reliability-registry.json`, and because `SDAP CI` was cancelled by the next push on most
recent commits, these had never surfaced. Two seen so far — `JobsEndpointsTests.Trigger_RunsJobOutOfBand_RecordsRun`
(registered) and `ReAnalysisFlowTests.ReAnalysis_HappyPath_...` (SSE stream, `TaskCanceledException`
after 2m26s on a contended runner; passes locally).

**Do not register flakes reactively one per CI cycle.** That is a ~30-minute loop per entry and it is
the "silently widen the tolerance" pattern. If `SDAP CI` needs to go green, enumerate the flake set in
one local sweep under load and propose a single reasoned batch. Otherwise treat it as known-red.

---

### Open items requiring owner attention

| # | Item |
|---|---|
| ~~1~~ | ❌ **THAT CLAIM WAS WRONG — CORRECTED 2026-08-24.** Nothing on PR #812 ever needed owner approval. The only `action_required` runs are on three `github-actions[bot]` auto-format commits (`7ca8669d5`, `7f36a5ffe`, `e12cc48d3`), each superseded by the next human commit within minutes. The claim was carried across three checkpoints unverified. **The real problem it was masking**: `CI / Router` had **never been green on this branch — 17 runs, 0 successes** — because tier2's unit-test job hit `timeout-minutes: 6` (a timeout reports as `cancelled`, which `alls-green`'s `allowed-failures` does not cover) → the gate hard-failed while Tier 1 was green. Repo-wide: 20 of 20 tier2 unit-test jobs across all branches were cancelled; `work/spaarkeai-compose-r8` failed identically. **Fixed here** (owner-approved, files owned by `ci-cd-unit-test-remediation-r1`): timeout 6→30, tier2 excluded from Router adjudication by construction, standalone `pull_request` trigger removed. **VERIFIED GREEN** at `f695ce38f` (run 32747593600): `CI / Router` = **SUCCESS — the first ever on this branch**; all 5 Tier 1 + all 7 Tier 2 jobs pass; zero `CANCELLED` rows (was 8+). ⚠️ `Full Unit Tests` took **exactly 24 min** — the first duration this job has ever produced — so 6 was 18 min short AND the 20 I first drafted would have been **4 min short**. Sizing a runaway-guard timeout at the edge of your estimate IS the bug. Full write-up + their decision list: [`notes/ci-router-gate-repair-2026-08-24.md`](notes/ci-router-gate-repair-2026-08-24.md) · [issue #813](https://github.com/spaarke-dev/spaarke/issues/813) |
| 2 | **D1 above** — ADR-028 A4 ruling (8th `WithClientSecret` site) |
| 3 | **D2 above** — `provision-project`: Write-on-project vs a privileged role for creating a BU |
| 4 | **D3 above** — download enforcement (Read) vs `CanDownload` (Write) |
| ~~5~~ | ✅ **CONFIRMED AND FIXED 2026-08-23** — `EntityAccessFilter` WAS inert: `POST /api/office/save` with a `targetEntity` returned 403 for every caller. Now resolves the target's own collection via `CallerRecordAccessProbe`. **Should fold back into `AuthorizationService` when task 032 generalizes the seam** (constraint filed) |
| 6 | **Needs its own task (002)**: `preview-url`, `view-url`, `office`, `preview` on `/api/documents` still have no per-document filter. They mint **URLs**, which outlive the request |
| ~~7b~~ ✅ | **FR-15's SPE half — CLOSED by task 017.** `ListExternalMembersAsync` now propagates and `RemoveAllExternalMembersAsync` returns `SpeBulkRemovalResult(Removed, Failed)`, so close-project's `container_not_cleared` guard is reachable and tested (listing failure AND partial clear). FR-15 and FR-16 are both fully closed |
| ~~7c~~ ✅ | **RESOLVED 2026-08-24 — KEEP `GrantMembershipAsync`.** Owner: do not delete unless 100% certain it is unused anywhere; the membership service is integral to access + notifications, so anything touching it must be exactly right. Verification done: **one** code occurrence repo-wide (its own definition), no reflection/dynamic-invocation path, not reachable from any endpoint, and no other worktree or open PR references it. Kept, with the no-callers-by-design header. ⚠️ Note for coordination: `code-quality-and-assurance-r3` task 020 plans to remove *4* dead `catch (ServiceException)` sites in this file — task 017 already removed one (in `ListExternalMembersAsync`), so their count is now **3**. *(superseded item below)* |
| ~~7c-old~~ | **Owner call wanted: delete `SpeContainerMembershipService.GrantMembershipAsync`?** (017) It has **zero callers** — Spaarke is broker-only and adds no container ACLs — so H-8b's "no dead branches implying grants add members" argues for deletion. It was KEPT because it defines the identity key the revoke matcher must match, and deleting a public service method exceeds this task's scope. It now carries an explicit no-callers-by-design header. Low risk either way |
| 7a | **Expiry enforcement is query-level only** (007) — the tests assert the emitted `$filter`, not Dataverse's evaluation of it (transport mocking is ban B1). Live confirmation of all three cases — past expiry gone, today's expiry still works, null expiry unaffected — filed on **task 034** |
| 7 | **RPA is now load-bearing for six mutation endpoints AND the Office save gate** (008 + follow-up), as well as the document read path (005) — still unverified against a live tenant → **task 034** (constraint filed). Also verify the new not-found retry actually absorbs the wizard's replication lag |
| 8 | **Duplicates remain invisible (010)** to the participation surface until Phase 1 replaces the read-side `GroupBy` collapse |
| 9 | **019's product-semantics question**: `includeRelated: true` is a logged-warning no-op; visible in the Playbook Builder canvas, does nothing |
| 10 | **A-23**: `AddOfficeDocumentAccessFilter` is a second orphaned filter → **task 018** |
| 11 | **I-4**: `sdap:auth:*` keys carry no tenant segment → **task 035** |
| 12 | Stale "task 054 implements" comments in `MembershipEndpoints.cs` + `IMembershipResolverService.cs` → **task 015** |
| 13 | `TypedResults.Unauthorized()` returns a bare 401, not ProblemDetails (ADR-019). Pre-existing; wrap-up candidate |
| 14 | **Suite-health caveat**: one full run during task 005 reported 1 failure that never reproduced; identity not captured. Not attributed, not exonerated. Use TRX if it recurs |

### Constraints filed on future tasks (do not lose these)

| Task | Constraint from |
|---|---|
| **005** ✅ done | 003 (`AppendToAccess`), 006 (verify capabilities light up) |
| **017** ✅ discharged | **010** — sweep preserved (pinned by `Revoke_WhenSpeFails_StillReportsTheDataverseRowsDeactivated` + the existing isolation tests); the "assess SPE-vs-logical-key" ask was **assessed and FILED** — an org revoke has no single grantee, so no email, so cleanup needs an org→members expansion this path lacks. Reports `NotAttempted`. Bounded: broker-only creates no member ACLs. · **016** — SPE reporting made honest, `container_not_cleared` now reachable + tested |
| **032** | 006 (one-access-path invariant), 005 (per-principal derivation + `AppendTo`), **008** (collapse `CallerRecordAccessProbe` into the generalized rights map; **and the `IAccessDataSource` must stay SCOPED** — a singleton would turn `DataverseAccessDataSource`'s `DefaultRequestHeaders` mutation into a cross-user OBO-token bleed) |
| **034** | 005 (verify RPA live; grep `RPA-FALLBACK`), **007** (verify the Date Only expiry predicate live — check the null-expiry case FIRST, because if it is broken external access is down for nearly everyone), **008** (RPA now gates six MUTATIONS against `sprk_projects`/`sprk_matters`/`sprk_workassignments` — a different target from 005's `sprk_documents`, so 005 passing does not imply 008 passes; also grep `DELEGATION-RPA-UNAVAILABLE`) |
| **065** | **008** — unblocked; MUST surface `sdap.access.deny.delegation_write_required` as a real message, MUST send `recordType`+`recordId` (not legacy `projectId`), MUST NOT add a client-side pre-check that skips the server call |
| **012/013/015/018** | 001 (own-coverage obligation) — 007 ✅, 016 ✅ and 017 ✅ discharged their own |
| **043** | **020** — the `sprk_enddate` read-side asymmetry: `QueryActiveOrgIdsAsync` considers `statecode` only, so a membership ended by date but never deactivated still confers inherited access. 020 does not change read behaviour; FR-24/FR-25 must decide whether an ended membership still inherits |
| **Phase 1 evaluator (032/043)** | **017** — if you build the organization→members expansion that FR-24/FR-25 need for org terms, the org-grant **SPE cleanup gap** becomes cheap to close at the same time (`RemoveSpeContainerPermissionAsync` currently reports `NotAttempted` for org revokes). See `notes/task-017-spe-revoke-matcher.md` §6 |

### Decisions carried in from design (unchanged)

| Decision | Where |
|---|---|
| Derived access default-on; **Secure is the veto** | design §4.5 |
| Level precedence = **highest wins**; vetoes AFTER the max | design §4.5 |
| **"No Access" is a veto, never a level** | spec FR-23 |
| Core records need direct grants; child records inherit **1 hop** via denormalized core ancestor | design §4.3 |
| **Matter does NOT inherit from Project** — both are core | design §4.3 |
| Type 1 root sets = Dataverse's real answer via the existing `MSCRMCallerID` seam | spec FR-20 |
| Secure Project = Secure BU + service-account owner + **share-only** | design §5.1 |
| BU restructure is **UAT/environment work, NOT a project task** | spec § UAT & Environment Setup |

### Blocking prerequisites (before Phase 4 live-dev acceptance)

- `prvActOnBehalfOfAnotherUser` on the BFF application user — **no runbook records this grant today**
- Whatever `RetrievePrincipalAccess` requires on the app-only path (read `systemuser` + the target) — task 005
- **OBO to Dataverse must work in every deployed environment** — task 008's delegation gate has no
  fallback, so if the BFF cannot perform the OBO exchange, all six external-access mutations return 403
- BFF app user stays **Org-scoped** (impersonated privileges = app user ∩ impersonated user)
- A **non-admin test user** in the Operations subtree with no Global-read role
- BU restructure + user migration + record re-homing (UAT)

### Hard gates

| Gate | Rule |
|---|---|
| **NFR-04** negative canary | Impersonated low-privilege read MUST return a strict subset AND **strictly fewer** rows than app-only. Equality = impersonation inert → build fails. Task 034 also owns RPA live verification |
| **NFR-05** role-depth assertion | No security role may reach the `Secure Projects` BU |
| **NFR-07** | ⚠️ Partial — 9 of 20 findings pinned, 1 partial, 10 owned by their fix tasks per the accepted escalation |
| **FR-07** delegation | ✅ **SHIPPED (task 008)** — the PCF "+ User" button (task 065) is unblocked |

### Coordination

`/conflict-check` before **every** BFF PR. Shares the external-access surface with
`spaarke-SPA-external-access-platform-r1/r2` and `teams-app-r1` (shipped) and `SPA-r3` (draft).
All `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, `Spaarke.Core/Auth/**` and
`DataverseWebApiService.cs` tasks are `parallel-safe:false`. Tasks 030/031/040 edit `.claude/**` →
**main-session-only**. **Phase 0 has no remaining co-schedulable pair** — run serially.
Last master check (2026-08-22): 1 docs-only commit ahead, **zero overlap**.
