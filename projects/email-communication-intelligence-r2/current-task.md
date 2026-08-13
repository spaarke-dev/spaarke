# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-08-13 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first, then "Fix Plan (TO-DO)".

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | R2 **deployed to dev + UAT in progress**. Reconciliation UI has real gaps (below). |
| **Branch** | `work/email-communication-intelligence-r2` @ `b94ad4a61` · clean · **5 ahead / 8 behind `origin/master`** |
| **Status** | in-progress — implementing UAT fix plan (approved by operator 2026-08-13) |
| **Next Action** | Start **Fix #1** (grid-config fix, cheapest/highest-impact): add `sprk_associationprovenance` + `sprk_regardingrecord*` to `needs-review.gridconfiguration.json` and re-seed → then **Fix #4** (enable triage). See Fix Plan below. |

### Critical context (3 sentences)
R2 is deployed to **dev** (spaarke-bff-dev + spaarkedev1); the reconciliation code page works but is **substantially incomplete vs the prototype** — the Related-to pane is collapsed to "Requires review" text, Fields/Tasks tabs are always disabled, no suggestions render, and triage columns are blank because **triage AI never populates real captures**. Root causes are known + file-referenced (below). The **`.eml` archive bug is fixed on this branch but NOT yet merged to master** (commit `0026af5e1`).

### Environment / key IDs
- **BFF (dev)**: `spaarke-bff-dev` / `rg-spaarke-dev` · https://spaarke-bff-dev.azurewebsites.net · audience `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` · deploy via `pwsh scripts/Deploy-BffApi.ps1` (do NOT use `-SkipBuild` after code edits — it ships stale `publish/`).
- **Dataverse**: `spaarkedev1` (pac + `az account get-access-token --resource https://spaarkedev1.crm.dynamics.com`).
- **Apps**: Spaarke Platform `d908a85b-9454-f111-a825-7c1e520aa4df`; **Matter Management `729afe6d-ca73-f011-b4cb-6045bdd8b757`** (operator added the reconciliation code page here).
- **Grid configs (`sprk_gridconfiguration`)**: needs-review = `00000000-0000-4000-8000-000000005001` · per-team = `d68c8b50-ca96-f111-b8dc-7ced8ddc4a05`.
- **Test data markers**: Mode-A seeded comms = `sprk_correlationid` LIKE `uat-seed-20260813-*` (14 comms + 3 proposals); Mode-C real captures = `uat-e2e-20260813-*`.
- ⚠️ **Security**: `AzureAd__ClientSecret` is a **plaintext app setting** on spaarke-bff-dev → move to Key Vault reference. (Value must never be printed/committed.)

---

## Fix Plan (TO-DO) — approved by operator 2026-08-13

Ordered by impact/effort. UAT feedback → root cause → fix. Detailed root-cause analysis (with file:line) is in the "UAT Root-Cause Reference" section below.

- [ ] **#1 — Grid-config fix (config only; cheapest, highest impact).** Edit `src/client/shared/Spaarke.Communication.Components/src/components/ReconciliationGrid/needs-review.gridconfiguration.json`: add `sprk_associationprovenance`, `sprk_regardingrecordname`, `sprk_regardingrecordnumber`, `sprk_regardingrecordtypename` to BOTH the fetchXml `<attribute>` list AND the layoutXml (they can be hidden columns). Re-seed via `pwsh scripts/seed-reconciliation-gridconfig.ps1 -Clean` (or a targeted PATCH of the config record's `sprk_configjson`). **This alone restores Related-to suggestions (#2) and lets `resolveRegarding` recognize confirmed rows → un-gates Fields/Tasks (#3/#4).**
- [ ] **#2 — Render `EmailConnectionsReview` INLINE in the reconciliation Related-to pane** (fixes "missing capabilities" + "no rich lookup pane"). In `ReconciliationWorkspace.tsx:303-310`, replace the `<RelatedToCell …/>` in the Related-to tab BODY with an inline `<EmailConnectionsReview …/>` (same component the email page renders inline at `EmailWorkspace.tsx:427`). Keep `RelatedToCell` only for the compact grid cell. **r5-contended shared lib (`Spaarke.Communication.Components`) → run `/conflict-check` + coordinate with `email-communication-solution-r5` before the PR.**
- [ ] **#3 — Stop the remount-on-confirm.** In `src/solutions/CommunicationReconciliation/src/main.tsx`, `onAssociationsChanged` bumps `refreshKey` which is the React `key` on `<ReconciliationWorkspace>` (`main.tsx:279,377,382`) → the whole workspace remounts and the browse shell closes, so the user never sees Fields/Tasks enable in-session. Replace with a targeted grid refresh OR an in-session confirmed-regarding map (mirror the prototype's local `confirmed[curId]`).
- [ ] **#4 — Triage AI not populating (blank grid columns).** Confirmed: **0 real captures have triage; only the 14 seeded rows do.** Likely cause: `ICommunicationTriageAi` is registered as `NullCommunicationTriageAi` in dev (feature-gated — `Infrastructure/DI/AnalysisServicesModule.cs:514` Null vs `:1299` real `CommunicationTriageAi`). Confirm the gating flag/config in the dev app settings and enable the real impl; verify triage populates on a new capture. (Triage's *input* — the ai-classify signal — IS present in `sprk_associationprovenance`, and rung-5 AI works, so it's specifically the triage step failing/gated.)
- [ ] **#5 — TWO NEW VIEWS (operator request).** Author two new `sprk_gridconfiguration` records + wire the grid's view-switcher (the "view dropdown" — currently blank, doesn't surface `display.title`) to list them:
  - **"Email Review All"** — all email comms regardless of status. fetchXml filter: `statecode eq 0` + `sprk_communicationtype eq 100000000` (Email), **no** `sprk_associationstatus` filter. `display.title = "Email Review All"`.
  - **"Email Review Completed"** — resolved only. fetchXml filter: `statecode eq 0` + `sprk_communicationtype eq 100000000` + `sprk_associationstatus eq 100000000` (Resolved). `display.title = "Email Review Completed"`.
  - The existing needs-review config stays as the default. Grid must let the user switch between the 3 views and show the active view's name in the dropdown (part of the grid/host work).
- [ ] **#6 — SpaarkeAi widget not in the library.** The reconciliation widget (task 062 `ReconciliationWorkspaceWidget`) isn't registered/visible in the SpaarkeAi widget library. Register it in the SpaarkeAi widget registry (`Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` or the context registry) and rebuild/redeploy SpaarkeAi (`Deploy SpaarkeAi` workflow triggers on `src/client/shared/Spaarke.AI.Widgets/**` on merge to master).
- [ ] **#7 — Merge the `.eml` archive fix to master.** Commit `0026af5e1` (9-site `sprk_communication`→`sprk_relatedcommunication` fix) is on this branch only. Everyone's captures stay archive-broken until it lands. Open PR / merge (touches r5-owned `Services/Communication` → note it).
- [ ] **#8 — Deploy sequence after fixes.** Frontend changes: rebuild + `/code-page-deploy` (CommunicationReconciliation) + SpaarkeAi redeploy. BFF changes (triage/archive): `pwsh scripts/Deploy-BffApi.ps1` (full build, no `-SkipBuild`). Re-run Mode-C send to verify.
- [ ] **#9 — Cleanup test data when done.** `pwsh scripts/seed-uat-communication-corpus.ps1 -Clean` (Mode-A) + delete `uat-e2e-20260813-*` rows (Mode-C).

---

## UAT Root-Cause Reference (prototype-vs-deployed comparison, 2026-08-13)

Prototype: `c:\code_files\spaarke-prototype\projects\email-communication-intelligence-r2-uat\src\App.tsx` (loads rows with ALL columns → provenance present; renders resolver INLINE with candidate cards + Confirm; local `confirmed[curId]` un-gates tabs same-session). The **email page** (`EmailWorkspace.tsx:427`) already renders `EmailConnectionsReview` inline with the rich OOB "Lookup Records" pane — that is the target UX the reconciliation page must match.

| Capability | Deployed root cause (file:line) |
|---|---|
| Related-to inline resolver | Pane uses `RelatedToCell` not inline resolver — `ReconciliationWorkspace.tsx:305`; "Requires review" text at `RelatedToCell.tsx:117-128` (real resolver hidden in a `SprkModal`) |
| Candidate suggestions | grid omits `sprk_associationprovenance` — `needs-review.gridconfiguration.json:5` → `resolveReview` passes null (`main.tsx:152`) → empty candidates (`provenance.ts:540-556, 781-783`) |
| Fields/Tasks tabs | `gated = !regarding` (`ReconciliationWorkspace.tsx:280,294,297`); `resolveRegarding` always null (`main.tsx:174-191`) because provenance+denorm not selected and needs-review excludes Resolved |
| In-session enable | `onAssociationsChanged`→`refreshKey`→`key` remount closes shell (`main.tsx:279,377,382`) |
| Rich "Lookup Records" pane | lives in `EmailConnectionsReview` (`lookupObjects` at `EmailConnectionsReview.tsx:184-203,252-278`); reconciliation hides it behind `RelatedToCell` modal vs email inline (`EmailWorkspace.tsx:427`) |

`ReconciliationWorkspace` props from `main.tsx`: `resolveReview`/`resolveRegarding` wired but data-starved; `toBrowseRecord`, `onProposalResolved`, `onOpenOriginalActivate`, `readerActions`, `membershipResolver` = NOT wired (null). `configId` omitted → grid uses `NEEDS_REVIEW_CONFIG_ID` placeholder (which the seeded record's GUID matches).

**"Sent email not in reconciliation view" = working as designed:** it auto-Resolved (`sprk_associationstatus=100000000`); needs-review grid excludes Resolved. The new "Email Review Completed" view (#5) will surface it.

---

## What was DONE this session (2026-08-13) — reference

1. **`/worktree-sync`** — merged master clean; **CI fixes A/B/C** (SDAP-CI LFS smudge + external-access stale-test drift + PCF lint) committed + **merged to master via PR #755** (`f9a1e0eb9`). Details: `notes/ci-failure-definition-and-plan.md`.
2. **Deployed R2 to dev**: BFF (`spaarke-bff-dev`, 48.48 MB, hash-verified, healthz 200), SpaarkeAi widget (workflow), CommunicationReconciliation code page (`sprk_communicationreconciliation` web resource), grid configs seeded. Log: `notes/deploy-results-2026-08-13.md`.
3. **Built UAT test data (3 modes)**: Mode A seed (`scripts/seed-uat-communication-corpus.ps1`), Mode B rung-preview harness (`scripts/uat-rung-preview-harness.ps1`, golden 2/2 pass), Mode C real end-to-end sends (`POST /api/communications/send` → real webhook capture). Plan: `notes/UAT-TEST-DATA-PLAN.md`.
4. **Found + FIXED + verified a High-sev bug**: `.eml` archive + attachment docs wrote non-existent `sprk_document.sprk_communication` (schema uses `sprk_relatedcommunication`). Fixed 9 sites + regression test (`0026af5e1`), redeployed, verified live. Detail: `notes/DEFECT-eml-archive-communication-lookup.md` (RESOLVED). **NOT yet merged to master.**
5. **UAT session** surfaced the reconciliation-UI gaps → this fix plan.

### E2E findings (real capture works)
- Conflicting matters (PAT-411021 + PAT-415062) → **Ambiguous** (withheld) ✅ on the real write path.
- Single explicit matter (PAT-545148) → **Resolved + auto-filed to the correct matter** ✅.
- Recipient-alias `Bcc matter-PAT-411021@` → **bounced (NDR)** — alias isn't a routable address; must route into a monitored mailbox to test RecipientAliasRung.
- Thread-continuity (`InReplyToMessageId`) → inbound `inReplyTo` empty + resolved Ambiguous (subject matter-numbers dominated); confirm the In-Reply-To header survives send→delivery.

### Files created/modified this session
- `scripts/seed-reconciliation-gridconfig.ps1`, `scripts/seed-uat-communication-corpus.ps1`, `scripts/uat-rung-preview-harness.ps1` (new)
- Archive fix: `IncomingCommunicationProcessor.cs`, `CommunicationService.cs`, `MessageAttachmentMaterializer.cs`, `DataverseServiceClientImpl.cs`, `DataverseWebApiService.cs`, `Services/Ai/Handlers/EmailDraftToolHandler.cs`, `tests/.../CommunicationServiceArchiveEmbedTests.cs`
- CI fixes (merged to master): `.github/workflows/sdap-ci.yml`, `src/client/pcf/CommunicationActions/**`, `src/client/pcf/TrackingFieldTrio/index.ts`, `tests/**/ExternalAccess/**`
- Docs: `notes/ci-failure-definition-and-plan.md`, `notes/deploy-results-2026-08-13.md`, `notes/UAT-TEST-DATA-PLAN.md`, `notes/DEFECT-eml-archive-communication-lookup.md`, `notes/fixtures/uat-emails/*.eml`
