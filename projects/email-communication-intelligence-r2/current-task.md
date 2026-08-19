# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-08-14 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Reconciliation UX **prototype-parity** pass — **ALL BATCHES DONE + DEPLOYED**. Batch 1 (visual parity), card-layout fix, B2.1 (attachment text), B2.2 (Fields+Tasks per-line undo), B2.3 (progress badges) — all shipped to dev, impersonated/secure. **Awaiting operator full UAT.** |
| **Branch** | `work/email-communication-intelligence-r2` · **clean** · pushed (`0283f2e89`) · synced to master · **master intentionally NOT merged** (operator: "do not /merge-to-master"). |
| **Deployed (dev, from BRANCH)** | BFF `spaarke-bff-dev` (44.96 MB, hash-verified, healthy) + `sprk_communicationreconciliation` (`1e191e05-…`) + `sprk_spaarkeai` (`5206a442-…`) → all published to `spaarkedev1`. |
| **Next Action** | Operator full UAT of the reconciliation form (both surfaces). Remaining project items unrelated to parity: **064** (close index row), **044** (Outlook add-in deploy), **090** (wrap-up: test-diet + doc-drift). |

### Batch 2 — DONE (B2.1 + B2.2 + B2.3)
- **B2.1** `GET /communications/{id}/attachments/text` — re-extracts attachment text (impersonated reads + OBO SPE + shared cache-aware ITextExtractor); client folds into reader.
- **B2.2** per-line undo: Fields `POST /proposals/{id}/undo` (reverse-apply to oldValue, impersonated + allow-list-gated, compensating audit row); Tasks `POST /communications/{commId}/tasks/{taskId}/undo` (impersonated soft-cancel `sprk_eventstatus`=Cancelled + compensating audit row — W1/W2 fix). Client: accepted/created rows retain an Undo button → terminal "Undone".
- **B2.3** progress badges: `Related ✓ · Fields n/N · Tasks n/N` in SprkModal header (`headerBadges` passthrough); workspace eager-total + cumulative-resolved.
- **Tests**: 12 undo seam tests + B2.1 (10) + client undo test — all pass. Two code-reviews (B2.1 security fix → impersonation; B2.2 W1/W2 → task-undo audit + provenance).
- **Known minor UX** (documented, non-blocking): progress badge "resolved" does not decrement on undo (item stays counted); eager queue-feed total duplicates the tabs' own fetch (best-effort).

### Files Modified This Session (ALL COMMITTED — Batch 1, 3 commits e27cfa991 / a01e9c633 / b19fbf0b6)
- `EmailAssociationsAndTracking/EmailConnectionsReview.tsx` + `.styles.ts` + `EmailConnectionsReviewRows.tsx` — `variant='reconcile'` compact single-row cards, `Select`→`Confirm` revert, per-line **Undo** on the Filed banner (`clearPrimaryRegarding`).
- `ReconciliationBrowseShell/ReconciliationBrowseShell.tsx` + `.types.ts` — standard footer (**Close** grey footerStart / **Save** blue footer), removed duplicate subject, reader **Date row**, **TRIAGE box**, email-level **Open original .eml** link. New optional `onSave` prop.
- `ReconciliationWorkspace/ReconciliationWorkspace.tsx` — `defaultToBrowseRecord` maps `sprk_receiveddate` + `sprk_triage*` (FormattedValues) into the browse record.
- `ReconcileTabs/FieldUpdateReconcileTab.tsx` — friendly field label via metadata `DisplayName`.
- `ReconcileTabs/TaskReconcileTab.tsx` — Status dropdown expanded to canonical `sprk_eventstatus` (Draft/Open/Completed/Closed/On Hold/Cancelled).
- Tests updated (compact-card reconcile tests; subject assertions → text-based). **49 reconcile/shell/workspace tests pass**; only pre-existing 3 `EmailTrackingPanel` failures remain.

### Critical Context (3 sentences)
Operator directive is **"make the reconciliation form match the prototype"** (prototype = `c:/code_files/spaarke-prototype/projects/email-communication-intelligence-r2-uat/src/App.tsx`), both surfaces (standalone code page + SpaarkeAi widget), Dataverse data-gaps to be surfaced for supplement. Batch 1 (client-only visual parity) is shipped; **Batch 2 is BFF-touching (§10 governance)**. Deploys are from the BRANCH (operator said do NOT merge to master yet); SpaarkeAi deploy is the `sprk_spaarkeai` web resource (auto-deploy does NOT fire on shared-lib-only changes → deploy manually).

---

## Batch 2 — plan + open decision (NOT STARTED)

**Investigation findings (verified this session):**
1. **Attachment extracted text is NOT persisted** anywhere (transient pipeline artifact; only triage is persisted to `sprk_communication`). The attachment-text endpoint must **RE-EXTRACT from SPE** (download the `sprk_document` binary → `ITextExtractor`). Cost driver.
2. **Task create returns `CreatedTaskId`** (both proposal-create + ad-hoc, `CommunicationCreateTaskApplyService.cs`) → Tasks undo = delete that `sprk_event`.
3. **Fields undo** = re-write `oldValue` to the regarding record's field — should go through BFF (audit + host-agnostic), not raw client `updateRecord`.

**Batch 2 pieces (§10 BFF-governed — load `.claude/constraints/bff-extensions.md`, do Placement Justification, publish-size ≤60 MB baseline ~45.9 excl PDBs, CVE scan, tests in `tests/unit/Sprk.Bff.Api.Tests/`):**
- **B2.1 Attachment-text endpoint** — new BFF `GET /api/communications/{id}/attachments/text`; client maps `text`/`extractable` into the reader folds (`EmailBodyView` already supports them; `ReconciliationWorkspace.resolveAttachments` currently leaves them unset).
- **B2.2 Fields undo** — BFF reverse-apply endpoint (writes `oldValue` under audit); client keeps the Fields row with an Undo after Accept.
- **B2.2 Tasks undo** — BFF delete endpoint for `CreatedTaskId` under audit; client Undo after Create.
- **B2.3 Progress badges** — CLIENT-ONLY (hoist Fields/Tasks counts to the SprkModal header; Related ✓/• already known in `renderTabs`). This is the one deferred Batch-1 item.

**OPEN DECISION (blocks B2.1 design) — attachment text re-extract vs cache:**
- (a) re-extract on demand, no cache (simplest, slower)
- **(b) re-extract + Redis cache** keyed by document id (ADR-009) — **RECOMMENDED**
- (c) persist extracted text to a new `sprk_communicationattachment` column at ingest (schema + backfill)

---

## Prototype-parity — matched vs remaining

**Matched (Batch 1, LIVE):** Confirm button · compact Related-to cards · per-line Undo · Close/Save footer · no dup subject · reader Date row · TRIAGE box · Open-original .eml · expanded task Status · friendly field labels.

**Remaining:** attachment CONTENT in folds (B2.1, needs data) · Fields/Tasks per-line undo reversals (B2.2) · progress badges (B2.3). Fields + Tasks tab bodies otherwise already close to the prototype (live is more complete — real BFF endpoints).

**Prototype run cmd:** `cd c:/code_files/spaarke-prototype/projects/email-communication-intelligence-r2-uat` → `SPAARKE_REPO_ROOT="c:/code_files/spaarke-wt-email-communication-intelligence-r2" npm run dev` (default `spaarke-wt-smart-todo-r4` root is DELETED). Was at http://localhost:5175/.

---

## Deploy / build reference

- **Code page build:** `cd src/solutions/CommunicationReconciliation && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/sprk_communicationreconciliation.html`.
- **SpaarkeAi build:** `cd src/solutions/SpaarkeAi && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/spaarkeai.html` (tsc-surface-gate: 253 pre-existing shared-lib errors are DEFERRED, only `Surface-owned: 0` matters).
- **Shared lib build FIRST if it changed:** `cd src/client/shared/Spaarke.Communication.Components && npm run build`.
- **Deploy web resource (upload+publish, no rebuild):** `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName {sprk_communicationreconciliation|sprk_spaarkeai} -FilePath {…/dist/*.html}` (needs `az login` — ralph.schroeder@spaarke.com / Spaarke Dev).

## Environment / key IDs
- **BFF (dev)**: `spaarke-bff-dev` / `rg-spaarke-dev` · https://spaarke-bff-dev.azurewebsites.net · deploy `pwsh scripts/Deploy-BffApi.ps1`. Dev now runs **.NET 10** (net8 deploys 503). SDK `10.0.101` installed machine-wide; `global.json` pins `10.0.100` rollForward latestFeature. Worktree merged to net10 master; BFF builds clean (0 err, 48.49 MB pkg).
- **Dataverse**: `spaarkedev1` (`https://spaarkedev1.crm.dynamics.com`).
- ⚠️ **Security (pre-existing)**: `AzureAd__ClientSecret` plaintext app-setting on spaarke-bff-dev → move to Key Vault. Never print/commit the value.

## Known-unrelated (pre-existing, not this work)
- 3 `EmailTrackingPanel` test failures (access-control renders MenuButton vs radios) — fail on clean HEAD.
- Legacy `sdap-ci.yml` "Client Quality" red — widgets tsc gate doesn't install Communication.Components deps → `Cannot find module 'react'`; branch protection is OFF so non-blocking.

## Other remaining project items (from TASK-INDEX)
- **064** — Quick Start "+ New record" + `.eml` pre-load: implemented; index row still 🔲 (close it). `.eml`-into-wizard is a DATA condition (no `.eml` archive linked via `sprk_document.sprk_relatedcommunication` → BFF `from-document` 404s, client swallows → wizard un-seeded). BFF/client paths correct.
- **044** — Deploy Pillar B Outlook add-in (Azure SWA) — 🔲.
- **090** — Project wrap-up (test-diet, doc-drift, size) — 🔲 terminal.
- Email list/form date-time fixes: coded + on master already; now surfaced via the `sprk_spaarkeai` redeploy this session.
