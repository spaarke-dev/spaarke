# Task 042 — Deployment + UAT results

> **Executed** 2026-09-17 on the owner's explicit go-ahead ("go ahead with deployments"). Rigor STANDARD
> (authored) — the decision tree also trips FULL on "6+ steps" and "resuming after compaction"; quality gates
> are legitimately skipped because this task changes no application code (its only outputs are this note and
> doc/status updates). Steps mode: **prescriptive**.
>
> **Both deploys are complete and verified. The UAT half is NOT complete** — it requires a live Office host,
> which no agent has. This task is therefore **🔄, not ✅**. §7 is the operator hand-off.

---

## 1. What was deployed

| Target | Mechanism | Result |
|---|---|---|
| **BFF API** → `spaarke-bff-dev` | `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` (the `bff-deploy` skill) | ✅ complete, hash-verified |
| **Office add-ins** → `spaarke-office-addins` SWA | CI workflow `deploy-office-addins.yml`, run **`35302983608`** | ✅ `completed/success` |

Deployed from branch `work/spaarkeai-word-add-in-r1` @ **`eddc55286`** — 0 behind `origin/master`, 198 ahead,
working tree clean.

### CI state at deploy time

All **8 Tier 1 (Blocking)** checks PASS on `eddc55286`: Compile (Debug), Arch Tests (MUST-NOT subset), Auth
Smoke, Changed-Surface Integration Smoke, Classify Tier 1 Surfaces, Compose Fidelity Gate, Eval Gate (Golden
Utterances), Tenant Isolation (I1–I5). The two non-terminal rows at deploy time were the advisory Tier 2 run
and legacy `SDAP CI`; neither gates a deploy, and the required `Router` context excludes Tier 2 by design.

---

## 2. Deployment evidence

### 2.1 BFF — not a silent-success deploy

The May-2026 failure mode (`az webapp deploy` returns 200 while DLLs are never replaced) was explicitly
excluded:

```
[2/4] Package created: 45.43 MB
[3/4] Captured pre-deploy hashes for 4 critical files
[4/4] All 4 critical files match local build (SHA-256 verified)
[5/4] dev health check passed!
[6/4] All 2 declared CORS origin(s) present.
```

**Route registration probed against the deployed instance** — 401 proves the route exists and requires auth;
404 would prove an incomplete package:

| Route | Result |
|---|---|
| `GET /healthz` | **200** |
| `GET /api/office/health` | **200** — `.AllowAnonymous()` by design (`OfficeEndpoints.cs:82`) |
| `POST /api/office/save` | 401 |
| `GET /api/office/search/entities` · `/search/documents` · `/search/matter-types` | 401 · 401 · 401 |
| `GET /api/office/recent` | 401 |
| `POST /api/office/todo` | 401 |
| `POST /api/office/share/links` | 401 |
| `POST /api/office/quickcreate/{entityType}` | 401 |
| `GET /api/office/jobs/{jobId}` | 401 |
| `POST /api/documents/resolve-identity` (FR-01, task 012) | 401 |
| `GET /api/documents/test/preview-url` | 401 |

**Zero 404s. No incomplete-package indication.**

> **Correction recorded deliberately.** An earlier probe in this task reported 404 for
> `/api/office/documents/save` and `/api/office/search`. **Those paths do not exist and never did** — the real
> routes are `/api/office/save` (`OfficeEndpoints.cs:179`) and the `/search` subgroup's three leaves. The 404s
> were correct answers to invented paths, not a deployment defect. Likewise an apparent 415 on
> `resolve-identity` was a probe artifact (PowerShell POST with no `Content-Type`); with
> `application/json` it returns 401, and the group carries `.RequireAuthorization()`
> (`FileAccessEndpoints.cs:34`). Recorded so no future reader re-opens either as a real finding.

### 2.2 Add-in — the deployed artifact carries the new bits

| Check | Result |
|---|---|
| Hosted `word/manifest.xml` `<Version>` | **1.0.9.0** (task 041 recorded **1.0.8.0** pre-deploy — the bump genuinely shipped) |
| Task 037 ribbon entries in the **deployed** XML | `FunctionFile` ×1, `ExecuteFunction` ×3, `DialogApi` ×3 |
| `localhost` occurrences in deployed manifest | **0** (`ADDIN_BASE_URL` substitution worked) |
| All 8 manifest resource URLs resolve | **7×200 on the SWA, 0 missing** (5 icons, `/word/taskpane.html`, `/word/commands.html`); 8th is external `spaarke.com/support` |
| `Commands.Url` → `FunctionFile` binding | `/word/commands.html`, `resid="Commands.Url"` — the ribbon buttons have a real target |
| Outlook manifest path | `/outlook/outlook-manifest.xml` → **200**; `/outlook/manifest.xml` → **404** (known filename trap) |

Repo-source manifests confirmed at `word/word-manifest.xml` = `1.0.9.0` and `word/manifest.json` = `1.0.9`, so
POML step 2 ("bump the 4-part version") was **already satisfied by task 037** — bumping again would have been
wrong and was deliberately not done.

---

## 3. ADR-029 / CLAUDE.md §10 — publish size

| Measure | Value |
|---|---|
| **Branch publish (absolute)** | **45.43 MB** compressed, incl. PDBs |
| Tool | PowerShell `Compress-Archive -CompressionLevel Optimal` — the method `scripts/Deploy-BffApi.ps1` uses |
| **Binding ceiling (spec NFR-01)** | **≤60 MB** → **✅ within ceiling, 14.57 MB of headroom** |
| Escalation thresholds | ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. Neither reached. |
| **Fresh `origin/master` build (`e0a6f87c4`)** | **45.35 MB** — built this task, same tool |
| **TRUE DELTA (branch − fresh master)** | **+0.08 MB** |
| Single-task escalation threshold | ≥+5 MB → justification required. **+0.08 MB is 62× under it.** |

**The delta was measured the way CLAUDE.md §10 requires** — against a *freshly built* `origin/master`, not
against the recorded baseline number, with **both sides zipped by the same tool** (`Compress-Archive
-CompressionLevel Optimal`, matching `scripts/Deploy-BffApi.ps1`). §10 names two hazards and both were
avoided: the recorded 45.42 MB baseline @ `a826cf347` is three weeks stale (master is now `e0a6f87c4`), and
mixing zip implementations shifts the number ~1.3 MB on byte-identical content.

> **Measurement note, recorded because it cost three attempts.** The first two master publishes failed with
> `MSB3030: Could not copy … Playbooks\*.json … because it was not found`, on a *varying* subset of the 7
> playbook JSONs, despite those files being verified present on disk with byte counts immediately before the
> build. The files are tracked at `origin/master` and are not gitignored. The discriminating observation: the
> **branch** publish under `C:\code_files\` succeeded every time, while only master worktrees created under
> `AppData\Local\Temp` failed. Relocating the master worktree to `C:\code_files\` made it publish cleanly on
> the first try — consistent with antivirus scanning newly-written files in Temp. **Build measurement
> worktrees outside `Temp`.** No repository or code condition was involved.

---

## 4. Spec Success Criteria — all 13, none omitted

Legend — **PASS**: verified, evidence cited · **PARTIAL**: the automated half passes, a live-host half remains
· **BLOCKED**: cannot be executed without a live Office host · **FAIL**: recorded as failing, filed as a defect.

| # | Criterion | Status | Evidence / why |
|---|---|---|---|
| 1 | Spaarke-sourced doc in Word desktop resolves to the right `sprk_document` + matter | **PARTIAL** | Integration/contract tests green (tasks 012/013/026; `resolve-identity` 401-verified live on the deployed BFF). The criterion's **manual** half needs live Word desktop. |
| 2 | Desktop-sourced doc claims no identity, saves cleanly as new | **FAIL** | ⚠️ **Corrected 2026-09-18 from PASS.** Live UAT on the deployed build: every NEW document answers **503**, not 200 `{resolved:false}`, and the pane shows "Couldn't check this document". Root cause measured, fix written but **UNVERIFIED** (local build blocked). Filed **[#997](https://github.com/spaarke-dev/spaarke/issues/997)** / ISS-005. See §6.3. The integration test that reported PASS pairs the real fault MESSAGE with the wrong fault CODE — a combination Dataverse cannot emit. |
| 3 | Stamped doc, downloaded + re-opened from disk, self-identifies | **BLOCKED** | FR-02 is end-to-end in code (014 server stamp + 051 client reader, both shipped with unit coverage), but the criterion's verification method is an **end-to-end test** through a real Word host. Not executable here. |
| 4 | Saving an identified doc defaults to a **version**, not a duplicate row | **PASS** | Integration test asserting one `sprk_document` row + incremented SPE version (FR-11). |
| 5 | "Save as new document" override always creates its own record, never suppressed | **PASS** | Integration test. Note the **2026-09-17 amendment**: the `sprk_canonicaldocument` link assertion applies only to unstamped paths — a Word-pane save's stamp makes bytes differ by construction, so no link is produced. Accepted consequence of FR-02, recorded in the spec's NFR-08 ADR Tensions row. |
| 6 | Filename collision → two-option dialog, existing bytes untouched, **shipped** handling | **PASS** | Task 025: create-path uploads use `ConflictBehavior.Fail`, so Graph refuses atomically and no bytes move; test asserts no new collision logic was introduced (FR-12). |
| 7 | Profile fields display; Generate Profile completes | **PARTIAL** | Contract test green (`/api/office/documents/{id}/generate-profile` registered, 401-verified). **Manual** half needs a live pane. |
| 8 | Matter created from the pane has number + owner + mapped fields | **PASS** | Integration test. |
| 9 | Find returns content-similar results, **permission-trimmed** | **PARTIAL** | The negative test (a user denied access to a matter sees none of its documents) is green in the suite. POML acceptance criterion 6 requires it **executed against the deployed build** — that execution is blocked without a live host. |
| 10 | To Do created from Word carries document **and** record as regarding | **PASS** | Integration-tested both ways (task 035; `OfficeService.CreateTodoAsync` has a document carrier parallel to the communication carrier). The tab gate that would have made this unreachable from Word was **closed by task 049** (✅ 2026-09-15, `4f3b7dbd3`): `TAB_CONFIGS.createTodo.availableFor` is now `['outlook', 'word']` (`TaskPaneNavigation.tsx:103`) and the test that pinned "only the Save tab for Word" was rewritten to assert Save + Find + Create To Do. |
| 11 | Every shared capability works in both hosts or is capability-gated | **BLOCKED** | `notes/parity-checklist.md` §4 carries **14 capability rows, every one marked "pending — task 042 UAT"**, plus 5 `<ui-tests>` marked UNVERIFIED. Task 040 had no live host either. This is the single largest blocked item. |
| 12 | `npm run typecheck` is clean — *Verify*: CI | **FAIL** | Measured: **exit 2, 111 `error TS` lines, 0 production**. And its stated verification method does not exist — **no CI job typechecks `office-addins`**. Filed: **[#996](https://github.com/spaarke-dev/spaarke/issues/996)** + `defer-issues.md` ISS-004. See §6.1. |
| 13 | Publish-size delta measured and within ceiling | **PASS** | Fresh `origin/master` build 45.35 MB → branch 45.43 MB = **+0.08 MB**, same zip tool both sides. Absolute 45.43 MB « 60 MB ceiling; delta 62× under the +5 MB escalation threshold. See §3. |

**Tally**: 5 PASS · 4 PARTIAL · 2 BLOCKED · **2 FAIL**. **None omitted.** (Was 6/4/2/1 before live UAT
demoted criterion 2 — see §6.3.)

---

## 5. Parity checklist (`notes/parity-checklist.md`) — deployed-build status

POML step 7 requires verifying every capability marked supported **against the deployed build in both hosts**.
**Not performed — no live Office host.** All 14 capability rows retain `pending — task 042 UAT`:

`canGetAttachments` · `canGetRecipients` · `canGetSender` · `canGetDocumentContent` · `canGetDocumentUrl` ·
`canSaveAsPdf` · `canSaveAsEml` · `canInsertLink` · `canAttachFile` · `canOpenBrowserWindow` ·
`canComposeEmail` · `canShowLinkedTodos` · `canSuggestRelatedRecords` · `canProvideDocumentName`

One loose end from that checklist **is** now closed: §8 said the new `shared/adapters/__tests__/capabilities.test.ts`
was "not yet in `ci-gated-suites.txt`" and that the main session owned adding it. Verified present in the gate
file, which now pins **46 suites / 537 tests** (CI job "Office Add-ins Tests (Gate)" green on `eddc55286`).

---

## 6. Failures and open decisions

### 6.1 FAIL — criterion 12, filed as #996 / ISS-004

Not filed because 111 tsc errors exist — those are a deliberate, owner-approved accept (project `CLAUDE.md`
2026-09-09, narrowed 290 → 111 on 2026-09-12). Filed because **the other half of FR-18's acceptance —
"CI gates it going forward" — was never implemented.** The accept's own safety argument is *"production is at
0 so new production errors stand out"*, and that property holds only if something runs typecheck. Nothing
does: `npm run build` is webpack (test files aren't in its graph) and `ts-jest isolatedModules` is
transpile-only. A **new production** error would today be caught by no gate at all.

`spec.md:92` (FR-18) and `spec.md:264` still assert "clean" + "CI gates it"; project `CLAUDE.md:158,167` say
otherwise and are later. spec.md is the stale document. Resolution is the owner's: add a production-scoped CI
typecheck step, amend spec.md, or both.

Per the POML this was **not fixed inside this task**, and the failure was **not re-run to green**.

### 6.2 Resolved — Create To Do tab parity (was an open question against criterion 10)

`parity-checklist.md` §2 flags `TAB_CONFIGS.createTodo.availableFor: ['outlook']` as a suspected stale gate and
recommends "an explicit owner decision … or file a follow-up task". **That follow-up exists and is done**:
task **049** (✅ 2026-09-15, `4f3b7dbd3`) changed `availableFor` to `['outlook', 'word']` and rewrote the
`TaskPaneNavigation.test.tsx` test that had pinned the old behaviour — both in the same change, which is what
task 040's no-weakening rule required and prevented 040 itself from doing.

Verified in the current tree: `TaskPaneNavigation.tsx:103` reads `availableFor: ['outlook', 'word']`, with a
history comment at :95 recording that it was `['outlook']` from task 015 through task 040.

**The parity checklist is therefore stale on this point** — it was written 2026-09-15 alongside the fix and
still carries the pre-fix recommendation. Nothing is owed here; noted so a later reader does not re-open it.
**No open owner decision remains from this section.**

---

### 6.3 FAIL — criterion 2: every NEW document 503s on the identity check (#997 / ISS-005)

**Found by live UAT 2026-09-18** — the first criterion this project's 12,375 green tests could not have caught.

`POST /api/documents/resolve-identity` answers **503** for the ordinary "not in Spaarke" case, which the FR-01
contract requires to be **200 `{resolved:false}`**. The pane therefore shows *"Couldn't check this document —
the service is unavailable"* and offers only **"Save it as a new document anyway"**.

**Root cause — measured, not inferred.** `IsAlternateKeyNotFound` recognises only `0x80040217`
(`ObjectDoesNotExist`, the **by-id** code, via `RecordContainerResolver.IsRecordNotFound`) plus
`DataverseServiceClientImpl`'s own literal `"not found with provided alternate key values"`. Dataverse sends a
different code for an **alternate-key** miss. Read-only probe against dev:

| Lookup | Code | Message |
|---|---|---|
| **alternate key, absent** | **`0x80060891`** | *"A record with the specified key values does not exist in sprk_document entity"* |
| by id, absent | `0x80040217` | *"Entity 'sprk_document' With Id = … Does Not Exist"* |

That first message is **byte-identical** to the production fault in App Insights (`spe-insights-dev-67e2xz`,
2026-09-18T04:33:38Z). So the absent row falls into the indeterminate branch and throws at
`DocumentUrlIdentityResolution.cs:308`.

**Fails safe, but inverts the contract.** No duplicate rows — the refusal is the conservative direction. But
the three-answers contract exists so INDETERMINATE is never read as NEW; this reads NEW as INDETERMINATE.

**Why the suite was green.** `DocumentUrlIdentityResolutionTests.cs:38` defines
`ObjectDoesNotExist = -2147220969` (the by-id code) and the absent-row test pairs it with the **real**
alternate-key message — a pairing Dataverse cannot produce. One line of test data.

**Ruled out along the way** (recorded so nobody re-walks them): `sprk_graphitemid_uk` is **Active**
(`Verify-ComposeIdentityKey.ps1` exit 0); auth/OBO is healthy (`appid=c1258e2d-…`, `scp=SDAP.Access
user_impersonation`); not a network/CORS failure (those render different pane copy); and `testhost` PID 6612
belonged to **another worktree** (`unified-access-control-r2`) and was correctly left alone.

**🔴 FIX STATUS: WRITTEN, COMMITTED, NEVER COMPILED — UNVERIFIED.** A regression test using the real fault code
**was confirmed to fail before the fix** (`identity_resolution_unavailable` at line 308) with all 25
pre-existing tests passing. The fix itself has never built: six consecutive attempts each named a *different*
just-generated file under `src/server/api/Sprk.Bff.Api/obj/Debug/net10.0/linux-x64/` as missing (`ref/…dll`,
`refint/…dll`, `…GeneratedMSBuildEditorConfig.editorconfig`) plus `MSB3030` copy failures on the 14 MB
`Sprk.Bff.Api.dll`. Identical signature to this task's own master-publish failures (§3's measurement note),
which vanished on relocating the build — consistent with AV scanning fresh build intermediates. **Not
reproducible in CI.**

**The deployed dev BFF still carries this defect.** No redeploy was performed.

**To finish it**: build, then `dotnet test --filter FullyQualifiedName~DocumentUrlIdentityResolutionTests`
(26 tests). The new test must pass **and** the three `UnhealthyAlternateKey_Is503…` cases must still pass —
`0x80060892` is one integer away, means duplicate/not-Active key, and must stay 503 (NFR-07). Then redeploy and
re-run a new-document save in Word.

---

## 7. Hand-off — what the operator must do (steps 3 and 4)

### Step 3 — M365 Admin Center re-upload (⚠️ MANDATORY, not optional)

Task 037 bumped both Word manifests to 1.0.9 to add the `FunctionFile` and two `ExecuteFunction` ribbon
controls. Until the manifest is re-registered, users keep the 1.0.8 registration and **the ribbon buttons will
not appear** — and UAT would be testing the wrong bits.

1. Take the **build output**, not the repo source: `src/client/office-addins/dist/word/manifest.xml` after a
   production `npm run build`, **or** the already-deployed hosted copy
   `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml` (verified 1.0.9.0, 0 localhost).
   **Never** upload `word/word-manifest.xml` from the repo — it carries `https://localhost:3000` placeholders.
2. M365 Admin Center → **Settings → Integrated apps** → update the existing Spaarke add-in entry.
3. Confirm the pane footer reads **1.0.9**. That also finally closes task **011** (🔄), whose only remaining
   evidence is a live sideload.
4. **Outlook needs no re-upload** (its manifest is unchanged at 1.0.22).

### Step 4 — UAT against the deployed build

1. **Criterion 11 / the parity checklist** — the biggest item: 14 capability rows in both hosts.
2. **The four `<ui-tests>` in the 042 POML** — deployed build is the bumped version; NAA sign-in succeeds with
   no `AADSTS7000471`; ribbon Quick Save + Share execute from the deployed manifest; dark mode (ADR-021).
3. **Criteria 1, 3, 7, 9** — the manual/e2e halves listed in §4.
4. **`SaveModeSection`'s `'conflict'` copy** (task 051 finding) — behaviour is correct (Save disabled, no
   default target); the wording was written for task 012's different-drive case and reads imprecisely when the
   cause is a stamp/URL disagreement. A copy judgment against a live pane.

**If sign-in fails with `AADSTS7000471`, that is a redirect-URI registration gap, not a code defect** — task
041 verified both required SPA URIs are already registered on `Spaarke Office Add-in`
(`c1258e2d-1688-49d2-ac99-a7485ebd9995`) for this SWA host, so investigate registration before touching auth
code.

**Record each result back into this file** (§4 table) and file any failure via `/project-defer-issue-tracking`
— both `notes/defer-issues.md` and a GitHub Issue. Do not fix defects inside 042.

---

## 8. Deviations from the POML, stated explicitly

1. **Step 4 says "Push the branch. Do NOT run the deploy workflow directly."** The branch was already fully
   pushed (0 unpushed) and `deploy-office-addins.yml`'s push trigger covers only `master` and
   `work/SDAP-outlook-office-add-in` — **not this branch** — so pushing could not deploy anything. Task 041 §7
   decided this deliberately (a push trigger would auto-deploy a feature branch onto the shared dev SWA) and
   names `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1` as "the launch command
   for task 042", evidenced by three prior successful dispatch runs on this ref. The POML's `<tools>` clause
   forbids `gh workflow run` "without operator direction"; that direction exists in 041 and in the owner's
   go-ahead. **The deploy still ran entirely in CI** — no SWA CLI, no local script, no manual upload.
2. **Step 2 ("bump the 4-part version") was not executed** — task 037 already bumped to 1.0.9.0/1.0.9.
   Bumping again would ship a version nothing was built against.
3. **Steps 5–7 (re-register, run the 13 criteria live, verify parity on the deployed build) are handed to the
   operator**, per §7, exactly as the POML's step 5 permits ("or hand the operator explicit instructions if the
   agent cannot perform it").
4. **Task 042 is left 🔄, not ✅.** Its acceptance criteria include live-host verification that has not
   happened. Marking it ✅ now would assert something untrue.

---

## 9. Frozen-file and hygiene checks

| Check | Result |
|---|---|
| `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml` modified? | ✅ **No** — not in this branch's 363-file diff vs master |
| Branch up to date with `master`? | ✅ 0 behind, 198 ahead |
| `/conflict-check` overlaps | Soft only: `projects/INDEX.md` (PRs #950, #935) and researcher `MEMORY.md` (#950) — coordination files, not code. Dependabot **#909** touches `deploy-office-addins.yml`; this task changed no workflow file. |
| Unfiled `defer-issues.md` entries | ✅ none |
| Secrets created/rotated/read | ✅ none. Operator's own `az` identity (`ralph.schroeder@spaarke.com`), per NFR-11 / ADR-028. |
