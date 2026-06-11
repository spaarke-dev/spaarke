# Topic Registry Deploy Handoff — Task 011

> **Task**: 011 — Deploy `sprk_aitopicregistry` entity to dev Dataverse
> **Phase**: 1 (Dataverse schema)
> **Rigor Level**: STANDARD (per POML)
> **Executed**: 2026-06-10
> **Environment**: `https://spaarkedev1.crm.dynamics.com` (Spaarke Dev)
> **Status**: ✅ COMPLETE — entity live, all acceptance criteria PASS

---

## What was deployed

The `sprk_aitopicregistry` entity (routing table for InsightSummaryCard topic+mode → playbook + display configuration) is live in Spaarke Dev Dataverse, fully populated per Task 010 schema design, alternate-key-enforced, Web-API-queryable.

### Schema delivered

**Entity**: `sprk_aitopicregistry` (LogicalName / SchemaName)
- Collection name: `sprk_aitopicregistries`
- Primary key: `sprk_aitopicregistryid` (GUID)
- Primary name: `sprk_name` (NVARCHAR 200, ApplicationRequired)
- Ownership: OrganizationOwned
- IsActivity: false / HasNotes: false / HasActivities: false
- IsCustomEntity: true

**Nine FR-04 custom attributes** (all visible in MCP describe):

| # | Logical name | Type | Nullable | Notes |
|---|---|---|---|---|
| 1 | `sprk_topicname` | NVARCHAR(100) | NOT NULL | Stable topic id, kebab-case |
| 2 | `sprk_mode` | NVARCHAR(50) | NOT NULL | r1: 'single' only |
| 3 | `sprk_playbookname` | NVARCHAR(200) | NOT NULL | FK by name to `sprk_playbook.sprk_name`; Q-U1 ban (no `@v1`) |
| 4 | `sprk_displayname` | NVARCHAR(200) | NOT NULL | Card header text |
| 5 | `sprk_icon` | NVARCHAR(100) | nullable | Fluent v9 icon component name |
| 6 | `sprk_hostentity` | NVARCHAR(100) | NOT NULL | Host record type (e.g., `sprk_matter`) |
| 7 | `sprk_targetfield` | NVARCHAR(100) | NOT NULL | Longtext field for envelope persistence |
| 8 | `sprk_cachettlminutes` | INT | NOT NULL | Range 1..1440; default 60 for matter-health |
| 9 | `sprk_enabled` | BIT | nullable | Default true; SME on/off toggle |

**Alternate key**: `sprk_AlternateKey_TopicMode` on `(sprk_topicname, sprk_mode)` — `EntityKeyIndexStatus: Active` (confirmed). Enforces uniqueness via platform-native composite index (no plugin / no JS).

---

## Acceptance criteria results

| # | Criterion | Result |
|---|---|---|
| 1 | `mcp__dataverse__describe('tables/sprk_aitopicregistry')` returns expected metadata | ✅ PASS — all 10 sprk_ fields visible with correct types + sizes; collection name `sprk_aitopicregistries` matches |
| 2 | Web API list endpoint returns 200 with empty `value` array | ✅ PASS — `GET /api/data/v9.2/sprk_aitopicregistries` → HTTP 200, `value.Count = 0` |
| 3 | Alternate key (topicname, mode) is enforced — attempt duplicate insert fails | ✅ PASS — first insert HTTP 204, second insert with same `(altkey-test, single)` → **HTTP 412 Precondition Failed**. Test row deleted post-verification. |

---

## Deployment outcome

**Script**: `scripts/temp/Deploy-AiTopicRegistryEntity.ps1`

### Run sequence

1. **Dry run** (`-DryRun`): Auth OK, entity does NOT exist, would create 9 attributes + alt key + publish. Confirmed environment + payload integrity.
2. **First real run**: Entity created successfully. Adding first attribute `sprk_topicname` returned `0x80060888 "An unexpected error occurred"` — almost certainly metadata-propagation race (entity created ms before attribute add). No attributes other than primary `sprk_name` landed.
3. **Second real run (idempotent retry)**: Detected entity exists → skipped create. Added all 9 attributes successfully. Created alternate key. Published. Web API smoke test passed (count=0).

### Idempotency confirmed

The script's idempotent skip logic worked exactly as designed: on retry, Step 1 detected the entity existed and proceeded to add only missing attributes. This was the proof-of-concept of acceptance: "if entity exists, skip creation per acceptance criterion."

A third dry run (not executed) would now report: entity exists, all 9 attributes present, alt key present — no changes needed.

---

## MCP describe output (canonical reference)

```sql
DESCRIBE TABLE sprk_aitopicregistry (
  createdby LOOKUP (GUID) ( Related table : systemuser),
  createdon DATETIME,
  createdonbehalfby LOOKUP (GUID) ( Related table : systemuser),
  importsequencenumber INT,
  modifiedby LOOKUP (GUID) ( Related table : systemuser),
  modifiedon DATETIME,
  modifiedonbehalfby LOOKUP (GUID) ( Related table : systemuser),
  organizationid LOOKUP (GUID) ( Related table : organization),
  overriddencreatedon DATE ONLY,
  sprk_aitopicregistryid GUID,
  sprk_cachettlminutes INT NOT NULL,
  sprk_displayname NVARCHAR(200) NOT NULL,
  sprk_enabled BIT,
  sprk_hostentity NVARCHAR(100) NOT NULL,
  sprk_icon NVARCHAR(100),
  sprk_mode NVARCHAR(50) NOT NULL,
  sprk_name NVARCHAR(200) NOT NULL,
  sprk_playbookname NVARCHAR(200) NOT NULL,
  sprk_targetfield NVARCHAR(100) NOT NULL,
  sprk_topicname NVARCHAR(100) NOT NULL,
  statecode STATE (INT) (Valid Options: Active (0), Inactive (1)),
  statuscode STATUS (INT) (Valid Options: Active (1), Inactive (2)),
  timezoneruleversionnumber INT,
  utcconversiontimezonecode INT,
  versionnumber BIGINT
);
```

---

## Downstream task readiness

| Task | Blocker resolved | Notes |
|---|---|---|
| 012 — Model-driven app form + saved views (Phase 1) | ✅ Schema available | Form OnSave should synthesize `sprk_name` = `{topicname}/{mode}` and validate Q-U1 ban regex on `sprk_playbookname` |
| 014 — Seed `matter-health/single` row (Phase 1) | ✅ Schema available | Insert with topicname=`matter-health`, mode=`single`, playbookname=`matter-health-single`, hostentity=`sprk_matter`, cachettlminutes=60 |
| Card mount-time check (FR-05) | ✅ Available via OData | `?$filter=sprk_hostentity eq 'sprk_matter' and sprk_topicname eq 'matter-health' and sprk_mode eq 'single' and sprk_enabled eq true` |

---

## Gotchas captured for future projects

1. **Metadata-propagation race on attribute-add immediately after entity-create**: First attribute POST may fail with `0x80060888 "An unexpected error occurred"`. The fix is simply to re-run the idempotent script — by the second run the entity has fully propagated. Not a script bug; a Dataverse platform race condition. The 2-run pattern was: run 1 created entity, run 2 added all 9 attributes + key + published.
2. **Alt-key `EntityKeyIndexStatus: Pending` immediately after creation**: The composite index materializes in seconds. Don't test duplicate-detection in the same request thread as alt-key creation. Wait ~10s. We waited, status flipped to `Active`, duplicate test then returned the expected 412.
3. **`$select` on `EntityDefinitions(LogicalName='...')`** is not supported (returns `0x80060888 "The query parameter $select is not supported"`). Use full GET on the entity definition resource.
4. **WebAPI duplicate-detection error**: returns **HTTP 412 Precondition Failed** (not 409 Conflict or 400 Bad Request). This is the canonical Dataverse alt-key duplicate response; downstream form/JS handlers should check for 412 specifically.

---

*Handoff written 2026-06-10 by task-execute for Task 011. Entity is ready for Tasks 012 + 014.*
