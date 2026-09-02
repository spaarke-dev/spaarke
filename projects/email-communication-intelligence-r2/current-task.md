# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-09-01 (context-handoff — UAT-driven fixes session: #919 document-profile AI bug FIXED, and the Document Upload wizard "Send Email" follow-on rebuilt end-to-end — dead-form fix → standard EmailComposer → Finish-guard → centered success. All merged or auto-merging + deployed to dev.)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | **All this session's work is DONE, deployed to dev, and merged (or auto-merging) to master.** No blocking work. The session was reactive UAT fixes on two fronts: **(A)** the #919 document-profile AI bug; **(B)** the Document Upload wizard's "Send Email" follow-on step. Every item was owner-UAT-confirmed except the final centering polish (deployed; test after a wizard reload). |
| **Branch** | `work/email-communication-intelligence-r2` (0 behind after each merge). Working tree CLEAN. |
| **Open PR** | **#930** (centering polish) — auto-merge on `Router`. **MERGED**: **#923** (#919 profiling fix, BFF), **#925** (Send Email dead-form fix), **#927** (EmailComposer redesign), **#929** (Finish-guard). |
| **Deployed (dev)** | **BFF** `spaarke-bff-dev` ← #923 (profiling fix, hash-verified + healthy). **Code page** `sprk_documentuploadwizard` on `spaarkedev1` ← latest build (composer + guard + centering; published). App Insights app id `6a76b012-46d9-412f-b4ab-4905658a9559`. |
| **Next** | Confirm #930 merged (`gh pr view 930`). No pending fix work — awaiting any further owner UAT. |

---

## A. #919 document-profile AI bug — FIXED (PR #923, deployed to BFF, UAT-confirmed ✅)

**Symptom (resolved)**: every saved Document stuck at `sprk_filesummarystatus = Failed` — profiling never completed.

**Root cause (empirically confirmed)**: the "Document Profile" **node playbook**'s Update Record node stores config in the Playbook-Builder **wrapper format** (JSON-inside-a-JSON-string). Layer-1 `RenderConfigJsonStructurally` escaped substituted values only at the OUTER level; `UpdateRecordNodeExecutor.ParseConfig` re-parsing the nested string threw `'0x0A' is invalid within a JSON string. Path: $.fieldMappings[0].value`. (The prior ":2284 fallback" hypothesis was WRONG — corrected.)

**Two fixes shipped in #923**:
1. **Renderer (root cause)** — `PlaybookOrchestrationService.RenderConfigJsonStructurally` now recurses into a nested JSON-containing-a-template string (escapes at the nested level). Protects Update Record / Create Task / Create Notification / Send Email nodes in ANY playbook. Made `internal static` for a rendered-config regression test (`UpdateRecordParseConfigReproTests`).
2. **Convergence** — `AppOnlyAnalysisService` routes the "Document Profile" consumer through the direct-Action (ADR-043) spine (`IActionResolver → IActionRunner → UpdateDocumentFieldsAsync`) like the wizard + Compose paths, not the node playbook. +2 optional ctor deps.

**Authoritative doc**: [`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`](../../docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md) — the 3 AI execution models (node playbook · direct Action · legacy), the 3 profiling entry paths, the failure mechanism (Part 4), and a change-safety checklist. Cross-ref `.claude/FAILURE-MODES.md` AP-10; GitHub #919.

---

## B. Document Upload wizard "Send Email" — rebuilt end-to-end (client `sprk_documentuploadwizard`)

Launched from the **Semantic Search PCF**; steps Add Files → Processing → Next Steps → **Send Email**. Four fixes, all deployed + UAT-confirmed (centering just deployed):

1. **Dead-form fix (#925)** — the embedded Send Email step rendered a form but **never sent** (`handleFinish` only built the success screen; fields trapped in local state). Wired it to `sendCommunication` (`POST /api/communications/send`). **Also unblocked the whole solution build**: `NextStepsStep` imported the stale `components/FindSimilarDialog` (renamed to `FindSimilarViewer/FindSimilarViewerDialog` by #714) — broke `npm run build` on master too. NOT a regression from #923 (Send Email is a direct BFF endpoint, not the AI node engine).
2. **Standard EmailComposer redesign (#927)** — replaced the basic To/Subject/Message form with the canonical **`EmailComposer`** mounted **inline** (`mount="inline"`, `mode="compose"`): native Send button in the top "From:" row (no Save Draft — that chrome only exists in dialog/page mounts), `sendMode="sharedMailbox"` (plain Send, no From switcher), uploaded docs auto-attached via `wizardContext` (the `'wizard'` attachment source), parent as `associations`, Xrm lookups via `createXrmEmailComposeHandlers`. The composer owns send mechanics.
3. **Finish-guard (#929)** — if the user composed an email (entered recipients) but hasn't sent, clicking **Finish** prompts a `ChoiceModal` (**Send email** / **Finish without sending** / **Keep editing**). `DocumentEmailStep` exposes a controller (`hasUnsentEmail()`+`send()`) via a composer ref + `onStateChange`; the dialog's `handleFinish` awaits the choice (cancel = `throw new Error("")` → WizardShell `finishError` falsy → stays open, no error bar).
4. **Centering polish (#930, auto-merging)** — the step's "Email sent" block (`DocumentEmailStep`) and the wizard's final success screen (shared **`WizardSuccessScreen`**, `justifyContent: 'safe center'` + `flexGrow`) now center vertically + horizontally. `safe center` protects tall success screens from clipping. Shared change is latent for other wizards until each rebuilds.

**Client files (all in `src/solutions/DocumentUploadWizard/src/`)**: `components/DocumentEmailStep.tsx` (the composer + controller), `components/NextStepsStep.tsx` (threads props + the FindSimilar import fix), `DocumentUploadWizardDialog.tsx` (Finish-guard + ChoiceModal). Shared: `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardSuccessScreen.tsx` (centering).

---

## Build / deploy reference (this session)

- **Code page** (`sprk_documentuploadwizard`, a Vite code page): `cd src/solutions/DocumentUploadWizard && rm -rf dist node_modules/.vite .vite && npm run build` (first run needs `npm install --legacy-peer-deps --no-audit --no-fund`; node_modules is gitignored). Verify a known string is in `dist/index.html`. Deploy: `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_documentuploadwizard -FilePath src\solutions\DocumentUploadWizard\dist\index.html` (uses `az` token — `az login` = ralph.schroeder@spaarke.com; `pac` active profile = SPAARKE DEV 1). The vite alias maps `@spaarke/ui-components` → the lib **source**, so shared-lib edits are picked up by rebuilding the code page (no separate lib build).
- **BFF** (`spaarke-bff-dev`): `.\scripts\Deploy-BffApi.ps1` (build + package + SHA-256 hash-verify + health check). net10 (`DOTNETCORE|10.0`); ~45 MB compressed.
- **PRs**: master is PROTECTED (ruleset `21824191`, required check literal `Router`). Use `gh pr create` + `gh pr merge {n} --auto --merge` (Path A). Always `git fetch origin && git merge origin/master` before pushing (master is very active).

## Merge / protection notes
- Direct `git push origin HEAD:master` is REFUSED. Classic `/branches/master/protection` returns a misleading 404 — check **rulesets**.
- Pre-commit hook runs `prettier --write` (.ts/.tsx) + `dotnet format` (.cs) on staged files — whitespace only; deployed bundles remain functionally identical.

## Key references
- Doc: `docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` · `.claude/FAILURE-MODES.md` AP-10.
- UAT findings: `notes/pillar-b-uat-findings-2026-08-31.md` (#7 = profiling, corrected).
- The EmailComposer engine + wrappers: `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/**` (SendEmailStep/SendEmailDialog wrappers; `createXrmEmailComposeHandlers`; inline mount shows the native Send in the From row, no ComposerActionBar).
