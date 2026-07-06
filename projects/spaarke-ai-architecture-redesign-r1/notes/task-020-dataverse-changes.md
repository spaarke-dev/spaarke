# Task 020 — Dataverse changes (FR-P1-01 chat-summarize catalog rows)

> **Date**: 2026-07-05 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **Idempotent**: yes (updates to pre-existing rows; re-runnable)

## 1. `sprk_analysisaction` — SUM-CHAT@v1 Action row (UPDATED)

| Column | Value |
|---|---|
| `sprk_analysisactionid` | `eeb05bfd-1260-f111-ab0b-70a8a59455f4` |
| `sprk_actioncode` | `SUM-CHAT@v1` (unchanged) |
| `sprk_name` | `Summarize Document for Chat` (unchanged) |
| `sprk_kind` | **100000000 (Prompted)** — NEW |
| `sprk_workflowclass` | null (prompted Action; explicitly cleared) |
| `sprk_modeltier` | **100000000 (Fast)** — NEW (gpt-4o-mini class per current deployment) |
| `sprk_inputschema` | **NEW** — typed args: `fileIds` (array, optional, ledger_resolution = session manifest / FR-08 default-all / NFR-02 cap 20, elicitation prompt) + `styleHint` (string, optional) |
| `sprk_systemprompt` | **REPLACED** with the SUM-CHAT@v1 JPS (`$schema: https://spaarke.com/schemas/prompt/v1`) — artifact: [`notes/jps/SUM-CHAT-v1.jps.json`](jps/SUM-CHAT-v1.jps.json); renders via PromptSchemaRenderer (JPS format detected; document text in `## Document`) |
| `sprk_outputschemajson` | UNCHANGED — pre-existing strict schema (declaration order `tldr → summary → keywords → entities`; keywords STRING; nested entities object) remains the constrained-decoding contract |

## 2. `sprk_playbookconsumer` — chat-summarize Binding row (UPDATED)

| Column | Value |
|---|---|
| `sprk_playbookconsumerid` | `651194cd-3670-f111-ab0e-70a8a590c51c` |
| `sprk_consumertype` / `sprk_consumercode` / `sprk_environment` / `sprk_priority` / `sprk_enabled` | `chat-summarize` / `default` / `*` / 500 / true (unchanged) |
| `sprk_action` | `eeb05bfd-1260-f111-ab0b-70a8a59455f4` → SUM-CHAT@v1 (unchanged) |
| `sprk_playbook` | **CLEARED (null)** — was `44285d15-1360-f111-ab0b-70a8a59455f4` (`summarize-document-for-chat@v1`); row is now pure-Linear post-migration state per the Binding contract; the engine fallback path was deleted in code (NFR-08 hard cutover) |
| `sprk_ucid` | **`UC-A-1`** — NEW |
| `sprk_disposition` | **100000000 (Informational)** — NEW |
| `sprk_risk` | **100000000 (None)** — NEW (read-only informational capability) |
| `sprk_capturemode` | **100000000 (Loop Elicitation)** — NEW |
| `sprk_surfaces` | **`assistant`** — NEW |
| `sprk_chiptransitions` | **`[]`** — NEW (no next-step chips seeded at P1; D4 chips arrive with later Bindings) |
| `sprk_tooldescription` | **NEW** — maker-editable intent surface for loop tool projection (P2) |

## 3. Verification

- Post-update `read_query` on both rows re-fetched and confirmed all values (transcript, task 020 execution 2026-07-05).
- Boot reconciliation (FR-P0-04 constants↔rows) unaffected: no consumer-type rows added/removed; `chat-summarize` remains 1 enabled row ↔ `ConsumerTypes.ChatSummarize`.
- The orphaned `summarize-document-for-chat@v1` playbook row + its nodes are NOT deleted here — playbook-table cleanup belongs to Track B / engine retirement (O-1), and task 073 owns stale seed/catalog files.
