# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-08-13 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | R2 UAT round 1 fixes **MERGED (PR #765) + deployed**. UAT round 2 fixes committed → **PR #768 open, CI running**. |
| **Branch** | `work/email-communication-intelligence-r2` · clean · **0 behind / 4 ahead** master. |
| **Status** | Round 1: all 6 UAT fixes (#1-#6) + `.eml` archive (#7) + a real compound-OFF host-boot BFF bug (ADR-032 §F.1) — merged (`1825a3047`) + deployed. Round 2 (this UAT): reconciliation newest-first ordering + received/sent date-time display — **PR #768**. |
| **Next Action** | **Merge PR #768** once Tier-1 green (`gh pr merge 768 --merge`) → **Deploy SpaarkeAi** auto-fires (carries the email-view date/time + spacing). Grid fixes already live. Then **#9 cleanup** UAT seed data. |

### UAT round 2 (2026-08-13) — what & where
Operator UAT found: (Q1) a captured email didn't appear in Needs Review; (Q2) no received/sent date-time shown; (Q3) "add space below the From: row".
- **Q1 root cause**: grid ordered by `sprk_triagepriority asc` first; triage null on real captures → newest email sank to pos 185/197 (25-row page never reached it). **Fixed**: all 4 grid configs → `sprk_receiveddate desc` primary. **LIVE in dev** (data-driven, re-seeded; commit `4bba0d8bd`).
- **Q2 grid**: Date column → `datetime` renderer (time WAS in data, `19:02:49`), relabeled "Received". **LIVE in dev** (`4bba0d8bd`).
- **Q2 email view + Q3**: `EmailCardList` time-aware card date (today→time, hover=full); `EmailRecipients` right-aligned "Received/Sent: {datetime}" in From row + extra paddingBottom below the block. Shared lib → **pending PR #768 merge → Deploy SpaarkeAi** (`34bbed52e`).

### Round-1 deploy status (dev)
- **#1 grid config / #4 triage catalog / #7 archive** — live (data / BFF, earlier).
- **#2/#3/#5 code page** `sprk_communicationreconciliation` (`1e191e05-...`) redeployed. **#2/#3/#6** via Deploy SpaarkeAi (auto). **BFF host-boot fix** on master, NOT redeployed (dev compound-ON, never crashed).

### Key IDs / mechanisms
- Grid configs: needs-review `00000000-0000-4000-8000-000000005001`, per-team `d68c8b50-...5001`→`d68c8b50-ca96-f111-b8dc-7ced8ddc4a05`, email-review-all `...5002`, email-review-completed `...5003`. Re-seed: `pwsh scripts/seed-reconciliation-gridconfig.ps1`.
- Code page deploy: build `src/solutions/CommunicationReconciliation` (`npm run build`) → `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_communicationreconciliation -FilePath .../dist/sprk_communicationreconciliation.html`.
- Email view surface = SpaarkeAi `email` widget + LegalWorkspace `email` section (both via Deploy SpaarkeAi on master merge). `EmailWorkspace`/`EmailCardList`/`EmailRecipients` in `Spaarke.Communication.Components`.
- **Operator TODO**: send a test email to `mailbox-central@spaarke.com` (matter # e.g. PAT-545148) → confirm #4 triage columns populate on the new capture (triage catalog seeded but not yet e2e-verified on a fresh capture).

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

- [x] **#1 — Grid-config fix (config only) — DONE 2026-08-13 (`7807c02a1`).** Added `sprk_associationprovenance` + `sprk_regardingrecord{name,number,typename}` to the fetchXml `<attribute>` list of BOTH `needs-review` + `per-team` `.gridconfiguration.json`. layoutXml unchanged (display-only — the DataGrid passes the full fetched record to the resolvers via `onRecordsLoaded`, confirmed against `useLazyLoad`). Config is data-driven (loaded from `sprk_gridconfiguration` by GUID) so **no code-page redeploy needed**; upgraded `seed-reconciliation-gridconfig.ps1` to PATCH-on-exists and re-ran → both dev records verified carrying the 4 new attrs live. Config-validation test passes. **Restores Related-to suggestions (#2) data + lets `resolveRegarding` recognize confirmed rows.**
- [x] **#2 — Inline `EmailConnectionsReview` — DONE 2026-08-13 (`6757a55ae`).** In `ReconciliationWorkspace.tsx` the browse tab body now renders the full `<EmailConnectionsReview {...review}/>` inline (candidate cards + the manual "Lookup Records" side-pane) instead of `RelatedToCell` (which hid it behind a "Requires review" + picker `SprkModal`). Confirm handshake preserved (`review.onAssociationsChanged` + `handleConfirmed`). `RelatedToCell` stays the compact grid-cell renderer. Test updated to assert the inline surface; typecheck 0 err; suite 6/6. **/conflict-check: CLEAR** (no open PRs on the shared lib; r5 closed + not diverged on these files). **Needs code-page redeploy (#8) to reach dev.**
- [x] **#3 — No remount-on-confirm — DONE 2026-08-13 (`a0e58c9ee`).** `ReconciliationWorkspace.handleConfirmed` now does a **targeted single-row re-fetch** (`dataverseClient.retrieveRecord` → merge into `rows`) so `resolveRegarding` sees the new Resolved status + denorm and un-gates Fields/Tasks **while the browse shell stays open**. `main.tsx` dropped `refreshKey` + `key={refreshKey}`; `handleAssociationsChanged` is now a no-op (workspace self-refreshes). Typecheck 0 err; suite 6/6. **Needs code-page redeploy (#8).** (Minor known trade-off: the grid-cell chip behind the shell may lag until next grid query — acceptable vs the remount.)
- [x] **#4 — Triage AI not populating — ROOT-CAUSED + FIXED 2026-08-13 (`d9ff4df82`); live e2e pending.** Handoff DI-gate hypothesis was **WRONG**: the compound AI gate is ON in dev (`Analysis__Enabled` + `DocumentIntelligence__Enabled` both true) → the real `CommunicationTriageAi` IS registered. Actual cause: the **3 email Linear-AI-Consumer Actions (`triage-email`/`propose-field-updates`/`create-task-from-email`) + their `sprk_playbookconsumer` routing rows were never seeded to spaarkedev1** → `ActionResolver.ResolveAsync("email-triage")` throws → triage degrades to null (NFR-04). Rung-5 classification works because it calls Azure OpenAI DIRECTLY (no routing table) — that's why the ai-classify signal is present while triage isn't. **Fixed**: new `scripts/seed-email-intelligence-actions.ps1` seeded the 3 `sprk_analysisaction` rows (JPS + flat shapes; derived draft-07 output schemas), + ran `Seed-PlaybookConsumers.ps1` for the 3 routing rows. Verified: rows created + read back valid + routing resolves to the action GUIDs. **⏳ FINAL VERIFY (operator-gated):** send a fresh email to `mailbox-central@spaarke.com` → next inbound capture runs `EnrichAsync` → confirm triage columns populate (category/priority/summary/riconfidence). Full analysis: `notes/DEFECT-triage-not-populating-root-cause.md`.
- [ ] **#5 — TWO NEW VIEWS (operator request).** Author two new `sprk_gridconfiguration` records + wire the grid's view-switcher (the "view dropdown" — currently blank, doesn't surface `display.title`) to list them:
  - **"Email Review All"** — all email comms regardless of status. fetchXml filter: `statecode eq 0` + `sprk_communicationtype eq 100000000` (Email), **no** `sprk_associationstatus` filter. `display.title = "Email Review All"`.
  - **"Email Review Completed"** — resolved only. fetchXml filter: `statecode eq 0` + `sprk_communicationtype eq 100000000` + `sprk_associationstatus eq 100000000` (Resolved). `display.title = "Email Review Completed"`.
  - The existing needs-review config stays as the default. Grid must let the user switch between the 3 views and show the active view's name in the dropdown (part of the grid/host work).
- [x] **#6 — Reconciliation as a system widget — DONE 2026-08-13 (`4d166fc28`).** Operator clarified: system widget like Matter/Project/Calendar. The workspace dropdown ("library") lists `sprk_workspacelayout` ROWS, each mounting a LegalWorkspace section. Added: (a) `reconciliation.registration.ts` LegalWorkspace section shim (renders shared `ReconciliationWorkspace`, mirrors `email.registration.ts`); (b) registered in `sectionRegistry.ts` + `sectionMetadataCatalog.ts`; (c) extracted `buildResolveReview`/`resolveRegarding` to the shared lib (`reconciliationResolvers.ts`) so all 3 mounts share one copy — code page main.tsx now imports them; (d) seeded the "Reconciliation" `sprk_workspacelayout` row (id `0a770ac3-3197-f111-b8dc-70a8a590c51c`, sections:["reconciliation"]) — verified in dev. Row shows in dropdown now; section content renders after the LegalWorkspace/SpaarkeAi redeploy (#8). Full analysis: `notes/FINDING-reconciliation-widget-library.md`.
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
