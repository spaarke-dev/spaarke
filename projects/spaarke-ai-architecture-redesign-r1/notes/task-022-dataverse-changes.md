# Task 022 — Dataverse changes (FR-P1-03 Event path catalog rows)

> **Date**: 2026-07-05 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **Idempotent**: yes (existence checked by `sprk_actioncode` / `sprk_consumertype` query before create; re-running updates in place)

## 1. `sprk_analysisaction` — CLS-CHAT@v1 Action row (CREATED)

| Column | Value |
|---|---|
| `sprk_analysisactionid` | `186fd4cf-db78-f111-ab0e-7ced8ddc4cc6` |
| `sprk_actioncode` | `CLS-CHAT@v1` |
| `sprk_name` | `Classify Document for Chat` |
| `sprk_kind` | 100000000 (Prompted) |
| `sprk_modeltier` | 100000000 (Fast) |
| `sprk_systemprompt` | CLS-CHAT@v1 JPS (`$schema: https://spaarke.com/schemas/prompt/v1`) — artifact: [`notes/jps/CLS-CHAT-v1.jps.json`](jps/CLS-CHAT-v1.jps.json); renders via PromptSchemaRenderer (document text in `## Document`) |
| `sprk_outputschemajson` | Strict draft-07 schema, declaration order `docType → confidence → rationale`, `additionalProperties:false`; `confidence` bounded [0,1] — the M4 policy input |
| `sprk_inputschema` | `fileId` (string, optional; ledger_resolution = session manifest top-1 per bulk bound) |

## 2. `sprk_playbookconsumer` — chat-classify Binding row (CREATED)

| Column | Value |
|---|---|
| `sprk_playbookconsumerid` | `5f3898d8-db78-f111-ab0e-7ced8ddc4cc6` |
| `sprk_consumertype` / `sprk_consumercode` / `sprk_environment` / `sprk_priority` / `sprk_enabled` | `chat-classify` / `default` / `*` / 500 / true |
| `sprk_action` | `186fd4cf-db78-f111-ab0e-7ced8ddc4cc6` → CLS-CHAT@v1 |
| `sprk_ucid` | `UC-A-7` |
| `sprk_disposition` | 100000000 (Informational) |
| `sprk_risk` | 100000000 (None) |
| `sprk_capturemode` | 100000000 (Loop Elicitation) |
| `sprk_surfaces` | `assistant` |
| `sprk_chiptransitions` | `[]` |
| **`sprk_oneventbindings`** | **`[{"event":"document_uploaded","order":1}]`** — event-rule member order 1 |
| `sprk_tooldescription` | classify-uploaded-document intent surface (P2 loop projection) |

## 3. `sprk_playbookconsumer` — chat-summarize Binding row (UPDATED)

Row `651194cd-3670-f111-ab0e-70a8a590c51c` (UC-A-1, from task 020):

| Column | New value |
|---|---|
| **`sprk_oneventbindings`** | **`[{"event":"document_uploaded","order":2}]`** — event-rule member order 2 (was null) |
| `sprk_chiptransitions` | `[{"target_binding_id":"651194cd-3670-f111-ab0e-70a8a590c51c","chip_label":"Summarize again"}]` (was `[]`) — a real, executable P1 chip so the FR-P1-03 acceptance "…+ chips" renders from Binding data; D4 chips to other capabilities arrive as their Bindings land |

## 4. FR-P0-04 constants ↔ rows parity

`ConsumerTypes.ChatClassify = "chat-classify"` added (+ `All` list) so the boot
reconciliation stays green: `chat-classify` = 1 enabled row ↔ 1 constant.

## 5. Verification

Post-write `read_query` re-fetched both `sprk_playbookconsumer` rows and confirmed
`sprk_oneventbindings` + `sprk_chiptransitions` values (transcript, task 022 execution
2026-07-05). The rule is now fully declarative: `document_uploaded → [chat-classify(1),
chat-summarize(2)]` exists ONLY in the Binding table (ADR-039).
