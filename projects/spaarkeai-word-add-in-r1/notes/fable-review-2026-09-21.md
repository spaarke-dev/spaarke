# Fable model-level project review — 2026-09-21

> Four independent Fable reviewers (security · spec-coverage · architecture · verification-integrity) run
> read-only against HEAD `3be220128` of `work/spaarkeai-word-add-in-r1` (PR #960). Each was given the
> project's own claims **as claims to verify, not as facts**. Three of those claims turned out to be wrong,
> and all three were mine.
>
> This note is the reconciled synthesis. Where two reviewers disagreed, the disagreement is stated and
> resolved rather than averaged.

---

## 0. The one-paragraph version

**"All implementation tasks done" is true. "The spec is satisfied" is not, and "the work is verified" is not.**
Four FRs are unsatisfied as written with no amendment recorded; two success criteria were marked PASS on
evidence that does not exist or asserts the opposite; **no merge-blocking check runs any of this project's
tests**; and the three remaining code tasks (058, 059, 060) are each aimed at the wrong target. The live
authorization defect on the Office surface is **not** the one task 058 deletes.

---

## 1. 🔴 The systemic finding — nothing that blocks a merge runs this project's tests

This is the finding that outlives the project, and no agenda item anticipated it.

| Layer | Reality |
|---|---|
| **Router** | The **only** required check (`office-addins-tests.yml:27-37` header; repo ruleset). |
| **office-addins gate** | Marked `"Reports, does not block"` — `office-addins-tests.yml:275`, `:427`. The gate I fixed this session makes a real decision that nothing consumes. |
| **Tier 1 blocking** | Runs `Sprk.Bff.Api.Tests` **only** under `Category=GoldenUtteranceEval` / `FidelityGate` filters (`ci-tier1-blocking.yml:577,690`). None of this project's ~30 new test files carry those categories. |
| **Tier 2 advisory** | `continue-on-error: true` + `timeout-minutes: 30` (`ci-tier2-advisory.yml:242-243`). Step "Run unit tests (pass 1)" **cancelled in 6 of 6** recent runs on this branch; the verdict step has never executed. |
| **Legacy `sdap-ci.yml`** | The only runner that *completes*: 12,379 passed / 0 failed / 56 skipped at HEAD. **Task 077 of another project deletes it.** |

**Consequence, stated plainly:** once `sdap-ci.yml` is retired, the `POST /api/office/save` contract tests —
the ones this project un-skipped as a deliverable — run **nowhere** on a PR. The same applies to NFR-02's
11-Fact per-row authorization suite: it exists, it executes, it is real, and a regression in
`AuthorizeRowsAsync` would not block a merge today.

This reframes FR-18 and NFR-02 alike. Both were written as "make it a gate." Both produced a test.

---

## 2. 🔴 Task 058 is aimed at the wrong target

### What 058 deletes leaks nothing

The claims are true — `/office/share/links` and `/share/attach` have no per-document authorization, and
`SimulateSharePermissionCheckAsync` (`OfficeService.cs:2183-2195`) is `return Task.FromResult(true)`. But the
routes **read nothing**:

- metadata is synthesized from the GUID (`OfficeService.cs:2200-2213`)
- the "share link" is the hard-coded string `https://spaarke.app/doc/{id}` (`:2218-2223`)
- attach returns fabricated names/sizes (`:2995-3004`) and literal `"Stub content for {id}"` (`:3043-3044`)
- its `downloadUrl` points at `/office/share/attach/{token}` — **a route that is never mapped**

Minting a link for any document works exactly as well for a GUID that does not exist. There is no disclosure
and no access grant. **Deleting them is correct** — as removal of a latent hazard (if anyone later implements
`GetDocumentMetadataForLinkAsync` with a real read and no filter, it becomes a cross-user document leak) —
but the reproduce-first criterion should use a **random** GUID, to demonstrate exactly that nothing is behind it.

> 058 must also delete `tests/e2e/pages/addins/OutlookTaskPanePage.ts:305-331` and
> `src/client/office-addins/shared/taskpane/hooks/useShareFlow.ts` (a dead hook referencing two
> `ApiClient` methods that do not exist). Both are missing from the POML's file inventory.

### What is actually live, and survives 058 untouched

| ID | Defect | Sev | Evidence |
|---|---|---|---|
| **F1** | **`/office/search/entities` — live, app-only, security-untrimmed enumeration** of every Matter / Project / Invoice / Account / Contact. Names, numbers, descriptions, GUIDs, `modifiedon`; 2-char substring; 50/type/page with `skip` paging. | **HIGH** | Chain is rate-limit + `AddOfficeAuthFilter` only (`OfficeEndpoints.cs:999-1008`); handler `:1064-1177`; the code says it outright at `OfficeService.cs:1652-1653`: *"app-only read (no per-user security trimming yet)"*. Query `:1699-1735` on the singleton app-only `DataverseWebApiClient`. **Pre-existing on master**; the pane depends on it (`matterTypeLookupService.ts:9`). |
| **F2** | **`POST /api/ai/rag/send-to-index`** — body `TenantId` never checked against `tid`; caller picks the index partition. | MED | `SendToIndexRequest` (`RagEndpoints.cs:1330-1341`) is not matched by `TenantAuthorizationFilter.ExtractTenantId` (`:106-141`), so `:76-81` passes through. Row read **and written** app-only (`:638`, `:748`). Pre-existing — but **this project made the pane a new caller** (`FindView.tsx:313-316`). |
| **F3** | **`POST /office/todo`** writes caller-supplied regarding / document / communication GUIDs app-only with no access check; 403-vs-201 makes it a record-existence oracle. | MED-LOW | `OfficeEndpoints.cs:1468-1481` (no resource filter); `OfficeService.cs:2536-2601`; `CoreAncestorResolver.cs:242-259`. **The `DocumentId`/`CommunicationId` carriers were added by this project (task 035).** |
| **F4** | **`/office/save` with no `TargetEntity`** bypasses `EntityAccessFilter` entirely, lands in the shared default container, then profiles and RAG-indexes the content. | MED-LOW | `OfficeEndpoints.cs:414-462`; `EntityAccessFilter.cs:201-212` calls `next()` when target absent; `OfficeService.cs:186-195`. Pre-existing. |
| **F5** | **Job-status ownership unenforced on the Dataverse path** — see §3, where the two reviewers disagreed. | LOW (authz) | `JobOwnershipFilter.cs:157` |
| **F9** | `/api/office/communications/*` carry only group `RequireAuthorization()`; app-only lookups by caller-supplied `internetMessageId`. The pane calls all three on this branch. | LOW-MED | `CommunicationsEndpoints.cs:62-64`, routes `:72`, `:93`, `:108`. Handler bodies not read. |

**F1 is the keystone.** Every other finding needs a record GUID; F1 hands them out. The post-058 attack path
is intact end to end: enumerate via `/search/entities` → act on the GUIDs via `/todo`, `send-to-index`, and
unassociated `/save`.

### 🔴 Why none of this was caught — the census misclassifies the whole Office surface

`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` `GovernedFiles` (`:111-252`) contains **no
`Api/Office/*` entry** and no `Api/Ai/RagEndpoints.cs`. The waiver list (`:327-471`) has no Office route.
So `OfficeEndpoints.cs` and `CommunicationsEndpoints.cs` fall into the 117 files classified as *"serves no
document or Dataverse content"* (`:84-86`) — **false** for `/save` (writes `sprk_document` rows + bytes),
`/search/entities`, `/generate-profile` and `/todo`. **No test will ever notice any of those routes losing a filter.**

> ⚠️ **Trap for whoever fixes this.** `FilterMarker` (`:1408-1411`) matches only
> `\.Add\w*AuthorizationFilter` or `AddEndpointFilter<*Authorization|Access*Filter>`. It does **not** match
> `AddEntityAccessFilter`, `AddJobOwnershipFilter`, `AddQuickCreateSourceAccessFilter` or `AddOfficeAuthFilter`.
> Classifying `OfficeEndpoints.cs` as `RouteLevelGate` today would false-flag `/save`, `/jobs/*` and
> `/quickcreate` (which *are* gated) while correctly flagging the rest. Rename the filters or widen the regex
> **in the same change**.

### ✅ F-b is CLOSED — and the agenda said otherwise

Discovery finding F-b (visualization per-row authorization) describes **`origin/master`, not HEAD**. Task 032
fixed it here: per-row `IAiAuthorizationService` trim (`VisualizationEndpoints.cs:173-174`, `:431-582`),
fail-closed forcing function (`:108-111`, `:247-250`), POST filter (`:63`), countOnly shortcut closed
(`:146-159`), tokenless refusal (`VisualizationAuthorizationFilter.cs:187-198`), **and the census entry**
(`RouteAuthorizationGuardTests.cs:174-186`). 11 contract tests, recorded 7/11 red with the trim disabled.

The finding was correct when written. It was carried forward without re-checking that our own task had closed it.
Honest residual, recorded in `notes/032-authorization-hardening.md` §6.3: parent-hub nodes are not authorized
against the parent record, so a matter's *name* can reach a caller with rights on the source document but not
the matter.

---

## 3. Where the reviewers disagreed — F5, reconciled

Security rated F5 **LOW**; architecture rated the same code **HIGH**. Both are right about different halves,
and averaging them would lose the point:

- **Authorization half — LOW.** `OfficeService.cs:1543-1586` maps no `CreatedBy`, and `JobOwnershipFilter.cs:157`
  guards with `if (!string.IsNullOrEmpty(jobStatus.CreatedBy) && ...)` — an empty value **passes**, so the filter
  fails *open*. Real (and an ADR-017 violation: *"MUST NOT expose status without authorization checks"*), but it
  needs a job GUID and yields only status. Also live: the hard-coded test job `0000…0001` returns fabricated
  "Running" data to any caller in production (`OfficeService.cs:1511-1541`).
- **Correctness half — HIGH, and it is not a security bug.** `Result.Artifact` is never persisted
  (`OfficeDocumentPersistence.cs:846-855`), so after a restart a *completed* job returns no document id.
  `useSaveFlow.ts:755-776` handles `Completed`-with-artifact and error — **neither branch matches
  `Completed`-without-artifact**, `cleanup()` runs, and the pane sits on the job card forever with no error.
  The SSE path does the same (`OfficeService.cs:3165-3172` → `useSaveFlow.ts:622`).

**Two independent analytical routes reached this filter.** Treat as CONFIRMED, not plausible.

---

## 4. The three remaining code tasks are each mis-aimed

### 058 — right instinct, wrong target
Delete the stubs (latent hazard, cheap). But **F1–F5 + the census classification need their own tasks**, and
F1 is the one with a live, exploitable consequence today. Until those land, the project cannot honestly claim
*"no endpoint mints or serves document data without a real per-resource check."*

### 059 — mis-cut, and its acceptance criterion is the metric CLAUDE.md §11.5 forbids
The cohesion question, answered properly: the **save spine** (`OfficeService.cs:148-1473`) is genuinely
cohesive single-purpose code — every method exists because of the save invariants. §11.5 says leave it.
What actually makes the constructor 18-wide:

1. **Six optional ctor params** (`:89-94`), each commented *"so bare test constructions keep compiling"* —
   and `grep -rn "new OfficeService(" tests/` returns **nothing**. The justification is for tests that do not
   exist. Every dependency is registered unconditionally in production, so the null-fallback branches
   (`:1638-1648`, `:1881-1884`, `:2345-2349`, `:2494-2498`, `:2619-2622`) are unreachable **and** untested.
   The empty-list-200 at `:1881` is the P2-quiet-null shape ADR-032 forbids — dead, but wrong-shaped.
2. **Three more params** exist only to hand-build `OfficeProfileDispatcher` in the constructor (`:116`) to
   honour *"ADR-010: no new DI registration"* (`:67-70`) — **ADR-010 does not say that.** It says register
   concretes inside feature modules (`ADR-010-di-minimalism.md:25-36`). The constructor grew 14 → 18 to comply
   with a rule that does not exist.

Removing those takes the constructor **18 → ~10 with no new service class**. 059 currently proposes extracting
*search* — the cheapest move with the smallest payoff (one param, ~300 already-cohesive lines) — and its
criterion is *"line count drops by at least 250."* **Re-scope.** Job-status extraction (060) is the one
genuinely correct cut.

### 060 — correct diagnosis, under-scoped
`OfficeService.cs:74` static `_jobStore` confirmed — **inherited from master** (master `:62`); this project
added five more write sites. Siblings that produce the *same* user-visible symptom and are out of scope as written:

1. **`CreatedBy` never persisted** → ownership check vacuous (§3). If 060 picks the Dataverse option, the
   `sprk_processingjob` row **has no creator-OID column today** — that is a schema change, not a store swap.
2. **`Result.Artifact` never persisted** → the silent pane stall (§3). 060's escalation trigger names SSE
   event *ordering*; the actual risk is the artifact payload.
3. **`OfficeProfileDispatcher.cs:125`** — `_ = Task.Run(...)` behind a 202; profile lost on restart, cancelled
   by `ApplicationStopping` (`:143`) mid-OBO. **Added by this project (task 022).** Its stated reason for
   avoiding the queue is the `analysis-{documentId}-documentprofile` idempotency trap (`:15-24`) — but
   **task 029 already solved that** with a per-save discriminator (`UploadFinalizationWorker.cs:790-792`).
   The design note predates 029 and was never revisited.
4. **`JobStatusService.cs:46`** `private readonly Dictionary<Guid,long> _jobSequences` — per-instance SSE
   sequence numbers on a singleton. On scale-out, two instances number the same job's events independently, so
   `Last-Event-ID` reconnect (`OfficeService.cs:3097-3107`) can skip or replay. Exactly the semantics 060's
   escalation trigger protects, and outside a `_jobStore` grep.

**Checked and clean** (not siblings): `IdempotencyFilter` is Redis-backed via `ITenantCache`;
`OfficeRateLimitService` Redis-backed; `_searchMeta`/`_todoRegardingMap` immutable catalogs.

---

## 5. Spec vs. reality — four FRs unsatisfied, two success criteria falsely PASS

| Req | Verdict | Detail |
|---|---|---|
| **FR-05** | **NOT SAT** | Word runs from **`dist/word/manifest.xml` (1.0.9.0)**, not the unified JSON manifest FR-05 requires. `notes/037-manifest-change.md:5-12` says so. Task 011's ACs 6/7 (sideload the JSON) were never verified — the 09-18 "close" was an XML re-upload — and the POML still reads `<status>in-progress</status>` while ✅ in the index. |
| **FR-12** | **PARTIAL** | Data loss is closed (`ResolveNameCollisionAsync`, `OfficeService.cs:1101-1244`; the same-record guard at `:1217-1222` that #1005 forced). But spec FR-12 still says *"Do not rebuild any of this"*, **SC-6 was marked PASS citing an integration test asserting "no new collision logic" — grep returns 0 hits and new logic demonstrably exists**, and the claimed §6.5 path-A deviation **has no row in spec.md's ADR Tensions table**. |
| **FR-13** | **NOT SAT** | See §6. |
| **FR-16** | **PARTIAL** | Ships **documents-only** (`FindResultsList.tsx:29-30`); records half deferred by decision note, spec unamended. Two structural gaps: Outlook's Find tab can never leave state 1 (`FindView.tsx:101` maps `documentIdentity === undefined` → `no-document`, and Outlook has no identity resolution); and a desktop-sourced doc saved *this session* never transitions (`App.tsx:712` updates `savedContext.documentId`, but `FindView` receives only `documentIdentity`, set solely by resolution at `:306`/`:316`). |
| **FR-18** | **PARTIAL** | The job reports; it cannot block (§1). |
| **NFR-02** | **PARTIAL** | Test is real and executing; it is not a merge gate (§1). |

**Ordering held** where it mattered: 032's hardening landed `f892c8ada` (09-08) *before* 033 `0a31802f8` (09-15),
and 033 consumed 032's D-032-2 warning channel. **FR-02 forward-only holds** — `Stamp()` has exactly one write
site (`OfficeService.cs:483`), on bytes already in flight; `:1019` is compare-only; nothing enumerates or
rewrites stored files.

**Genuinely well-evidenced** (say so): FR-01, 02, 06–11, 14, 17; NFR-03–09, 11. The save spine is the strongest
part of the project — 039/045/046/047/054/055 each reproduce-first.

---

## 6. 🔴 The blank primary name has no owner anywhere

`sprk_matternumber` is `sprk_matter`'s **primary name column**, and the creation path never writes it
(`RecordCreationService.cs:124-126`, `:149-152`; protected-attribute sets `:172`, `:185` close the
field-mapping back door). Same for `sprk_projectnumber`. So a pane-created Matter has an **empty display name
in every lookup, subgrid, and the pane's own To Do "regarding"**.

The owner re-scoped this 09-11/17 and the code enforces the re-scope well. But:

- spec FR-13 and **SC-8 still require the number**, and 042 recorded SC-8 **PASS "Integration test"** — the only
  integration test on that path is `OfficeQuickCreateContractTests.cs:512 AssertNoMatterNumberSent`, which
  asserts the **opposite**;
- the "separate numbering project" **does not exist** — no folder under `projects/`, no GitHub issue, not in
  `defer-issues.md`.

**This is the UAT complaint the spec was written to fix, and today it lives in a note.**

---

## 7. ✅ CORRECTION — the POML claim in the 09-21 handoff was wrong

I wrote *"six POMLs fail XML validation, including `090-project-wrap-up.poml`"* and made it a blocker on the
path to project close. **Both reviewers ran `scripts/Validate-TaskPoml.ps1` independently and agree:**

**61 scanned · 39 clean · 10 errors · 12 warnings. `090` PARSES CLEANLY** (root `task`, 9 steps,
`not-started`; one WARN for a missing `<justification>`). It is not a blocker and never was.

The ten that fail — **all of them `<status>completed</status>`**:

| Malformed XML (5) | Error |
|---|---|
| `005-…collision-path.poml` | 167:5 — `rigor-reason` start tag (166:26) ≠ end tag `notes-completion` |
| `010-adapter-consolidation-word.poml` | 172:41 — name cannot begin with ' ' |
| `018-useannounce-react19-lifecycle.poml` | 115:7 — `div` start tag (91:42) ≠ end tag `completed` |
| `028-editable-save-link-graduate.poml` | 63:216 — name cannot begin with '>' |
| `053-pane-swallows-quickcreate-errors.poml` | 105:216 — `relevant-files` start (101:61) ≠ end tag `notes` |

**Missing `<steps>` (5):** `009`, `017`, `019`, `043`, `044`.

---

## 8. Verification hygiene — the gate is sound, its surroundings are not

**The office-addins gate itself can no longer pass vacuously.** Both jobs make real decisions on real input
(5 red → 3 green observed across the two fixes; both classifier directions seeded). Two caveats:

1. **The trailing-newline claim at `:252-254` is false.** Both sides of the suite-count assertion derive from
   the *same* `while read` array (`:193-199` → `:218-219`), so a dropped last line shrinks both numbers equally.
   Currently harmless (file ends `0x0a`, last real entry followed by comments) — but the comment asserts a
   protection that does not exist.
2. **The 111 test-debt total is an unpinned observation**, not a baseline (`:400`, `:423`, `:437`). 111 → 112
   passes silently; only the production count is asserted. The 111 exists solely in comments (`:295`, `:312`).

**Node is split three ways, and it is worse than recorded.** Gate 20 (`:162`, `:335`) · nightly 20
(`client-tests.yml:151`) · **deploy of what actually ships: 18** (`deploy-office-addins.yml:34`) · this desktop
22.14.0 · `package.json:68` says `>=18` · **no `.nvmrc` or `.node-version` anywhere in the repo.**

**⚠️ This desktop's `node_modules` is stale** (mtime 2026-09-04; `@testing-library/jest-dom` and `user-event`
added 09-09 in `cc318390f` are **absent**). Locally *every* suite fails at `jest.setup.js:12`, and local
`npm run typecheck` emits a path-less `TS2688` the classifier counts as **production**. **The five "green"
runs recorded for task 043 cannot have been produced on this install.** Run `npm install` before trusting any
local result here.

**`npm run lint` is broken and unwired** — `package.json:13` is `eslint src`, there is no `src/`, and **no
workflow calls it**. CI passes because lint never runs, not because lint passes.

### The 10 red jest suites: 84 failures, **zero** point at a shipped code bug

| Suite | Fail | Cause |
|---|---|---|
| `OutlookAdapter.test.ts` | 18/34 | `mockReadItem` (`:11-37`) lacks `itemType`; adapter keys on `'itemType' in item` (`OutlookAdapter.ts:193`) → `unknown` → every mode-dependent call degrades |
| `ShareView.test.tsx` | 16/20 | `ResizeObserver is not a constructor` — no polyfill in `jest.setup.js`; 10 suites polyfill per-file, this one does not. **Harness gap** |
| `SaveView` 22/26 · `SaveFlow` 9/27 · `EntityPicker` 4/32 | 35 | Stale label strings superseded by tasks 015/020/024/038 |
| `useSaveFlow.test.ts` | 7/26 | Test expects `isValid` false; it is hard-coded true by design (`:508-509`) — and the suite's own `:248` asserts document-only save works. Plus a fetch mock with no `text()` |
| `TaskPaneShell.test.tsx` | 5/10 | Exact-match `Spaarke` vs `appName = 'Spaarke DMS'`; expects a Share tab the Save\|Find shell no longer has |
| `TaskPaneNavigation` 1 · `ApiClient` 1 · `useEntitySearch` 1 | 3 | Fluent emits `disabled` not `aria-disabled`; mock lacks `headers`; `searchNow` awaited under fake timers |

**All test-side or harness-side.** Gated set = the 46 uncommented paths in `ci-gated-suites.txt`; ungated = these
10 (46 + 10 = 56 files). Every *new* client suite the project added is gated. Four of the red suites were
**modified by this project** and left outside the gate. `tests/e2e/specs/word-addins/save-flow.spec.ts` was
modified and **no workflow runs `tests/e2e`**.

**Skips improved materially**: `OfficeEndpointsContractTests.cs` is now 9 skipped of 49 (merge-base: 11 of 23).
**Both `POST /api/office/save` tests execute** (`:63-129`). Remaining skips: 5 search, 3 quick-create, 1 todo.

---

## 9. Process defects worth fixing once, not task-by-task

- **14 ✅ tasks have completion claims unsupported by evidence** — 011 (JSON sideload), 030/031 (spec AC unmet),
  025 (SC-6), 033/034 (FR-16 halves), 040 (AC1 grep is 9, not 0), 056 ("CI gates it"), 010 (host auto-detection
  🔴 unverified live), and 013/021/026/027/036/037 (all `<ui-tests>` deferred to 042).
- **7 POMLs read `<status>not-started</status>` while ✅ in the index** — 014, 025, 031, 037, 050, 051, 054.
  Root CLAUDE.md §7 step 1 not done.
- **042's tally records SC-6 and SC-8 as PASS on evidence that does not exist or contradicts them.** SC-9 is
  correctly PARTIAL.
- **Dead code with no owner**: `outlook/OutlookHostAdapter.ts` — 502 lines, self-described as dead, **edited
  five times on this branch** (tasks 013/020/036/040/051) purely to keep `tsc` quiet after each interface
  change. `mintDocumentShareLink` duplicated verbatim (`shareLinkService.ts:50-69` ≡ `sendEmailService.ts:98-119`).
  `HostAdapterFactory`'s `getOrCreate`/`clearCache`/`hasAdapter`/`getRegisteredHosts`/`waitForOfficeReady` have
  no non-test callers.
- **A stale premise still asserted in code**: `OfficeService.cs:143-145`, `:341-343` claim the no-target branch
  *"exists for the contract rather than for traffic"*. False since task 037 — `quickSaveHelpers.ts:172-200`
  sends no `targetEntity` and `word/commands/index.ts:151-153` posts it, so **every Word ribbon quick-save**
  lands in the shared default container (F4's path).

---

## 10. Recommended re-plan

**Do not execute 058 as written next.** Recommended order:

| # | Work | Why first |
|---|---|---|
| 1 | **New task: F1** — trim `/office/search/entities` per-caller (OBO / `MSCRMCallerID`, or post-trim via `AuthorizationService`) | The only HIGH with a live consequence; it is the keystone that supplies GUIDs to F2/F3/F4 |
| 2 | **New task: census** — govern `Api/Office/*` + `RagEndpoints.cs`, **and** widen `FilterMarker` or rename the four `Add*Filter` methods in the same change | Without it, every fix below can silently regress |
| 3 | **058 re-scoped** — delete the four stubs + `useShareFlow.ts` + the e2e mocks; random-GUID reproduce | Cheap, removes the latent hazard |
| 4 | **New task: F2–F5** — `SendToIndexRequest` tenant binding + per-row write authz; `/todo` source gate; `TargetEntity` required on `/save`; persist creator OID + fail `JobOwnershipFilter` closed; delete test job `0000…0001` | F5's fix shares a schema change with 060 |
| 5 | **060 re-scoped** — store **plus** creator-OID **plus** `Result.Artifact`; fold in `OfficeProfileDispatcher` (via 029's discriminator) and `JobStatusService._jobSequences`; make `useSaveFlow` treat `Completed`-without-artifact as terminal error | Under-scoped as written; shares schema with (4) |
| 6 | **057** — must precede `/test-diet`, or 090's escalation trigger 1 fires on the 71 methods under `tests/unit/Sprk.Bff.Api.Tests/**`. `.claude/` ⇒ **main-session only** | Sequencing constraint |
| 7 | **New task: reconciliation** — amend FR-05/12/13/15/16 + SC-6/SC-8; add the FR-12 ADR-Tensions row; fix 042's tally; sync the 7 POML statuses; repair the 10 POMLs | Without it 090's trigger 2 fires on an honest run |
| 8 | **Register the numbering project** (§6) and file issues for the unowned items | The spec's headline UAT fix currently has no home |
| 9 | **059 re-scoped or dropped** — remove the six optional params + dead branches; drop the LOC criterion | Lowest value as written |
| 10 | **042** (operator, live host) → **090** | Genuine external dependency |

**Also worth a decision, outside this project's scope:** §1 means the repo will have **no completing PR-time
runner for `Sprk.Bff.Api.Tests`** once task 077 deletes `sdap-ci.yml`. That belongs to the CI owner, but this
project should not close without surfacing it.

---

## 11. What the reviewers could not verify

Live Office host behaviour (SC-1/3/7/9/11 manual halves, all `<ui-tests>`, dark mode, keyboard, host
auto-detection) · actual test *execution* of the C# suites (read + recorded gate runs only) · deployed-build
behaviour for 058's "verified against `spaarke-bff-dev`" claim · `CommunicationsEndpoints` handler bodies
(registration chain only) · whether Dataverse returns a formatted lookup name for an unreadable record (the
*name* half of F3; the GUID half is certain) · Tier 2's cancellation cause (step conclusion `cancelled`; log
not retrievable) · publish-size numbers (recorded in 042 §3, not re-measured).
