# Deferrals & Issues — `spaarkeai-word-add-in-r1`

> Source of truth. Every entry must ALSO have a GitHub Issue (visibility). `push-to-github` blocks on entries missing a GitHub URL.
> File via `/project-defer-issue-tracking` (alias `/defer`) — it writes both places in one step.
> Per CLAUDE.md §11, every entry names a concrete behavior or contract that fails. "Future flexibility" is not a reason.

## Register — reconciled 2026-10-02 by task 079

The surface task 090's escalation trigger 2 runs against (*"deferred entries that describe unfinished spec
requirements"*). GitHub states read live on 2026-10-02. **"Spec gap?"** answers one question: does this entry leave a
requirement of THIS project's `spec.md` unfinished?

| ID | Subject | Status | Owner | Spec gap? | GitHub |
|---|---|---|---|---|---|
| ISS-001 | `sprk_event` written to `sprk_document`, column absent | Routed out-of-band | `unified-access-control-r2` | No (UAC-r2's filter) | not filed (operator) |
| ISS-002 | CI shadow window false green | ✅ Resolved 2026-09-10 | — | No | not filed (operator) |
| ISS-003 | Global handler serves `application/json` | ✅ Closed | — | No | #975 closed |
| ISS-004 | office-addins typecheck job reports but does not block | **Open** — job built (056), not merge-blocking; FR-18/SC-12 amended 2026-10-02 to "CI reports" (owner) | **Project owner** (#996, promotion to blocking) | **No** — FR-18 now says "reports"; promotion is an improvement, not a spec gap | #996 open |
| ISS-005 | New Word documents 503 on identity | ✅ Closed 2026-09-18 | — | No | #997 closed |
| ISS-006 | Collision "Save as new version" wrote into an unrelated document | **Fixed in code (055), deployed; live re-check pending** | **This project — task 042 UAT** | FR-12 behaviour, pending the live re-check | #1005 open |
| ISS-007 | I-6 not applied by ~20 writers outside the Office surface | Open | The projects that own those writers (Communication, Compose, Finance, Events, Playbook) | No (outside this spec) | #1034 open |
| ISS-008 | `/api/v1/work-assignments` writes absent columns | Open | Work-assignment surface owner (not this project) | No | #1035 open |
| ISS-009 | Playbook create/clone set no owner | Open | Playbook surface owner (not this project) | No | #1036 open |
| ISS-010 | Picker trims by Read, save demands AppendTo | ✅ Done (084) | — | No | #1037 closed |
| ISS-011 | `/api/v1/documents` lets the caller choose the owner | ✅ Done (080) | — | No | #1043 closed |
| ISS-012 | Team-owned To Dos drop out of the Daily Briefing | Open | **Task 083** (ours, blocked on UAC-r2 141) + UAC-r2 141/152 | No (cross-project regression from 080) | #1044 open |
| ISS-013 | Secure Record Owner role privileges | ✅ Done (082) | — | No | #1046 closed |
| ISS-014 | Outlook ribbon quick-save sends the logical name | ✅ Done (084) | — | No | #1075 closed |
| ISS-015 | `sprk_invoice` has no `sprk_invoicename` | Our half ✅ (085); 3 other-owner sites open | Other owners named in #1079 | No | #1079 open |
| ISS-016 | Root BU team had no roles | ✅ Done 2026-10-02 (owner) | — | No | #1081 closed |
| ISS-017 | Office job-row reads | ✅ Done (060); live-checked 2026-10-02 | — | No | #1084 closed |
| ISS-018 | Background work lost on restart; SSE numbering | ✅ Done (068); live-checked 2026-10-02 | — | No | #1086 closed |
| ISS-019 | Flaky Communication seam test | Open | `email-communication-intelligence-r2` | No | #1088 open |
| ISS-020 | Six `sdap-jobs` handlers drop a crashed job | Open | Owners of those six handlers | No | #1089 open |
| ISS-021 | The pane reads the profile once after Generate Profile | Open | **This project** (unscheduled) | Partly — FR-08's result appears only on reload | #1090 open |

**Open work that is a TASK, not a deferral** (listed so nothing is invisible to 090):

| Item | Owner | Spec link |
|---|---|---|
| ~~**Task 076** — pane-created Matter/Project has a **blank name** (no number written)~~ ✅ **DONE 2026-10-03** — Dataverse platform autonumber `MAT-`/`PRJ-######` (interim until the numbering function); dev applied; `notes/076-record-numbering.md`. **Open: run `scripts/Set-RecordNumberingSchema.ps1` in every other environment** (production after the owner's uniqueness check) | This project — owner answered 2026-10-02 | FR-13 / **SC-8 → PASS** |
| **Task 083** — Office To Do names its person | Blocked on UAC-r2 task 141's link contract | FR-14 (completeness) |
| **Task 042 UAT** — the live list: SC-1/3/7/9 manual halves, SC-11 parity rows; the `<ui-tests>` of 010, 013, 021, 026, 027, 033, 034, 036, 037, 040, 077; #1005 live re-check; 037's ribbon Quick Save/Share (only live attempt, 09-30, failed — blamed on the environment, never re-run); the unexplained 09-30 Create To Do failure; 084's latency check | **Project owner** (needs live Word/Outlook) | Several |
| ~~Task 079 sign-offs~~ ✅ all signed 2026-10-02. **Task 086** builds two of them: Word Send Email choice (Spaarke email / Outlook on the web) and the focused record page (`navbar=off`) | This project (task 086) | FR-15, FR-10 |
| Owner actions: 080 backfill `-Apply`; 078's observed install (`notes/078-manifest-decision.md` §6) | **Project owner** | FR-05 (078), I-6 data |
| Drift checker cannot read this index's layout on master | `customer-provisioning-orchestration-r1` — fix `233ff9341` unmerged (verified: reads 86/86, no drift) | None (tooling) |
| Handed off, tracked there: ADR-038 KEEP-path amendment (057) | `unified-access-control-r2` | #1014 open |
| Handed off, delivered there as UAC-r2 task 120: Office route census (061) | `unified-access-control-r2` (issue not yet closed) | #1015 open |

---

## ISS-001 — `sprk_event` is written to `sprk_document` but does not exist on the entity

| Field | Value |
|---|---|
| **Type** | Issue (live defect in shipped code) |
| **Found** | 2026-09-04, during `/project-pipeline` initialization (finding **F-g**) |
| **Owner** | **`unified-access-control-r2`** — its 2026-09-03 Q4 widening added the `event` entry, and `EntityAccessFilter` is its component |
| **Severity** | Filing a document to an Event is authorized and then cannot associate |
| **GitHub Issue** | ➖ **Not filed — operator decision 2026-09-04**: no GitHub Issue needed. Routing to `unified-access-control-r2` handled out-of-band. This entry remains the record; do not treat the missing URL as an unfiled obligation or block push on it. |

### What fails

`sprk_document` has **no `sprk_event` attribute**. Verified live via Dataverse MCP 2026-09-04 and corroborated by the maker-portal Columns view:

```
SELECT sprk_event FROM sprk_document
→ 'sprk_Document' entity doesn't contain attribute with Name = 'sprk_event'
```

The entity has **four** direct lookups (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`) and **twelve** `sprk_related*` lookups. The Event column is **`sprk_relatedevent`** only.

Yet three shipped layers treat a direct `sprk_event` as real:

| Layer | Behaviour |
|---|---|
| `Api/Filters/EntityAccessFilter.cs:126-127` | Accepts `event` / `sprk_event` — authorizes the record-keyed upload route |
| `Spaarke.Dataverse/Models.cs` `DocumentAssociationMap.TryApply` | Maps it → `EventLookup`, returns `true` (success) |
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs:916` | Writes `document["sprk_event"]` |

Both call sites carry a comment claiming the columns were "verified against live Dataverse metadata 2026-09-03". That holds for `sprk_workassignment`; it is wrong for `sprk_event`.

### Why it matters

This is exactly the failure mode the lockstep invariant exists to prevent. [`coordination-document-association-map-from-email-r2-2026-09-04.md`](../coordination-document-association-map-from-email-r2-2026-09-04.md) §3 states the rule — *"a type belongs in **both** maps or **neither**"* — and then names `event` as compliant. `event` is in both maps with no column.

Likely mechanism: the 2026-09-03 verification checked the `sprk_related*` family (where `sprk_relatedevent` does exist) while the code writes the direct family — the same family confusion visible in that doc's §1, which describes the write target as `sprk_related{recordtype}` when the code writes direct slots.

### Two further false claims from the same source

| Claim | Reality |
|---|---|
| `sprk_todo` is "unmappable — a document cannot be associated to a to-do at all … needs a schema change first" (§3) | **`sprk_relatedtodo` exists.** True only of a column literally named `sprk_todo`. No schema change required. |
| "`sprk_document` has no account/contact lookup column" (§4.1) | **`sprk_relatedcontact` exists.** `account` is correct — no account lookup at all. |

Knock-on: §4.2 repoints `RecordKeyedUploadAuthorizationTests`' deny-path example at `sprk_todo` as "genuinely unmappable" — that example rests on the same false premise and should be re-examined.

### What r1 did (and deliberately did not do)

- ✅ Corrected tasks **026** and **035** so they do not inherit the false premise; 026 reads `sprk_relatedevent`, never `sprk_event`
- ✅ Appended a §0 correction to the coordination doc
- ✅ Recorded as finding **F-g** in `plan.md` §3, `TASK-INDEX.md`, and project `CLAUDE.md`
- ❌ **Did not change `EntityAccessFilter`, `DocumentAssociationMap`, or `DataverseServiceClientImpl`** — out of r1's scope, and UAC-r2 is live on those files (`parallel-safe:false`). Fixing them here would collide.

### Suggested fix for the owner

Either add a direct `sprk_event` column to `sprk_document`, or repoint `EventLookup` at `sprk_relatedevent` and drop `event` from `EntitySetByType` until the families are reconciled. The broader question — why two lookup families exist and which one the association map should target — is worth settling before either.

---

## ISS-002 — 🟡 CI shadow window false green — **DIAGNOSED**; residual is an operator decision

| Field | Value |
|---|---|
| **Type** | Issue (blocks a repo-wide CI cutover) |
| **Found** | 2026-09-09, while scoping task 043 |
| **Diagnosed** | ✅ 2026-09-10 by task 044 — **ROUTER DEFECT, already remediated by PR #944**. See [`notes/044-false-green-diagnosis.md`](044-false-green-diagnosis.md). |
| **Owner** | ✅ **RESOLVED 2026-09-10.** Operator chose **Option 1** — advance `-Since` to `2026-09-04T22:13:10Z` (immediately after PR #944, the last change to the CI configuration under observation). Applied to `scripts/ci/shadow-window-status.ps1`. |
| **Outcome** | Window re-measured live after the change: **0 false greens, 0 false reds, 5/20 agreeing, 3/5 days.** The banner flipped from "A false green is DISQUALIFYING" to "Window still open. Nothing to do — keep merging normally." The cutover is unblocked and simply needs merges to accumulate. Cost paid: 6 comparable PRs, 1.1 days. |
| **⚠️ Residual** | The **latch itself was NOT repaired** — `$falseGreens` is still computed over the whole window while `$ready` gates on it unfiltered, so the NEXT false green will latch exactly the same way. Advancing `-Since` sidestepped #934; it did not fix the mechanism. Repairing it means deciding what the exit criterion MEANS — a hard stop, or a reset — which belongs to whoever owns the cutover. Documented in the script's own `.PARAMETER Since` block so it cannot be lost. |
| **Severity** | ⬇️ Downgraded. The new tier is no longer unproven — the gap is closed and verified live. `sdap-ci.yml` still cannot retire, but now only because the *measurement* latches on a fixed defect. |
| **GitHub Issue** | ➖ not filed. Diagnosed under r1 task 044 (operator, 2026-09-09), which carried explicit authorization to touch the frozen tier files — **authorization not exercised; no tier file changed**. |

`scripts/ci/shadow-window-status.ps1` reports, as of 2026-09-09:

```
Window opened           : 2026-08-27 20:47 UTC
Comparable PRs examined : 44
Agreeing                : 11 / 20
Calendar-day span       : 4.1 / 5
False reds (logged)     : 0
FALSE GREENS            : 1
```

The tool's own words: *"A false green is DISQUALIFYING — the new tier passed a commit the legacy system
failed. Diagnose before continuing; the count above restarts from the most recent one."*

The offending merge is **PR #934 — `feat(email-intelligence-r2): Outlook/Word add-in — Create To Do
(sprk_todo)`**: legacy `failure`, Router `success`. That it is an **add-in PR** is why this project
found it, and is a reason r1 should care — the new tier passed something on our own surface that the
old one caught.

**Deliberately NOT absorbed by task 043.** 043 adds a jest gate for `office-addins`; diagnosing a
false green in the tier-cutover measurement is a different problem on a frozen surface, and merging
the two would put a project-scoped CI task in charge of a repo-wide cutover decision. 043 is
constrained to surface this and leave it alone.

---

### ✅ DIAGNOSED 2026-09-10 by task 044 — verdict: **ROUTER DEFECT, already remediated**

Full report: [`notes/044-false-green-diagnosis.md`](044-false-green-diagnosis.md).
**Task 044 changed no workflow file. Cost to the window: 0 PRs, 0 days.**

**What legacy caught** — run `33775635122`, job `100716509606` `Build & Test (Debug)`, **step 7
`Build`** (every test step `skipped`, so it is a **compile** failure, not a test failure):

```
Phase2EndToEndFixture.cs(443,17): error CS1503: Argument 4: cannot convert from
  'System.Threading.CancellationToken' to 'string?'  [Sprk.Bff.Api.IntegrationTests.csproj]
```

#934 added a 4th argument to `IOfficeService.QuickCreateAsync` without updating the fixture.

**Why Router passed** — Tier 1's `Compile (Debug)` (job `100716653033`) ran
`dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`: **the production BFF csproj only,
no test project at all.** Tier 2's `Full Unit Tests` (job `100716654163`) hit the identical CS1503
at its own `Build` step — but Tier 2 is excluded from Router by construction. Green `CI / Router`
over a solution that does not build. **5 of 8 solution test projects had no blocking compile
coverage**; `Spaarke.Core.Tests` (46 tests) was not even in `Spaarke.sln`.

**Verdict — DEFECT, not DESIGNED-BEHAVIOR.** The failing *job* sat in Tier 2, but the failing
*concern* is **compilation**, which is Tier 1's declared charter (the job is named `Compile
(Debug)`). Tier 2 caught it only incidentally, as the build that precedes its tests. Per the tier
design's own words: *"the gate is 'the solution builds', not 'the tests pass'."* Test **execution**
staying advisory in Tier 2 remains correct and is not a gap.

**Already fixed** — PR **#944** / `ce5c2c3d7`, merged 2026-09-04T22:13Z: Tier 1 now builds
`Spaarke.sln`. Verified from a live recorded run (job `102698717302`, 2026-09-10) compiling
`Sprk.Bff.Api.IntegrationTests.dll`. Compile coverage now **9/9** test projects.

**🔴 STILL OPEN — the blocker moved, it did not clear. Owner: OPERATOR (§6.5 path B decision).**

#944 fixed the CI defect but not the *measurement*. `shadow-window-status.ps1` computes
`$falseGreens` over the whole window and gates on `$falseGreens.Count -eq 0`, never filtered by
`$countingFrom` — so **#934 is a permanent latch** and `$ready` can never become true, however many
PRs subsequently agree. This contradicts the script's own prose (*"the count restarts from the most
recent one"*). Separately, its `-Since` docstring still claims PR #841 is *"the last change to the
CI configuration under observation"* — false since #944.

Decision required (details + rationale in §6 of the report):

- **Option 1 (recommended)** — advance default `-Since` to `2026-09-04T22:13:10Z`. The script's own
  doctrine; criterion semantics unchanged. **Costs 6 PRs / 1.1 days** (today: 5/20, 3.0/5 days,
  **0 false greens, 0 false reds**).
- **Option 2** — de-latch `$ready` to evaluate false greens over the counting run only. **0 PRs,
  0 days** (keeps 11/20, 4.1/5) but relaxes a safety gate.

Neither was applied — path B requires operator sign-off, not a unilateral rewrite. Note the marginal
cost is **6 PRs / 1.1 days, not the 11 / 4.1 assumed at filing**: #934's own reset had already
discarded everything before it.

Minor, non-blocking: #944's comment in `ci-tier1-blocking.yml` says *"widened 2026-08-31"* — wrong
and chronologically impossible (it predates the 09-03 failure). Actual: 2026-09-04. Fix in passing;
not worth a dedicated edit of a frozen file.

---

## ISS-003 — Global exception handler serves `application/json`, never `application/problem+json` (ADR-019, repo-wide)

| Field | Value |
|---|---|
| **Type** | Issue (live defect in shipped code — ADR-019 contract violation) |
| **Found** | 2026-09-11, task 016, while writing `DocumentIdentityContractTests.cs`'s malformed-URL contract test |
| **Owner** | Whoever next touches `MiddlewarePipelineExtensions.cs` — a one-line fix, no design work |
| **Severity** | Every `SdapProblemException`-driven HTTP error across the ENTIRE BFF API (400/404/409/503/…) is served with the wrong media type |
| **GitHub Issue** | [spaarke-dev/spaarke#975](https://github.com/spaarke-dev/spaarke/issues/975) |

**Description**

`src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs` `UseSpaarkeMiddleware`'s
global exception handler (the ONE place every `SdapProblemException` is rendered) does:

```csharp
ctx.Response.ContentType = "application/problem+json";
...
await ctx.Response.WriteAsJsonAsync(new { type, title, detail, status, correlationId, extensions });
```

`HttpResponseJsonExtensions.WriteAsJsonAsync` (no explicit `contentType` argument passed) unconditionally sets
`response.ContentType = contentType ?? "application/json; charset=utf-8"` — it does not consult the header
already set two lines above. **Every** `SdapProblemException`-driven response is therefore served as
`application/json`, never `application/problem+json`, contrary to ADR-019 ("MUST return ProblemDetails for all
HTTP failures" — RFC 7807 defines that media type as part of the contract).

**Concrete failure mode**: any client (or contract test) that dispatches on `Content-Type:
application/problem+json` to recognize an RFC-7807 error body — rather than sniffing the JSON shape — will not
recognize a Spaarke BFF error as one. This is exactly the class of client-complexity ADR-019 exists to prevent
("Consistent error shapes reduce client complexity, improve debuggability").

**Confirmed NOT a fixture artifact.** The SAME test fixture's 403 response — a DIFFERENT code path
(`DocumentAuthorizationFilter`'s `Results.Problem(...)`, which sets the header through ASP.NET Core's own
`ProblemHttpResult` rather than a raw `WriteAsJsonAsync`) — gets the header right in the identical in-process
host. Only the global-exception-handler path is affected.

**Confirmed genuinely untested, not previously known-and-worked-around**: `grep -rln
"application/problem\+json" tests/` returns zero hits anywhere in the repository before this task.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs:29-87` (the handler)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs:73` (the `WriteAsJsonAsync` call to fix)
- Reproduction: `tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs`
  `ResolveIdentity_ForAMalformedOrEmptyUrl_Returns400ProblemDetails_NotA500` — currently asserts the JSON body
  shape rather than the header (working around the defect, not fixing it) with the full diagnosis inline as a
  code comment.
- Full diagnosis: `notes/016-fixture-diagnosis.md` §3 and §6.

**Suggested fix**

Pass the content type explicitly: `ctx.Response.WriteAsJsonAsync(payload, contentType: "application/problem+json")`
(overload exists), or replace the raw `WriteAsJsonAsync` call with `Results.Problem(...)`/`TypedResults.Problem(...)`
so ASP.NET Core's own `ProblemHttpResult` owns the header the way `DocumentAuthorizationFilter` already does.
Either is a small, contained change to one file; add a regression test asserting
`response.Content.Headers.ContentType?.MediaType == "application/problem+json"` for at least one
`SdapProblemException` path once fixed (the KEEP path this file already uses,
`tests/integration/contract/**`).

**Estimated effort**: <1 hour (one-line fix + one regression test)
**Blockers**: none
**Related**: ADR-019 (concise: `.claude/adr/ADR-019-problemdetails.md`; full: `docs/adr/ADR-019-api-errors-and-problemdetails.md`)

---

## ISS-004 — `office-addins` is typechecked by NO CI job; FR-18's "CI gates it going forward" was never implemented

| Field | Value |
|---|---|
| **Type** | Issue (missing gate — the safety property an accepted decision depends on is absent) |
| **Found** | 2026-09-17, task 042, while recording spec Success Criterion 12 against the deployed build |
| **Owner** | ~~Whoever next owns office-addins CI~~ **The project owner** (reconciled 2026-10-02 by task 079). Task 056 built the job (`office-addins-tests.yml` `typecheck`): it fails ITS OWN run on any production error, but it is in neither required check (`Router`; classic protection's `Build & Test (Debug)`), so it **does not block a merge**. Promoting it is an owner action on #996; FR-18 / SC-12's "CI gates" wording awaits the owner's sign-off on an amendment (task 079). |
| **Severity** | A NEW **production** typecheck error in `src/client/office-addins` would be caught by no gate at all |
| **GitHub Issue** | [spaarke-dev/spaarke#996](https://github.com/spaarke-dev/spaarke/issues/996) |

**Description**

Spec Success Criterion 12 reads *"`npm run typecheck` is clean — Verify: CI"*. Measured on the deployed
build's commit: `npm run typecheck` (= `tsc --noEmit --skipLibCheck`) **exits 2 with 111 `error TS` lines**,
**0 of them production** (all under `__tests__/`, `*.test.*`, or `shared/__mocks__/office-js.ts`). So the
criterion **FAILS as literally worded**.

The 111 are not an unowned regression — project `CLAUDE.md` records a deliberate accept (2026-09-09,
narrowed 290 → 111 on 2026-09-12, owner-approved). The real defect is the OTHER half of FR-18's acceptance:

> FR-18 (`spec.md:92`): *"Acceptance: `npm run typecheck` is clean; **CI gates it going forward**."*

**No CI job typechecks this package.** `grep -n typecheck .github/workflows/*.yml` matches only
`sdap-ci.yml:432-485`, every hit belonging to a different package (`Spaarke.UI.Components`,
`Spaarke.AI.Widgets`, `Spaarke.Auth`, …).

**Why it matters**: the 2026-09-09 accept's stated safety property is *"production is at 0 so new production
errors stand out"*. That holds only if something runs typecheck. Nothing does — `npm run build` is webpack
and test files are not in its graph; `ts-jest isolatedModules` is transpile-only. The property is currently
unenforced, which the accept itself flagged as the thing to watch.

**Document conflict to resolve (owner decision)**

- `spec.md:92` (FR-18) + `spec.md:264` ("Typecheck debt | Clean in this project? | **Yes**") assert clean + CI-gated.
- Project `CLAUDE.md:158` + `:167` accept 111 test-file errors and state no CI job typechecks the package.

The `CLAUDE.md` decisions are later and explicit; `spec.md` is the stale document and has never been amended.

**Entry-points**

- `src/client/office-addins/package.json` — `"typecheck": "tsc --noEmit --skipLibCheck"`
- `.github/workflows/sdap-ci.yml:432-485` — the typecheck steps that exist, none for this package
- `projects/spaarkeai-word-add-in-r1/CLAUDE.md:158,167` — the two accept decisions
- `projects/spaarkeai-word-add-in-r1/spec.md:92,214,264` — the stale assertions

**Suggested fix**

1. Add a CI typecheck step for office-addins gating on **production errors only** (fail on any error outside
   `__tests__`/`*.test.*`/`__mocks__`). This preserves the accepted debt while restoring the missing property.
2. Amend `spec.md` FR-18 + the Assumptions table to state the accepted baseline (0 production / ~111
   test-file) instead of "clean".

**Estimated effort**: ~1 hour (one workflow step + spec edit)
**Blockers**: none
**Related**: `spec.md` FR-18 / criterion 12; project `CLAUDE.md` 2026-09-09 + 2026-09-12 rows;
`notes/009-jest-harness-repair.md`, `notes/017-testing-library-alignment.md`

---

## ISS-005 — Every NEW Word document 503s on the identity check: alternate-key absent-row fault `0x80060891` misclassified as indeterminate

| Field | Value |
|---|---|
| **Type** | Issue (live defect on the deployed dev build; FR-01 primary path) |
| **Found** | 2026-09-18, task 042 UAT — the first defect this project's 12,375 green tests could not have caught |
| **Owner** | **This project — ✅ CLOSED 2026-09-18.** Fix verified (26/26 tests), deployed to `spaarke-bff-dev` hash-verified, all 8 Tier 1 CI checks pass on `62f52d2d3`, and **live-confirmed by the operator**: a new Word document shows the Save tab with no banner. Spec criterion 2 → PASS. ⚠️ A SEPARATE finding from the same session (save produces an SPE file with `_sprk_matter_value` NULL) is under triage in `notes/042-uat-findings-2026-09-18.md` — deliberately NOT folded into this entry |
| **Severity** | Fails SAFE (no duplicate rows) but breaks FR-01's primary path: spec Success Criterion 2 does not hold against the deployed build |
| **GitHub Issue** | [spaarke-dev/spaarke#997](https://github.com/spaarke-dev/spaarke/issues/997) |

**Description**

`POST /api/documents/resolve-identity` returns **503** for the ordinary "not a Spaarke document" case, which the
contract requires to be **200 `{resolved:false}`**. `IsAlternateKeyNotFound` matches only `0x80040217`
(`ObjectDoesNotExist` — the **by-id** code) and `DataverseServiceClientImpl`'s own literal *"not found with
provided alternate key values"*. Dataverse sends **`0x80060891`** for an **alternate-key** miss, with the
message *"A record with the specified key values does not exist in sprk_document entity"* — measured read-only
against dev and byte-identical to the App Insights fault from the failing save.

It is the three-answers contract inverted: it exists so INDETERMINATE is never read as NEW; this reads NEW as
INDETERMINATE.

**Why the suite was green**: `DocumentUrlIdentityResolutionTests.cs:38` sets `ObjectDoesNotExist = -2147220969`
(by-id) and the absent-row test pairs it with the REAL alternate-key message — a pairing Dataverse cannot emit.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Documents/DocumentUrlIdentityResolution.cs:308` (the 503 throw),
  `:324-331` (`IsAlternateKeyNotFound`), `:334-367` (the committed fix)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentUrlIdentityResolutionTests.cs:38` (the wrong
  constant), `~:181-210` (the new regression test)
- Full record: `notes/042-uat-results.md` §6.3

**Suggested fix** (committed, unverified)

Exact-match typed predicate on `unchecked((int)0x80060891)`, mirroring
`DataverseServiceClientImpl.IsAlternateKeyDuplicate`. **Never a range** — `0x80060892` is one integer away,
means duplicate / not-Active key, and must keep answering 503 (NFR-07). Deliberately NOT applied to the shared
`RecordContainerResolver.IsRecordNotFound`, which guards a security decision.

**Blockers**: local build — six attempts, each naming a different just-generated file under the API's
`obj/Debug/net10.0/linux-x64/` as missing; consistent with AV scanning build intermediates; not reproducible in CI.

**Estimated effort**: <1 hour once a build works (fix + test already written)
**Related**: FR-01, spec Success Criterion 2, NFR-07; ISS-003 (#975) for the sibling "one call site, one
classification bug" pattern. Latent risk recorded in #997: five call sites classify not-found by English
message text; only `RecordContainerResolver` does it typed on `ErrorCode`.

---

## ISS-006 — Word pane: "Save as new version" on a filename collision versions an UNRELATED document and silently drops the selected record

| Field | Value |
|---|---|
| **Type** | Issue (live defect on the deployed dev build; cross-record content exposure) |
| **Found** | 2026-09-18, task 042 UAT — observed live, then reproduced from logs + rows |
| **Owner** | ~~Unassigned — needs an owner decision.~~ **This project. Fixed in code by task 055** (closed `23fd17991`; deployed to dev 2026-09-19): the collision withholds the colliding document's id — so no "Save as new version" is offered — when it is filed to a different record or the caller cannot read it (`OfficeCreateCollisionTests` `Collision_WhenTheCollidingDocumentIsFiledToADifferentRecord_…`, `…CannotRead…`). **#1005 stays open until a live re-check in task 042's UAT** (reconciled 2026-10-02 by task 079). |
| **Severity** | 🔴 A user's document is written as a new version of an **unrelated** `sprk_document`, then profiled and RAG-indexed under that row, while the record the user selected receives nothing |
| **GitHub Issue** | [spaarke-dev/spaarke#1005](https://github.com/spaarke-dev/spaarke/issues/1005) |

**What fails**

A create save whose file name collides is refused with `OFFICE_020` (correct — nothing is uploaded). The pane
then offers **"Save as new version"**, which versions the open document onto whichever `sprk_document` owns the
colliding **file name** in the container. Because `WordAdapter.getSubject()` falls back to
`'Untitled Document'` (Word's Title metadata, normally blank) and containers are **business-unit scoped**, that
is routinely an unrelated document belonging to someone else. The pane never names it, and
`useSaveFlow.ts:937-941` (`sentEntity = versionTarget ? null : selectedEntity`) discards the user's selected
Related-to record before the request is sent.

**The bug is not the dropped association** — task 023 D-4/D-5 deliberately omits `targetEntity` on a version
save, and that is correct for its intended case. **The bug is that a filename match is treated as document
identity** (`errorMessages.ts:34-36`: the id is *"the `sprk_document` that already holds `fileName` in the
target drive"*), and the pane offers to write into it unnamed.

**Evidence** — five independent lines agree; full chain in
[`notes/042-uat-findings-2026-09-18.md`](042-uat-findings-2026-09-18.md) §2–§3.

**Suggested fix** — two changes that must land together: (1) name the document in the prompt
("Save as a new version of **{name}** — filed to **{record}**"); (2) do not offer the retry when the colliding
document's association contradicts the user's selection, or carry `targetEntity` and refuse server-side rather
than dropping it silently. ⚠️ **Do NOT fix it by making the version save re-associate** — that silently re-files
another user's document onto the current user's record, which is worse and crosses task 023's authorization
boundary.

**Live data remediation is outstanding** and is the operator's (this project is read-only on Dataverse): one
row in dev currently holds another document's content, profile and index chunks. See §8 of the findings note.

---

## ISS-007 — Write-path invariant I-6 (record owner = BU default Owner team) is not yet applied by ~20 BFF writers outside the Office surface

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | task 080 full create-path inventory (`notes/080-record-ownership.md` §6.5 — 78 create sites + 4 upserts) |
| **GitHub Issue** | [#1034](https://github.com/spaarke-dev/spaarke/issues/1034) |

**Description**

Task 080 made every Office writer own what it creates by a business unit's default Owner team (`IRecordOwnershipResolver`,
record-first, refuse when unresolved). The same defect remains everywhere else the BFF creates a user-facing record
app-only with no `ownerid`: the row is owned by the BFF application user in the ROOT business unit, so no child-BU user can
read it at Deep depth. Only two BFF creates run as the user; none impersonates.

Unfixed writers of record entities: `sprk_communication` (5 app-only paths, **including the Office email-save capture** —
the owner's 066 decision, Communication project), `sprk_document` from Communication archive/inbound attachments
(`CommunicationService` ×3, `IncomingCommunicationProcessor` ×2), the external portal (`ExternalDataService`) and the
Compose upsert (`ComposeCreateOnSavePromoter`), `sprk_event` (`TaskActionCore` when no owner is passed,
`/api/v1/events`, external portal), `sprk_todo` (`TodoGenerationService` — it has an owner parameter no caller passes —
and the external portal), `sprk_invoice` (`InvoiceReviewService` PATCH upsert), `sprk_analysis` (×6, AI zone), and the
Finance job's `sprk_spendsignal` / `sprk_spendsnapshot`. Also `ThreadResolver`'s per-user MASTER thread has no owner.

**Entry-points**

- `projects/spaarkeai-word-add-in-r1/notes/080-record-ownership.md` §6.5 (the full table, file:line per site) and §6.6
  (recipes for analysis and communication)
- The seam to call: `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs`
- For the Office email capture: `OfficeService.SaveAsync` already resolves the team before
  `EmailUploadCaptureService.CaptureAsync`; that service needs to accept it and set it in `BuildCommunicationEntity`.

**Suggested fix**

Each owning project adopts the resolver in its writers (inject `IRecordOwnershipResolver`; record-first context; refuse,
never app-own). Then add an ArchTest census — the `RouteAuthorizationGuardTests` shape — that fails when an app-only create
of a registered record entity sets no owner and carries no documented waiver, so a new writer cannot quietly reintroduce
root ownership.

**Estimated effort**: ~0.5 day per owning area; the census guard ~1 day.
**Blockers**: owner decision on WHO does it — task 080 (the I-6 owner per the write-path registry) or each owning project.
**Related**: ADR-002 WP-1 / invariant registry I-6 (`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5); UAC-r2
#1010 (grant ownership), #1011 (membership resolver).

---

## ISS-008 — `POST /api/v1/work-assignments` writes two columns that do not exist, and uses `ownerid` as the only record of the assignee

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | task 080 — live schema check of `sprk_workassignment` (Dataverse MCP `describe`) while deciding its owner |
| **GitHub Issue** | [#1035](https://github.com/spaarke-dev/spaarke/issues/1035) |

**Description**

`WorkAssignmentEndpoints.CreateWorkAssignmentAsync` writes `sprk_matterid` and `sprk_duedate`. Neither column exists on
`sprk_workassignment` — the real ones are `sprk_regardingmatter` and `sprk_responseduedate`. So any create that supplies a
matter or a due date faults at Dataverse. Separately it sets `ownerid = AssignedToUserId` (a `systemuser` from the request
body), and that is the ONLY place the assignee is recorded: every `sprk_assignedto*` lookup on the entity points at
`contact`. The owner has ruled work assignments are core records that must be team-owned (2026-09-22), which this endpoint
cannot do without losing who the work is assigned to.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs:71-84`
- `describe tables/sprk_workassignment` (Dataverse MCP) — lookups `sprk_regardingmatter`, `sprk_responseduedate`,
  `sprk_assignedto` → contact

**Suggested fix**

Owner decision first: where the assignee lives once the record is team-owned (a `systemuser` lookup would be a schema
change; or map the assignee to their contact). Then fix the two column names and own the row by the matter's team.

**Estimated effort**: ~0.5 day after the decision.
**Blockers**: owner decision on the assignee column.
**Related**: task 080 §6.6; ISS-007.

---

## ISS-009 — Playbook create and clone set no owner, yet playbook ownership checks filter on `ownerid = user`

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | task 080 full create-path inventory |
| **GitHub Issue** | [#1036](https://github.com/spaarke-dev/spaarke/issues/1036) |

**Description**

`PlaybookService.CreatePlaybookAsync` and `ClonePlaybookAsync` are passed the caller's `userId` but never write `ownerid`,
so every playbook — including a clone meant to be private to its creator — is owned by the BFF application user. The same
service's ownership checks filter on `_ownerid_value eq {userId}` (`PlaybookService.cs:254`, `:318`), so a user's own
playbooks cannot satisfy them.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs:133` (create), `:254`, `:318` (the checks)

**Suggested fix**

Decide whether a playbook is per-user (then `ownerid = the caller's systemuserid`, as `WorkspaceLayoutService` does for
layouts) or team-owned (then the checks must change). Per-user matches the checks' intent.

**Estimated effort**: ~2 hours plus tests.
**Blockers**: none.
**Related**: ISS-007.

---

## ISS-010 — The record picker trims by Read, but the save demands AppendTo: a View-Only user can pick a record and then fail to save

| Field | Value |
|---|---|
| **Status** | **Done**: task 084 (`91e73b6fc`), merged in #1082 (`d68924b93`, 2026-10-01). Disabled with the reason, via the save's own evaluator. #1037 closed. The live pane check waits on the next deploy |
| **Urgency** | next-round |
| **Filed** | 2026-09-30 |
| **Source** | UAC-r2 post-merge message 2026-09-30 (`notes/uac-r2-findings-2026-09-30.md` §6 (e)) |
| **GitHub Issue** | [#1037](https://github.com/spaarke-dev/spaarke/issues/1037) |

**Description**

Task 062's impersonated entity search returns every record the caller can READ. The save authorizes its target by
APPENDTO. A View-Only access grant (Read without AppendTo) — realistically a secure record reached through a UAC view-only
grant — lets the user pick the record in the pane and then fail the save. It fails closed (nothing is written), so this is a
correctness/UX defect, not a leak. Granting AppendTo on the role does not close it: the gap is per-record access.

**Entry-points**

- `OfficeService.SearchEntitiesAsync` / `QuerySearchEntityAsync` (the picker); `EntityAccessFilter` +
  `OperationAccessPolicy.cs:176-185` (the save's AppendTo demand)

**Suggested fix**

Make "pickable" equal "savable": either hide records the caller cannot AppendTo, or show them disabled with the reason.
Which is an owner call — it decides whether a view-only user can see in the pane that the record exists.

**Estimated effort**: ~1 day (per-row AppendTo evaluation for a page of results).
**Blockers**: ~~owner decision (hide vs disable)~~ **DECIDED 2026-09-30: option A, show the record DISABLED with the
reason** (UAC-r2 `session27` note; relayed again 2026-09-30). ✅ **Owner go 2026-10-01 ("yes write the task"): tasked
as 084.** Research for 084 corrected two facts here:
- The search is app-only with `MSCRMCallerID` impersonation, not the user client.
- `POST /api/office/todo` demands **Read** (`TodoSourceAccessFilter`), not AppendTo; only `/save` demands AppendTo.

It also found that Outlook's suggestion cards and the ribbon quick-save offer filing targets too, so 084 covers all
three.
**Related**: tasks 062, 065; UAC-r2 task 128's verification lesson (check that a role holds the right a filter demands).

---

## ISS-011 — `POST /api/v1/documents` lets any caller choose the owning team and primary key (LIVE on master)

| Field | Value |
|---|---|
| **Status** | **Done** — merged with task 080; #1043 closed (reconciled 2026-10-02 by task 079) |
| **Urgency** | now |
| **Filed** | 2026-09-30 |
| **Source** | task 080 inventory; master exposure confirmed by UAC-r2 |
| **GitHub Issue** | [#1043](https://github.com/spaarke-dev/spaarke/issues/1043) |

`CreateDocumentRequest.OwningTeamId` (`db046e534`) and `.Id` (task 014) reached master in #960 without
`[JsonIgnore]`. `DataverseDocumentsEndpoints` binds the type from the request body, so a caller can set the owning
team (Secure Record included) and the GUID. Fixed by `5d870b898`. Detail: `notes/080-record-ownership.md` §6.12.

---

## ISS-012 — Team-owned To Dos drop out of the Daily Briefing (regression introduced by task 080)

| Field | Value |
|---|---|
| **Status** | Open. **Route decided by the owner 2026-09-30**; split between task **083** (ours) and UAC-r2 tasks 141 + 152 |
| **Urgency** | now |
| **Filed** | 2026-09-30 |
| **Source** | UAC-r2 session review of task 080 |
| **GitHub Issue** | [#1044](https://github.com/spaarke-dev/spaarke/issues/1044) |

`DailyBriefingCollector` filters to-dos on `owninguser = caller`; a team-owned To Do matches nobody. The options
(caller-owned To Dos / a "for" user column / accept until the "who is notified" design) are in the issue. Detail:
`notes/080-record-ownership.md` §6.12.

**Owner decision, 2026-09-30, relayed by UAC-r2:** use the existing `sprk_todo.sprk_assignedto` (contact) plus
Created By. No new column.

Created By cannot identify the person for BFF-created To Dos: they are app-only, and live `createdby` is
`# mi-bff-api-dev`. So:
- **083 (ours):** the Office writer defaults `sprk_assignedto` to the caller's contact.
- **UAC-r2 141:** the user↔contact link.
- **UAC-r2 152:** the briefing matches Assigned To plus human-only Created By, and fixes the server generators.

Detail: `notes/uac-r2-findings-2026-09-30.md` §9.

---

## ISS-013 — The `Secure Record Owner` role cannot own the children task 080 assigns to it, and the setup guide strips what it has

| Field | Value |
|---|---|
| **Status** | **Fixed live in dev by task 082 (⚠️ 2026-09-30)**: role 36 → 40; the list is `config/secure-record-owner-role.json`. #1051 merged. **Drift decided and removed 2026-10-01** (owner: *"yes can remove them if not needed"*): role 40 → 8, proven by probes (082 note §4.1) |
| **Urgency** | now |
| **Filed** | 2026-09-30 |
| **Source** | 080 note §6.12 item 3; ownership moved to this project by owner instruction 2026-09-30 |
| **GitHub Issue** | [#1046](https://github.com/spaarke-dev/spaarke/issues/1046) |

Record-first ownership gives a child of a secure record to the Secure Record team. Dataverse requires that team's
role to hold `Read` on the child's table.

Measured live on 2026-09-30:
- `sprk_todo`: none, so a secure-record To Do is refused.
- `sprk_document`: all 8, so secure-target saves work in dev.
- `sprk_communication`/`event`/`memo`: none.

The guide's §5.4 strip script keeps only the three root Reads, so re-running it removes the document privilege.
Earlier notes called this "UAC-r2's C10"; that was wrong on both sides. Detail: the 082 POML.

**UAC-r2 agreed on 2026-09-30:**
- 082 owns it and grants on the existing role.
- The drift has no known origin.
- The guide edits are ours.
- The ONE codified JSON set is extended by their task 146.
- The NFR-05 census clause is theirs and reads our file.

---

## ISS-014 — The Outlook ribbon quick-save sends the LOGICAL name, so the save refuses every predicted quick-save (`OFFICE_002`)

| Field | Value |
|---|---|
| **Status** | **Done**: task 084 (`91e73b6fc`), merged in #1082 (`d68924b93`, 2026-10-01). The ribbon sends `target.entityType`; regression test `Issue1075_QuickSaveLogicalNameTests`. #1075 closed |
| **Urgency** | before task 078's unified package is installed (latent until then) |
| **Filed** | 2026-10-01 |
| **Source** | Research for task 084 (code reading; not yet reproduced by a test) |
| **GitHub Issue** | [#1075](https://github.com/spaarke-dev/spaarke/issues/1075) |

- `quickSaveHelpers.ts:81-85` sends `targetEntity.entityType = target.logicalName` (`"sprk_matter"`), and
  `quickSaveHelpers.test.ts:37` pins that value.
- `ValidateSaveRequest` (`OfficeEndpoints.cs:444-455`) accepts only friendly names (`matter`, `project`, …) and returns
  400 `OFFICE_002` for anything else.
- `EntityAccessFilter` accepts both forms, so the request reaches the handler and is refused there.
- The pane's own save sends the friendly name and is unaffected. The predicted object already carries the friendly
  `entityType` (`communicationSuggestionsService.ts:162-169`).

**Why it is latent:** the button exists only in the unified JSON manifest (`outlook/manifest.json:101`). Production
Outlook runs the XML manifest, which has no quick-save button. Installing task 078's package makes the bug live.

**Fix:** send `target.entityType`, reproduced first by a contract test (`"sprk_matter"` → 400). It is in task 084
because 084 also changes which predicted record the ribbon may auto-file to.

---

## ISS-015 — `sprk_invoice` has no `sprk_invoicename` column: the Office invoice quick-create always fails

| Field | Value |
|---|---|
| **Status** | **Our half DONE**, on master via #1082 (`d68924b93`) (task 085, `04158652e`): the invoice quick-create writes `sprk_name`, pinned by `Issue1079_InvoiceQuickCreateNameTests` (red before, green after). The live pane check waits on the next BFF deploy from master. **The issue stays OPEN** for the other owners' three sites below |
| **Urgency** | now (LIVE: every invoice quick-create from the pane fails) |
| **Filed** | 2026-10-01 |
| **Source** | unified-access-control-r2 task-130 verifier; confirmed live here |
| **GitHub Issue** | [#1079](https://github.com/spaarke-dev/spaarke/issues/1079) |

- `OfficeService.cs:1916` writes `entity["sprk_invoicename"] = name`. The column does not exist; `sprk_invoice`'s
  primary name is `sprk_name` (live metadata, 2026-10-01).
- It was introduced by #934 (`f5fee2141`). No test asserts the invoice name attribute.
- Fix: write `sprk_name`, with a regression test that asserts the attribute name.
- Same wrong column, NOT ours:
  - `Services/RecordMatching/DataverseIndexSyncService.cs:52-55` (invoices likely missing from the records index);
  - `scripts/ai-search/Sync-RecordsToIndex.ps1`;
  - `scripts/backfill-multi-container-multi-index/...ParentRecords.ps1`.
- `sprk_billingevent.sprk_invoicename` does exist, so references on that entity are correct.
- **Found by 085's review:**
  - `Sync-RecordsToIndex.ps1` also selects `sprk_invoicedescription`, which does not exist either (the real column
    is `sprk_description`).
  - `spaarke-ai-azure-setup-dev-r1/notes/phase-5-ingestion-evidence.md` had already recorded "0 invoices" indexed
    because of this column, and filed it as backlog. So invoices have likely never been in the records index.
  - Both points were added to #1079.

---

## ISS-016 — The root BU's default team has no roles, so Dataverse refuses any Office create it would own

| Field | Value |
|---|---|
| **Status** | **Done 2026-10-02 (owner).** The owner assigned **Spaarke Basic User** to the dev root team "Spaarke". Verified live: probe rows owned by it on all six tables, and a real unfiled save through the deployed BFF (master `5e39f2bea`) produced a document owned by it. **Owner's ruling:** the root BU holding users is a **dev data artifact**; production users sit in the customer's named child BU, and the BFF's application user is placed in the customer BU. So the role's organization-wide read (Deep at the root) reaches no production user, and no root-team role is codified for production. Residual, stated on #1081: a production root-BU account that creates from the add-in with no target would be refused |
| **Urgency** | closed |
| **Filed** | 2026-10-01 |
| **Source** | Task 085's live real-Dataverse probe (push-to-github Step 1.7) |
| **GitHub Issue** | [#1081](https://github.com/spaarke-dev/spaarke/issues/1081) (closed 2026-10-02) |

**Description**

Dataverse refuses to make a team the owner of a row unless that team holds Read on the row's table. The caller's
privileges don't matter; an admin is refused too, the same mechanism as ISS-013. In dev, the **root** BU's default
team **"Spaarke"** (`09fbf21c-1872-f011-b4cb-7c1e52671ad0`) has **0 privileges** (`RetrieveTeamPrivileges`). Task
080's `RecordOwnershipResolver` returns that team in two cases: a root-BU caller with no target record, and a target
that is itself root-owned.

**Concrete failure:** after the next deploy from master, the **9 interactive people in the root BU, the owner's
account among them**, get a 5xx on the unfiled save, the Matter / Project / Invoice quick-creates and To Do creates.
It is a 5xx, not `OFFICE_022`, because the resolver propagates Dataverse faults by design. 080's backfill also plans
7 To Dos to the root team, which would be refused too.

Live, 2026-10-01: an invoice owned by the root team was refused (*"Read Privilege Check For Owner … privilegeCount=0 …
missing prvReadsprk_Invoice"*). The same create owned by the "Spaarke Business Unit 1" team (725 privileges, Read Deep)
returned 204, was read back and deleted. Full table: `notes/085-invoice-quickcreate-name.md` §2.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs:317-356` (no root-BU exclusion).
- `GET {org}/api/data/v9.2/teams(09fbf21c-1872-f011-b4cb-7c1e52671ad0)/Microsoft.Dynamics.CRM.RetrieveTeamPrivileges()`
  returns `RolePrivileges: []`.
- The precedent: `config/secure-record-owner-role.json` + `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` (082).

**Suggested fix: the owner chooses**

- **(A) Recommended:** a minimal owner role on the root default team, as 082 did:
  - Read only, Basic depth, on `sprk_document`, `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_todo` and
    `sprk_communication`;
  - codified in `config/` and the setup guide;
  - side effect: every root-BU member gains Basic Read on rows owned by the root team.
- **(B)** The resolver refuses the root team with `OFFICE_022`. That is a clean refusal, but root users still cannot
  create anything.
- **(C)** Move the 9 people into child BUs.

**Estimated effort**: (A) about an hour on the 082 tooling, plus probes.
**Blockers**: the owner's decision.
**Related**: #1045 (080), #1046 / ISS-013 (the same mechanism), #1079 / ISS-015 (where it was found).

---

## ISS-017 — Office job-row reads never worked in production, and 68% of saves had no job row

| Field | Value |
|---|---|
| **Status** | **Done** 2026-10-01: task 060, PR #1085 merged as `402afb657` (typed reads, the row as the durable record, metadata-only payload). #1084 closed. The live restart check waits on the next deploy |
| **Urgency** | now |
| **Filed** | 2026-10-01 |
| **Source** | Task 060's investigation; confirmed in App Insights (`spe-insights-dev-67e2xz`, 60 days) |
| **GitHub Issue** | [#1084](https://github.com/spaarke-dev/spaarke/issues/1084) |

**Description**

1. **The `dynamic` reads.**
   - `IProcessingJobService`'s two reads return **anonymous types** (`internal` to `Spaarke.Dataverse`), which the
     BFF reads through `dynamic`.
   - The binder checks the call site's access, so every read throws (`RuntimeBinderException`, 2026-08-25, *"'object'
     does not contain a definition for 'Status'"*), and the callers swallow it.
   - **Concrete failure:** 039's idempotency check never detected a duplicate in production, and a job-status poll
     that misses memory returns 404.
   - The test double returns an `ExpandoObject`, so tests never saw it.
2. **The payload overflow.**
   - `sprk_payload` received the document/attachment base64 and the email body, and the column holds at most 50,000
     characters.
   - **40 saves, 13 rows, 27 refusals in 60 days.** Those saves ran with an in-memory-only job id, and every later
     status write failed with "Does Not Exist" (75 + 104).

**Entry-points**: `DataverseServiceClientImpl.GetProcessingJob*Async`; `OfficeService.GetJobStatusAsync` and the
`payload` in `SaveAsync`; `OfficeDocumentPersistence.CheckForExistingJobAsync`.

**Fix**: task 060, `notes/060-job-status-store-and-extraction.md` §2–§3.
**Related**: #229 (stale TRACKED marker).

---

## ISS-018 — Office background work is lost on an app restart, and SSE event numbers are per instance

| Field | Value |
|---|---|
| **Status** | **Done** 2026-10-01: task 068, PR #1091 merged as `08b70d6cc`. #1086 closed. **Live checks done 2026-10-02** after the deploy of `5e39f2bea`: a graceful restart (the work finished first) and a hard kill mid-run (redelivered, lock taken back by its owner, completed) — `notes/068-durability-siblings.md` §9 |
| **Urgency** | now |
| **Filed** | 2026-10-01 |
| **Source** | Task 068's investigation (`notes/068-durability-siblings.md` §1); the Redis lock behaviour verified against the .NET 10 `RedisCache` source |
| **GitHub Issue** | [#1086](https://github.com/spaarke-dev/spaarke/issues/1086) |

**Description**

1. **Generate Profile is fire-and-forget.** The route returns 202, then `OfficeProfileDispatcher` runs the profile in
   `Task.Run` under `ApplicationStopping`. A restart cancels it, and nothing was persisted.
2. **The profile job loses its own redelivery.**
   - `AppOnlyDocumentAnalysisJobHandler` holds a 10-minute Redis processing lock and releases it with the job's token.
   - On a graceful stop that token is cancelled. `RedisCache.RemoveAsync` → `ConnectAsync` calls
     `ThrowIfCancellationRequested()` first, so the release throws (caught) and the lock stays. A hard crash leaves it
     too.
   - `sdap-jobs` redelivers after its 5-minute lock. The handler finds the stale lock and answers "already being
     processed by another instance" with **Success**, so the message is completed with no profile.
   - This hits every profile a save queues, not only the button.
3. **SSE event numbers are per instance.** `JobStatusService` numbers events from an in-memory dictionary on each
   instance (never pruned), and the stream mixes that number line with its own counter, so ids repeat and a
   `Last-Event-ID` reconnect can drop live events. The stream also reads its snapshot before subscribing.

**Concrete failure**: a deploy while a profile runs leaves the document unprofiled, with its status stuck; a
reconnecting SSE client can miss the job's completion. The pane polls, so it has not hung.

**Entry-points**: `OfficeProfileDispatcher.Dispatch`/`RunAsync`; `AppOnlyDocumentAnalysisJobHandler.ProcessAsync`;
`JobStatusService.GetNextSequenceAsync`; `OfficeJobStatusService.ProduceJobStatusEventsAsync`.

**Fix**: task 068, `notes/068-durability-siblings.md` §2.
**Related**: #1084 (060). Observed and not fixed by 068: `ComposeProfileDispatcher` has the same `Task.Run` shape;
`POST /api/documents/{id}/analyze` enqueues with the bare key; the other lock users share hazard 2.

---

## ISS-019 — Flaky: a Communication seam test fails under full-suite load (1 s regex timeout)

| Field | Value |
|---|---|
| **Status** | Open — not this project's code (email-communication-intelligence-r2, `18cfcbd660`) |
| **Urgency** | next-round |
| **Filed** | 2026-10-01 |
| **Source** | Task 068's full-suite run: 13,065 passed / **1 failed** / 54 skipped; the failure passes alone 3 of 3 |
| **GitHub Issue** | [#1088](https://github.com/spaarke-dev/spaarke/issues/1088) |

**Description**

`EmailRegardingIntentSeamTests.EnrichAsync_PresentsNewRecordReferencingExisting_StoresGatedProposalAndNotesSummary` took
3 s in the full run and failed: *"Expected row not to be <null> because a new-record intent stores a gated
create-new-record proposal."* Alone it passes in about 200 ms.

**Likely mechanism:** `NewRecordIntentDetector` uses three `RegexOptions.Compiled` patterns with a 1-second match
timeout and treats `RegexMatchTimeoutException` as "no intent", so under CPU load the intent is missed. Three timeouts
fit the 3 s. Not confirmed beyond that: outside 068's scope.

**Concrete failure**: an intermittent red in the BFF suite; in production, a regarding-intent proposal silently skipped
under load.

**Entry-points**: `Services/Communication/Engine/NewRecordIntentDetector.cs:50-70, :136`;
`tests/integration/seam/Communication/EmailRegardingIntentSeamTests.cs:122-135`.

---

## ISS-020 — Six `sdap-jobs` handlers still drop a job whose process crashed

| Field | Value |
|---|---|
| **Status** | Open — the graceful-stop half is fixed for every handler by task 068; the crash half only for the profile handler |
| **Urgency** | next-round |
| **Filed** | 2026-10-01 |
| **Source** | Task 068's independent code review (finding W5) |
| **GitHub Issue** | [#1089](https://github.com/spaarke-dev/spaarke/issues/1089) |

**Description**

`EmailAnalysisJobHandler`, `ProfileSummaryJobHandler`, `RagIndexingJobHandler` (indexes Office saves),
`AttachmentClassificationJobHandler`, `InsightsIngestJobHandler` and `IncomingMessagingJobHandler` take an ownerless
Redis processing lock. A crash leaves it; the 5-minute redelivery finds it and is completed as "already being processed"
with nothing done. Task 068 made `IdempotencyService.ReleaseProcessingLockAsync` always release (fixing the graceful stop
for all of them) and made `AppOnlyDocumentAnalysisJobHandler` lock under its job's id with a one-minute takeover age.

**Concrete failure**: a crash while one of these runs drops that unit of work; an Office save's index never happens.

**Entry-points**: each handler's `TryAcquireProcessingLockAsync(...)` call; copy `AppOnlyDocumentAnalysisJobHandler.ProcessAsync`.

---

## ISS-021 — The pane reads the profile once after Generate Profile

| Field | Value |
|---|---|
| **Status** | Open — predates 068; the server side is now truthful |
| **Urgency** | next-round |
| **Filed** | 2026-10-01 |
| **Source** | Task 068's independent code review (finding S3), confirmed in `useDocumentProfile.ts` |
| **GitHub Issue** | [#1090](https://github.com/spaarke-dev/spaarke/issues/1090) |

**Description**

After the 202, `useDocumentProfile.generateProfile` shows Pending and re-reads the document once, immediately, before the
queued job has started. The pane then shows the status from before the click and never reads again. The queued job now
writes Pending → Completed or Failed; the pane needs to keep reading until the status leaves Pending (and to close the
race with the job's start, either the BFF writes Pending at queue time or the pane ignores reads taken before it).

**Concrete failure**: a user who regenerates a Failed profile sees "Failed" again while the new profile runs or after it
succeeds.

**Entry-points**: `src/client/office-addins/shared/taskpane/hooks/useDocumentProfile.ts` `generateProfile`; the 202 now
carries `jobId` and `Location: /api/v1/documents/{id}`.

---

## Deferrals

### ✅ D-032-1 — WITHDRAWN 2026-09-10 (final). The cascade setting was the wrong question.

**Operator, 2026-09-10**, stating the model precisely: *if a user has access to a record they have
access to its documents, and vice versa; SPE access is at the container level, not the file level;
secure access grants the record + its documents + the files in the secure BU's container. There is no
case where a user should hold a document but not its record.*

**Verified against CODE — deliberately not docs, since three docs in this area were stale today:**
- **Internal access is caller-scoped and fails closed.** `Spaarke.Core/Auth/AuthorizationService.cs:54`
  denies when there is no caller token and "must never degrade to app-only evaluation" (task 004,
  finding A-2; zero app-only consumers, verified 2026-08-21). `:79` passes the caller's token and
  `:224-225` "queries Dataverse AS THE USER". So Read on a document is decided by Dataverse's native row
  security for *that user* — BU ownership, role depth, teams — the same evaluation that decides Read on
  the matter.
- **External access is granted at the ROOT record only.** `GrantAccessRequest` targets exactly one
  Project, Matter or Work Assignment — never a document. External document visibility is
  `_sprk_project_value eq {projectId}` (`ExternalDataService.cs:209-213`): documents are visible
  *because of* the root.
- **SPE is broker-only for external users** — contacts are never added to containers.
- **No code shares an individual document row.** The only Dataverse SDK message import in the BFF is
  `UserPrivilegeChecker`'s `RetrieveUserPrivilegesRequest` — read-only.

**Why the same-day reinstatement below was wrong.** I asked whether `sprk_document → sprk_matter`
cascades. Cascade (Referential vs Parental) governs what happens during a *share / assign / delete of the
matter* — whether that operation propagates to its documents. The model never grants document access by
sharing the matter; it grants BOTH through the same BU/role evaluation (internal) or the same root grant
(external). So "Referential" answered a question the model does not depend on. The finding assumed
access can diverge; the model is built so it cannot, and the code enforces it.

**One honest residual — configuration, not code, and the operator's to own:** the equivalence holds as
long as each security role grants **aligned depth on `sprk_document` and `sprk_matter`** and documents
are owned by the same BU as their record. A role granting Org-depth Read on documents but BU-depth on
matters would reintroduce the divergence. (Dataverse MCP was down this session; role depths were not
inspected.) Separately, `unified-access-control-r2` already records that the secure-BU model is
currently non-functional in dev — `Spaarke` is the root BU and `Spaarke Basic User` holds Deep depth
reaching the secure BU. That is a real secure-isolation risk, but it exposes the record AND its
documents together — not one without the other — so it does not revive this finding.

**No hand-off.** Nothing goes to UAC-r2 for this entry.

**Doc drift found while verifying (not this entry's problem, recorded so it is not lost):**
`docs/architecture/uac-access-control.md` (corrected 2026-08-20) still states `AuthorizationService`
passes `userAccessToken: null` and runs app-only; the code was changed to fail-closed caller-scoped one
day later (2026-08-21). Relying on it would have produced a false caveat here.

#### ~~D-032-1 — REINSTATED 2026-09-10~~ (SUPERSEDED the same day — see the withdrawal above)

**Operator verified in the maker portal, 2026-09-10**: the `sprk_document` → `sprk_matter`
relationship is **Referential**, with **Delete: Remove Link** and no cascade share.

**So Dataverse does NOT automatically grant Read on a document when a caller is granted its matter.**
The one question this entry was reduced to has been answered, and it answered against the withdrawal.

**I withdrew this finding too readily on 2026-09-09** and should record why, because the mistake is
reusable. The operator challenged the premise ("aren't we granting access at the container level, via
the parent record?"), I checked, and I found the codebase describing exactly that model — so I treated
a description of INTENT as evidence of MECHANISM and withdrew. The very sentence I quoted should have
stopped me: `Spaarke.Core/Auth/AuthorizationService.cs:246-249` calls the parent check *"what makes
'access flows from the parent' an **enforced property rather than a stated intention**"*. That phrasing
only makes sense if, absent that check, it is NOT enforced. The cascade setting is the mechanism, and
nobody had looked at it.

**The precise position, which is narrower than either extreme:**
- The model holds **wherever code enforces it** — `GetCallerRecordAccessAsync` exists precisely to make
  it true on the paths that call it. `unified-access-control-r2` task 070 built it for that reason.
- The **platform** does not enforce it. Referential means document ACLs are independent of the matter's.
- `VisualizationService.CreateParentHubNode` is **not** one of the paths that enforces it.

**Therefore the original finding below stands as written**: a hub node carries the source document's
matter/project/invoice NAME and a deep link to that record, built without any check against the parent,
and reaches any caller authorized on the document alone. On a secure matter the name is frequently the
sensitive fact.

**Owner: `unified-access-control-r2`.** It owns parent→child access (its Amendment 1) and it owns
`GetCallerRecordAccessAsync`, the correct mechanism. This is NOT a second mechanism r1 should build —
root CLAUDE.md §11. The hook already exists: task 032's `AuthorizeRowsAsync` drops hubs left with no
surviving document, so the drop path is there; what is missing is authorizing the hub itself against
its parent record.

**⚠️ HAND-OFF OBLIGATION — a GitHub Issue is NOT a hand-off.** Per the operator's standing instruction
(2026-09-09): *"filing a github issue also does not explicitly raise it to the project — we need to
provide the note and actively instruct to read it."* This entry must be actively delivered to UAC-r2
with an instruction to read it, not filed and forgotten. Until that happens this is not handed off.

<details>
<summary>Superseded 2026-09-09 withdrawal reasoning (retained — it is the record of the wrong call)</summary>

### ⚠️ D-032-1 — WITHDRAWN AS A FINDING 2026-09-09; reduced to a one-question verification

**Operator challenge, 2026-09-09** — and it was correct. The entry below rests on the premise
*"Read on a document does not imply Read on its matter."* That premise contradicts Spaarke's actual
access model: **access flows from the parent record → container → documents**. If a caller can read
the source document, that is normally *because* they hold the matter, so the hub node's matter name
discloses nothing they did not already have.

The codebase says the same thing in its own words. `Spaarke.Core/Auth/AuthorizationService.cs:246-249`
(written by `unified-access-control-r2` task 070) describes the parent check as *"what makes 'access
flows from the parent' an enforced property rather than a **stated intention**"*.

**What is genuinely unresolved** is narrow, and it is a Dataverse configuration question, not a code
defect: `DataverseAccessDataSource` hard-codes `sprk_documents({id})`, so it asks Dataverse about the
**document row itself** — it does not derive the answer from the matter. Whether matter access
therefore implies document Read depends on the `sprk_document` → `sprk_matter` relationship's cascade
configuration. The ERD records that lookup as **optional** (`sprk_document }o--o| sprk_matter`), and a
document may have no matter at all.

**The single question to close this**: is `sprk_document` → `sprk_matter` **Parental / cascading-share**,
or **Referential** (no cascade)?
- **Parental/cascading** → the operator's model holds, there is no disclosure, **delete this entry.**
- **Referential** → document Read can exist without matter Read, and the model is a stated intention
  rather than an enforced one. That is UAC-r2's Amendment 1 territory and would then warrant an
  ACTIVE hand-off (a note they are instructed to read — not a GitHub issue nobody opens).

Checkable in two minutes in the maker portal (Tables → sprk_document → Relationships → the
`sprk_matter` lookup → Advanced/cascade behavior). Dataverse MCP was down 2026-09-09 so it could not
be queried live.

**Do NOT hand this to another project until that question is answered.** Handing over a finding whose
premise is unverified is exactly the burial-by-deferral the project policy forbids, and it would
create a cross-project dependency for something that may not exist.

> **✅ ANSWERED 2026-09-10 — REFERENTIAL.** The condition above is met, so the hand-off is now
> warranted rather than premature. See the reinstatement block at the top of this entry.

</details>

<details>
<summary>Original entry as filed by task 032 (premise now disputed — retained for the record)</summary>

### D-032-1 — Visualization parent HUB nodes are not authorized against their parent record

**Found by** task 032 while closing finding F-b. **Owner: `unified-access-control-r2`** (its surface).

`VisualizationService.CreateParentHubNode` builds a hub node carrying the SOURCE document's own matter /
project / invoice name (`sourceDoc.MatterName`, `BuildParentRecordUrl("sprk_matter", …)`). The caller is
authorized on the source document, but **Read on a document does not formally imply Read on its matter** —
so a matter's NAME, and a deep link to its record, can reach a caller holding no rights on the matter itself.
On a secure matter the name is frequently the sensitive fact (a matter named for a counterparty discloses the
engagement's existence), which is the same argument `RecordSearchAuthorizationFilter`'s remarks make.

**Not fixed in task 032, deliberately.** F-b is about RESULT ROWS; hub nodes are pre-existing and are the
source's own information. Widening a security task's scope silently is what CLAUDE.md §11 forbids, and the
correct check (`GetCallerRecordAccessAsync` against `sprk_matters`/`sprk_projects`/`sprk_invoices`) is
UAC-r2's mechanism, not a second one built here.

**Suggested fix**: authorize each hub against its parent record and drop the hub — and the source's edge to
it — when Read is absent. Task 032's `AuthorizeRowsAsync` already drops hubs left with no surviving
document, so the hook exists.

*(end of retained original D-032-1 entry)*

</details>

---

### ✅ D-032-2 — NOT DEFERRED. Folded into task 033 by operator decision 2026-09-09

This is **not** a hand-off and **not** a deferral. It has no external owner, and the parties it would
mislead — tasks **033** and **034** — are in this project. Per the project deferral policy (defer only
for a good technical reason *or* a clear hand-off; never bury), it becomes an acceptance criterion of
**task 033**, which renders this surface.

The original reasoning for not fixing it *inside task 032* still stands and is unchanged: a
response-contract change does not belong inside a security fix. That justified deferring it out of
**032**; it never justified deferring it out of the **project**.

Scope when 033 runs: add a warnings channel to `GraphMetadata` mirroring cross-record document
search's existing `PARTIAL_RESULTS` warning, and surface it in the Find view.

<details>
<summary>Original entry as filed by task 032 (retained for the record)</summary>

### D-032-2 — Rows dropped past the authorization budget are not announced to the client

**Found by** task 032. Same shape as the limitation `RecordSearchEndpoints.AuthorizeRowsAsync` recorded.

`VisualizationEndpoints.AuthorizeRowsAsync` bounds its work at `MaxDocumentAuthorizationChecks = 100` and
**drops** everything past it (fail closed). The caller sees a short graph that is indistinguishable from
"there simply are not many related documents". Cross-record document search announces exactly this with a
`PARTIAL_RESULTS` warning; `GraphMetadata` has no warnings channel.

**Not fixed in task 032, deliberately**: adding one is a response-contract change, and a response-contract
change does not belong inside a security fix — the same call `RecordSearchEndpoints` made and recorded.

**Relevant to task 033/034**, which render this surface: a short result page may mean "withheld", not
"nothing matched". See `notes/032-authorization-hardening.md` §5 for the counts that make this reachable
(five hardcoded relationship queries at `TopCount = 50` each).

</details>

---

### D-043-1 — `deploy-office-addins.yml` path filter misses a file the add-ins ship

**Found by** task 043 while path-filtering the new office-addins CI gate.
**Owner**: whoever next touches `.github/workflows/deploy-office-addins.yml`. One-line change.
**GitHub Issue**: [#1039](https://github.com/spaarke-dev/spaarke/issues/1039) (filed 2026-09-30 at push time — it had been recorded here only).

`src/client/office-addins/webpack.config.js:101` aliases
`@spaarke/communication-components/logic/connections/provenance` at
`src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts`, and
`shared/taskpane/services/communicationSuggestionsService.ts:7` imports it. So that file is
**bundled into the shipped add-ins**.

`deploy-office-addins.yml` triggers only on `src/client/office-addins/**` and
`src/client/shared/Spaarke.Auth/**`. Editing `provenance.ts` alone therefore changes the add-in's
behaviour **without redeploying it** — the identical failure mode the `Spaarke.Auth` path was added
to prevent, and its comment says so in as many words: *"Without this path the add-ins would keep
deploying against a stale auth lib."*

**Fix**: add `- 'src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts'`
to that workflow's `paths:`. `provenance.ts` has no imports of its own, so the exact file is the
precise filter; no wildcard needed.

**Not fixed in task 043, deliberately**: it is a different workflow with different blast radius
(a deploy trigger, not a test gate), and widening someone else's deploy trigger is not a change to
make as a side effect. The new gate `.github/workflows/office-addins-tests.yml` **does** carry the
path, so the test side is already covered.

Detail: `notes/043-office-addins-ci-gate.md` §8 F-1.

---

### D-043-2 — `identity-obj-proxy` is mapped by `jest.config.js` but is not a dependency

**Found by** task 043 on a from-scratch `npm install` of `src/client/office-addins`.
**Owner**: next person editing that package's test setup. One `devDependencies` line.
**GitHub Issue**: [#1040](https://github.com/spaarke-dev/spaarke/issues/1040) (filed 2026-09-30 at push time — it had been recorded here only).

`src/client/office-addins/jest.config.js` maps `\.(css|less|scss|sass)$` → `identity-obj-proxy`,
which appears in neither `package.json` nor `package-lock.json`, and is not installed.

Latent, not currently breaking: jest resolves `moduleNameMapper` targets lazily, and no suite in
this package imports a stylesheet today — confirmed on a clean tree. The first CSS import added to
the test graph will fail with a "could not locate module" error that says nothing about CSS.

Detail: `notes/043-office-addins-ci-gate.md` §8 F-4.

---

### D-063-1 — the three sibling RAG index routes authorize no document before writing to the tenant partition

**Found by** task 063 while closing F2 on `/send-to-index`.
**Owner**: whoever next hardens `Api/Ai/RagEndpoints.cs`. Not this project's finding.
**GitHub Issue**: [#1041](https://github.com/spaarke-dev/spaarke/issues/1041) (filed 2026-09-30 at push time — it had been recorded here only).

`POST /api/ai/rag/index`, `/index/batch` and `/index-file` are all bound by
`AddTenantAuthorizationFilter()` — and unlike `send-to-index` they always were, because their request
types (`KnowledgeDocument`, `IEnumerable<KnowledgeDocument>`, and the query string) ARE matched by
`TenantAuthorizationFilter.ExtractTenantId`. So the tenant half of F2 does not apply to them.

**What does apply**: none of the three authorizes a *document* before writing chunks into the tenant's
search partition. `/index-file` is the sharpest case — it takes a caller-supplied `DriveId` + `ItemId`
and indexes whatever those name. The content is downloaded OBO, so SPE container permissions are a
real gate; but that is container-level, and it is the same coarse gate task 063 replaced on
`send-to-index` with a per-document Dataverse check.

**Not fixed in task 063, deliberately**: F2 names `send-to-index` and only `send-to-index`. Widening a
security fix to three adjacent routes without their own reproduce-first evidence is exactly what root
CLAUDE.md §11 forbids, and each of the three has different callers whose breakage would have to be
sized separately. Recorded rather than silently accepted.

**Explicitly NOT in scope of this item**: `POST /api/ai/rag/enqueue-indexing`. It authenticates under
the `RagApiKey` scheme, carries no `tid`, and takes its tenant from the body **by design** —
`TenantResolution`'s own remarks name it as the one principal in the system with no tenant claim.
Applying a token-derived binding there would break it.

Detail: `notes/063-send-to-index-authz.md` §8.4 and §8.5.

---

### D-063-2 — `.claude/constraints/auth.md` states two things about `RetrievePrincipalAccess` that are no longer true

**Found by** task 063, which depends on the corrected behaviour.
**Owner**: main session (sub-agents cannot write to `.claude/`). One paragraph.
**GitHub Issue**: [#1042](https://github.com/spaarke-dev/spaarke/issues/1042) (filed 2026-09-30 at push time — it had been recorded here only).

The "Authorization Check Pattern" section carries a ⚠️ correction dated **2026-08-20** claiming that
`RetrievePrincipalAccess` **"has zero call sites in the repository"** and that both modes **"grant at
most `AccessRights.Read`"**.

Both statements are now false. `DataverseAccessDataSource.TryRetrievePrincipalAccessAsync` is live on
**both** access paths — `GetUserAccessAsync` (`:662`) and `GetRecordAccessAsync` (`:433`) — and returns
Dataverse's real rights, including `Write`. The retrieval probe that grants at most `Read` is now only
the **fallback** taken when RPA gives no answer (`:436-449`).

**Why it matters rather than being pedantry**: task 063's gate is `AccessRights.Write` on
`sprk_document`. A reader who trusted this file would conclude that check can never pass and that the
route is dead — or, worse, would "fix" it back to `Read` and silently reopen the finding. The stale
text is also the reason the file's own header block argues that stale review metadata is itself a
control.

Detail: `notes/063-send-to-index-authz.md` §3b.
