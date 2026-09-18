# Current Task State — spaarkeai-word-add-in-r1

> **Last Updated**: 2026-09-17 (task 042 — **BOTH DEPLOYS DONE AND VERIFIED**; UAT + M365 re-upload are the operator's, then 090)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-word-add-in-r1`, PR #960.

---

## Quick Recovery (READ THIS FIRST)

> ⚠️ **This block (2026-09-17 handoff) is authoritative and supersedes the table further down**, whose
> "Next Action" row accreted across the session and contains stale, duplicate-numbered entries. Read this;
> treat the table below as historical detail only.

| Field | Value |
|---|---|
| **Where** | Branch `work/spaarkeai-word-add-in-r1` · PR **#960** · **0 behind / 198+ ahead of `origin/master`**. Deployed state is the stable anchor: BFF live on `spaarke-bff-dev`, add-in live on `icy-desert-0bfdbb61e.6.azurestaticapps.net` at manifest **1.0.9.0**. (No head SHA pinned here — task 042's own docs commit supersedes it; use `git log -1`.) |
| **Progress** | **52 of 55 tasks ✅.** Open: **011 🔄** (closes on the M365 re-upload), **042 🔄** (**deploys DONE**; UAT outstanding), **090 🔲** (wrap-up). **Every implementation task is done, and both deploys are live.** |
| **Baselines — use these, do not re-derive** | BFF `Sprk.Bff.Api.Tests` **12,375 passed / 0 failed / 56 skipped** (12,431 total) · ArchTests **191/191** · client `tsc --noEmit` **111 total / 0 production** · gated jest **46 suites / 537 tests** |
| **CI on `6f29ef236`** | **Terminal, 0 pending.** All **8 Tier 1 (Blocking) checks PASS**; 32 pass overall; Trivy skipped. One cancel: `Tier 2 (Advisory) / Full Unit Tests`, killed at its 30-minute job cap — **advisory by design, lives in the frozen `ci-tier2-advisory.yml` this project does not own, and the required `Router` context excludes Tier 2 from its adjudication.** Not a failure and not a merge blocker. |
| **Next Action** | **Operator**: (1) re-upload the 1.0.9 manifest in M365 Admin Center → Integrated apps — this also closes **011**; (2) run UAT per `notes/042-uat-results.md` §7 and record results back into its §4 table. **Then** 090. Agent-side work on 042 is complete. |
| **042 result** | Deploys ✅ verified. Criteria **5 PASS / 4 PARTIAL / 2 BLOCKED / 2 FAIL**, none omitted. Publish **+0.08 MB** vs fresh master. Full record: **`notes/042-uat-results.md`**. **FAIL 1** = criterion 12 → [#996](https://github.com/spaarke-dev/spaarke/issues/996) / ISS-004 (**no CI job typechecks `office-addins`**; the 111 test-file tsc errors are the accepted 2026-09-09 baseline, 0 production). **FAIL 2** = criterion 2 → see the row below. |
| 🔴 **LIVE UAT DEFECT (2026-09-18)** | **Every NEW Word document 503s on the identity check** → pane shows "Couldn't check this document". `resolve-identity` must answer 200 `{resolved:false}`; it throws at `DocumentUrlIdentityResolution.cs:308`. **Cause measured**: Dataverse sends **`0x80060891`** for an *alternate-key* miss; the predicate only knew `0x80040217` (*by-id*). Probed read-only against dev; message byte-identical to the App Insights fault. Fails SAFE (no duplicate rows) but inverts FR-01's three-answers contract. Filed **[#997](https://github.com/spaarke-dev/spaarke/issues/997)** / ISS-005. Full detail: `notes/042-uat-results.md` §6.3. |
| ⚠️ **Fix state — READ BEFORE TOUCHING** | Fix is **committed but NEVER COMPILED — UNVERIFIED**. The regression test **was confirmed to fail before the fix** (25 others passing); the fix itself never built. **The deployed dev BFF STILL CARRIES THE DEFECT — no redeploy was done.** Blocker: six builds each named a *different* just-generated file under `src/server/api/Sprk.Bff.Api/obj/Debug/net10.0/linux-x64/` as missing (`ref/…dll`, `refint/…dll`, `…GeneratedMSBuildEditorConfig.editorconfig`) + `MSB3030` on the 14 MB `Sprk.Bff.Api.dll`. Same signature as this task's master-publish failures, which vanished on relocating the build → consistent with AV scanning build intermediates. **Not reproducible in CI.** **To finish**: build, then `dotnet test --filter FullyQualifiedName~DocumentUrlIdentityResolutionTests` (26 tests) — the new test must pass **and** the three `UnhealthyAlternateKey_Is503…` cases must STILL pass (`0x80060892` is one integer away, means duplicate/not-Active key, must stay 503 per NFR-07). Then redeploy the BFF and re-run a new-document save. |
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
