# Orphan Verification — DAILY-BRIEFING-NARRATE + spaarke-playbook-embeddings (design §10 row 12)

> **Task**: AIR2-076 (Phase H — G-R2-D Hardening) · **FR**: FR-D-07 · **Author**: task-execute (STANDARD rigor)
> **Basis**: This verification is CONTINGENT on and grounded in task 001's recorded r1 P4-close disposition
> (`projects/spaarke-ai-architecture-redesign-r2/notes/r1-p4-reconciliation.md`, Row 12, lines 96-107). Task 001
> found: DAILY-BRIEFING-NARRATE independently **verified-closed**; `spaarke-playbook-embeddings` was the
> **in-scope-FR residual** — zero code consumers since r1 task 035, but the live Azure AI Search index had not yet
> been physically deleted (an ops-only action r1 registered as out-of-task-boundary, item O-1). Task 076 re-verifies
> both with fresh, independent evidence per NFR-13 (grep-zero / catalog-absence).

---

## Verdict

**Both named orphans are CONFIRMED CLOSED as of this verification (2026-07-10).** No residual remains open. No
cleanup was needed or performed by this task (read-only verification only).

| Orphan | Task 001 state (2026-07-08/09) | This verification (2026-07-10) | Evidence |
|---|---|---|---|
| DAILY-BRIEFING-NARRATE playbook | verified-closed (Inactive) | **RE-CONFIRMED closed** | Fresh Dataverse read, spaarkedev1 |
| `spaarke-playbook-embeddings` AI Search index | residual — zero code consumers, index not yet deleted | **NOW CLOSED** — index physically deleted since task 001's recording | Direct Azure AI Search REST call: 404 |

---

## 1. DAILY-BRIEFING-NARRATE — CLOSED (re-confirmed)

**Evidence (fresh, live Dataverse query on spaarkedev1, 2026-07-10):**

```sql
SELECT sprk_analysisplaybookid, sprk_name, statecode, statuscode, modifiedon
FROM sprk_analysisplaybook
WHERE sprk_analysisplaybookid = '7b5a6ed3-0271-f111-ab0e-000d3a13a4cd'
```

Result:
```json
{
  "sprk_analysisplaybookid": "7b5a6ed3-0271-f111-ab0e-000d3a13a4cd",
  "sprk_name": "Daily Briefing Narrate",
  "statecode": 1,
  "statuscode": 2,
  "modifiedon": "2026-07-07T23:36:36",
  "statecodename": "Inactive",
  "statuscodename": "Inactive"
}
```

This matches task 001's cited evidence (r1 Track-B completion audit §9: "read Active(0/1) → deactivated → re-read
Inactive(1/2)") — the record is still Inactive with the same `modifiedon` timestamp (2026-07-07), confirming no
regression (nothing re-activated it between task 001's recording and now). **Catalog-absence criterion satisfied**:
the playbook is deactivated on the live catalog, not deleted, per r1's chosen disposition (RETIRE-data, deactivate
rather than hard-delete) — this is the correct closure shape for a Dataverse row per r1's own audit.

---

## 2. `spaarke-playbook-embeddings` — CLOSED (residual resolved since task 001)

### 2a. Code-side grep-zero (src/) — re-confirmed clean

```
rg "spaarke-playbook-embeddings|playbook-embeddings" src/
```

9 hits, all in `src/server/api/Sprk.Bff.Api/`, **all are comments referencing the historical r1 task 035 / FR-P2-06
deletion** — no live writer, reader, handler, or job references the index:

- `Services/Ai/PlaybookService.cs:45,487`
- `Services/Ai/NullPlaybookService.cs:142`
- `Services/Ai/IPlaybookService.cs:156`
- `Models/Ai/PlaybookDto.cs:128`
- `Infrastructure/DI/RateLimitingModule.cs:228`
- `Infrastructure/DI/AiModule.cs:24,255,258`

This reconfirms r1's own task-035 grep-zero finding (cited in task 001's note) — no regression.

### 2b. Live Azure AI Search index — CONFIRMED DELETED (closes the residual)

Direct query against the AI Search service (`spaarke-search-dev`, per `config/spaarke-resources.yaml:221-222`):

```
GET https://spaarke-search-dev.search.windows.net/indexes?api-version=2023-11-01&$select=name
```

Returns 7 indexes — `spaarke-playbook-embeddings` is **absent**:
```
spaarke-discovery-index
spaarke-files-index
spaarke-insights-index
spaarke-invoices-index
spaarke-rag-references
spaarke-records-index
spaarke-session-files
```

Direct per-index lookup confirms deletion (not just omission from a paged list):
```
GET https://spaarke-search-dev.search.windows.net/indexes/spaarke-playbook-embeddings?api-version=2023-11-01
→ 404: "No index with the name 'spaarke-playbook-embeddings' was found in the service 'spaarke-search-dev'."
```

This matches `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §4, which already documents "Active Index Catalog
(7 indexes)" with an explicit note: *"`spaarke-playbook-embeddings` (former #7) was RETIRED by
`spaarke-ai-architecture-redesign-r1` task 035 / FR-P2-06 ... delete the live Azure index during the P4 sweep."*
The P4 sweep's ops action (O-1, registered by r1 as out-of-task-boundary/operator-executed) has since been carried
out — the index is gone. **The residual that task 001 recorded as still-open is now closed.**

---

## Ancillary finding (NOT a cleanup performed here — recorded for disposition only)

`config/spaarke-resources.yaml:230-276` still lists `spaarke-playbook-embeddings` under the comment heading
"Canonical 8-index catalog (post `spaarke-ai-azure-setup-dev-r1` Phase 5)" with a stale `doc_count_2026_06_26: 34`
entry. This is **documentation drift**, not a functional orphan — the manifest's own sibling document
(`docs/architecture/AI-SEARCH-INDEX-CATALOG.md`) already correctly reflects the 7-index post-retirement state, and
the live resource is confirmed gone (§2b above). Per this task's read-only-verification constraint, no edit was
made. **Disposition recommendation**: a low-priority follow-up (e.g. via `/doc-drift-audit` or the next task
touching `config/spaarke-resources.yaml`) should either move the `spaarke-playbook-embeddings` entry to a
`_meta: {status: retired, retired: 2026-0X-XX}` block (per the manifest's own documented lifecycle convention,
lines 10-19) or delete the entry outright, and update the "8-index catalog" heading comment to "7-index catalog."
This does not block FR-D-07 closure — it is a separate, much lower-severity doc-hygiene item.

---

## Disposition summary

| Item | Status | Evidence standard met (NFR-13) |
|---|---|---|
| DAILY-BRIEFING-NARRATE | **CLOSED** (re-confirmed) | ✅ Live Dataverse read, spaarkedev1 |
| `spaarke-playbook-embeddings` | **CLOSED** (residual resolved) | ✅ Grep-zero (code) + live Azure AI Search 404 (index) |

**Row 12 (design §10) is now fully closed.** No further action needed for FR-D-07. The one ancillary
documentation-drift item (`config/spaarke-resources.yaml` stale entry) is noted above as a recommendation, not
a blocking residual.
