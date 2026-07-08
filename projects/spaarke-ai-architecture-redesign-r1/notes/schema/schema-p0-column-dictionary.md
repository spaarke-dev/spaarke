# FR-P0-03 Column Dictionary — Catalog Schema Extensions (Task 003)

> **Deployed**: 2026-07-05 to `spaarkedev1.crm.dynamics.com` (published; verified by Web API attribute query + MCP re-describe)
> **Deployment script** (idempotent, re-runnable per environment): [`scripts/Deploy-AiCatalogSchemaExtensions.ps1`](../../../../scripts/Deploy-AiCatalogSchemaExtensions.ps1)
> **Governing semantics**: canonical doc `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` v0.4 §6.1–6.2; ADR-039 (closed catalogs)
> **Consumers**: task 004 (`ConsumerRoutingService` full Binding contract), 005, 008, 009

All columns are **additive**; no existing column was modified or dropped. All are optional (RequiredLevel=None) so existing rows remain valid. Choice values use the standard `100000000+` publisher range.

---

## Shared global option set

### `sprk_aimodeltier` (global option set — shared by `sprk_analysisaction.sprk_modeltier` and `sprk_playbookconsumer.sprk_modeltieroverride`)

| Value | Label | Meaning |
|---|---|---|
| 100000000 | Fast | cheap/fast models (e.g. gpt-4o-mini) — classification, validation, entity resolution |
| 100000001 | Standard | capable models (e.g. gpt-4o) — high-quality content generation |
| 100000002 | Reasoning | reasoning models (e.g. o-series) — complex multi-step planning |

**Type rationale**: tier vocabulary grounded in the three tiers documented on `ModelSelector.cs` (Services/Ai). A single GLOBAL set guarantees Action default and Binding override can never drift. Concrete deployment mapping (tier → model deployment name) stays in code/config per ADR-016 — the catalog stores intent, not deployment names.

---

## 1. `sprk_analysisaction` (Action — the execution unit) — 4 new columns

| Logical name | Type | Options / size | Purpose |
|---|---|---|---|
| `sprk_kind` | Choice (local) | Prompted (100000000), Coded (100000001) | Execution kind. Prompted (default; JPS prompt via ActionRunner) vs Coded (registered `ICodedWorkflow`). Canonical §6.1. Task 004 should treat null as Prompted. |
| `sprk_workflowclass` | String (200) | — | For `kind=Coded`: registered `ICodedWorkflow` class reference resolved by assembly-scan discovery (E-1). Length mirrors existing `sprk_handlerclass` NVARCHAR(200). |
| `sprk_inputschema` | Memo / multiline (100000) | JSON | Typed-argument schema: per arg `name`, `type`, `required`, `ledger_resolution`, elicitation prompt. JSON-in-memo so routing/loop deserializes cheaply (task-004 alignment). |
| `sprk_modeltier` | Choice (global `sprk_aimodeltier`) | Fast / Standard / Reasoning | Default model tier for this Action; overridable per Binding via `sprk_modeltieroverride`. Null = platform default. |

Pre-existing related column left untouched: `sprk_modeldeploymentid` (lookup → `sprk_aimodeldeployment`).

---

## 2. `sprk_playbookconsumer` (Binding — the invocation unit) — 9 new columns

| Logical name | Type | Options / size | Purpose |
|---|---|---|---|
| `sprk_ucid` | String (100) | e.g. `UC-A-1` | Ties the Binding to the §3 use-case vocabulary. |
| `sprk_tooldescription` | Memo / multiline (100000) | free text | Maker-editable intent surface the agent loop sees when this Binding is projected as a capability tool. Memo because makers may write a paragraph+ of intent guidance. |
| `sprk_disposition` | Choice (local) | Informational (100000000), Work Product (100000001), Overlay (100000002), Email (100000003), Record (100000004), Notification (100000005) | Output routing disposition consumed by the Output Router (P1-2). Values exactly per canonical §6.2 (`informational \| work_product \| overlay \| email \| record \| notification`). |
| `sprk_chiptransitions` | Memo / multiline (100000) | JSON `[{target_binding_id, chip_label}]` | Next-step chips (D4). JSON-in-memo per task-004 deserialization guidance. |
| `sprk_risk` | Choice (local) | None (100000000), Confirm When Uncertain (100000001), Always Confirm (100000002) | Confirmation-gate risk posture. Values per canonical §6.2. |
| `sprk_capturemode` | Choice (local) | Loop Elicitation (100000000), Modal (100000001) | Missing-required-args capture: loop elicitation (default; clarifying turns, OQ-3) vs modal escape hatch. Task 004: treat null as Loop Elicitation. |
| `sprk_oneventbindings` | Memo / multiline (100000) | JSON `[{event, order}]` | Event-path membership, e.g. `[{"event":"document_uploaded","order":2}]`. JSON-in-memo. |
| `sprk_surfaces` | String (400) | comma-separated tokens: `assistant`, `record-form`, `wizard`, `office`, `external-spa`, `scheduler`, `inbound-email` | Placement — which surfaces offer this Binding. Text (not multiselect choice) so shipping a new surface (a code-level event anyway, canonical §6.3) never requires a schema migration; token vocabulary is the §4.1 surface list. Empty = all surfaces. |
| `sprk_modeltieroverride` | Choice (global `sprk_aimodeltier`) | Fast / Standard / Reasoning | Per-Binding override of the Action's `sprk_modeltier`; null = use Action default. This is the "model override" column named in FR-P0-03/canonical §6.2. |

Pre-existing routing columns left untouched: `sprk_consumertype`, `sprk_consumercode`, `sprk_environment`, `sprk_priority`, `sprk_matchconditions`, `sprk_enabled`, `sprk_action` (FK), `sprk_playbook` (FK).

---

## 3. `sprk_analysistool` (Tool manifest) — 5 new columns (+1 already present)

| Logical name | Type | Options / size | Purpose |
|---|---|---|---|
| `sprk_toolid` | String (100) | **ALREADY EXISTED** — not created by this task | Full namespaced tool id (e.g. `dataverse.read_query`). Pre-existing NVARCHAR(100) satisfies the FR-P0-03 contract; no change needed. |
| `sprk_namespace` | String (100) | e.g. `dataverse`, `document` | Namespace segment; startup health check verifies row ↔ handler bijection (FR-P0-04). |
| `sprk_outputschema` | Memo / multiline (100000) | JSON schema | Tool output contract, used for grounding/citation enforcement. JSON-in-memo. |
| `sprk_sideeffectclass` | Choice (local) | Read (100000000), Write (100000001), Communicate (100000002), Pure (100000003) | Declared side-effect class driving the ONE confirmation gate (P2-2 gating is by this column). Values per canonical §6.2 (`read \| write \| communicate \| pure`). |
| `sprk_permissionscope` | String (200) | free token, e.g. `dataverse-user-context` | Permission scope required to project the tool into a loop turn (user-OBO enforced, NFR/MUST: user-OBO for all Dataverse tool access). |
| `sprk_budgetclass` | String (100) | named profile, e.g. `light` / `standard` / `heavy` | Named budget profile; per ADR-016 the actual limits (max tokens/docs/duration) are configured in code against this class name. String (not choice) because ADR-016 defines budgets as per-operation profiles with no fixed platform vocabulary. |

Pre-existing related columns left untouched: `sprk_handlerclass`, `sprk_jsonschema` (input schema), `sprk_toolcode`, `sprk_requiredcapability`, `sprk_configuration`.

---

## Type-choice principles applied (for downstream authors)

1. **Enumerations explicitly listed in canonical §6 → Choice** (`sprk_kind`, `sprk_disposition`, `sprk_risk`, `sprk_capturemode`, `sprk_sideeffectclass`). Option labels/values are 1:1 with the doc; no extra options invented (ADR-039 closed-catalog constraint).
2. **Structured payloads → JSON in Memo** (`sprk_inputschema`, `sprk_chiptransitions`, `sprk_oneventbindings`, `sprk_outputschema`) — per task-004 guidance that `ConsumerRoutingService` deserializes JSON from memo columns.
3. **Open vocabularies → String** (`sprk_ucid`, `sprk_surfaces`, `sprk_namespace`, `sprk_permissionscope`, `sprk_budgetclass`, `sprk_workflowclass`) — where the doc names no closed value set, a string avoids freezing an invented enum into schema.
4. **Cross-table shared enum → one GLOBAL option set** (`sprk_aimodeltier`) so default + override columns cannot drift.
5. **Everything optional** — additive-only; existing rows remain valid with nulls; code supplies defaults (Prompted / Loop Elicitation / platform model tier).

## Verification evidence

- Deployment script run 2026-07-05: 18 columns CREATED + 1 SKIP-exists (`sprk_toolid`), publish OK, script's Web API verification printed `OK` for all 19 contract columns.
- MCP `describe` re-run post-publish shows all columns with expected types on all three tables.
- First run failed mid-flight on global-optionset Name-binding (`GlobalOptionSet@odata.bind` requires MetadataId GUID, not Name key); fixed in script; idempotent re-run completed cleanly (3 SKIPs for the columns created before the failure).
