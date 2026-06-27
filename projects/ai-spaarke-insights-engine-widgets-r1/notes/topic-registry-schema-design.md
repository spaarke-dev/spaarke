# `sprk_aitopicregistry` — Entity Schema Design

> **Task**: 003 — sprk_aitopicregistry entity design + ADR check
> **Status**: Design ratified — ready for Phase 1 Task 010 verbatim implementation
> **Author**: Task 003 (2026-06-10)
> **Implements**: spec FR-04 + FR-05 + FR-09
> **References**: Q-U1, Q-U2, Q-U4 (Resolution Decisions in `spec.md`)
> **ADR check**: No new ADR (spec NFR-09); pattern parity with existing `sprk_gridconfiguration`

---

## 1. Purpose

The `sprk_aitopicregistry` entity is the **routing table** between an `InsightSummaryCard` instance (host entity + topic + mode) and the JPS playbook that produces the narrative. It is the **only** mechanism that gates whether a sparkle icon appears on a host record (spec FR-05 — no orphan sparkles). SMEs add/disable topic rows in a model-driven app form (spec FR-09) without code deploy.

Per **Q-U4** (evidence-resolved): the registry **routes** topic+mode → playbook by name. It does **NOT** carry a `sprk_systemprompt` field — the playbook is the canonical prompt source per ADR-014 (`sprk_analysisaction.sprk_systemprompt`).

---

## 2. Naming + convention precedent

Pattern parity with `sprk_gridconfiguration` (Q-U2 evidence base):

| Convention | `sprk_gridconfiguration` | `sprk_aitopicregistry` |
|---|---|---|
| Primary key GUID | `sprk_gridconfigurationid` | `sprk_aitopicregistryid` |
| Primary name attribute | `sprk_name` (NVARCHAR 850, NOT NULL) | `sprk_name` (NVARCHAR 850, NOT NULL) — synthesized `{topicname}/{mode}` |
| Display name on form | (uses `sprk_name`) | `sprk_displayname` (separate from `sprk_name`, human-facing) |
| Icon storage | `sprk_iconname` NVARCHAR (Fluent component name string) | `sprk_icon` NVARCHAR (Fluent component name string) |
| Boolean toggle | `sprk_isdefault` (BIT) | `sprk_enabled` (BIT) |
| Free-text fields | `sprk_entitylogicalname` NVARCHAR(100) | `sprk_hostentity` NVARCHAR(100) |

**Field name in FR-04 is `sprk_icon`** (not `sprk_iconname`). Per spec FR-04 normative text, we keep `sprk_icon` — but the *storage convention* (Fluent component name as string) and the *resolution pattern* mirror `sprk_iconname` per Q-U2.

---

## 3. Complete field list (9 + primary name + system)

### 3.1 Field-by-field specification

| # | Logical name | Display name | Type | Length / range | Required | Default | Description |
|---|---|---|---|---|---|---|---|
| 0 | `sprk_aitopicregistryid` | (PK) | UNIQUEIDENTIFIER | — | system | autogen | Dataverse primary key (auto-generated). |
| 0p | `sprk_name` | Name | String (NVARCHAR) | MaxLength 200 | **ApplicationRequired** | — | Primary name attribute. Convention: `{sprk_topicname}/{sprk_mode}` (e.g., `matter-health/single`). Synthesized at row insert by maker; not user-editable post-create (form makes read-only after save). Required because Dataverse mandates a primary name attribute. NOT part of FR-04 9-field count but a Dataverse system requirement. |
| 1 | `sprk_topicname` | Topic name | String (NVARCHAR) | MaxLength 100 | **ApplicationRequired** | — | Stable topic identifier (kebab-case). Example: `matter-health`. Lowercase ASCII letters / digits / hyphens only (validated client-side on form). Combined with `sprk_mode` is unique (Alternate Key — see §4). |
| 2 | `sprk_mode` | Mode | String (NVARCHAR) | MaxLength 50 | **ApplicationRequired** | `single` | Analysis mode discriminator. r1 ships `single` only; framework-shaped for `portfolio` / `comparative` per spec Out of Scope. Lowercase ASCII letters only. |
| 3 | `sprk_playbookname` | Playbook name | String (NVARCHAR) | MaxLength 200 | **ApplicationRequired** | — | Foreign key by **name** (not lookup) to `sprk_playbook.sprk_name`. Example: `matter-health-single`. Must match an existing playbook row name exactly (case-insensitive). Not a Dataverse lookup because playbooks are referenced by name in `Insights:Playbooks:Map.<name>` config (r2 convention) and a name-string FK keeps the registry portable across environments. **MUST NOT contain `@v1`/`@vN` suffix** (Q-U1). |
| 4 | `sprk_displayname` | Display name | String (NVARCHAR) | MaxLength 200 | **ApplicationRequired** | — | Human-facing card title. Example: `Matter Health Insights`. Rendered as card header by `InsightSummaryCard`. |
| 5 | `sprk_icon` | Icon | String (NVARCHAR) | MaxLength 100 | None (optional) | `Sparkle24Filled` | Fluent UI v9 icon component name as string (Q-U2). Resolved client-side via `@fluentui/react-icons` named export lookup (same pattern as `ConfigurationService.resolveIconName()` for `sprk_iconname`). Examples: `Sparkle24Filled`, `Bot24Regular`, `Lightbulb24Filled`. Must end with size+style suffix per Fluent v9 icon convention. Empty/unresolved → fallback to `Sparkle24Filled`. |
| 6 | `sprk_hostentity` | Host entity | String (NVARCHAR) | MaxLength 100 | **ApplicationRequired** | — | Dataverse logical name of the host record type. Example: `sprk_matter`. Used by `InsightSummaryCard` mount-time check: card renders the sparkle only when host entity + topic + mode triple is registered (FR-05). |
| 7 | `sprk_targetfield` | Target field | String (NVARCHAR) | MaxLength 100 | **ApplicationRequired** | — | Logical name of the single longtext field on `sprk_hostentity` where the playbook's JSON envelope is persisted. Example: `sprk_performancesummary`. Read by the `UpdateRecord` node in the playbook (FR-14). |
| 8 | `sprk_cachettlminutes` | Cache TTL (minutes) | Integer (INT) | MinValue 1, MaxValue 1440 | **ApplicationRequired** | `60` | Server-side cache TTL applied to invocations for this topic (FR-21). Read by the BFF cache layer; overrides the r2 universal 15-minute `IInsightsPlaybookExecutionCache` default. Bound 1..1440 (1 minute to 24 hours). Default 60 (1 hour, matches r1 design). |
| 9 | `sprk_enabled` | Enabled | Boolean (BIT) | TrueOption=`Yes`(1), FalseOption=`No`(0) | None | `true` (1) | Soft on/off. When `false`, the sparkle icon does NOT render on host records for this topic+mode, even if other fields are valid. Distinct from `statecode` (which is the Dataverse soft-delete / lifecycle state). Allows SMEs to disable a topic in production without record deletion. |

### 3.2 System fields (Dataverse defaults — present automatically)

`createdon`, `createdby`, `modifiedon`, `modifiedby`, `ownerid`, `owningbusinessunit`, `statecode`, `statuscode`, `versionnumber`. Mirror the system-field set observed on `sprk_gridconfiguration` (MCP describe 2026-06-10).

### 3.3 Field count reconciliation

FR-04 lists **9** business fields:

`sprk_topicname`, `sprk_mode`, `sprk_playbookname`, `sprk_displayname`, `sprk_icon`, `sprk_hostentity`, `sprk_targetfield`, `sprk_cachettlminutes`, `sprk_enabled` → ✅ all 9 present.

Plus `sprk_name` (Dataverse-mandated primary name attribute, synthesized — NOT business data per se, but a system requirement). Total physical schema = 10 custom attributes + Dataverse system attributes.

### 3.4 Explicitly absent (Q-U4 — registry routes only)

The following fields are **NOT** on the registry and **MUST NOT** be added in r1:

- ❌ `sprk_systemprompt` — prompt is canonical in playbook (`sprk_analysisaction.sprk_systemprompt` per ADR-014). Registry routes; playbook holds prompt.
- ❌ `sprk_modelname` / `sprk_temperature` / `sprk_maxtokens` — model/inference config lives in the playbook node, not the topic registry.
- ❌ `sprk_outputschema` — envelope schema is fixed and documented in `notes/insight-envelope-schema.md` (Task 010 / Task 014).

---

## 4. Unique constraint on (topicname, mode)

### 4.1 Mechanism: **Dataverse Alternate Key**

Dataverse supports **alternate keys** — composite unique constraints defined at the entity metadata level. This is the canonical mechanism (NOT a plugin, NOT a JavaScript form validator, NOT a workflow).

Alternate keys are created via Web API `Keys` collection on `EntityDefinitions`. Mechanism details for Phase 1 task 010:

```
POST /api/data/v9.2/EntityDefinitions(LogicalName='sprk_aitopicregistry')/Keys
{
  "@odata.type": "Microsoft.Dynamics.CRM.EntityKeyMetadata",
  "SchemaName":  "sprk_AlternateKey_TopicMode",
  "DisplayName": { "LocalizedLabels": [{ "Label": "Topic + Mode", "LanguageCode": 1033 }] },
  "KeyAttributes": [ "sprk_topicname", "sprk_mode" ]
}
```

### 4.2 Behavior

- Dataverse will reject inserts/updates that would create a duplicate `(sprk_topicname, sprk_mode)` combination with a structured error (`alternate key violation`).
- The alternate key is **indexed** automatically by Dataverse — lookups by `(topicname, mode)` are O(log n).
- Surfaces in the model-driven app as a duplicate-record check at save time (no extra JS needed).
- Survives environment migration via solution export/import (alternate key metadata is part of the solution).

### 4.3 Why not just `sprk_topicname` unique?

`sprk_topicname` alone is **not** unique — the registry is designed to support multiple modes per topic (`matter-health` × `single`, `matter-health` × `portfolio`, `matter-health` × `comparative`). The composite key enforces "one row per topic+mode pair" — exactly the FR-04 acceptance criterion.

### 4.4 Why not a plugin?

A pre-validation plugin querying for duplicates would work but is more brittle, harder to test, slower (network round-trip), and violates ADR-010 DI minimalism (no new plugin assemblies in r1). Alternate keys are the platform-native solution.

---

## 5. Model-driven app form layout (FR-09 — SME-editable)

### 5.1 Form structure (one-tab, three-section layout)

```
┌──────────────────────────────────────────────────────────────────────┐
│ Topic Registration                                                   │
│ [ Sparkle24Filled icon ]   matter-health / single                    │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ ┌── Section 1: Identity ─────────────────────────────────────────┐  │
│ │ Topic name *           [ matter-health           ] (lowercase)  │  │
│ │ Mode *                 [ single  ▼ ] (default)                  │  │
│ │ Name (auto)            [ matter-health/single   ] (read-only)   │  │
│ │ Display name *         [ Matter Health Insights ]               │  │
│ │ Icon                   [ Sparkle24Filled        ] (Fluent v9)   │  │
│ └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│ ┌── Section 2: Routing ──────────────────────────────────────────┐  │
│ │ Playbook name *        [ matter-health-single ]                 │  │
│ │ Host entity *          [ sprk_matter          ]                 │  │
│ │ Target field *         [ sprk_performancesummary ]              │  │
│ └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│ ┌── Section 3: Runtime ──────────────────────────────────────────┐  │
│ │ Cache TTL (minutes) *  [ 60      ] (1..1440)                    │  │
│ │ Enabled                [ ☑ Yes  ]                               │  │
│ └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### 5.2 Form-level behaviors

| Behavior | Mechanism | Why |
|---|---|---|
| `sprk_name` auto-populated as `{topicname}/{mode}` on save | Form OnSave JS (single small handler, no plugin) | Dataverse requires primary name; SME shouldn't type it. Read-only post-save. |
| `sprk_mode` shows dropdown of allowed values | Field-level option set OR string with form-level dropdown (r1: string + dropdown JS) | r1 ships `single` only; future modes added by SMEs without schema migration if string-typed. |
| `sprk_icon` autocomplete from Fluent v9 icon names | Optional form helper (Phase 1 — out of scope for r1, just NVARCHAR field) | Tutorial documents the convention; no live autocomplete in r1. |
| Validation: `sprk_topicname` lowercase / kebab-case | Form OnSave JS regex `/^[a-z0-9-]+$/` | Stable identifier hygiene. |
| Validation: `sprk_playbookname` MUST NOT contain `@v1`/`@vN` | Form OnSave JS regex `/@v\d+/` → reject | Q-U1 ban; playbook versioning via `sprk_version` column on `sprk_playbook`. |
| Duplicate detection on (topicname, mode) | Native — alternate key (§4) | Platform-native. |
| Required field enforcement | `RequiredLevel = ApplicationRequired` per field (§3.1) | Native Dataverse. |

### 5.3 Views

Two saved views shipped with the entity:

| View name | Filter | Columns | Default |
|---|---|---|---|
| **Active topics** | `statecode = Active AND sprk_enabled = true` | Topic name, Mode, Display name, Host entity, Target field, Cache TTL | ✅ Yes |
| **All topics** | (no filter) | Topic name, Mode, Display name, Host entity, Enabled, Status | No |

### 5.4 Seed row (per FR-04 Acceptance + spec §SC-03)

Phase 1 task 010 seeds one row:

| Field | Value |
|---|---|
| `sprk_name` | `matter-health/single` |
| `sprk_topicname` | `matter-health` |
| `sprk_mode` | `single` |
| `sprk_playbookname` | `matter-health-single` |
| `sprk_displayname` | `Matter Health Insights` |
| `sprk_icon` | `Sparkle24Filled` |
| `sprk_hostentity` | `sprk_matter` |
| `sprk_targetfield` | `sprk_performancesummary` |
| `sprk_cachettlminutes` | `60` |
| `sprk_enabled` | `true` |
| `statecode` | `Active (0)` |

---

## 6. Indexing + search needs

| Need | Mechanism |
|---|---|
| Lookup by (topicname, mode) — `InsightSummaryCard` mount-time check (FR-05) | Alternate key index (§4) — already O(log n) |
| Lookup by host entity — admin view "all topics for sprk_matter" | Dataverse query against `sprk_hostentity` — no additional index needed (table will have ≤200 rows even at r5+ scale) |
| Active records view | `statecode + sprk_enabled` filter (no index — small table) |
| Quick find search by topic name | Standard Dataverse quick find — searches `sprk_name`, `sprk_topicname`, `sprk_displayname` |

**No additional custom indexes required**. Small lookup table; alternate key satisfies the only hot-path query (component mount-time registration check).

---

## 7. ADR check (spec NFR-09 — no new ADR)

| ADR | Relevance | Conformance |
|---|---|---|
| ADR-001 (single BFF runtime) | No new microservice; registry is Dataverse-native. | ✅ |
| ADR-010 (DI minimalism) | No new plugin assemblies; uses native alternate key + form JS. | ✅ |
| ADR-013 (AI extends BFF) | Registry is data layer; BFF reads it via existing Dataverse client. No new facade required. | ✅ |
| ADR-014 (prompts in playbook) | Registry has NO `sprk_systemprompt` — Q-U4 enforced. | ✅ |
| ADR-022 (unmanaged solutions in dev) | Schema deploy via Web API → automatically unmanaged. | ✅ |
| ADR-009 (caching) | `sprk_cachettlminutes` plumbs into existing `IInsightsPlaybookExecutionCache` per FR-21. No new cache abstraction. | ✅ |

**No new ADR required.** Spec NFR-09 satisfied.

---

## 8. Phase 1 Task 010 implementation checklist (verbatim)

Task 010 implements the following sequence (uses `dataverse-create-schema` skill patterns):

1. Authenticate via `az account get-access-token` (Dataverse audience).
2. Create entity `sprk_aitopicregistry` with primary name attribute `sprk_name` (MaxLength 200, ApplicationRequired).
3. Add 8 custom attributes (§3.1 rows 1, 2, 3, 4, 5, 6, 7, 8 — `sprk_topicname`, `sprk_mode`, `sprk_playbookname`, `sprk_displayname`, `sprk_icon`, `sprk_hostentity`, `sprk_targetfield`, `sprk_cachettlminutes`).
4. Add 1 Boolean attribute `sprk_enabled` with Yes/No option set, default `true` (§3.1 row 9).
5. Add alternate key `sprk_AlternateKey_TopicMode` on `(sprk_topicname, sprk_mode)` (§4.1).
6. Generate model-driven app form (one tab, three sections) per §5.1.
7. Attach two saved views (§5.3).
8. Publish customizations (`PublishXml` against `sprk_aitopicregistry`).
9. Seed one row per §5.4.
10. MCP-verify via `mcp__dataverse__describe('tables/sprk_aitopicregistry')` — confirm 9 custom columns + primary name + alternate key.

---

## 9. Acceptance-criterion mapping (task 003)

| Criterion (POML) | Where satisfied |
|---|---|
| All 9 fields documented with type + constraints | §3.1 table |
| Unique constraint on (topicname, mode) is explicit | §4 (alternate-key approach) |
| `sprk_icon` documented as string holding Fluent icon component name per Q-U2 | §2 (precedent table), §3.1 row 5 |
| No reference to `sprk_systemprompt` (Q-U4 — registry routes only) | §3.4 (explicitly absent), §7 ADR-014 row |
| Phase 1 task 010 can be implemented verbatim | §8 (10-step checklist) |

---

*End of design note. Phase 1 Task 010 implements §8 verbatim.*
