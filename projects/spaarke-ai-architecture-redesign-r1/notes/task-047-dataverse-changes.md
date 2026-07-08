# Task 047 — Dataverse changes (FR-P3-08 work-product record persistence)

> **Date**: 2026-07-06 · **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **Idempotent**: yes (one column create + two row creates; re-runnable by GUID / logical name)

## 1. `sprk_matter.sprk_mattersummary` — NEW memo column (CREATED + PUBLISHED)

| Property | Value |
|---|---|
| Logical name | `sprk_mattersummary` |
| Type | Memo (MULTILINE TEXT), MaxLength 1,048,576, Format TextArea, RequiredLevel None |
| Display name | `Matter Summary (AI Work Product)` |
| Description | Record-persisted AI work-product envelope (`work-product-envelope-v1` JSON) written by the BFF OutputRouter work_product disposition leg via the `sprk_aitopicregistry` target mapping (topic `matter-summary`). |
| Created via | Web API `POST EntityDefinitions(LogicalName='sprk_matter')/Attributes` (204) + `PublishXml` (204); verified `AttributeType=Memo` |

**CLAUDE.md §11 justification (new host-data slot)**: (1) *Existing* — all 7 existing MULTILINE TEXT columns on `sprk_matter` are owner-rendered or feature-mapped (grep evidence: `sprk_recordsummary` rendered as plain text by MatterHeader PCF + record-header hooks; `sprk_performancesummary` owned by the widgets-r1 `matter-health` topic envelope; `sprk_searchprofile` written by `DocumentProfileFieldMapper`/observation mirror; `sprk_matterdescription` user-authored; `sprk_financialsummary`/`sprk_monthlyspendtimeline`/`sprk_tasksummary` finance/VisualHost surfaces). (2) *Extension* — reusing any would corrupt its owner's contract (JSON in a text-rendered header field, or clobbering the matter-health envelope). (3) *Cost-of-doing-nothing* — the `matter-summary` work product has no target column on the flagship host entity; FR-P3-08 acceptance ("envelope on host record") fails. One target field per topic is the DESIGNED maker workflow of the shipped topic-registry pattern (`sprk_targetfield` is ApplicationRequired and topic-specific — widgets-r1 schema design §3.1 row 7). This is host-record DATA schema, not a manifest table — the "no new manifest tables" rule is untouched (declaration reuses existing Binding + registry columns only).

## 2. `sprk_playbookconsumer` — chat-summarize `matter-summary` Binding row (CREATED)

| Column | Value |
|---|---|
| `sprk_playbookconsumerid` | **`05618e5d-ab79-f111-ab0e-7ced8ddc4cc6`** |
| `sprk_name` | `Matter Summary - work product leg (chat-summarize/matter-summary)` |
| `sprk_consumertype` / `sprk_consumercode` / `sprk_environment` / `sprk_priority` / `sprk_enabled` | `chat-summarize` / **`matter-summary`** / `*` / 500 / true |
| `sprk_action` | `eeb05bfd-1260-f111-ab0b-70a8a59455f4` → SUM-CHAT@v1 (the SAME prompted Action as the `default` row — canonical §3.10.3: one UC, two Consumers, two dispositions; the "matter-summary-artifact" pattern) |
| `sprk_ucid` | `UC-A-1` |
| `sprk_disposition` | **100000001 (Work Product)** — routed by `OutputRouter` to `TopicRegistryWorkProductPersister` after the ledger write |
| `sprk_risk` | 100000000 (None) — see gating note below |
| `sprk_capturemode` | 100000000 (Loop Elicitation) |
| `sprk_surfaces` | `assistant` (host-context-carrying assistant sessions; a session without HostContext fails the persistence LOUDLY after the ledger store) |
| `sprk_chiptransitions` | `[]` |
| `sprk_tooldescription` | loop intent surface ("save/persist the summary to the matter"; steers plain summaries to the default capability) |

**Gating note (FR-P2-02)**: the persistence write is performed by the ROUTER (not a tool-plane write), so the Binding-level `sprk_risk` is its declared gate surface. It is declared `None` matching the shipped widgets-r1 behavior (registry-declared persistence to the session's OWN host record, single field, user-OBO, explicit user intent) and the 041/042 precedent (risk None on the capability; gates fire on tool-plane writes). If the operator wants this leg confirmation-gated, it is catalog-data-only: flip `sprk_risk` — no code change.

Seed JSON (row shape for re-creation in another environment):

```json
{
  "sprk_name": "Matter Summary - work product leg (chat-summarize/matter-summary)",
  "sprk_consumertype": "chat-summarize",
  "sprk_consumercode": "matter-summary",
  "sprk_environment": "*",
  "sprk_priority": 500,
  "sprk_enabled": true,
  "sprk_action@lookup": "sprk_analysisaction: SUM-CHAT@v1",
  "sprk_ucid": "UC-A-1",
  "sprk_disposition": 100000001,
  "sprk_risk": 100000000,
  "sprk_capturemode": 100000000,
  "sprk_surfaces": "assistant",
  "sprk_chiptransitions": "[]",
  "sprk_tooldescription": "Summarize the session's documents AND save the summary onto the current matter record as a durable work product. Use when the user asks to save, persist, or file a summary to the matter (e.g. 'summarize this and save it to the matter', 'put the summary on the matter record'). Requires a matter-hosted session (the assistant embedded on a matter form); the summary is stored to the session ledger first, then persisted to the matter's Matter Summary (AI Work Product) field. For a chat-only summary that is NOT saved to the record, use the plain summarize capability instead. Optional arg: fileIds (subset of session file ids; omit to use all)."
}
```

## 3. `sprk_aitopicregistry` — `matter-summary/single` target-mapping row (CREATED)

| Column | Value |
|---|---|
| `sprk_aitopicregistryid` | **`cfca6a65-ab79-f111-ab0e-7ced8ddc4cc6`** |
| `sprk_name` | `matter-summary/single` |
| `sprk_topicname` | **`matter-summary`** — joins to the Binding's capability code (`sprk_consumercode`, falling back to `sprk_consumertype` for `default`-code rows) per the `TopicRegistryWorkProductPersister` declaration contract |
| `sprk_mode` | `single` |
| `sprk_playbookname` | `chat-summarize` — informational for this leg (Action-target capability, no playbook; Q-U1 `@vN` ban respected); the generalized persister never reads it |
| `sprk_displayname` / `sprk_icon` | `Matter Summary` / `Sparkle24Filled` |
| `sprk_hostentity` / `sprk_targetfield` | **`sprk_matter` / `sprk_mattersummary`** — THE target mapping |
| `sprk_cachettlminutes` / `sprk_enabled` | 60 / true |

Seed JSON:

```json
{
  "sprk_name": "matter-summary/single",
  "sprk_topicname": "matter-summary",
  "sprk_mode": "single",
  "sprk_playbookname": "chat-summarize",
  "sprk_displayname": "Matter Summary",
  "sprk_icon": "Sparkle24Filled",
  "sprk_hostentity": "sprk_matter",
  "sprk_targetfield": "sprk_mattersummary",
  "sprk_cachettlminutes": 60,
  "sprk_enabled": true
}
```

## 4. Verification

- Post-change `read_query` on `sprk_playbookconsumer WHERE sprk_consumertype='chat-summarize'` returned exactly 2 rows: `default` → Informational, `matter-summary` → **Work Product**, both `sprk_action = eeb05bfd-…` (transcript, task 047 execution 2026-07-06).
- Post-change `read_query` on `sprk_aitopicregistry WHERE sprk_topicname='matter-summary'` returned the mapping row (`sprk_matter`/`sprk_mattersummary`, enabled).
- `EntityDefinitions(...)/Attributes(LogicalName='sprk_mattersummary')` → `AttributeType=Memo` after PublishXml.
- Boot reconciliation (FR-P0-04 constants↔rows): unaffected — no consumer TYPE added; `chat-summarize` remains in `ConsumerTypes.All` (second consumerCode row does not change type parity — same as the 043 `daily-briefing-narrate/email` precedent).
- `Seed-PlaybookConsumers.ps1` regeneration remains FR-P4-02 (task 051) per the standing decision.
