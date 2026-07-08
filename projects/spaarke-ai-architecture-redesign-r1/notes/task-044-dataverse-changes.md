# Task 044 — Dataverse changes (FR-P3-05 engine-shell deletions / audit F-1 closure)

> **Date**: 2026-07-06 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **Idempotent**: yes (five statecode updates; re-runnable by GUID)

## 1. `sprk_analysistool` — five rows DEACTIVATED (statecode 0 → 1, statuscode → 2)

The rows registered the three app-only legacy tool-handler legs deleted by this task
(audit F-1 closure, G-P0 ruling 2026-07-05). Deactivation (not deletion) preserves audit
history while removing the rows from the active catalog — the FR-P0-04 bijection health
check and the loop's catalog projection read active rows only, so `/healthz/catalog`
stays Healthy with the handlers gone.

| Row | id | Handler class (deleted) |
|---|---|---|
| SYS-Invoke Playbook | `7389739e-ec6d-f111-ab0e-7ced8ddc4a05` | the generic playbook dispatcher |
| SYS-Analysis Query | `8e33860b-3d63-f111-ab0c-70a8a53ec687` | the analysis-query handler |
| SYS-Working Document Edit | `d90f647e-5863-f111-ab0c-000d3a4d8152` | the working-document handler |
| SYS-Working Document Append Section | `3db0c084-5863-f111-ab0c-000d3a4d8152` | the working-document handler |
| SYS-Working Document Write Back | `ae580d84-5863-f111-ab0c-000d3a582930` | the working-document handler |

Post-change `read_query` verified all five rows `statecode = 1 (Inactive)` (transcript, 2026-07-06).

## 2. Seed mirror + script

- The five `infra/dataverse/sprk_analysistool-*.json` seed files for these rows were DELETED
  (grep-zero per NFR-08).
- `scripts/Seed-TypedHandlers.ps1` entries removed with do-not-re-seed tombstones (task-044 markers)
  so a future full re-seed cannot resurrect the rows.

## 3. No other Dataverse changes

No Binding (`sprk_playbookconsumer`) or Action (`sprk_analysisaction`) rows were touched.
The chat-summarize / summarize-file / document-profile Binding rows continue to serve their
consumers — only the EXECUTION code path behind them changed (server-side wrapper absorption).
