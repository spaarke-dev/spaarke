# Task 043 — Dataverse changes (FR-P3-04 Daily Briefing first full coded composite)

> **Date**: 2026-07-06 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **Idempotent**: yes (one create + one update + one create; re-runnable by GUID)

## 1. `sprk_analysisaction` — DAILY-BRIEFING@v1 coded Action row (CREATED)

| Column | Value |
|---|---|
| `sprk_analysisactionid` | **`2fa8ab19-7879-f111-ab0e-7ced8ddc4cc6`** |
| `sprk_actioncode` | `DAILY-BRIEFING@v1` |
| `sprk_name` | `Daily Briefing (Coded Composite)` |
| `sprk_kind` | **100000001 (Coded)** — the platform's FIRST full coded composite Action (ADR-039) |
| `sprk_workflowclass` | **`DailyBriefingNarrator`** — resolved via the task-007 `ICodedWorkflowRegistry` class-ref convention |
| `sprk_inputschema` | typed args: `briefingPayload` (object, required, ledger_resolution = system-supplied by `DailyBriefingCollector`; never user-elicited) |
| `sprk_systemprompt` | (none) — the composite's prompts stay hot-editable in the EXISTING `BRIEF-NARRATE-TLDR` / `BRIEF-NARRATE-CHANNEL` Action rows read by the narrator at runtime |
| `sprk_description` | FR-P3-04 provenance + dispatch statement |

Seed JSON (row shape for re-creation in another environment):

```json
{
  "sprk_name": "Daily Briefing (Coded Composite)",
  "sprk_actioncode": "DAILY-BRIEFING@v1",
  "sprk_kind": 100000001,
  "sprk_workflowclass": "DailyBriefingNarrator",
  "sprk_inputschema": "{\"args\":[{\"name\":\"briefingPayload\",\"type\":\"object\",\"required\":true,\"ledger_resolution\":\"system-supplied — DailyBriefingCollector live-queries the acting user's 6 entity channels; never user-elicited\",\"elicitation\":null}]}"
}
```

## 2. `sprk_playbookconsumer` — daily-briefing-narrate `default` Binding row (UPDATED — the cutover)

| Column | Value |
|---|---|
| `sprk_playbookconsumerid` | `b4503359-1771-f111-ab0e-7ced8ddc4a05` (pre-existing R4 row) |
| `sprk_consumertype` / `sprk_consumercode` / `sprk_environment` / `sprk_priority` / `sprk_enabled` | `daily-briefing-narrate` / `default` / `*` / 500 / true (unchanged) |
| `sprk_action` | **`2fa8ab19-7879-f111-ab0e-7ced8ddc4cc6`** → DAILY-BRIEFING@v1 — NEW |
| `sprk_playbook` | **CLEARED (null)** — was `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` (`DAILY-BRIEFING-NARRATE` playbook); the engine path is deleted in code (NFR-08 hard cutover) |
| `sprk_ucid` | **`UC-D-1`** — NEW |
| `sprk_disposition` | **100000000 (Informational)** — render leg (widget) |
| `sprk_risk` | **100000000 (None)** — read-only informational capability |
| `sprk_capturemode` | **100000000 (Loop Elicitation)** |
| `sprk_surfaces` | empty (all surfaces) |
| `sprk_chiptransitions` | `[]` |
| `sprk_tooldescription` | maker-editable intent surface (briefing / daily update / what changed since yesterday) |

## 3. `sprk_playbookconsumer` — daily-briefing-narrate `email` Binding row (CREATED)

| Column | Value |
|---|---|
| `sprk_playbookconsumerid` | **`800cc81f-7879-f111-ab0e-7ced8ddc4cc6`** |
| `sprk_name` | `Daily Briefing - email leg (coded composite)` |
| `sprk_consumertype` / `sprk_consumercode` / `sprk_environment` / `sprk_priority` / `sprk_enabled` | `daily-briefing-narrate` / **`email`** / `*` / 500 / true |
| `sprk_action` | `2fa8ab19-7879-f111-ab0e-7ced8ddc4cc6` → DAILY-BRIEFING@v1 (same coded Action — one workflow, two dispositions) |
| `sprk_ucid` | `UC-D-1` |
| `sprk_disposition` | **100000003 (Email)** — routed by `OutputRouter` to the Communication (Email) service after the ledger write |
| `sprk_risk` / `sprk_capturemode` | 100000000 / 100000000 |
| `sprk_surfaces` | **`scheduler`** — NOT offered to the chat loop |
| `sprk_oneventbindings` | **`[{"event":"briefing_scheduled","order":1}]`** — the declarative scheduled trigger; the scheduler invokes `POST /api/ai/daily-briefing/email` per user at the per-user time |
| `sprk_chiptransitions` | `[]` |

Seed JSON (email row):

```json
{
  "sprk_name": "Daily Briefing - email leg (coded composite)",
  "sprk_consumertype": "daily-briefing-narrate",
  "sprk_consumercode": "email",
  "sprk_environment": "*",
  "sprk_priority": 500,
  "sprk_enabled": true,
  "sprk_action@lookup": "sprk_analysisaction: DAILY-BRIEFING@v1",
  "sprk_ucid": "UC-D-1",
  "sprk_disposition": 100000003,
  "sprk_risk": 100000000,
  "sprk_capturemode": 100000000,
  "sprk_surfaces": "scheduler",
  "sprk_chiptransitions": "[]",
  "sprk_oneventbindings": "[{\"event\":\"briefing_scheduled\",\"order\":1}]"
}
```

## 4. Verification

- Post-change `read_query` on `sprk_playbookconsumer WHERE sprk_consumertype='daily-briefing-narrate'` returned exactly the 2 rows above: `default` → Informational, `email` → Email, both `sprk_action = 2fa8ab19-…`, default row's `sprk_playbook` cleared (transcript, task 043 execution 2026-07-06).
- Boot reconciliation (FR-P0-04 constants↔rows): unaffected — no consumer TYPE added/removed; `daily-briefing-narrate` remains in `ConsumerTypes.All` (a second consumerCode row does not change type parity).
- The orphaned `DAILY-BRIEFING-NARRATE` playbook row (`7b5a6ed3-0271-f111-ab0e-000d3a13a4cd`) + its nodes are NOT deleted here — playbook-table cleanup belongs to Track B / engine retirement; `Seed-PlaybookConsumers.ps1` regeneration belongs to FR-P4-02.
- **Note for task 044 + chat loop**: `SessionDispatchOrchestrator` intentionally REJECTS non-prompted kinds (`dispatch.action-kind-unsupported`), so a chat-loop text dispatch of the (now coded) default Binding refuses cleanly rather than crashing. Loop-side coded execution is a later catalog addition; the briefing's product surfaces are the widget (/render, /narrate) and the scheduler (/email).
