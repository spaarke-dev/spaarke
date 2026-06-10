# Phase G — Lookup-Driven Multi-Index (Option 3)

> **Project**: `spaarke-multi-container-multi-index-r1`
> **Phase**: G (extension to R1 scope)
> **Branch**: `work/spaarke-multi-container-multi-index-r1`
> **Status**: Design (2026-06-09)

---

## 1. Motivation

Phases A–F (already shipped) plumbed a string field `sprk_searchindexname` end-to-end from BU → parent record → PCF → URL envelope → code page → BFF → AI Search. UAT exposed two structural problems:

1. **No referential integrity.** A typo'd index name silently routes to a non-existent or wrong index. The App Service `AiSearch__AllowedIndexes` array is a rejection guard, not a source of truth.
2. **Manual allow-list maintenance.** Every new index requires editing App Service config (operational toil + footgun: forget the config → silent failures).

Phase G replaces the text field with a lookup to a new master-data table `sprk_aisearchindex`, making the table the single source of truth.

---

## 2. Scope

### In scope
- New `sprk_aisearchindex` table (already created; needs 3 added columns: `sprk_displayname`, `sprk_description`, `sprk_isdefault`)
- Add `sprk_aisearchindexid` lookup to 7 entities; drop `sprk_searchindexname` text column from same
- Replace 1:N field mappings (text→text) with (lookup→lookup)
- PCF v1.1.75: drop the `searchIndexName` bound property; resolve lookup via `context.webAPI`
- Code page UI dropdown sourced directly from `sprk_aisearchindex` via Dataverse Web API
- BFF: change `SearchIndexNameResolver` to read lookup + expand; replace `AiSearch__AllowedIndexes` config with a cached Dataverse query
- One-time data migration script (text → lookup)
- Remove `AiSearch__AllowedIndexes__*` App Service settings post-soak

### Out of scope
- No changes to AI Search index schemas themselves
- No changes to the upload / indexing job pipeline (resolver still reads doc → parent → BU)
- No new entity types beyond the 7 listed
- No changes to the chat / RAG paths beyond what the resolver change brings
- The follow-on `sdap-client-shared-library-fix-r1` work stays a separate project

---

## 3. Entities affected

| Entity | Add lookup | Drop text |
|---|---|---|
| `businessunit` | `sprk_ai_search_index` | `sprk_searchindexname` |
| `sprk_matter` | `sprk_ai_search_index` | `sprk_searchindexname` |
| `sprk_project` | `sprk_ai_search_index` | `sprk_searchindexname` |
| `sprk_invoice` | `sprk_ai_search_index` | `sprk_searchindexname` |
| `sprk_event` | `sprk_ai_search_index` | `sprk_searchindexname` |
| `sprk_workassignment` | `sprk_ai_search_index` | `sprk_searchindexname` |
| `sprk_document` | `sprk_ai_search_index` | `sprk_searchindexname` |

`sprk_document` is included because the BFF `SearchIndexNameResolver` reads the document-level value first in its 3-step chain (doc → parent → BU).

### 3.1 🚨 Schema name vs. doc shorthand — load-bearing implementer note

**The actual column name on the 7 entities is `sprk_ai_search_index`** (derived by Dataverse from display name "AI Search Index" using the `sprk_` publisher prefix + lowercase + underscores).

This spec was originally drafted using `sprk_aisearchindexid` as a placeholder. Where this spec still references `sprk_aisearchindexid` in the context of a **column on a source entity** (Matter/Project/Invoice/Event/WorkAssignment/Document/BU), the implementer MUST substitute the actual schema name:

| Spec text says (legacy placeholder) | Actual schema/wire form |
|---|---|
| `sprk_aisearchindexid` (as a column on Matter/Project/etc.) | `sprk_ai_search_index` |
| `_sprk_aisearchindexid_value` (OData lookup value) | `_sprk_ai_search_index_value` |
| `$expand=sprk_aisearchindexid` (OData navigation property) | `$expand=sprk_ai_search_index` |
| FetchXml `to='sprk_aisearchindexid'` (lookup column on source) | `to='sprk_ai_search_index'` |
| Field mapping source/target reference (lookup→lookup) | `sprk_ai_search_index → sprk_ai_search_index` |
| PowerShell `-Fields sprk_aisearchindexid` (setting source's lookup col) | `-Fields sprk_ai_search_index` |

**Do NOT substitute** where `sprk_aisearchindexid` is the **primary key (GUID) column of the `sprk_aisearchindex` table itself**:

| Spec text says | Meaning | Keep as-is? |
|---|---|---|
| `sprk_aisearchindexid` in `<entity name='sprk_aisearchindex'>` attribute list | PK column of catalog table | ✅ Keep |
| FetchXml `from='sprk_aisearchindexid'` (link-entity FROM the catalog table) | PK column of catalog table | ✅ Keep |
| `Get-CrmRecords -EntityLogicalName sprk_aisearchindex -Fields sprk_aisearchindexid, ...` | PK column selected from catalog | ✅ Keep |

If unsure, ask: "is this referring to (a) a column on a Matter/Project/Document record, or (b) the GUID identity of an `sprk_aisearchindex` catalog row?" — (a) becomes `sprk_ai_search_index`; (b) stays `sprk_aisearchindexid`.

This note was added 2026-06-10 after MCP `update_table` created the columns and revealed the actual schema name. The placeholder text was not back-substituted to keep the spec diff minimal.

---

## 4. `sprk_aisearchindex` columns

Final schema (as of 2026-06-10, all user-confirmed):

| Column | Type | Notes |
|---|---|---|
| `sprk_searchindexname` | NVARCHAR(850) NOT NULL | Azure AI Search physical index name; canonical BFF wire value |
| `sprk_displayname` | NVARCHAR(100) | UI label shown in dropdown (e.g., "Development Files 2", "Matters") |
| `sprk_description` | MULTILINE TEXT | Ops note / what this row is for |
| `sprk_isdefault` | BIT | Exactly one row marked Yes; fallback when no record/BU specifies an index |
| `sprk_displayorder` | INT | Stable sort order for the dropdown (lower first) |
| `sprk_targetentitytype` | Choice (Option Set) | Which entity this row searches for; see Choice values below |
| `sprk_containerid` | NVARCHAR(100) | SPE container ID where applicable (1:1 with index) |
| `sprk_endpoint` | NVARCHAR(100) | Azure AI Search service endpoint (multi-region future-proofing) |
| `sprk_embeddingmodel` | NVARCHAR(100) | Embedding model used by this index (e.g., `text-embedding-3-large`) — metadata only |
| `statecode` / `statuscode` | int/State | Active (0) / Inactive (1) — filters which rows appear in dropdown |
| Standard audit | (system) | createdby / modifiedby / etc. |

### Choice values for `sprk_targetentitytype`

User-confirmed labels (renumbered 2026-06-10; integer values are NOT depended on by code — see §6.5):

- All
- Matter
- Project
- Invoice
- Event
- Work Assignment
- Document

(Final integer values arrive in next attachment; the design is value-agnostic.)

### Row pattern

One row per (physical index, target entity type) pair. The same physical Azure index can appear in multiple rows with different `sprk_targetentitytype` values when one index serves multiple record types — e.g., `spaarke-records-index` appears 5 times (Matters / Projects / Invoices / Events / Work Assignments), each with its own `sprk_displayname`.

Example seed:

| `sprk_displayname` | `sprk_searchindexname` | `sprk_targetentitytype` | `sprk_isdefault` | `sprk_displayorder` |
|---|---|---|---|---|
| Development Files 1 | spaarke-knowledge-index-v2 | Document | No | 10 |
| Development Files 2 | spaarke-file-index | Document | Yes | 20 |
| Matters | spaarke-records-index | Matter | No | 30 |
| Projects | spaarke-records-index | Project | No | 40 |
| Invoices | spaarke-records-index | Invoice | No | 50 |
| Events | spaarke-records-index | Event | No | 60 |
| Work Assignments | spaarke-records-index | Work Assignment | No | 70 |
| All Records | spaarke-records-index | All | No | 80 |

---

## 5. PCF v1.1.75 contract

### Manifest delta
Remove the bound property:
```xml
<!-- DELETE this entire <property> element from ControlManifest.Input.xml -->
<property name="searchIndexName" of-type="SingleLine.Text" usage="bound" required="false"/>
```

### Index resolution
The PCF resolves the index name itself in `init()` using the host record's lookup:

```typescript
// Pseudocode — replaces context.parameters.searchIndexName?.raw
private async resolveSearchIndexNameAsync(
  context: ComponentFramework.Context<IInputs>
): Promise<string | null> {
  const entityType = context.mode.contextInfo.entityTypeName; // 'sprk_matter' etc.
  const entityId = context.mode.contextInfo.entityId;
  if (!entityType || !entityId) return null;

  try {
    const record = await context.webAPI.retrieveRecord(
      entityType,
      entityId,
      '?$select=_sprk_aisearchindexid_value' +
      '&$expand=sprk_aisearchindexid($select=sprk_searchindexname)'
    );
    return record.sprk_aisearchindexid?.sprk_searchindexname ?? null;
  } catch {
    return null; // BFF tenant default takes over
  }
}
```

Result feeds the existing `boundSearchIndexName` variable. Downstream (URL envelope, searchUnion calls, `useSemanticSearch` hook signature) unchanged — wire format stays string.

### Form-level changes
After v1.1.75 deploys, each form's PCF instance must have the (now-invalid) `searchIndexName` binding **removed**. No new binding is set; the PCF resolves internally.

### Version locations (the 5 places)
Bump to `1.1.75` in:
1. `control/ControlManifest.Input.xml`
2. `control/components/SemanticSearchControl.tsx` (footer string)
3. `Solution/solution.xml`
4. `Solution/Controls/.../ControlManifest.xml`
5. `Solution/pack.ps1`

---

## 6. Code page UI — unified Index dropdown + filter redesign

### What this replaces

Current UI (per 2026-06-10 screenshot at [`screenshot-current-ui.png` if archived]):
- "Search Criteria" panel with 4 domain tabs: **Documents / Matters / Projects / Invoices**
- Below the tabs: label-above-control filter fields — Saved Searches, AI Search query, Document Type, File Type, Matter Type, Date Range

Both the 4-tab selector AND the label+box filter layout are replaced.

### New: single "Search Index" dropdown (replaces the 4 tabs)

A single `Dropdown` populated with the full list of `sprk_aisearchindex` rows where `statecode = 0` (Active), ordered by `sprk_displayname`. Each option shows the **display name** (e.g., "Development Files 2", "Matters") — never the raw Azure index name.

Selecting an option fully determines the search target. The dropdown selection drives both the index and the entity filter via the row's `sprk_targetentitytype` Choice value. See §6.5 for the value-agnostic mapping approach (no hardcoded Choice-integer → entity map).

### Data source (direct Dataverse Web API)

The code page reads the Choice's FormattedValue (label) — never the integer — and normalizes it for the BFF wire format. See §6.5 for rationale.

```typescript
// src/client/code-pages/SemanticSearch/src/services/aiSearchIndexService.ts (new)
export interface AiSearchIndexRow {
  sprk_aisearchindexid: string;
  sprk_displayname: string;
  sprk_searchindexname: string;
  sprk_targetentitytype: number;                  // Choice integer (NOT used downstream)
  sprk_targetentitytypeLabel: string;             // Choice label, used by §6.5 normalize
  sprk_isdefault: boolean;
  sprk_displayorder: number;
}

export async function listActiveSearchIndexes(): Promise<AiSearchIndexRow[]> {
  const url = `${dataverseUrl}/api/data/v9.2/sprk_aisearchindexes` +
    '?$select=sprk_aisearchindexid,sprk_displayname,sprk_searchindexname,' +
              'sprk_targetentitytype,sprk_isdefault,sprk_displayorder' +
    '&$filter=statecode eq 0' +
    '&$orderby=sprk_displayorder,sprk_displayname';

  const response = await fetch(url, {
    headers: {
      'Accept': 'application/json',
      'OData-Version': '4.0',
      'Prefer': 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"',
    },
    credentials: 'include',
  });
  const data = await response.json();
  return data.value.map((row: any) => ({
    sprk_aisearchindexid: row.sprk_aisearchindexid,
    sprk_displayname: row.sprk_displayname,
    sprk_searchindexname: row.sprk_searchindexname,
    sprk_targetentitytype: row.sprk_targetentitytype,
    sprk_targetentitytypeLabel: row['sprk_targetentitytype@OData.Community.Display.V1.FormattedValue'],
    sprk_isdefault: row.sprk_isdefault === true,
    sprk_displayorder: row.sprk_displayorder ?? 999,
  }));
}
```

### 6.5 Choice-value-agnostic mapping (future-proof)

**Problem**: Dataverse Choice columns expose integer values (e.g., 100000000), but the BFF wire format expects entity logical names ("matter", "sprk_matter"). Any hardcoded `100000000 → 'sprk_matter'` map in client code would break if the Choice values are ever renumbered.

**Solution**: never depend on the Choice integer. Read the Choice's FormattedValue (the human label), normalize it to the BFF's expected form, and rely on the BFF's existing label-tolerance.

```typescript
// src/client/code-pages/SemanticSearch/src/services/targetEntityNormalize.ts (new)

const ALL_SENTINEL = 'all';

/**
 * Converts the Choice label (e.g., "Matter", "Work Assignment", "All")
 * into the BFF entityType wire form ("matter", "workassignment", "all").
 *
 * Future-proof: adding a new Choice option in Dataverse requires NO code change here.
 * Just ensure the new label is URL/identifier-safe (letters + spaces only, no special chars).
 */
export function normalizeTargetEntityLabel(label: string | undefined): string | null {
  if (!label) return null;
  return label.toLowerCase().replace(/\s+/g, '');
}

export function buildSearchRequestFragment(row: AiSearchIndexRow): {
  scope: string;
  entityType?: string;
  searchIndexName?: string;
} {
  const normalized = normalizeTargetEntityLabel(row.sprk_targetentitytypeLabel);

  // "All" → search-everything; no entityType filter, scope=all
  if (normalized === ALL_SENTINEL) {
    return { scope: 'all', searchIndexName: row.sprk_searchindexname };
  }

  // Specific entity → scope=entity + entityType + searchIndexName
  return {
    scope: 'entity',
    entityType: normalized ?? undefined,
    searchIndexName: row.sprk_searchindexname,
  };
}
```

**Why this design is the most flexible**:
- No hardcoded Choice-integer → string map anywhere in the code
- Adding a new entity type to the table = add Choice option + add table row. Zero code changes anywhere.
- Choice integer values can be reordered/renumbered freely (already done once in this design) — code is immune
- Single source of truth: the Dataverse Choice labels themselves drive the wire format
- BFF already handles both "matter" and "sprk_matter" via [`SearchFilterBuilder.cs:127-128`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SearchFilterBuilder.cs#L127-L128) backward-compat OR-clause — no BFF code change needed for the normalized labels

**Convention enforced**: Choice labels for `sprk_targetentitytype` MUST be URL/identifier-safe (letters + spaces only; spaces are stripped on normalize). This is documented in §13 and the table seed instructions.

**The "All" sentinel** is the only special-case: `normalizeTargetEntityLabel("All") === "all"` triggers `scope: 'all'` (no entity filter) instead of `scope: 'entity'`. Wired into `buildSearchRequestFragment` above; no other special cases needed for the 6 entity-bearing labels.

### Default selection precedence

1. **PCF context launch** — URL envelope carries `searchIndexName` from the host record's lookup → match it to a row and select it. If no exact match (e.g., the row was deleted), fall back to step 2.
2. **Direct code-page launch** — select the row marked `sprk_isdefault = true`.
3. **Final fallback** — first row alphabetically by display name; if no rows at all, the dropdown disables and the BFF tenant default applies.

### Behavior

- On mount: fetch the list, populate dropdown
- On change: cancel any in-flight search; trigger new search with new `searchIndexName` + new `entityType` derived from the selected row
- Selection persists in component state only — resets on page navigation
- The query box stays where it is; only the index/domain selector is replaced

### Side pane layout (Search Criteria)

The left-side collapsible "Search Criteria" pane stays as-is in structure. Existing filter fields (Saved Searches, AI Search query, Document Type, File Type, Matter Type, Date Range) keep their current label-above-control layout — no per-field redesign.

**Two changes**:

1. **Replace the 4 domain tabs** (Documents / Matters / Projects / Invoices) at the top of the pane with the new **Search Index** dropdown described above.
2. **Move `Relevance Threshold` and `Search Mode`** from the hidden top-right menu INTO the Search Criteria pane (they're currently too hidden for users to discover). Place them below the existing filters, above the Search button.

The top-right per-item info icons stay; convert any inline-help labels to **popup info icons** (Fluent UI v9 `Popover` triggered by an `InfoButton` / `i` icon next to the field label) so the pane stays compact.

### Resulting side-pane structure (top to bottom)

```
[<<]  Search Criteria

  Search Index            ← NEW (replaces 4 tabs)
  [Dropdown: Matters / Documents / ...]

  Saved Searches          ← existing
  [Dropdown]

  AI Search  (i)          ← existing; (i) = popup icon
  [Textarea]

  Document Type           ← existing
  [Dropdown]

  File Type               ← existing
  [Dropdown]

  Matter Type             ← existing
  [Dropdown]

  Date Range              ← existing
  [Dropdown + range pickers]

  Relevance Threshold (i) ← MOVED IN from top-right
  [Slider/Dropdown]

  Search Mode (i)         ← MOVED IN from top-right
  [Dropdown: RRF / Vector / Keyword]

  [Search]  [Cancel]
```

### Authorization

Dataverse table-level security on `sprk_aisearchindex` controls visibility. Users without read access get an empty dropdown → the code page disables search and shows a clear "no indexes available" message (don't silently fall back to tenant default — the user has no way to recover from that). Intentional: ops scope indexes per security role.

---

## 7. BFF changes

### `SearchIndexNameResolver` (background indexing path)

Single-fetch with FetchXml `link-entity` (decided 2026-06-10). Resolves doc → parent → BU chain in one query per step, projecting the index name through the lookup join.

```csharp
// Step 2 example — parent record + linked index name in one fetch
var fetchXml = $@"
  <fetch top='1'>
    <entity name='{parentEntityType}'>
      <attribute name='owningbusinessunit' />
      <filter><condition attribute='{parentEntityType}id' operator='eq' value='{parentGuid}' /></filter>
      <link-entity name='sprk_aisearchindex'
                   from='sprk_aisearchindexid'
                   to='sprk_aisearchindexid'
                   link-type='outer'
                   alias='idx'>
        <attribute name='sprk_searchindexname' />
      </link-entity>
    </entity>
  </fetch>";

var result = await _entityService.RetrieveMultipleAsync(new FetchExpression(fetchXml), ct);
var row = result.Entities.FirstOrDefault();
var indexName = row?.GetAttributeValue<AliasedValue>("idx.sprk_searchindexname")?.Value as string;
if (!string.IsNullOrWhiteSpace(indexName)) return indexName;
```

**Why `link-type='outer'`**: we want rows where the parent's `sprk_aisearchindexid` is NULL to still return (with null projection on the linked attribute), so the resolver chain continues to Step 3 (BU lookup). Inner join would drop them and break the cascade.

**Edge cases handled**:
- Parent has no lookup set → `idx.sprk_searchindexname` is null → fall to Step 3 (BU)
- Parent's lookup points to a deleted `sprk_aisearchindex` row → outer join projects null → same as above
- Parent's lookup points to an Inactive row → outer join still projects the name; we DON'T filter by statecode here because the BFF allow-list validation in `KnowledgeDeploymentService` rejects inactive index names (single source of truth for "is this allowed")

### Allow-list validation
Replace the `AiSearchOptions.AllowedIndexes` array with a cached Dataverse query:

```csharp
public sealed class DataverseAllowedIndexesProvider : IAllowedIndexesProvider
{
  private readonly IGenericEntityService _entityService;
  private readonly IMemoryCache _cache;
  private const string CacheKey = "sprk_aisearchindex:active";
  private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

  public async Task<bool> IsAllowedAsync(string indexName, CancellationToken ct) {
    var allowed = await _cache.GetOrCreateAsync(CacheKey, async entry => {
      entry.AbsoluteExpirationRelativeToNow = CacheTtl;
      var rows = await _entityService.RetrieveMultipleAsync(
        new FetchExpression(@"
          <fetch>
            <entity name='sprk_aisearchindex'>
              <attribute name='sprk_searchindexname' />
              <filter><condition attribute='statecode' operator='eq' value='0' /></filter>
            </entity>
          </fetch>"), ct);
      return rows.Entities
        .Select(e => e.GetAttributeValue<string>("sprk_searchindexname"))
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });
    return allowed!.Contains(indexName);
  }
}
```

### Optional convenience endpoint
Skipped per decision (code page calls Dataverse directly). The BFF only validates, never enumerates.

---

## 8. Field mapping changes

Existing 1:N mappings (presumed — to be confirmed during implementation):
- `businessunit → sprk_matter` : `sprk_searchindexname → sprk_searchindexname`
- `sprk_matter → sprk_document` : `sprk_searchindexname → sprk_searchindexname`
- (similar for project / invoice / event / workassignment chains)

Replace each with the lookup equivalent:
- `businessunit → sprk_matter` : `sprk_aisearchindexid → sprk_aisearchindexid`
- `sprk_matter → sprk_document` : `sprk_aisearchindexid → sprk_aisearchindexid`
- etc.

These are configured in the parent entity's 1:N relationship mappings tab in Power Apps.

---

## 9. Data migration script

PowerShell script under `scripts/phase-g-lookup-migration/`:

```powershell
# Migrate-SearchIndexLookup.ps1 (high-level)
$indexes = Get-CrmRecords -EntityLogicalName sprk_aisearchindex `
  -Fields sprk_aisearchindexid, sprk_searchindexname

foreach ($entity in @('businessunit','sprk_matter','sprk_project',
                      'sprk_invoice','sprk_event','sprk_workassignment','sprk_document')) {
  $records = Get-CrmRecords -EntityLogicalName $entity `
    -Fields sprk_searchindexname `
    -FilterAttribute sprk_searchindexname -FilterOperator NotNull

  foreach ($r in $records.CrmRecords) {
    $matchingIndex = $indexes.CrmRecords |
      Where { $_.sprk_searchindexname -eq $r.sprk_searchindexname } |
      Select -First 1
    if ($matchingIndex) {
      Set-CrmRecord -EntityLogicalName $entity -Id $r[$idField] `
        -Fields @{sprk_aisearchindexid = $matchingIndex.sprk_aisearchindexid }
    }
  }
}
```

### Verification queries (post-migration)
```sql
-- Count text vs lookup populated by entity
SELECT
  (SELECT COUNT(*) FROM sprk_matter WHERE sprk_searchindexname IS NOT NULL) AS text_set,
  (SELECT COUNT(*) FROM sprk_matter WHERE _sprk_aisearchindexid_value IS NOT NULL) AS lookup_set;
-- text_set and lookup_set must be equal before dropping text column
```

Repeat for each of the 7 entities. Both counts must match before Step 9 (drop text column).

---

## 10. Deploy order (load-bearing)

| # | Step | Reversible? |
|---|---|---|
| 1 | Add `sprk_aisearchindexid` lookup column on all 7 entities (keep text column) | ✅ drop the new column |
| 2 | Add `sprk_displayname`, `sprk_description`, `sprk_isdefault` on `sprk_aisearchindex` | ✅ drop columns |
| 3 | Populate `sprk_aisearchindex` records for `spaarke-file-index` (isdefault=Yes) + `spaarke-knowledge-index-v2` | ✅ delete records |
| 4 | Deploy BFF v(+) with lookup-first resolver + Dataverse-backed allow-list (text-fallback retained — backward compat) | ✅ redeploy prior BFF |
| 5 | Run data migration script (text → lookup); verify counts | ✅ clear lookup column |
| 6 | Add lookup-to-lookup field mappings (keep text-to-text mappings) | ✅ remove new mappings |
| 7 | Build + deploy PCF v1.1.75 | ✅ revert to v1.1.74 |
| 8 | Update form-level PCF instances: remove `searchIndexName` text binding (no new binding needed) | ✅ re-add binding |
| 9 | Build + deploy code page with dropdown | ✅ revert to prior bundle |
| 10 | UAT soak (24–48 hr) — confirm chain works end-to-end | — |
| 11 | Drop text-to-text field mappings | ⚠️ recreate from history |
| 12 | Drop `sprk_searchindexname` text column from 7 entities | ⚠️ recreate column (no data) |
| 13 | Remove BFF text-fallback code (next BFF deploy) | ✅ revert |
| 14 | Remove `AiSearch__AllowedIndexes__*` App Service config | ✅ re-add |

Each step is independently deployable; the system stays in a working state between steps.

---

## 11. Rollback gates

Before step N, confirm:

| Before step | Verification | If fails |
|---|---|---|
| 5 (migration) | All 7 entities still serve via text column (PCF reads text, BFF resolver text-first OR lookup-first both work) | Stop; investigate |
| 7 (PCF v1.1.75 deploy) | Migration counts match; lookup populated on every record that had text | Re-run migration |
| 8 (form binding removal) | v1.1.75 installed and verified in Power Apps; control footer shows new version | Don't touch forms; uninstall PCF first |
| 12 (drop text column) | After 48-hr soak — App Insights shows BFF resolver returning from lookup path on every search; zero text-fallback hits | Don't drop; investigate text-fallback hits |

---

## 12. Test plan

### Unit tests
- BFF `DataverseAllowedIndexesProvider` — cache hit/miss, active filter, empty result
- BFF resolver — lookup-first; text-fallback (during migration only); chain still walks doc → parent → BU
- PCF `resolveSearchIndexNameAsync` — happy path, missing lookup, Web API failure
- Code page `aiSearchIndexService.listActiveSearchIndexes` — sorts by displayname, includes isdefault

### Integration tests
- BFF: 400 INDEX_NOT_ALLOWED when index name not in `sprk_aisearchindex` active rows
- BFF: 200 OK with results when index name matches an active row
- PCF: opens with correct `boundSearchIndexName` on Matter with lookup set
- PCF: opens with `null` and BFF tenant-default kicks in when lookup is empty

### UAT
- Open Matter w/ lookup → `spaarke-knowledge-index-v2`; semantic search shows files from that index
- Open Matter w/ lookup → `spaarke-file-index`; shows files from file index
- Open Matter w/ no lookup; BFF tenant default applies; shows files from `spaarke-file-index`
- Code page direct (no PCF context): dropdown lists both indexes; default = `spaarke-file-index` (`isdefault=Yes`)
- Code page direct: change dropdown → new search fires with new index
- Find Similar from a Matter file: results from the same index as the source doc

---

## 13. Open questions

1. ~~**Plugin/automate concerns**~~ — **RESOLVED 2026-06-10**: new field mappings acceptable. No plugins or Power Automate introduced.
2. ~~**`businessunit` schema additions**~~ — **RESOLVED 2026-06-10**: BU custom columns acceptable.
3. ~~**Dropdown labels**~~ — **RESOLVED 2026-06-10**: use `sprk_displayname` always; treat empty as a data-quality issue, not a display fallback.
4. ~~**Empty `sprk_aisearchindex` table**~~ — **RESOLVED 2026-06-10**: BFF falls back to `appsettings.AiSearch.KnowledgeIndexName` with a logged warning (App Insights) so ops are notified to populate the table.
5. ~~**`sprk_endpoint` field**~~ — **RESOLVED 2026-06-10**: column already exists in the table (user added).
6. ~~**Filter UI pattern**~~ — **RESOLVED 2026-06-10**: keep existing left-side collapsible "Search Criteria" pane and its label-above-control layout. Only changes: (a) the 4 domain tabs are replaced by the Search Index dropdown, (b) `Relevance Threshold` + `Search Mode` move from the hidden top-right menu INTO the side pane, (c) inline-help labels convert to popup info-icon (`Popover` on `i` icon).

---

## 14. Task decomposition (preview)

Anticipated 10–12 POML task files under `tasks/`:

| ID | Phase | Title | Parallel-safe? |
|---|---|---|---|
| 080 | G | Schema: add `sprk_aisearchindexid` lookup to 7 entities | Yes (entity-independent) |
| 081 | G | Schema: add display/description/isdefault columns to `sprk_aisearchindex`; seed 2 records | Yes (independent of 080) |
| 082 | G | BFF: lookup-first resolver + Dataverse allow-list (text-fallback retained) | No (depends on 080+081) |
| 083 | G | Tests + deploy BFF + verify both paths via App Insights | No (depends on 082) |
| 084 | G | Data migration script + execution + verification | No (depends on 083) |
| 085 | G | Field mappings: add lookup→lookup mappings (keep text mappings) | No (depends on 084) |
| 086 | G | PCF v1.1.75: drop bound property, add resolver, version bump in 5 places | No (depends on 080) |
| 087 | G | PCF build + deploy + update form instance bindings | No (depends on 086) |
| 088 | G | Code page: dropdown component + service + integration + tests | Yes (depends on 081 only) |
| 089 | G | Code page build + deploy + e2e verification | No (depends on 088) |
| 090 | G | Post-soak cleanup: drop text mappings, drop text columns, remove App Service config | No (final) |
| 091 | G | Phase G wrap-up: lessons learned, runbook update | No (final) |

(Existing `090-project-wrap-up.poml` task may need renumbering or merging.)

---

## 15. Effort estimate

- Spec + task decomposition: 0.5 day
- Schema + data work (steps 1–3, 5, 6): 0.5 day
- BFF (steps 4 + 13): 0.5 day
- PCF v1.1.75 (steps 7–8): 0.5 day
- Code page dropdown (step 9): 0.5 day
- Migration + UAT soak: 0.5 day active + 24–48 hr passive
- Cleanup (steps 11–12, 14): 0.25 day

**Total**: ~2.5 days active work + 1–2 days soak.

---

## 16. Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-06-09 | Option 3 (lookup-only) chosen over Option 2 (formula column) | "We don't like formula fields" — user preference for clean schema |
| 2026-06-09 | Continue in R1 vs new R2 project | Same branch; ship together as cohesive multi-index story |
| 2026-06-09 | Code page calls Dataverse Web API directly (not new BFF endpoint) | Simpler; same source of truth; defers a BFF surface area expansion |
| 2026-06-09 | `sprk_isactive` not added | `statecode` already provides this |
| 2026-06-09 | `sprk_aisearchindex.sprk_containerid` is 1:1 with index | Pre-existing schema; revisit if 1:N container→index needed |
| 2026-06-10 | Add `sprk_targetentitytype` Choice column on `sprk_aisearchindex` | Enables the unified-dropdown design — one row per (index, entity) pair |
| 2026-06-10 | Row-multiplicity for shared indexes | E.g., `spaarke-records-index` appears in 5 rows (one per record type) — Option B over Option A (single row + multi-value subject). One row = one dropdown option = one search behavior; simplest contract end-to-end. |
| 2026-06-10 | BFF resolver uses FetchXml `link-entity` (single fetch) over two-step retrieve | Performance + atomicity. `link-type='outer'` to preserve null cascade through the 3-step chain. |
| 2026-06-10 | Filter UI redesign: kept open as Q6; collapse 4 domain tabs into the unified Index dropdown | Domain selection now belongs to the index choice; eliminates redundant "Documents/Matters/Projects/Invoices" tabs |
| 2026-06-10 | Empty `sprk_aisearchindex` → fall back to `appsettings.AiSearch.KnowledgeIndexName` with warning log | Avoids hard failure when ops hasn't populated the table; warning ensures it's visible |
| 2026-06-10 | Filter UI: keep existing side-pane layout; only swap tabs→dropdown + relocate Relevance Threshold & Search Mode into pane | More conservative redesign; preserves familiar pattern; pulls hidden settings into the discoverable surface |
| 2026-06-10 | Convert inline-help labels to popup info-icon (Popover on `i` icon) | Keeps the side pane compact while preserving help content |
| 2026-06-10 | Choice column `sprk_targetentitytype` confirmed; labels include "All" (sentinel) + 6 entity types | User-confirmed final schema |
| 2026-06-10 | Code never depends on Choice integer values — only on FormattedValue (label) + normalization | Future-proof: Choice can be renumbered freely; new entity = add row, zero code changes |
| 2026-06-10 | "All" Choice label → scope='all' (no entityType filter) | Single special-case; all other labels normalize to entity wire-form |
| 2026-06-10 | Choice labels must be URL/identifier-safe (letters + spaces, no special chars) | Convention enforced by normalize function (lowercase + strip spaces) |
| 2026-06-10 | Bonus columns confirmed: `sprk_displayorder` (sort), `sprk_embeddingmodel` (metadata) | User-added; spec updated to include |

---

## 17. References

- Existing project spec: [`projects/spaarke-multi-container-multi-index-r1/spec.md`](../../spec.md)
- BFF resolver: [`src/server/api/Sprk.Bff.Api/Services/Ai/SearchIndexNameResolver.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/SearchIndexNameResolver.cs)
- BFF allow-list options: [`src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs`](../../../../src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs)
- PCF manifest: [`src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml`](../../../../src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml)
- PCF entry: [`src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx`](../../../../src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx)
- Code page entry: [`src/client/code-pages/SemanticSearch/src/App.tsx`](../../../../src/client/code-pages/SemanticSearch/src/App.tsx)
- Code page hook: [`src/client/code-pages/SemanticSearch/src/hooks/useSemanticSearch.ts`](../../../../src/client/code-pages/SemanticSearch/src/hooks/useSemanticSearch.ts)
