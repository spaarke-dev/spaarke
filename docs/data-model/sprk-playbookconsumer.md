# Data Model — `sprk_playbookconsumer` (Binding — the invocation unit)

> **Last Updated**: 2026-07-07
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03) — full refresh; pre-redesign version superseded
> **Status**: Current
> **Schema source**: live `spaarkedev1` describe + full-table `read_query` (2026-07-07) cross-checked against shipped code (`ConsumerRoutingService.cs`, `Binding.cs`, `BindingCapabilityTool.cs`)
> **Canonical architecture**: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §6.2 / §6.4
> **Naming note**: the table's product name is now **Binding**; the logical name `sprk_playbookconsumer` (and this file's name, which other docs link to) is historical.

---

## 1. Purpose — THE single routing surface

One Binding row maps an **invocation context** (consumer type + code + environment + surfaces + events) to its **execution unit** (an Action, or — legacy — a frozen-engine playbook), plus everything the platform needs to *offer, gate, route, and chain* that capability.

**Single-routing-surface rule (canonical §6.4, ADR-039 — BINDING)**: this table is the ONLY answer to "which capability runs". The former `LinearConsumers` appsettings maps, `Workspace.*PlaybookId` fallbacks, and `Insights.Playbooks.Map` config keys all migrated to rows and were deleted (P3 task 040). `ConsumerTypes.cs` remains compile-time constants only, boot-reconciled against rows by `RoutingConsumerTypeHealthCheck` (drift → `/healthz` Unhealthy). Do NOT introduce a second routing contract or side-channel routing config.

Every redesign consumer reads this one contract: the Event path reads `sprk_oneventbindings`; the Output Router reads `sprk_disposition`; loop tool-projection reads `sprk_tooldescription` + `sprk_surfaces`; the confirmation gate reads `sprk_risk`; chips carry Binding ids via `sprk_chiptransitions`.

**Cardinality**: one Action, many Bindings — e.g. `daily-briefing-narrate` has a `default` (Informational/widget) row and an `email` (Email/scheduler) row targeting the SAME coded Action.

---

## 2. Schema

### 2.1 Entity metadata

| Property | Value |
|---|---|
| **Logical name** | `sprk_playbookconsumer` |
| **Collection name** | `sprk_playbookconsumers` |
| **Primary key** | `sprk_playbookconsumerid` (GUID) — this GUID **is the Binding's identity**: ledger outputs key on `{bindingId}@t{n}` (ADR-040), chips dispatch by it, `GetBindingByIdAsync` resolves it |
| **Primary name column** | `sprk_name` (NVARCHAR(850), required) |
| **Ownership** | Organization (`organizationid` — infrastructure routing rows, not user-owned) |
| **State** | Standard `statecode`/`statuscode`; use `sprk_enabled` for soft-disable |

### 2.2 Columns (live spaarkedev1 schema, 2026-07-07)

Identity + resolution columns (Phase 1R originals):

| Logical name | Type | Meaning |
|---|---|---|
| `sprk_playbookconsumerid` | GUID (PK) | The Binding id (see above). |
| `sprk_name` | NVARCHAR(850), required | Display name. |
| `sprk_consumertype` | NVARCHAR(250) | Stable consumer-type code (lower-kebab-case, e.g. `chat-summarize`, `create-task`). Must match a constant in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs); boot-reconciled both directions. |
| `sprk_consumercode` | NVARCHAR(100) | Sub-discriminator. Resolution prefers exact match, falls back to `default`; null/empty is treated as `default`. |
| `sprk_environment` | NVARCHAR(100) | `dev` \| `test` \| `prod` \| `*` (wildcard). Specific beats wildcard. |
| `sprk_priority` | Int | Tiebreaker — **lowest wins**. Null → 500. Convention: named canonical rows 400, `default` alias 500, overrides 100, fallback 900. |
| `sprk_enabled` | Boolean | `false` rows are invisible to every resolve path. Soft-disable without deletion. |
| `sprk_matchconditions` | Memo (JSON) | Optional flat `{key: string \| string[]}` predicate against `IRoutingContext` (`mimeType`, `documentType`). Null/empty/`{}` always matches; malformed JSON **fails closed**; unknown keys fail the match (context doesn't expose them). |
| `sprk_playbook` | Lookup → `sprk_analysisplaybook` | Legacy frozen-engine target. Null on Action-target rows. |
| `sprk_action` | Lookup → `sprk_analysisaction` | The execution-unit target (canonical). Null on pure-engine legacy rows. Rows with NEITHER target are admin errors — skipped by resolution. |

FR-P0-03 Binding-contract columns (task 003 schema extension; all optional — pre-extension rows carry nulls and map to the documented safe defaults, never throw):

| Logical name | Type | Meaning | Null default |
|---|---|---|---|
| `sprk_ucid` | NVARCHAR(100) | Use-case id tying the row to the canonical §3 vocabulary (e.g. `UC-A-1`, `UC-D-1`, `L4-REFUSAL`). | null |
| `sprk_tooldescription` | Memo | **The intent surface the agent loop sees** — see §3.1. Non-empty = the maker's explicit opt-in to text-path projection. | null = not loop-projected |
| `sprk_disposition` | Choice | Output routing: `Informational` (100000000) \| `Work Product` (100000001) \| `Overlay` (100000002) \| `Email` (100000003) \| `Record` (100000004) \| `Notification` (100000005). See §4. | Informational |
| `sprk_chiptransitions` | Memo (JSON) | Curated next-step chips — see §3.2. **Chips carry Binding ids** (D4). | `[]` |
| `sprk_risk` | Choice | Confirmation-gate posture: `None` (100000000) \| `Confirm When Uncertain` (100000001) \| `Always Confirm` (100000002). Complements (does not replace) tool-level `side_effect_class` gating — the ONE gate. | None |
| `sprk_capturemode` | Choice | Missing-required-args capture: `Loop Elicitation` (100000000) \| `Modal` (100000001, routes to wizard via the `elicitation_modal` SSE event). | Loop Elicitation |
| `sprk_oneventbindings` | Memo (JSON) | Event-path memberships — see §3.3. | `[]` |
| `sprk_surfaces` | NVARCHAR(400) | Comma-separated placement tokens (§4.1 vocabulary: `assistant`, `record-form`, `wizard`, `office`, `external-spa`, `scheduler`, `inbound-email`). **Empty = offered on ALL surfaces.** | all surfaces |
| `sprk_modeltieroverride` | Choice | `Fast` (100000000) \| `Standard` (100000001) \| `Reasoning` (100000002). Per-Binding override; wins over the Action's `sprk_modeltier` (`Binding.EffectiveModelTier`). | use Action default |

> **Corrections vs the pre-redesign version of this doc** (verified against live schema 2026-07-07): the playbook lookup's logical name is **`sprk_playbook`** (not `sprk_playbookid`); the match-conditions column is **`sprk_matchconditions`** (not `sprk_matchconditionsjson`); the primary name column is **`sprk_name`** (not `sprk_consumertype`); `sprk_environment` is NVARCHAR(100) (not 50).

### 2.3 Relationships

| Direction | Related entity | Via | Behavior |
|---|---|---|---|
| Many-to-One | `sprk_analysisaction` | `sprk_action` | The execution unit. `ConsumerRoutingService` left-outer-joins it on every resolve to project `sprk_kind` / `sprk_workflowclass` / `sprk_inputschema` / `sprk_modeltier` into the `Binding` record. |
| Many-to-One | `sprk_analysisplaybook` | `sprk_playbook` | Legacy engine target (Insights family; ai-summary/document-profile/pre-fill rows). |

No alternate keys — uniqueness is operational (resolution picks the best match); multiple rows per `(type, code, environment)` are valid override-with-fallback patterns.

---

## 3. JSON field contracts

### 3.1 `sprk_tooldescription` — the maker-editable steering surface

When non-empty, `BindingCapabilityTool` projects the row into the agent loop as function `capability_{consumer-type}` whose **description is this column verbatim** and whose parameter schema is the target Action's `sprk_inputschema`. There is no dispatcher, classifier, or trigger-phrase index — capability tool *descriptions* ARE the text-path intent surface (ADR-039), and editing this column changes model behavior with **zero deploy**.

Proven in production tuning: G-P3 UAT rounds 2 and 3 (2026-07-07) fixed model misbehavior *purely by editing catalog text* — round 2 added the ASSIGNEE RULE to the create-task Binding's `sprk_tooldescription` (stop composing unresolvable assignee lookups); round 3 appended the POST-CONFIRMATION RULE ("ask for confirmation in chat AT MOST ONCE… IMMEDIATELY invoke dataverse.create_record… do NOT re-invoke this capability"). See `projects/spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round2-findings.md` / `-round3-findings.md`.

Routing regressions are caught by the golden-utterance eval suite in CI (`golden-utterances.json`), not threshold tuning.

### 3.2 `sprk_chiptransitions` — curated next-step chips (D4)

```json
[
  {
    "target_binding_id": "05618e5d-ab79-f111-ab0e-7ced8ddc4cc6",
    "chip_label": "Summarize this document",
    "bulk_chip_label": "Summarize",
    "requires_attachments": true,
    "prefill_slots": { "styleHint": "brief" }
  }
]
```

| Member | Meaning |
|---|---|
| `target_binding_id` | The Binding id the chip dispatches (Click path: `invoke(binding_id, args)` → `SessionDispatchOrchestrator` → same executor stack as text). |
| `chip_label` | Rendered label. |
| `bulk_chip_label` | Optional SHORT verb for server-derived composite labels ("{bulk} all N files?"); falls back to `chip_label`'s first token. |
| `requires_attachments` | Client disables the chip at zero session attachments. |
| `prefill_slots` | Pre-filled capability args forwarded verbatim as the chip's `args`. |

Malformed maker JSON degrades to an empty list — routing never throws.

### 3.3 `sprk_oneventbindings` — Event-path membership

```json
[{ "event": "briefing_scheduled", "order": 1 }]
```

Membership in the closed platform event vocabulary (`document_uploaded`, `matter_form_opened`, `session_started_with_context`, `inbound_email_routed`, `schedule:{name}`, …); `order` sequences members within an event's composite (lower runs first). Resolved by `ResolveEventBindingsAsync` (Event Rules service reads this — rules are data, not code).

---

## 4. Dispositions and their shipped Output Router legs

`sprk_disposition` is the ONLY rendering/routing contract for a capability's output. `OutputRouter` writes the ledger entry FIRST (ADR-040 storage-precedes-rendering), then routes by disposition:

| Disposition | Shipped leg (2026-07-07) |
|---|---|
| `informational` | ✅ Rendered to the Assistant pane from the stored ledger entry (platform default; the terminal chunk renders FROM the store). |
| `work_product` | ✅ `TopicRegistryWorkProductPersister` (task 047): persists the `work-product-envelope-v1` JSON to the host record field declared by the capability's `sprk_aitopicregistry` row (topic = ConsumerCode, e.g. `matter-summary` → `sprk_matter.sprk_mattersummary`). Store-first, idempotent single-field PATCH, user-OBO, loud failures. |
| `email` | ✅ `IEmailDispositionSender` leg (task 043): routes to the Communication (Email) service — Daily Briefing `email` row is the shipped instance (DRAFT-only posture for chat drafting). |
| `overlay` | ⏳ Not yet dispatchable — `SessionDispatchOrchestrator` 422s `dispatch.disposition-not-supported`. |
| `record` | ⏳ Not yet dispatchable (same 422). |
| `notification` | ⏳ Not yet dispatchable (same 422). |

---

## 5. Resolution (how a row is picked)

[`ConsumerRoutingService`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs) queries enabled rows by `sprk_consumertype` (left-joining the Action), then in memory: filter by consumer-code (exact or `default`), environment (exact or wildcard), and match-conditions; pick lowest `sprk_priority`; tiebreak specific-code > `default`, specific-env > `*`. Routing **never throws to the consumer** — failures graceful-degrade to null/empty. Resolve surface:

| Method | Returns |
|---|---|
| `ResolveAsync` / `ResolveActionAsync` | Playbook / Action GUID (rows lacking that target kind are invisible to that call) |
| `ResolveBindingAsync` | The full [`Binding`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/Binding.cs) contract |
| `GetBindingByIdAsync` | Click-path dispatch by chip-carried id (unknown/disabled → clean rejection, no fallback) |
| `GetBindingByPlaybookIdAsync` | Reverse lookup (E-2 engine-output ledger adapter, topic TTLs) |
| `ResolveEventBindingsAsync` | Ordered event-composite members |
| `ListTextProjectableBindingsAsync` | Enabled rows with non-null `sprk_tooldescription`, deterministic order — the loop's capability-tool projection (FR-P2-01) |

**Cache**: `IMemoryCache`, 5-minute absolute TTL per resolve key (hits AND misses cached). A row edit propagates within 5 minutes per BFF instance; restart for instant effect.

---

## 6. Live rows (spaarkedev1, full-table read 2026-07-07)

18 enabled rows. Rows with null FR-P0-03 columns are pre-extension legacy rows (safe defaults apply):

| `sprk_consumertype` | code | ucid | disposition | risk | surfaces | Notes |
|---|---|---|---|---|---|---|
| `ai-summary` | default | — | — (→Informational) | — | — | legacy; playbook target |
| `chat-classify` | default | UC-A-7 | Informational | None | assistant | |
| `chat-summarize` | default | UC-A-1 | Informational | None | assistant | SUM-CHAT@v1 |
| `chat-summarize` | matter-summary | UC-A-1 | **Work Product** | None | assistant | → `sprk_matter.sprk_mattersummary` (task 047) |
| `compose-summarize` | default | — | — | — | — | legacy |
| `create-task` | default | UC-H-1 | Informational | None | assistant | drafting capability; write bridges to `dataverse.create_record` |
| `daily-briefing-narrate` | default | UC-D-1 | Informational | None | (all) | coded Action DAILY-BRIEFING@v1 |
| `daily-briefing-narrate` | email | UC-D-1 | **Email** | None | scheduler | `on_event: briefing_scheduled` |
| `document-profile` | default | — | — | — | — | legacy; playbook + action targets |
| `draft-correspondence` | default | UC-G-2 | Informational | None | assistant | DRAFT-CORR@v1 |
| `email-analysis` | default | — | — | — | — | legacy; playbook target |
| `insights-ask` | default | UC-C-2 | Informational | None | — | not loop-projected (no tooldescription) |
| `insights-ask` | matter-health-single | UC-C-2 | Informational | None | — | priority 400 (named-row rule) |
| `insights-search` | default | UC-C-2 | Informational | None | — | catalog-parity row; NO target (skipped by resolution by design) |
| `matter-pre-fill` | default | — | — | — | — | legacy |
| `no_match_handler` | default | L4-REFUSAL | Informational | None | assistant | honest-refusal capability |
| `project-pre-fill` | default | — | — | — | — | legacy |
| `summarize-file` | default | — | — | — | — | legacy |

All rows: environment `*`, priority 500 (except insights-ask/matter-health-single at 400), enabled.

---

## 7. Seed + operations

- Row provenance is recorded per task in `projects/spaarke-ai-architecture-redesign-r1/notes/task-0*-dataverse-changes.md`; partial seed mirrors live at `infra/dataverse/` (e.g. `sprk_playbookconsumer-insights-rows.json`). `scripts/dataverse/Seed-PlaybookConsumers.ps1` predates the FR-P0-03 columns — its **regeneration from the live table is tracked by FR-P4-02**.
- Standard Dataverse audit is enabled; edits to `sprk_action`/`sprk_playbook`, `sprk_enabled`, `sprk_priority`, `sprk_tooldescription`, `sprk_disposition`, or `sprk_risk` redirect dispatch / change model behavior — treat row edits like config changes.
- Diagnosis "why doesn't this consumer resolve?": (1) enabled row for the type? (2) environment matches? (3) exact code or `default` row? (4) target lookup populated (target-less rows are skipped)? (5) match-conditions JSON valid (malformed fails closed)? (6) 5-min cache elapsed?

---

## 8. Related docs

| Doc | Topic |
|---|---|
| [`sprk_analysisaction.md`](sprk_analysisaction.md) | The Action (execution unit) incl. the `sprk_inputschema` contract |
| [`sprk_analysistool.md`](sprk_analysistool.md) | The closed tool catalog + `side_effect_class` gate |
| [`sprk_playbooknode.md`](sprk_playbooknode.md) | Frozen engine node table |
| [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) | Canonical design (§6.2 Binding, §6.4 single-routing-surface, §7 three-path dispatch) |
| [`docs/guides/ai-guide-consumer-wiring.md`](../guides/ai-guide-consumer-wiring.md) | How to wire a new consumer |
| [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) | Compile-time constants boot-reconciled against rows |
