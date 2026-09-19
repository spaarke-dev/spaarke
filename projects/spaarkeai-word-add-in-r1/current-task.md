# Current Task State — spaarkeai-word-add-in-r1

> **Last Updated**: 2026-09-19 (by task-execute Steps 9.5–10) — **task 055 COMPLETE: every gate green.** BFF suite 12,379 passed / 0 failed · ArchTests 191/191 · gated jest 46/46 suites, 540 tests · publish **+0.075 MB** vs a fresh master build · no vulnerable packages · code-review + adr-check **0 critical, 0 ADR violations**.
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-word-add-in-r1`, PR #960.

---

## Quick Recovery (READ THIS FIRST)

> ⚠️ **This block (2026-09-17 handoff) is authoritative and supersedes the table further down**, whose
> "Next Action" row accreted across the session and contains stale, duplicate-numbered entries. Read this;
> treat the table below as historical detail only.

| Field | Value |
|---|---|
| 🔴 **HANDOFF 2026-09-19 — READ THIS ROW FIRST** | **Task 055 is COMPLETE — every gate green, nothing outstanding.** BFF 12,379 passed / 0 failed · ArchTests 191/191 · gated jest 46/46 suites, 540 tests · publish **+0.075 MB** vs a fresh master build (45.430 vs 45.355 MB, `Compress-Archive -Optimal`, PDBs in) · no vulnerable packages · code-review + adr-check **0 critical, 0 ADR violations**. **[#1005](https://github.com/spaarke-dev/spaarke/issues/1005) stays OPEN** until confirmed on a deployed build (the ISS-005 precedent). **Do NOT re-derive the design**: it is settled and written up in `notes/055-collision-names-its-target.md` (§1 the trigger-(b) authorization answer, §2 why the gate is at the ENDPOINT not the service, §3 the one-projection mechanics, §4 why half (2) withholds rather than re-associates, §5 the stale `constraints/auth.md`, §5A three fixture gaps). |
| **055 — ALL ACTIONS COMPLETE (kept for provenance)** | **(1)** ✅ **DONE — full BFF suite GREEN**: `Failed: 0, Passed: 12379, Skipped: 56, Total: 12435` (32 m 2 s). Baseline was 12,375 / 0 / 56 = 12,431 total, so **+4 total** of which my three new `OfficeCreateCollisionTests` account for +3. ⚠️ **The remaining +1 is unexplained and is NOT hand-waved**: no other commits landed on this branch between the baseline measurement and this run, and the deleted temp diagnostic was excluded from the binary used (`--no-build` over the post-deletion build). Most likely the recorded baseline was itself off by one or measured at a different commit — **0 failed is the load-bearing fact** — but reconcile it before quoting 12,379 as a new baseline. ⚠️ This run used the **pre-format** binary (it predates the hook's reformat in `a936353d8`), so it validates the logic, not the committed bytes. **(2)** Gated jest by path — from `src/client/office-addins`: `npx jest --runTestsByPath $(grep -vE '^\s*(#\|$)' ci-gated-suites.txt)`; expect **46 suites green**. (Full `npx jest` gives 10 failed / 46 passed of 56 — the 10 are PRE-EXISTING non-gated reds, stated as such in `ci-gated-suites.txt`'s own header; do not chase them.) **(3)** ArchTests: `dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj` → expect **191/191**. **(4)** BFF publish size vs a **FRESH `origin/master` build** with `Compress-Archive -Optimal` (CLAUDE.md §10 bullet 4 — never vs the recorded number) + `dotnet list package --vulnerable --include-transitive`. **(5)** ✅ **DONE** — `/code-review` + `/adr-check`: **0 critical, 0 ADR violations**; two non-logic findings fixed (an orphaned XML doc comment, and §5A's "Two fixture gaps" heading over a three-row table). **(6)** ✅ **DONE** — POML `completed`, TASK-INDEX ✅, note §7 written, committed + pushed. **[#1005](https://github.com/spaarke-dev/spaarke/issues/1005) deliberately left OPEN** until confirmed on a deployed build (the ISS-005 precedent). |
| ⚠️ **055 — the pre-commit hook REFORMATTED every file after those runs** | `lint-staged` ran `prettier --write` (4 ts/tsx) and `dotnet format` (6 .cs) and **applied modifications** during commit `a936353d8`. Both are behaviour-preserving, but the bytes verified below are **pre-format** bytes — so the committed tree has not, strictly, been tested. Next-action (1) and (2) re-run against the committed sources and settle it; treat a surprise there as formatter fallout, not a logic regression. |
| **055 — verification already DONE (do not repeat)** | Server targeted: **22/22 passed, 0 failed** — all 7 `OfficeCreateCollisionTests` (4 pre-existing + 3 new), plus `OfficeImmutableSaveFileSafetyTests` and `OfficeEmailSaveNamingTests` (both must-pass-**UNMODIFIED**, confirmed BY NAME) and all `OfficeSaveSpineIdempotencyTests`. Client collision suites **14/14**. `npm run typecheck` **111 total / 0 production** — baseline exactly held. Build exit 0. All temporary instrumentation removed (`Tmp055Diagnostic.cs` deleted; zero `TMP055` in `OfficeService.cs` — verified). |
| **055 — files modified, UNCOMMITTED** | **Server**: `Models/Office/SaveResponse.cs` (+`SaveError.ExistingDocumentName`) · `Api/Office/OfficeEndpoints.cs` (2 usings, `AuthorizationService` handler param, the gate call, new `WithholdCollisionIdentityIfUnauthorizedAsync`, `existingDocumentName` extension) · `Services/Office/OfficeDocumentPersistence.cs` (`DocumentNameAttribute`, `DirectAssociationAttributes`, `CollisionTarget` record, `FindDocumentIdByLocationAsync` **renamed** → `FindCollisionTargetByLocationAsync` with a widened `ColumnSet`) · `Services/Office/OfficeService.cs` (`ResolveNameCollisionAsync` takes `targetEntity`; `associationMatches`/`offersVersionRetry`; call-site threading). **Client**: `utils/errorMessages.ts` · `components/SaveFlow.tsx`. **Tests**: `data-mutation/OfficeVersionSave/OfficeCreateCollisionTests.cs` (+3) · `contract/Api/Office/OfficeEndpointsContractTests.cs` (fixture) · `__tests__/SaveFlowCollision.test.tsx` (+1) · `__tests__/errorMessages.collision.test.ts` (+2). **Docs**: `notes/055-collision-names-its-target.md`, this file. |
| **055 — decisions (settled; do not re-open)** | (a) Gate at the **endpoint**, not `OfficeService` — ADR-008, and that service has 15 deps and no authorization concern. (b) Half (2) **withholds the id** when the colliding doc's association ≠ the caller's target; it does **not** re-associate (escalation trigger (a)) and does not carry `targetEntity` server-side. (c) The **name travels with the id** — never named when the pane cannot act on it. (d) Server tests live in `OfficeCreateCollisionTests.cs`, **not** the POML's `OfficeEndpointsContractTests.cs` (that fixture "models no collisions at all"); `directional` steps sanction the deviation. (e) **Three fixture gaps were fixed in the FAKE, never by relaxing assertions** — a ColumnSet-blind projection, a discarded `MatterLookup`, and no access grant on create. Each surfaced as a red in a **pre-existing** test; relaxing any of them would have deleted the guarantee the retry depends on. |
| **Task** | **055 — The collision prompt must name the document it would write into, and must not silently discard the selected record.** Phase 2 Save flow · **status: completed** (2026-09-19) · POML `tasks/055-collision-names-its-target.poml` (committed `d9d3a7dfa`) · FULL rigor · opus @ xhigh · steps `directional` · deps 025 ✅ 026 ✅ 054 ✅. Fixes [#1005](https://github.com/spaarke-dev/spaarke/issues/1005) / ISS-006. |
| **Where** | Branch `work/spaarkeai-word-add-in-r1` · PR **#960** · **0 behind / 198+ ahead of `origin/master`**. Deployed state is the stable anchor: BFF live on `spaarke-bff-dev`, add-in live on `icy-desert-0bfdbb61e.6.azurestaticapps.net` at manifest **1.0.9.0**. (No head SHA pinned here — task 042's own docs commit supersedes it; use `git log -1`.) |
| **Progress** | **53 of 55 tasks ✅** — **011 CLOSED 2026-09-18** (operator re-uploaded the 1.0.9 manifest; pane footer confirmed `v1.0.9 (Sep 18, 2026)`). Open: **042 🔄** (deploys done, fix deployed, one live re-test outstanding), **090 🔲** (wrap-up). |
| **Baselines — use these, do not re-derive** | BFF `Sprk.Bff.Api.Tests` **12,375 passed / 0 failed / 56 skipped** (12,431 total) · ArchTests **191/191** · client `tsc --noEmit` **111 total / 0 production** · gated jest **46 suites / 537 tests** |
| **CI (last terminal read: `3c8680088`)** | **Terminal, 0 pending. 32 pass, 0 fail.** All **8 Tier 1 (Blocking) checks PASS**; Trivy skipped. One cancel: `Tier 2 (Advisory) / Full Unit Tests`, killed at its 30-minute job cap — **advisory by design, lives in the frozen `ci-tier2-advisory.yml` this project does not own, and the required `Router` context excludes Tier 2 from its adjudication.** Not a failure, not a merge blocker. ⚠️ Commits `62f52d2d3` (the fix) and the docs commit after it have **not** had their CI read yet — check `gh pr checks 960` and require `pending == 0` before trusting any verdict. |
| **Next Action (055)** | ✅ Steps 2-3 DONE (trigger-(b) answer written + committed `e6400213c`; reproduce-first RED captured on both sides). **NOW: implement.** Server — add `ExistingDocumentName` to `SaveError` (`SaveResponse.cs:104`); emit it from the `NameCollision` arm of `MapSaveErrorToProblem` (`OfficeEndpoints.cs:558-570`); widen the ONE `ColumnSet` at `OfficeDocumentPersistence.cs:419` to fetch `sprk_documentname` + the four direct slots and return a record instead of `Guid?`; in `ResolveNameCollisionAsync` do the *match* comparison (pure, not authz — it has `request.TargetEntity`); in the **endpoint handler** do the *authorization* gate via `GetCallerAccessAsync` and strip name+id when Read is absent (ADR-008 split, per note §2). Client — add `existingDocumentName` to `ProblemDetails`, `collisionExistingDocumentName` to `ErrorMessage`, thread through `describeCollisionFailure` (`errorMessages.ts:362-374`), render in `renderCollisionState` (`SaveFlow.tsx:1239-1260`). **Then re-run both suites and confirm the six reds go green with the pre-existing ones still passing.** |
| **055 reproduce-first evidence (recorded)** | **Server** (build exit 0, `--no-build`, by name): `…NamesIt_SoTheyCanSee…` → *"Expected problem {…} to contain key \"existingDocumentName\""*; `…WithholdsItsNameAndItsId` → *"…not to contain key \"existingDocumentId\" … but found it anyhow"* — the payload shows the id WAS returned to a caller seeded `AccessRights.None`, which is the leak stated concretely. The 4 pre-existing `OfficeCreateCollisionTests` passed throughout. **Client**: `2 failed, 12 passed, 14 total`; `SaveFlowCollision.test.tsx:216` "unable to find element" with the collision MessageBar provably rendered (Dismiss button in the DOM dump) — i.e. the pane reaches the right state and simply does not show the name. `grep` proves `errorMessages.ts` has no `existingDocumentName`/`collisionExistingDocumentName`, so the second client red is the absent field, not a harness fault. |
| **055 fixture work (done, verified additive)** | `OfficeVersionSaveWorld` gained `DocumentRow.DocumentName` + `.MatterId`, `SeedDocument(documentName:, matterId:)` (both defaulted null → existing callers byte-identical), and `ProjectDocument(row, columnSet)` so the collision branch honours the query's `ColumnSet`. **Why that last one mattered**: it previously returned `new Entity(name, id)` and ignored `ColumnSet` entirely, so a reproduce-first test for a widened projection would have gone red because the FIXTURE models no columns — a failure for the wrong reason. Verified: build exit 0; `OfficeImmutableSaveFileSafetyTests` + `OfficeEmailSaveNamingTests` (must-pass-UNMODIFIED) ran **by name** and passed, 71/0/9 across the wider filter. |
| **055 superseded POML detail** | The POML names `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` for server coverage. That fixture *"models no collisions at all"* (`:759-762`); the real home is **`tests/integration/data-mutation/OfficeVersionSave/OfficeCreateCollisionTests.cs`**. Steps are `directional`, so adapting is sanctioned — recorded here rather than following a file list now known to be wrong. |
| ~~Superseded next action~~ | ~~Write the escalation-trigger-(b) authorization answer into `notes/055-collision-names-its-target.md` BEFORE touching any server code~~ (done, `e6400213c`). The evidence is gathered: `AuthorizationService` IS caller-scoped today (fails closed `sdap.access.deny.no_caller_token` at `:54-72`; queries Dataverse AS THE USER at `:223-225`) — so `.claude/constraints/auth.md:239-249`, which says the opposite, is **STALE** (describes the pre-UAC-r2-task-004 state; record as doc drift, do not fix inside 055). Seams to use: `GetCallerAccessAsync` (document) / `GetCallerRecordAccessAsync` (association); `AccessRights` is `[Flags]` None/Read/Write/Delete. `SaveAsync` already receives `userId` + `httpContext` (`:199-203`) so the gate is implementable at the collision site (`:640`). Half (1)'s data fetch is ONE line: the `ColumnSet` at `OfficeDocumentPersistence.cs:419`. **Open design question**: gate inside `OfficeService` (needs `AuthorizationService` injected) vs at the endpoint in `MapSaveErrorToProblem` (closer to ADR-008). |
| **055 conflict-check (done, benign)** | Hard-warn condition fired on file-level overlap, then **downgraded on evidence**: `unified-access-control-r2` (#950, active) touches `OfficeService.cs` only at lines **1699/1763-1771/1825-1831** and `OfficeEndpoints.cs` at **347-357** — disjoint from my collision path (~580-720, ~1100-1250, and 179/512-582). `code-quality-and-assurance-r4` is a **merge commit with an empty diff** (false positive). `origin/master` is **0 commits** ahead of my merge-base. Proceeding; coordinate with #950 only if it lands in the collision path. |
| **Prior context (task 042, closed)** | ✅ **The 2026-09-18 finding is DIAGNOSED and filed — [#1005](https://github.com/spaarke-dev/spaarke/issues/1005) / ISS-006.** Triage is complete; `notes/042-uat-findings-2026-09-18.md` is now a diagnosis, not an investigation plan — **do not re-run its old §4 diagnostics** (App Insights retention is ~2h in that component; the 04:32 telemetry is gone and the question was settled from Dataverse row times instead). **Two things are owed, both the operator's**: (1) decide the fix for #1005 — it spans the pane and arguably the 409 contract, and is not a one-liner; (2) remediate the live dev row that currently holds another document's content, profile and index chunks (findings note §8) — this project is read-only on Dataverse and did not touch it. **090 is no longer blocked by triage**, but starting it leaves #1005 open on a deployed build. |
| ✅ **#997 CONFIRMED FIXED LIVE (2026-09-18)** | Operator's live Word test: a brand-new document shows the Save tab with **no "Couldn't check this document" banner** — the correct 200 `{resolved:false}` path. The `0x80060891` fix is working in the deployed build. **Criterion 2 → PASS; [#997](https://github.com/spaarke-dev/spaarke/issues/997) can be closed.** Tally becomes 6 PASS / 4 PARTIAL / 2 BLOCKED / 1 FAIL. |
| 🔴 **DIAGNOSED 2026-09-18 → [#1005](https://github.com/spaarke-dev/spaarke/issues/1005) / ISS-006 — the collision's "Save as new version" writes into an UNRELATED document** | Full chain: **`notes/042-uat-findings-2026-09-18.md`**. A collision refusal (`OFFICE_020`) is **correct** — nothing is uploaded. But the pane then offers **"Save as new version"**, which versions the open document onto whichever `sprk_document` owns the colliding **file name** (`errorMessages.ts:34-36`), *never named in the UI*; and `useSaveFlow.ts:937-941` (`sentEntity = versionTarget ? null : selectedEntity`) **drops the user's selected record**. Because `getSubject()` falls back to `'Untitled Document'` and containers are BU-scoped, the "existing document" is routinely someone else's. Live result: a patent document was written, profiled and RAG-indexed under an unrelated orphan row while the selected matter got nothing. **The dropped association is NOT the bug** (task 023 D-4/D-5 omits `targetEntity` on a version save deliberately) — **the bug is a filename match treated as document identity.** ⚠️ Do NOT fix by re-associating on version save: that re-files another user's document onto the current user's record. |
| ⚠️ **Container model — operator correction 2026-09-18 (an earlier handoff had this WRONG)** | **Containers are BUSINESS-UNIT scoped, not per-record.** `RecordContainerResolver.cs:54-55` — the business unit's `sprk_containerid` is the non-secure default, resolved from the record's `owningbusinessunit` (`:52`, `:113-116`). A record gets its **own** container **only** when flagged `sprk_issecure`; `OfficeService.ResolveContainerAsync:156-166` refuses rather than falling back for a secure record with no container. **Consequences**: (1) an orphaned file is **NOT** evidence of a prior save against that matter — it can come from any non-secure save by any user in the BU; (2) filename collisions are **BU-wide**, and since `Untitled Document.docx` is Word's default name for every unsaved document, ONE orphan blocks that filename for **every user saving to any non-secure record in the whole business unit**. Diagnostic §4.1 must query **BU-wide, never by record** — scoping it to one matter returns a false "no rows" and flips the triage the wrong way. |
| **042 result** | Criteria **5 PASS / 5 PARTIAL / 2 BLOCKED / 1 FAIL**, none omitted. Publish **+0.08 MB** vs a fresh master build. Full record: **`notes/042-uat-results.md`**. The **1 FAIL** is criterion 12 → [#996](https://github.com/spaarke-dev/spaarke/issues/996) / ISS-004 (**no CI job typechecks `office-addins`** — FR-18's "CI gates it going forward" was never built; the 111 test-file tsc errors are the accepted 2026-09-09 baseline, 0 production). Criterion 2 went **PASS → FAIL → PARTIAL** (see below) — that arc is the honest record of this task. |
| ✅ **UAT DEFECT — FIXED, TESTED, DEPLOYED (2026-09-18)** | **Every NEW Word document 503'd on the identity check** → pane showed "Couldn't check this document", offering only save-as-new-anyway. `resolve-identity` must answer 200 `{resolved:false}`; it threw at `DocumentUrlIdentityResolution.cs:308`. **Cause measured, not guessed**: Dataverse sends **`0x80060891`** for an *alternate-key* miss; the predicate only knew `0x80040217` (*by-id*). Probed read-only against dev; message byte-identical to the App Insights fault. Failed SAFE (no duplicate rows) but **inverted FR-01's three-answers contract** — it exists so INDETERMINATE is never read as NEW, and this read NEW as INDETERMINATE. **[#997](https://github.com/spaarke-dev/spaarke/issues/997)** / ISS-005 — **deliberately still OPEN** until a live new-document save is confirmed clean. Detail: `notes/042-uat-results.md` §6.3. |
| ✅ **Fix state — VERIFIED + LIVE** | Commit `62f52d2d3`. **Reproduce-first**: the regression test using the REAL fault code **failed before the fix** for the right reason (`identity_resolution_unavailable` at line 308) with all 25 pre-existing tests passing; **after the fix 26/26 pass**, and all three `UnhealthyAlternateKey_Is503…` adjacency guards still pass — `0x80060892` is one integer away, means duplicate/not-Active key, and must stay 503 (NFR-07), so the match is **exact, never a range**. **Deployed** to `spaarke-bff-dev`: 45.43 MB, **4/4 critical files SHA-256 verified**, `/healthz` 200, probes `resolve-identity`/`office/save` → 401, zero 404s. Fix is in `DocumentUrlIdentityResolution.IsAlternateKeyRecordDoesNotExist`; deliberately **NOT** applied to the shared `RecordContainerResolver.IsRecordNotFound`, which guards a security decision. |
| 🔧 **BFF build + deploy — the exact sequence (`/bff-deploy`)** | **Never deploy without the gate.** (1) Pre-flight: `Get-Process testhost` — if one exists, check its **command line** before killing; it may belong to another worktree (PID 6612 was `unified-access-control-r2`) and then cannot be yours. (2) Build: `dotnet build tests\unit\Sprk.Bff.Api.Tests\Sprk.Bff.Api.Tests.csproj -c Debug -m:1` and **check the exit code**. (3) Test: `dotnet test … --filter "FullyQualifiedName~DocumentUrlIdentityResolutionTests"` — confirm your test appears **BY NAME** and the count is right. (4) Only then deploy: `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` (**`pwsh`**, not `powershell`). Expect **~45 MB**; under 30 MB = incomplete zip. It hash-verifies 4 critical DLLs via Kudu — **never trust "deployment successful" alone**. Linux cold start is 90–120 s, so hash-verify passing + a `/healthz` timeout means *correct and still booting* — do **not** redeploy. (5) Probe: `/healthz` → 200, an authed route → **401** (404 = incomplete package). |
| **Build-trap warnings (cost 6 attempts)** | (1) **Never `--no-build` after an unchecked build** — a failed build + `--no-build` printed `Test Run Successful` over a **01:17** binary that predated the fix and did not even contain the new test. Always check the build exit code AND that your test appears **by name**. (2) `dotnet clean` and any `Remove-Item` near build paths get **rejected** by the permission layer — a rejected command runs *nothing*. (3) `--no-incremental` in a per-project loop destroys the next project's `obj/ref` → `CS0006`. (4) A `testhost` PID may belong to **another worktree** (6612 was `unified-access-control-r2`) — check its command line before killing; it cannot lock this worktree's DLLs anyway. |

### 🚀 TASK 042 — DEPLOYMENT RUNBOOK (everything verified present 2026-09-17)

> ✅ **STEPS 1–2 EXECUTED 2026-09-17 — DO NOT RE-RUN.** BFF deployed to `spaarke-bff-dev` (45.43 MB, 4/4
> critical files SHA-256 verified, `/healthz` 200, 11 routes probed → 9×401 + 2×200, **zero 404s**). Add-in
> deployed via CI run **`35302983608`**; hosted `word/manifest.xml` went **1.0.8.0 → 1.0.9.0** with task 037's
> `FunctionFile` + 3×`ExecuteFunction` present in the deployed artifact and all 8 resource URLs resolving.
> **Step 2 of the POML (4-part version bump) needed no action — task 037 had already done it.**
> **Steps 3–4 below are the operator's and remain outstanding.** Full evidence: `notes/042-uat-results.md`.

**Both deploy mechanisms are already owner-authorised.** The UAT half needs a live Office host and is the owner's.

**Step 0 — pre-flight (always).** `Set-Location 'C:\code_files\spaarke-wt-spaarkeai-word-add-in-r1'` and confirm the
environment says *"is a git worktree"* — it repeatedly flips to a lowercase `c:` path that reports otherwise.
Confirm CI is terminal and Tier 1 green before deploying anything. Confirm **no `testhost.exe` is running**
(`Get-Process testhost`) before any build — a killed agent shell leaves its `testhost` child alive holding a DLL
lock, which fails the rebuild with `MSB3027` and lets a later `--no-build` run report green for the *previous*
binary.

**Step 1 — BFF → `spaarke-bff-dev`.** Use the `/bff-deploy` skill (owner-specified). It wraps
`scripts/Deploy-BffApi.ps1` (verified present). Invoke with **`pwsh`**, not `powershell`:
`pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1`. Expect a **~45 MB** package; anything under
30 MB means an incomplete zip. The script hash-verifies 6 critical DLLs via Kudu and auto-recovers — **never
trust "deployment successful" alone.** Linux cold start takes 90–120 s, so hash-verify passing plus a `/healthz`
timeout means the deploy is *correct and still booting* — do not redeploy. Verify:
`curl -s -o /dev/null -w "%{http_code}" https://spaarke-bff-dev.azurewebsites.net/api/documents/test/preview-url`
→ **401 expected** (route found, auth required). A **404 means an incomplete package.**

**Step 2 — add-in → Azure Static Web App.** The workflow does **not** trigger on this branch by design (a push
trigger would auto-deploy a feature branch onto the shared dev SWA). It declares `workflow_dispatch`, and three
dispatch runs on this branch have already succeeded. Run:
`gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1`
The build injects `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, `BFF_API_BASE_URL` **and `ORG_URL`** —
without `ORG_URL` task 027's Open buttons hide themselves rather than doing nothing.

**Step 3 — M365 manifest re-upload (⚠️ NOW MANDATORY, not optional).** Task 037 bumped both Word manifests to
**1.0.9** (XML `<Version>1.0.9.0</Version>`, JSON `"version": "1.0.9"`, `APP_VERSION` synced) to add the
`<FunctionFile>` + two `ExecuteFunction` ribbon controls. **Task 041's "a re-upload may not be needed" is
superseded.** Upload the **build output** `src/client/office-addins/dist/word/manifest.xml` (after a production
`npm run build`) or the hosted copy `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml` —
**never** the repo source `word/word-manifest.xml`, which carries `https://localhost:3000` placeholders.
Path: M365 Admin Center → Settings → Integrated apps. Then confirm the pane footer reads **1.0.9**, which also
finally closes **011**. Outlook needs no re-upload; note its hosted XML is `/outlook/outlook-manifest.xml`
(**not** `/outlook/manifest.xml` — that 404s).

**Step 4 — UAT.** Three things no agent could settle, all requiring a live Office host:
1. **037's four ribbon `<ui-tests>`** — quickSave and shareDocument firing from the ribbon without opening the
   pane; double-click creates exactly one `sprk_document`; the failure path returns the button to idle rather
   than spinning. Implemented and unit-proven (14 tests assert `event.completed()` on every branch); only live
   confirmation remains.
2. **`SaveModeSection`'s `'conflict'` copy** (task 051 finding) — wording was written for task 012's
   different-drive case and reads imprecisely when the cause is a stamp/URL disagreement. **Behaviour is correct**
   (Save disabled, no default target); this is a copy judgment against a live pane.
3. **`notes/parity-checklist.md`** — 16 entries still marked "pending live check"; 042 converts each into real
   verification.

### Owner decisions that must survive compaction

- **Document matching = A + C, with B supporting** (2026-09-17). A = the invisible custom-XML marker *inside* the
  `.docx` (tasks 014 + 051, both shipped — FR-02 is end-to-end). C = the save-time collision prompt (025, shipped).
  B = the content hash (028), already shipped, stays the supporting signal. Explainer:
  `notes/document-matching-explained.md`.
- **Identity precedence**: a resolved cloud URL **wins**; the stamp is the fallback; disagreement is
  `identity_conflict`. This *overrides* task 014's POML step 5 — stamp-first has a documented data-loss path.
- **Project numbering is a separate project and NOT a dependency** — a Project may be created with no number;
  `sprk_projectnumber` is in the protected-attribute set so field mapping cannot write it.
- **Document Name defaults to the file name**; **#975 was assigned here** (task 050, closed); **Send Email is
  Outlook-only** (hidden in Word).
- **A typed name is never changed automatically** — which is why an immutable collision (054) has no in-pane
  retry; the user renames and re-saves.

### Still genuinely unowned (not this project's, but nobody's)

- The idempotency filter's no-header path is a no-op on all 4 routes that use it.
- CI capacity: Tier 2 "Full Unit Tests" keeps hitting its 30-minute cap against a ~12,400-test suite. The limit
  lives in `ci-tier2-advisory.yml`, which this project does not own.
- `unified-access-control-r2` has **unpushed local** edits to `OfficeService.cs` and `OfficeEndpoints.cs`. Line
  ranges do not overlap this branch's changes, so a clean merge is expected — but neither side is in master yet,
  so whoever merges second should check.

### Historical detail (superseded — kept for provenance)

| Field | Value |
|---|---|
| **State (2026-09-17, context-handoff before /compact)** | Origin = local = `63902d479` plus this handoff commit. **41 of 50 tasks ✅** (50 now includes the new 050). Landed and pushed since the last handoff: 036 (Send Email, Outlook-only), 047 (a version save repeating an earlier version''s content is written again), 029 (profile + index refresh after a version save), 040 (parity audit), 049 (Create To Do tab in Word), 048 (trim on every re-index path) and 020 (the typed Document Name reaches `sprk_documentname`). CI on `63902d479` was still running when this was written; the previous head `4c7af1c21` was green — 32 pass, all blocking Tier 1, with only the advisory Tier 2 unit job cancelled at its 30-minute limit (that limit lives in `ci-tier2-advisory.yml`, which this project does not own). PR #960''s description is published and current. |
| **Gates on the merged tree (through 020)** | BFF (`37c829e60`): build 0/0; **full `Sprk.Bff.Api.Tests` 12,314 passed / 0 failed / 56 skipped**; ArchTests 191/191. Add-in: typecheck 111 / production 0; build exit 0; full jest = the ten known failing suites (84 tests); **34/34 gated suites, 432 tests**. |
| **Owner decisions 2026-09-17** | (1) **Document Name defaults to the FILE NAME** — answers 025 open question 8; 020 already ships it. (2) **Document matching is still open**; the owner asked for a fuller explanation → `notes/document-matching-explained.md` (key point: 014''s marker is INVISIBLE, inside the `.docx`, not the file name). (3) **Manifest upload artifact** = the build output `src/client/office-addins/dist/word/manifest.xml` or the hosted `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml` — never the repo source (it holds `localhost:3000` placeholders). Verified live: hosted XML 1.0.8.0, hosted JSON 1.0.8, both with the SWA host. (4) **041 authorized** ("proceed with the correct technical fix") — still additive-only, read back after writing, operator identity. (5) **Deploy**: BFF via the `/bff-deploy` skill; the add-in needs the SWA workflow run (it triggers only on `master` / `work/SDAP-outlook-office-add-in`, so `workflow_dispatch` it or add this branch). (6) **Project numbering** = separate project, NOT a dependency; a Project may be created with no number → 031 re-scoped, MUST NOT write `sprk_projectnumber`. (7) **#975 assigned to this project** → new task 050. |
| **Dispatchable now (no owner input needed)** | **031** (opus, **DISPATCHED 2026-09-17** to an agent in its own worktree) — re-scoped: owner + BU defaults + field mapping + route Project through 030''s creation service; never writes `sprk_projectnumber`; `<owner-decisions>` block is in its POML. ✅ **050 DONE + merged 2026-09-17** (agent `899bdcfdc` → merge `a348e8c3f`; merged-tree gates build 0/0, ArchTests 191/191, targeted 8/8; follow-up task **052** filed for 3 more sites it found) — was: GitHub #975: the global handler''s `WriteAsJsonAsync` overwrites the `application/problem+json` header it just set; reproduce first, survey every consumer that branches on error content type (the add-in''s `ApiClient.ts` at minimum), then fix. ~~**041**~~ **DONE 2026-09-17** — verification only, no Entra write was needed (both URIs already registered for the only add-in SWA) and `deploy-office-addins.yml` is unchanged (trigger stays `workflow_dispatch`). See `notes/041-entra-redirects.md`. |
| **Still waiting on the owner** (⚠️ item 1 was ANSWERED 2026-09-17 — **document matching = A + C, with B supporting**; 014, 025, 045 and the new 051 are all GO; see the CLAUDE.md decision rows and `notes/document-matching-explained.md`) | (1) **How Word documents are matched** — read `notes/document-matching-explained.md` and pick A (invisible marker, task 014) and/or C (the clash prompt, task 025). Decides 014, 025, 045 and 047''s create-path leftover. (2) **The Word manifest re-upload** — only needed if the version installed in the M365 Admin Center is older than 1.0.8.0; our merged work changed no manifest file, so a static-site redeploy may be all that is required (011 would change this by migrating Word to the unified JSON manifest). (3) **Owners** for the idempotency filter''s no-header no-op and the Tier 2 30-minute CI limit. |
| **Next Action** | **1)** ✅ 031 and 050 are MERGED and GATED on `97e0755ce` — build 0/0, ArchTests 191/191, targeted 14/14, **full `Sprk.Bff.Api.Tests` 12,328 passed / 0 failed / 56 skipped** (= 12,314 + 12 + 2, reconciles exactly). **This 12,328/0/56 is the NEW branch baseline** — use it for every later task. **2)** ✅ **025 is MERGED and GATED** on `d21e54e98` — D1 closed at the source (refuse-before-upload, `ConflictBehavior.Fail`, Document only). Merged-tree gates: build 0/0, ArchTests 191/191, **full suite 12,333/0/56**, **gated jest 36/36 suites / 443 tests**. **These are the NEW baselines.** **3)** **014 is DISPATCHED** (opus, own worktree, based on `d21e54e98`) — both halves now in scope since 025 landed; warned that `OFFICE_020` is already taken by 025. **4)** ✅ **053 is MERGED and GATED** on `0d93636bc` — tsc 111 total / **0 production**, build exit 0, **gated jest 39/39 suites, 461/461 tests**. ⚠️ **Baseline note for later tasks**: when counting "production" tsc errors, `shared/__mocks__/office-js.ts` (24 of the 111) is jest manual-mock infrastructure, NOT production code — a filter that only excludes `__tests__`/`.test.` will misreport it as a regression, as this session's own check did before the per-file breakdown settled it. **5)** ✅ **045 is MERGED and GATED** on `7349ba0db` — tsc 111 / **0 production**, build exit 0, **gated jest 41/41 suites, 473/473 tests**. **NEW client baselines: gated 41/473; tsc 111 total, 0 production.** **6)** ✅ **014 is MERGED and GATED** on `e89162f89` (+ a follow-up notes merge) — build 0/0 both projects, ArchTests 191/191, **full `Sprk.Bff.Api.Tests` 12,366 passed / 0 failed / 56 skipped**, publish +0.0111 MB. **NEW BFF baseline: 12,366/0/56.** ⚠️ **AC2 is NOT fully realised until 051 lands**: the server writes and can read the stamp (`TryReadStamp`, tested) but **nothing consumes it yet** — a downloaded/edited/re-uploaded document does not self-identify until the client reader ships. **7)** ✅ **051 is MERGED and GATED** on `72f7a89f5` — **FR-02 is END-TO-END; 014's AC2 is realised.** tsc 111 / **0 production**, build exit 0, **gated jest 44/44 suites, 508/508 tests**. **NEW client baselines: gated 44/508; tsc 111 total / 0 production.** **8)** Remaining, both dispatchable (files now free, nothing in flight): **052** (`problem+json` at 3 hand-rolled sites — `ChatEndpoints.cs` ~536, `OfficeEndpoints.cs` ~739/~764; the two SSE sites need confirming the error precedes stream framing, NOT a blind copy of 050's fix) and **054** (typed-name Email/Attachment collision; opus/xhigh — an ORDERING conflict: suppress needs the hash which exists only after upload, a name refusal must decide before it; `OfficeImmutableSaveFileSafetyTests` + `OfficeEmailSaveNamingTests` must pass UNMODIFIED). **9)** ✅ **052 and 054 are MERGED and GATED TOGETHER** on `b37d34329` — build 0/0 both projects, ArchTests 191/191, **full `Sprk.Bff.Api.Tests` 12,375 passed / 0 failed / 56 skipped**. **NEW BFF BASELINE: 12,375/0/56.** It reconciles exactly: 12,366 + 4 (052) + 5 (054), and that 5 cross-checks against 054's reproduce-first, which reported exactly 5 failures pre-fix. The three protected files are confirmed unchanged across the whole range. **10)** ✅ **037 is MERGED and GATED** on `119d3a011` — tsc **111 / 0 production**, build exit 0 with `dist/word/manifest.xml` emitting `1.0.9.0` and `manifest.json` `1.0.9`, **gated jest 46/46 suites, 537/537 tests**. **NEW client baselines: gated 46/537; tsc 111 total / 0 production.** 🔎 Keep visible for any future ribbon work: **the Office Ribbon API cannot change button text** — `Office.Control` exposes only `id`/`enabled?`, so transient status must go through `displayDialogAsync` (037 found this via TS2353, not at runtime). **ALL IMPLEMENTATION TASKS ARE NOW COMPLETE.** Only **042** (deploy + UAT) and **090** (wrap-up) remain. **11)** Then **042** (deploy + UAT) and **090**. ⚠️ **042 now has a MANDATORY step it did not have before**: task 037 bumped both Word manifests to **1.0.9** (XML `1.0.9.0`, JSON `1.0.9`, `APP_VERSION` synced), so the M365 Admin Center **re-upload is required** — task 041's "a re-upload may not be needed" conclusion is superseded. Artifact: the build output `dist/word/manifest.xml`, or the hosted copy once redeployed. See `notes/037-manifest-change.md`. ⚠️ **Carry into 042's UAT**: `SaveModeSection`'s `'conflict'` message copy was written for task 012's different-drive case and reads imprecisely when the cause is a stamp/URL disagreement (task 051 finding). Behaviour is correct — Save disabled, no default target — so this is a **copy judgment against a live pane**, not a code fix to guess at now. **7)** Then **042** (deploy + UAT — `/bff-deploy` for the BFF, `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1` for the add-in, and turn every "pending live check" in `notes/parity-checklist.md` into real verification) and **090**. **2)** ~~041~~ DONE — no Entra write needed, trigger stays `workflow_dispatch`, workflow file untouched. **3)** Then 042: `/bff-deploy` for the BFF, `workflow_dispatch` for the add-in, and turn every "pending live check" in `notes/parity-checklist.md` into a real verification. **4)** 090 closes the project. **5)** ✅ **Matching DECIDED 2026-09-17 (A + C, B supporting).** Newly dispatchable: **025** (build the collision prompt — do it FIRST; it gates 014's create-path half and unblocks 045), then **014** (version-path stamping now, create-path after 025), then **051** (client reader, deps 014), then **045**. |
| **Dispatch rules (learned the hard way)** | Before any Agent dispatch, run a `Set-Location 'C:\code_files\spaarke-wt-spaarkeai-word-add-in-r1'` command and confirm the environment says "is a git worktree" — the lowercase `c:` path once placed an agent worktree INSIDE this worktree. One worktree creation per turn, never while another git worktree operation runs. Every brief: confirm `git rev-parse --show-toplevel` is its own worktree; rebase onto the LOCAL `work/spaarkeai-word-add-in-r1`; never `git stash`; long jobs in the FOREGROUND, never wait on a background job; new tests in NEW files; POML line numbers are stale — re-locate symbols by name. Agents must not edit `TASK-INDEX.md`, `current-task.md`, the project `CLAUDE.md` or `ci-gated-suites.txt`. Only `agent-a97210f38cd331cfb` remains and it belongs to ANOTHER session — never touch it. |
### What 013 must honour from 012 (notes/012 §2, "Rules for task 013")
- **503 = could not determine.** Retry or let the user choose; never treat it as a new document.
- **A 403 with `reasonCode` `sdap.access.error.system_failure` is indeterminate too.**
- **Don't call the route for an empty or non-absolute `document.url`** (an unsaved document). Treat it as new locally.
- **`identity_conflict` is not "new"**: do not offer save-as-new.
- Word desktop and web both return the raw-space path form (spike-1 §19, §23). Send it exactly as returned.

### Last session (2026-09-10 → 11) — all committed + pushed to PR #960
- **012:** `8fec97b2d` (resolver + route), then `9750b4968` (Graph 403 → `not_resolvable`, after the live evidence). It was deployed twice to `spaarke-bff-dev` via `/bff-deploy`, hash-verified and healthy. Decisions are in `notes/012-identity-resolver-decisions.md`; the live table is in §8.
- **015:** `aff1ca9e4` (agent, isolated worktree) plus `4ea3cf6de` (corrected stale "waits on 032" claims). The add-in site was redeployed (run 34546485352), and Find is live for Word and Outlook.
- **Gates:** full `Sprk.Bff.Api.Tests` 12,139/0; ArchTests 191; no CVE; publish +0.016 MB against a fresh master build.
- **This worktree** now has root `node_modules`, so the husky/lint-staged pre-commit hook runs. Never `--no-verify`.

### Completed after the handoff — doc accuracy pass (committed with this update)

The background doc-accuracy agent finished and its edits were verified in the main session before commit:
8 files, no workflow YAML touched, the one C# change comment-only. It corrected the admin guide + deployment
checklist (non-existent `manifest-working.xml`, `build:prod`, the `localhost` trap), `uac-access-control.md`
(the stale app-only claim), `.github/WORKFLOWS.md` + the incident runbook (only `Router` is required; three
undocumented workflows added), `src/client/office-addins/CLAUDE.md` and the architecture doc (React 19, build
command, typecheck count), and the `ChatWordExportEndpoints.cs` URL-shape comment.

**It caught two errors in the main session's own brief — keep these:**
- **Outlook's production XML is `/outlook/outlook-manifest.xml`, NOT `/outlook/manifest.xml`** (404 vs 200,
  verified). Only Word's XML output is named `manifest.xml`. Word URL: `/word/manifest.xml` (200, serves 1.0.8.0).
- The Word adapter consolidation (task 010) was already documented correctly; the brief over-claimed that.
- Typecheck: 289 test-file errors at the 2026-09-09 accept decision; 284 on re-measure 2026-09-10; 0 production.

### Operator pending (none can be done by an agent)

1. ~~Word DESKTOP capture~~ **DONE 2026-09-11.** The desktop value is byte-identical to the web capture and resolves
   (spike-1 §23).
2. **Re-upload the Word manifest at 1.0.8.0** — the operator uploaded from the SWA while it still served 1.0.7.0.
   The SWA now serves 1.0.8.0 (verified). Path: M365 Admin Center → Settings → Integrated apps →
   `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml`. Then confirm the pane footer = 1.0.8 →
   **task 011 can close** (via the XML path). Outlook needs no re-upload (1.0.22.0, unchanged; its XML is
   `/outlook/outlook-manifest.xml`).
3. **Optional (012):** the URL of any OneDrive or SharePoint file that has no Spaarke record, to exercise the
   Dataverse not-found shape live (notes/012 §8). The az CLI cannot list OneDrive (AADSTS65002), and the BFF has no
   route that lists container children.
4. **Share privilege check** — security roles → `sprk_document` → Share. If it is not granted, the record⇔document
   access model has no gap (see D-032-1). OR reconnect Dataverse MCP (`/mcp`) and an agent can check.
5. **Dataverse MCP has been DOWN** (`CONNECTION_CLOSED`). Nothing here was "verified via MCP". Use `/mcp` or a
   restart to reconnect.

### Pending decisions (operator)

- ~~**Spike-1 §7 — stamp-as-primary.**~~ **Superseded 2026-09-14.** 013 is done, and task 014 (the stamp) was escalated: stamping the stored bytes breaks 028's content link, and the owner does NOT want an id added to documents automatically. The owner asked to DISCUSS how Word documents are matched to avoid duplicates — see Quick Recovery, "Waiting on the owner" (1). Design note: `notes/014-xml-part-stamp-decisions.md`.
- **Shadow-window latch mechanism** (ISS-002 residual) — `-Since` was advanced (Option 1, applied); the latch
  itself (`$falseGreens` unfiltered by `$countingFrom`) still exists and the NEXT false green will latch the same
  way. Needs a cutover-owner decision: hard stop or reset.

---

## Critical Context (the 5 things a fresh session must not re-learn the hard way)

1. **Docs and agent reports in this area have repeatedly been WRONG vs code.** Verify against code/live before
   relaying. Examples this session: ArchTests "broken" (false — concurrent-build contention), UAC doc "app-only"
   (stale 1 day; code is fail-closed caller-scoped), office-addins gate "PR-blocking" (false — only `Router` is a
   required check), `manifest-working.xml` (doesn't exist), `ChatWordExport` URL-shape comment (wrong).
2. **The access model is ENFORCED in code** (D-032-1 WITHDRAWN, final): record access ⇔ document access.
   Internal: `AuthorizationService` fails closed, queries Dataverse AS THE USER (`:54`, `:79`, `:224-225`).
   External: grants at ROOT record only; SPE broker-only (BFF app-only download, `ExternalProjectDataEndpoints`
   `DownloadDocumentContent`); `GrantMembershipAsync` has no callers. No code shares an individual row. Access is
   BU-assigned, never org-wide (operator). Cascade (Referential) was the WRONG question — it governs
   share/assign/delete propagation, which the model never uses.
3. **Link 2 cannot be tested outside the BFF** — only the owning app or a container-type-REGISTERED app reads SPE
   files. `az` CLI / Graph Explorer would 403 regardless of URL validity (false negative).
4. **Space encoding is the live link-2 question**: `document.url` returns RAW spaces; the BFF's own open-links
   returns `%20`. Same URL after decoding (shape check MATCH, spike-1 §21). Task 012 must test both forms.
5. **Many agents in ONE worktree** caused lost writes to shared files (TASK-INDEX, current-task) and phantom
   findings from build contention. Prefer separate worktrees for waves of build-heavy agents.

---

## Session summary — 2026-09-08 → 2026-09-10 (all committed + pushed to PR #960)

**Phase 0 COMPLETE.** Tasks ✅: 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 017, 018, 019, 028, 032, 043, 044.
011 🔄 (awaiting 1.0.8 re-upload).

| Area | Outcome | Key commits |
|---|---|---|
| History repair | Squashed Copilot commit split into 3 honest commits; out-of-scope collision fix REVERTED (violated FR-12) | `df1a3805b`, `0b68943d1` |
| FR-12 / collision | Premise FALSE (add-in uses a different upload path). Path C→B: build FR-11 first, then amend FR-12 | `c72455e5a`, `e7c79b698`, `436507b32` |
| F-h / 028 | Editable Office saves link/graduate, never immutable-suppress; host-neutral on `SaveContentType`; mutation-tested | `dd286200f` |
| F-b / 032 | Per-row authorization on Find; closed a `countOnly` count side-channel; negative test | `f892c8ada` |
| FR-18 | Production typecheck 88 → **0** (A1+B1); 289→284 test-file errors CONSCIOUSLY ACCEPTED (inert: no CI/build/test gate) | `cc318390f` |
| Jest harness | jest-dom + user-event were NEVER installed; RTL 14→16 (+ `@testing-library/dom` peer) | `cc318390f`, `abe10e431` |
| 018 | `useAnnounce` (NFR-11) out-of-tree DOM → React-owned; 19/19 | `fe9684ab0` |
| 010 | Single Word adapter via `HostAdapterFactory`; `getCompressedFile` byte-identical (36-case differential incl. multi-slice); Option B = pass Stage-1 host | `eb92c604d`, `36bb6dc3d` |
| 019 | Custom-XML parts premise CONFIRMED; `CustomXmlParts 1.1` declared, WordApi NOT bumped (would drop Office 2019/2021 LTSC). 014 GO with 4 conditions (explicit `xmlns` on stamp root!) | `1f47fde3e` |
| 011 | Word unified manifest + WordApi 1.1→1.3 fix. XML is the M365 Admin Center artifact (binding rule; JSON is `devPreview`) | `336ea3809`, `12c1c3b74` |
| Spike-1 | Link 1 GREEN (web), link 4 GREEN, shape check MATCH; links 2-3 moved into 012 | `223a3a148`, `8babf3f21`, `d7a71baaf`, `7d7beb9ae` |
| 043 | office-addins jest CI check (REPORTS, not blocking — only `Router` is required); vacuous-green count assertion; CODEOWNERS on allow-list | `c2f924ef8`, `02e17260e`, `9c7a29a09` |
| 044 / ISS-002 | Shadow false green = real ROUTER DEFECT, already fixed by PR #944; `-Since` advanced → window clean (0 false greens) | `db6dbe6ab`, `c525f3276` |
| Deploys | Office add-ins redeployed from this branch — SWA now serves **1.0.8.0** | runs `34414699298`, `34532444044` |

### Open findings surfaced, NOT yet actioned

- ~~**F-1**~~ **FIXED 2026-09-11** — `deploy-office-addins.yml` now watches
  `src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` (verified: webpack
  `webpack.config.js:101` aliases exactly that file; it has zero imports, so one path entry is complete).
- **W-5** `identity-obj-proxy` mapped in `jest.config.js:48` but not installed. **Verified INERT 2026-09-11**: no
  file in `src/client/office-addins` (source or test) imports a `.css/.less/.scss/.sass`, so the mapping is never
  resolved. Becomes live only when a stylesheet import is added. Fix after 013 lands (it owns office-addins this
  wave): install it as a devDependency, or drop the dead mapping. **Still open 2026-09-14** (013 has landed; not yet
  picked up — small, unowned → ours).
- **`npm run lint` in `src/client/office-addins` is broken** — the script points at a non-existent `src` subfolder
  (found by task 026, 2026-09-14; pre-existing, same class as D-043-2). Agents have been running `eslint` directly on
  touched files instead. Small, unowned → ours; fix alongside W-5.
- 🟠 **`sprk_matternumber` is `sprk_matter`'s PRIMARY NAME column** (task 030 agent, verified live 2026-09-11 —
  the main session has NOT independently re-verified). Consequence: a pane-created Matter shows a **blank name**
  in lookups/views until the separate numbering project ships. Not a regression (the pane already sends no
  number) — it raises that project's urgency. Recorded in `notes/030-numbering-handoff.md`; tell the owner.
- **W-2 (043)** manifest-line deletion from `ci-gated-suites.txt` — mitigated by CODEOWNERS, not mechanically.
- **W-7** CLAUDE.md §12 `npm ci` ban may not hold for office-addins (`npm ci --dry-run` exits 0) — §6.5 question.
- 10 failing jest suites (84 tests) — genuine mock/assertion defects, unowned by a task.
- F-6 `unified-access-control-r2` has a branch diff on the frozen `ci-tier1-blocking.yml` — operator's call.

---

## Decisions made (operator, this session)

- 2026-09-08 — FR-12 path **C then B**; **F-h owned by r1**, fix host-neutral.
- 2026-09-09 — Deferral policy: defer only with a good technical reason OR an ACTIVE hand-off (a note the other
  project is instructed to read — a GitHub issue alone is NOT a hand-off). D-032-2 folded into task 033.
- 2026-09-09 — FR-18 = production types only (A1+B1); 289 test-file errors consciously accepted.
- 2026-09-09 — React 19 stays; RTL aligns to it. ci-cd-unit-test-remediation-r1 is CLOSED → r1 owns CI work.
- 2026-09-09 — r1 owns ISS-002 (044) with authorization to touch frozen tier files (not exercised).
- 2026-09-10 — Legacy/existing documents don't matter (dev only). Shadow window Option 1 applied.
- 2026-09-10 — Access model confirmed as enforced; D-032-1 withdrawn (final).
- 2026-09-10 — Operator authorized agent-triggered deploys of `deploy-office-addins.yml`.
