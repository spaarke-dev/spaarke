# Spaarke Multi-Container Multi-Index Routing — Design

> **Status**: Draft for review
> **Authored**: 2026-06-05
> **Author**: Ralph Schroeder + Claude
> **Worktree**: `work/semantic-search-pcf-ui-tweaks-2026-06-05` (design only; implementation branch TBD)
> **Precedes**: `spec.md` → `/project-pipeline` → tasks
> **Related**: PR #363 (SemanticSearch PCF v1.1.73 UI tweaks — ships independently)

---

## 1. Background & motivation

Spaarke stores documents in **SharePoint Embedded (SPE) containers** and indexes them in **Azure AI Search indexes** for semantic + keyword retrieval. Historically the platform assumed one container + one index per tenant ("knowledge-index-v2" for dev documents).

That assumption no longer holds:

- A recent migration introduced a new container + new index. Old dev documents remain in the original container/index; new documents land in the new one.
- A "Protected Matter" requirement: certain Matters (and their Documents) must store + index to a **separate**, access-controlled container + index distinct from the general working set.
- Anticipated growth: per-Business-Unit isolation for compliance, segregation of duties, or partner-tenant work.

This project introduces **record-scoped routing** of documents to the correct SPE container + Azure AI Search index, sourcing the discriminator from the record itself (with a Business Unit cascade default).

---

## 2. Current state (what works, what's missing)

### What already works
- BU has `sprk_containerid` (existing); user added `sprk_searchindexname` ✓
- Matter / Project / Invoice / WorkAssignment / Event have `sprk_containerid` (existing); user added `sprk_searchindexname` ✓
- Document has `sprk_searchindexname` (existing per user) + `sprk_containerid` (existing schema)
- `DocumentUploadWizard`'s `AssociateToStep.tsx::resolveContainerIdForRecord` already does **parent-record-first, BU-fallback** resolution for `sprk_containerid` ([AssociateToStep.tsx:147-163](../../src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx#L147-L163))
- The "Protected Matter" container path already works at the wizard layer today (verified during design)

### Field-name reality (clarified during design review)
The naming of the "container id" field is **not consistent across entities**, and that's intentional:
- **BU** + **Matter/Project/Invoice/WorkAssignment/Event**: field is `sprk_containerid` (logical "container reference for documents owned here")
- **Document**: field is `sprk_graphdriveid` (the SPE-side drive identifier — this is what's actually populated today; `sprk_containerid` on Document exists in schema but is blank and unused)

Both fields hold the same kind of value (an SPE container id like `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`), but the canonical Document-side field is `sprk_graphdriveid`. **This project follows existing convention — no attempt to populate Document.sprk_containerid.**

### Gaps
| # | Gap | Impact |
|---|---|---|
| G1 | No resolution path exists for `sprk_searchindexname` in any of the 5 parent-record create wizards or in `DocumentUploadWizard` | New field is unused; semantic search still routes by tenant default only |
| G2 | `CreateProjectWizard` does not set `sprk_containerid` (10/10 sampled Projects show NULL) | Latent bug — Projects created via the wizard have no container reference. Documents created under them have to fall back to user-BU. Fix alongside this project. |
| G3 | Other create paths (direct Dataverse API, mobile, future imports) don't populate `sprk_searchindexname` | Wizards are the only paths that get it right; other creates produce empty-field records. Backfill catches them. |
| G4 | BFF `IKnowledgeDeploymentService.GetSearchClientAsync(tenantId)` ignores any caller-supplied index name | Even if client knows the right index, server can't be told to use it |
| G5 | PCF + Code Page don't send `searchIndexName` in search request bodies | All searches go to the tenant default index regardless of scope |
| G6 | PCF → Code Page `navigateTo` envelope doesn't carry index identifier | Code page opened from "Open in Semantic Search" can show different results than the PCF |
| G7 | BU `sprk_searchindexname` values are NULL across all BUs today (schema exists, never populated) | Wizards would read NULL and persist NULL until operator sets BU values. Pre-deploy operator setup step. |
| G8 | Existing records have empty `sprk_searchindexname`, and BU's `sprk_containerid` has been changed since older records were created — so backfilling parent records from BU is now wrong | Backfill must derive index from each record's existing-document evidence (most common `sprk_graphdriveid` across child Documents), not from the (now-different) BU values |

---

## 3. Invariants (binding contracts)

Every implementation decision MUST honor these. The plugin code, BFF resolver, wizard, PCF, Code Page, and backfill scripts all derive from these:

### INV-1 — BU values are cascading defaults at create time
The Business Unit's `sprk_containerid` + `sprk_searchindexname` are **defaults for new records**, not runtime authority.

### INV-2 — Record's own fields are authoritative after create
Once a record exists, **its own `sprk_containerid` + `sprk_searchindexname` are the source of truth** for routing its documents.

### INV-3 — BU change does NOT propagate
Updating a BU's container/index does NOT cascade to existing records. **No auto-sync.** Old records continue to point at their original container/index; new records get the new BU defaults.

> This is the feature, not a bug. It enables the migration scenario (old docs stay in old index, new docs land in new) without any sync engine.

### INV-4 — Document inherits from its immediate parent record, not from the user's BU
A Document under a Matter takes its container + index from the **Matter's** fields (which themselves came from the Matter's BU at create time, possibly overridden). A Document NEVER reads the **current user's BU** to resolve routing.

> This is what enables "Protected Matter". A user with default-BU access to a Protected Matter still has their Document land in the Matter's protected container, not the user's BU default.

### INV-5 — Explicit overrides are sacred
If a field is set to a non-empty value at create time (regardless of source), no default-fill logic (plugins, backfill, future migrations) may overwrite it.

### INV-6 — Container reference and index travel together
The pair `(<container-reference>, sprk_searchindexname)` is the routing tuple. They're set together, never independently. The container-reference field is `sprk_containerid` on BU + parent records, and `sprk_graphdriveid` on Document — different names, same kind of value (an SPE container id).

### INV-7 — Resolution chain (canonical order)
For any record needing a container/index:
1. **Record's own field** (if set) — wins
2. **Parent's BU's field** (for Documents: parent record's BU; for Matters etc.: own BU) — cascading default
3. **Tenant-level default** (server fallback, defined in BFF config) — last resort

This chain is implemented at create-time (plugins + wizard) so reads at query-time are simple field reads — no chain traversal needed at search time.

### INV-8 — Plugins must never block create
Default-fill plugins log on lookup failure and leave fields empty. Empty values are recoverable (server fallback applies); a blocked create is not.

---

## 4. Architecture

### 4.1 Schema (done by user; documenting for completeness)

| Entity | Field | Purpose |
|---|---|---|
| `businessunit` | `sprk_containerid` (existing) | Default container for new records owned by this BU |
| `businessunit` | `sprk_searchindexname` (new) | Default index for new records owned by this BU |
| `sprk_matter` | `sprk_containerid` (existing), `sprk_searchindexname` (new) | Authoritative for this Matter's documents |
| `sprk_project` | same pair | Authoritative for this Project's documents |
| `sprk_invoice` | same pair | Authoritative for this Invoice's documents |
| `sprk_workassignment` | same pair | Authoritative for this Work Assignment's documents |
| `sprk_event` | same pair | Authoritative for this Event's documents |
| `sprk_document` | `sprk_containerid` (existing schema, currently unpopulated — G1), `sprk_searchindexname` (existing per user) | Authoritative for THIS document's storage + index |

### 4.2 Create-time inheritance — Spaarke Create Wizards (CANONICAL)

**Spaarke convention**: record creation for the entities this project targets is **intercepted by Spaarke create wizards** (Code Pages launched via ribbon commands). The wizards are the canonical place that BU → record cascading happens today, and they are the canonical place this project extends.

**No plugins. No Power Automate. No new Dataverse field mappings. No form scripts.** The two existing cascading mechanisms remain untouched:
- **OOB Dataverse attributemaps**: continue to cascade `securitybu + securitybuname` from BU to parent records (their current scope).
- **Spaarke `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` framework**: continues to power the "Matter to Event" / "Project to Event" regarding-selection on Event forms (its current scope).

#### Evidence-based reality (verified during design review, 2026-06-05)

MCP queries against live data + codebase trace surfaced:

| Concern | Verified state |
|---|---|
| `businessunit.sprk_searchindexname` + parent schema fields | EXIST ✓ (added by operator) |
| BU `sprk_searchindexname` values | **NULL on every BU** — operator hasn't populated yet |
| Existing Matter records' `sprk_containerid` | **POPULATED** (10/10 sampled; mix of old `b!yLRd...` and new `b!vzGD...` reflecting the migration) |
| Existing Project records' `sprk_containerid` | **NULL** (10/10 sampled — latent gap in CreateProjectWizard) |
| Mechanism populating Matter.sprk_containerid today | **`CreateMatterWizard` code page** — [matterService.ts:216](../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L216): `entity['sprk_containerid'] = this._containerId;` |
| Sitemap "+ New" interception | `sprk_wizard_commands.js` ribbon command launches the wizard instead of opening the default form |
| OOB businessunit → {matter, project, invoice, workassignment} entitymaps | EXIST ✓, but only carry `businessunitid → sprk_securitybu` and `name → sprk_securitybuname` (their scope is security-BU + name, NOT container/index) |
| Spaarke `sprk_fieldmappingprofile` records | 2 active: "Matter to Event", "Project to Event" — scoped to regarding-selection on Event forms only |

**Conclusion**: the cascade for container/index is wizard-driven, not platform-mapping-driven. The latent gap on Project is a CreateProjectWizard bug, not a missing field mapping.

#### 4.2.1 Extend the 5 parent-record create wizards

Each of these Code Pages launches when the user clicks "+ New" for its respective entity (via ribbon command interception):

| Wizard | Today | Add for this project |
|---|---|---|
| `CreateMatterWizard` | Sets `sprk_containerid` from user's BU ✓ | Also set `sprk_searchindexname` from user's BU |
| `CreateProjectWizard` | **Latent gap**: does not set `sprk_containerid` (Project records show NULL) | Set BOTH `sprk_containerid` AND `sprk_searchindexname` from user's BU |
| `CreateInvoiceWizard` | TBD (verify in spec) | Same — both fields if missing |
| `CreateWorkAssignmentWizard` | TBD | Same — both fields if missing |
| `CreateEventWizard` | TBD | Same — both fields if missing |

Each wizard already reads user's BU to resolve container id; the extension is a second lookup (`sprk_searchindexname` on the same BU record) and an extra field in the create payload. Spec phase identifies the exact `EntityCreationService` / per-wizard `*Service.ts` extension points and verifies the latent containerid gap on CreateProjectWizard.

#### 4.2.2 Extend `DocumentUploadWizard`

Already handles container resolution via `AssociateToStep::resolveContainerIdForRecord` (parent record's `sprk_containerid` → user-BU fallback). Two extensions:
- Add `resolveSearchIndexNameForRecord(xrm, entityLogicalName, recordId)` mirroring the existing function shape — read parent record's `sprk_searchindexname` first, fall back to parent's owner BU's `sprk_searchindexname`, then to empty
- Add `sprk_searchindexname` to the Document create payload in `DocumentRecordService.buildRecordPayload` (continue setting `sprk_graphdriveid` as today; no `sprk_containerid` on Document per Spaarke convention)

#### 4.2.3 BU value setup (operator prerequisite — see §5.0)

The wizards read `sprk_searchindexname` from user's BU. Operator must populate this field on each BU *before* the extended wizards ship to users — otherwise wizards would read NULL and persist NULL on new records. See §5.0.

#### 4.2.4 Non-wizard create paths

Anything that creates Matter/Project/etc./Document records via raw API outside the wizards (Power Automate, custom apps, mobile, future imports) leaves `sprk_searchindexname` (and possibly `sprk_containerid`) empty.

Mitigation: **rely on backfill (§5)** for one-time correction. BFF resolver falls back to tenant default when field is empty (search still works, just at tenant index). We do not introduce plugins or background jobs to catch these.

> **Trade-off acknowledged**: records created via undocumented programmatic paths may have empty fields until backfill runs. Acceptable in dev/test (per §9 round-3 resolution); production handling is a future epic.

### 4.3 BFF resolver extension

**Existing resolver context (verified during design review)**: `IKnowledgeDeploymentService` already supports per-tenant routing via the `sprk_aiknowledgedeployment` Dataverse entity (3 deployment models: Shared, Dedicated, CustomerOwned) with fallback to `appsettings.AiSearch.KnowledgeIndexName` (default `spaarke-knowledge-index-v2`). See [IKnowledgeDeploymentService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs).

Signature change:

```csharp
// Before
Task<SearchClient> GetSearchClientAsync(string tenantId, CancellationToken ct);

// After
Task<SearchClient> GetSearchClientAsync(
    string tenantId,
    string? indexName = null,   // NEW: caller-supplied index name (record's sprk_searchindexname)
    CancellationToken ct = default);
```

Resolution chain inside the BFF:
1. If `indexName` is non-empty: **validate against an allow-list** (see §4.3.1) → if valid, resolve `SearchClient` for that index; if not in allow-list, return `ProblemDetails` **400 INDEX_NOT_ALLOWED**.
2. If `indexName` is empty: fall back to today's chain — `sprk_aiknowledgedeployment` for the tenant, then `appsettings.AiSearch.KnowledgeIndexName`.
3. If neither resolves: `ProblemDetails` **500 OPERATOR_MISCONFIG** (no default configured for tenant).

Request DTO changes — `SemanticSearchRequest` (and `RagSearchRequest`, `RecordSearchRequest`, etc.):
- Add optional `string? SearchIndexName { get; init; }`
- (No container parameter — confirmed §4.4 that the OData filter does not scope by container.)

Callers that don't pass `SearchIndexName` continue to work via the existing tenant resolution chain. **Backward-compatible.**

#### 4.3.1 Index allow-list validation (per design-review decision)

The BFF validates `indexName` against an allow-list to surface typos in BU field values and misconfiguration as 400s with a helpful error rather than silently routing to a nonexistent index. Implementation options (resolved in spec):

- **Static appsettings list**: `AiSearch.AllowedIndexes: ["spaarke-knowledge-index-v2", "spaarke-file-index", "discovery-index", "spaarke-rag-references"]` — simple, version-controlled, requires redeploy to add an index
- **Computed from `sprk_aiknowledgedeployment` records** for the tenant: dynamic, no redeploy needed, but adds a Dataverse read per request (cache-friendly)

Recommendation: **start with static appsettings list** for R1; promote to dynamic config if/when index churn justifies it.

### 4.4 Client contract (PCF + Code Page)

**Confirmed by BFF trace** ([SearchFilterBuilder.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SearchFilterBuilder.cs)): the OData filter is `tenantId eq '...' AND parentEntityType eq '...' AND parentEntityId eq '...'` plus optional filters. **No container/drive filter exists.** Multiple containers can share one index — the index distinguishes documents by tenant + parent-entity scope, not by container. So the client only needs to send `searchIndexName` (which physical index to query); no container parameter is needed.

#### PCF (`SemanticSearchControl`)
- Manifest already has `scopeId` as a bound property. Add one new bound property:
  - `searchIndexName` (bound to `sprk_searchindexname` on the scope record)
- `SemanticSearchApiService.search()` populates request body with `searchIndexName`
- `NavigationService.openSemanticSearchPage()` includes `searchIndexName` in the navigateTo data envelope
- For `searchScope === 'all'` (system-wide search, no scope record): `searchIndexName` is empty → server uses tenant default

#### Code Page (`sprk_semanticsearch`)
- `parseUrlParams` reads `searchIndexName` from the envelope
- App.tsx propagates it to `useSemanticSearch` + `useRecordSearch` hooks
- Hooks include it in request bodies

### 4.5 (removed)
Originally a separate `DocumentUploadWizard` extension section — folded into §4.2.2 since both parent-record wizards and the Document wizard are the same mechanism (Spaarke Code Page create wizards).

---

## 5. Backfills (one-time after deploy)

**Execution mechanism**: All backfill + audit scripts are PowerShell (`.ps1`) run from the Claude Code project as one-time operations. No background jobs, no scheduled reconciliation — single shot, idempotent, resumable on failure (checkpoint per N records).

**Critical context**: BU's `sprk_containerid` has been changed (the migration that motivated this project). Backfilling parent records from BU would produce **wrong values** for historical records — their documents are still stored in the OLD container/index. The backfill must derive each record's effective container from **evidence in the existing data** (each Document's `sprk_graphdriveid`), then map that container to its index name via a known table.

### 5.0 Operator setup — populate BU `sprk_searchindexname` values (PREREQUISITE)

Before any scripted backfill runs, the operator manually sets the `sprk_searchindexname` field on each Business Unit:

| BU | sprk_containerid (verified via MCP) | sprk_searchindexname (to set) |
|---|---|---|
| Spaarke Demo | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` | `spaarke-knowledge-index-v2` |
| Spaarke | `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` | `spaarke-file-index` |
| Spaarke Dev 1 | (NULL today) | operator-determined (R1: leave NULL → tenant default applies) |
| Spaarke Test 1 | (NULL today) | operator-determined (R1: leave NULL → tenant default applies) |

This step is a manual edit in Power Apps maker or via a small `.ps1` helper. Doing it BEFORE the field mappings are deployed means new records created post-deploy will pick up correct values via mapping.

### 5.1 Container → index name mapping (the table)

This mapping is fixed at design time and used by both backfill scripts and (optionally) by operator-side validation tooling:

| `sprk_graphdriveid` (SPE container) | `sprk_searchindexname` |
|---|---|
| `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` | `spaarke-knowledge-index-v2` |
| `b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh` | `spaarke-file-index` |
| (any other container id encountered) | **fail loud — operator must extend the map before backfill proceeds** |

> The "fail loud" rule is intentional. An unmapped container id in production data means the operator added a new container/index without updating this map — silently defaulting would hide the misalignment. Better to halt and surface.

### 5.2 Parent-record backfill (Matter / Project / Invoice / WorkAssignment / Event)

For each record:
1. Query its existing child Documents
2. IF ≥1 child Documents exist: take the **most common `sprk_graphdriveid` across them** (mode) as the record's effective container
3. IF 0 child Documents exist (record never had files): fall back to owner BU's current `sprk_containerid` (assumes BU's current value is correct for new records)
4. IF record's `sprk_containerid` is empty → set it from step 2/3
5. IF record's `sprk_searchindexname` is empty → map step 2/3's container id via §5.1 table; if unmapped, halt with surfaceable error
6. **NEVER overwrite existing values** (INV-5)
7. Generate per-record audit line: existing value, derived value, source (existing-doc-mode / BU-fallback), action taken (filled / skipped)

### 5.3 Document backfill

For each Document with empty `sprk_searchindexname`:
1. Read its `sprk_graphdriveid` (the SPE container the file actually lives in)
2. Map via §5.1 table → set `sprk_searchindexname`
3. IF `sprk_graphdriveid` is also empty (orphan Document): log + skip for manual review
4. IF `sprk_graphdriveid` is unmapped: halt with surfaceable error (operator extends the map)
5. **NEVER overwrite existing values** (INV-5)

This is simpler than the parent-record backfill because the Document already holds the authoritative container reference — no aggregation across children needed.

### 5.4 Drift audit (informational, no writes)
Generate a report listing records where the stored value DIFFERS from what the chain would derive today. Distinguishes:
- **Intentional overrides**: record's value differs from BU's; record was deliberately routed elsewhere (Protected Matter or pre-migration record)
- **Possible data drift**: BU was changed post-create (the current migration); record kept old value (this is CORRECT per INV-3)
- **Anomalies**: unmapped containers, parent-Document container mismatches → flagged for manual investigation

Operator reviews; intentional overrides and BU drift are normal post-migration. Only anomalies require action.

---

## 6. Operator runbook (content outline)

- **Pre-deploy (one-time)**: populate `sprk_searchindexname` on every BU that currently has a `sprk_containerid`. Reference §5.0 table (Spaarke Demo → `spaarke-knowledge-index-v2`, Spaarke → `spaarke-file-index`). This MUST happen before the extended wizards ship to users.
- **Deploy**: deploy the 5 extended create wizards (Code Pages) + extended DocumentUploadWizard + BFF v.next + PCF v1.1.74 + SemanticSearch code page. Run the backfill PowerShell scripts once.
- **How to assign a new index to a BU**: edit BU `sprk_searchindexname`. **New records created via the wizards** inherit the new value. Existing records are unaffected (INV-3).
- **How to mark a single record as Protected**: set its `sprk_containerid` + `sprk_searchindexname` explicitly on the record. Wizard and backfill respect explicit overrides (INV-5).
- **Drift coexistence model**: old records keep their original routing; "drift" between BU and record values is intentional after BU changes.
- **Adding a new physical index** (rare, requires R1+ work): add to `appsettings.AiSearch.AllowedIndexes` allow-list + redeploy BFF → set BU `sprk_searchindexname` to new value → first record created via wizard flows to new index.
- **Document container reference clarification**: the canonical Document container field is `sprk_graphdriveid`, NOT `sprk_containerid` (which is intentionally blank on Documents).
- **Non-wizard creates**: records created outside the 5 wizards (raw API, Power Automate, mobile) will have empty `sprk_searchindexname` until backfill runs. Acceptable in dev/test; production handling is a future epic.

---

## 7. Trade-offs & alternatives considered

### Alternative A — BFF re-reads record at search time instead of trusting client
**Considered**: Client passes only `scope=entity` + `entityType` + `entityId`. BFF re-loads the record, reads `sprk_containerid` + `sprk_searchindexname`, then queries.

**Rejected because**:
- Extra Dataverse round-trip on every search (slower, more load)
- Defensive against tampering but the worst case is "user sees empty result set" — no cross-tenant data leak (Azure AI Search has its own auth)
- The PCF/Code Page already read these fields from Dataverse via Xrm.Page or bound parameters; client tampering would require modifying the bundle in DevTools, at which point the user can just talk directly to the BFF
- Chosen design (A.2 below) is faster and equally safe in practice

### Alternative B — BU-change fan-out / auto-sync
**Considered**: When BU's `sprk_containerid` or `sprk_searchindexname` changes, fire an async job that updates all child records to the new value and re-indexes their documents.

**Rejected because**:
- Breaks INV-3 (BU change doesn't propagate)
- Re-indexing thousands of documents is expensive and noisy
- The migration use case is BETTER served by coexistence (old records stay where they are; new records flow to the new index)
- Adds a sync engine with its own correctness model
- Operator can always trigger a per-record re-index when truly needed

### Alternative C — Single config entity (`sprk_aiindexconfig`) keyed by container
**Considered**: Store `(containerId, indexName)` pairs in a separate entity. Record only stores `sprk_containerid`; index is looked up via this table.

**Rejected because**:
- One more entity to maintain
- Extra Dataverse read at create time (plugin chain becomes longer)
- The denormalized approach (both fields directly on record) is simpler and matches how `sprk_containerid` already works
- The "fields travel together" invariant (INV-6) is enforced naturally by the plugin code

### Alternative F — Add `sprk_containerid` + `sprk_searchindexname` attributemap rows to existing OOB entitymaps
**Considered**: Create the missing attributemap rows on the existing `businessunit → {matter, project, invoice, workassignment}` entitymaps, create the missing `businessunit → sprk_event` entitymap, and add equivalent rules to the parent → sprk_document entitymaps.

**Rejected because**:
- MCP + data trace proved this isn't how the cascade works today. `CreateMatterWizard` sets `sprk_containerid` programmatically; OOB attributemaps only carry `securitybu + name`.
- Adding attributemap rows would only fire on sub-grid "+ New" + Quick Create flows. The dominant Spaarke create path is the ribbon-intercepted Code Page wizards, which set fields BEFORE the create call — attributemaps would either no-op (wizard already set the value) or compete (with last-write-wins semantics that are not what we want).
- Two mechanisms for the same job invites drift. Wizard-driven is the canonical pattern; we extend it.

### Alternative G — Extend `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` framework for BU → parent → Document
**Considered**: Add new profile records for `businessunit → {matter, project, invoice, workassignment, event}` and `{parent} → sprk_document`, with appropriate rules. Use the existing BFF `/api/v1/field-mappings/push` endpoint for backfill.

**Rejected because**:
- The framework runs **client-side** in the `AssociationResolver` PCF, triggered by **regarding-record selection** on a form. It does not fire during the create wizards' programmatic record creation.
- Wiring a new trigger (form-onLoad invocation) is more work than the wizard extension and produces double-write conflicts (wizard sets `sprk_containerid`; framework runs after and sets it again).
- The framework remains the right mechanism for its current scope ("Matter to Event" regarding-selection) and for future cases needing richer logic (type validation, push, bidirectional). It's not the right hammer for the create-time default cascade.

### Alternative D — Dataverse plugins as the create-time enforcer
**Considered**: A pre-create plugin on each parent entity + on Document, with default-fill logic.

**Rejected because**:
- **Spaarke convention does not use plugins** (per design review feedback)
- Plugins introduce a deployment lifecycle, signing, version drift, and runtime failure modes that the team has chosen to avoid platform-wide
- The existing `sprk_containerid` inheritance works WITHOUT plugins today — extending the same mechanism for `sprk_searchindexname` keeps the platform internally consistent

### Alternative E — Power Automate flows on Create
**Considered**: A flow per entity that runs on Create and fills the fields from BU / parent.

**Rejected because**:
- **Spaarke convention does not use Power Automate** (per design review feedback)
- Same concerns: lifecycle, version drift, latency, observability

### Chosen design (A.2): Denormalize `sprk_searchindexname` onto every record, extend the existing Spaarke create wizards, trust client at query time
- Simple read path (one new field on the bound entity)
- **Existing Spaarke create wizards (5 parent + 1 Document) are the canonical cascade mechanism** — extended in-kind for the new field. No new enforcement layer.
- Wizard sets values at create time — operator-visible in the wizard UI, persisted on the record before the record exists
- BFF resolver does last-mile validation (index exists in tenant's allowed set)
- Backfill catches historical records using existing-data evidence (Document.sprk_graphdriveid as ground truth)
- Document's canonical container reference is `sprk_graphdriveid` (existing); `sprk_searchindexname` is the only new field touched on Document
- OOB Dataverse mappings and Spaarke FieldMappingProfile framework continue unchanged within their existing scopes — three orthogonal layers (OOB platform, Spaarke custom mapping, Spaarke create wizards)

---

## 8. Out of scope (R1)

- **Dataverse plugins** (Spaarke convention)
- **Power Automate flows** (Spaarke convention)
- Populating `sprk_containerid` on `sprk_document` (canonical Document container field is `sprk_graphdriveid` — `sprk_containerid` on Document remains blank by convention)
- Automatic BU-change fan-out / sync (Alternative B — explicitly rejected)
- Cross-tenant search (BFF resolver could be extended for this, but adds auth + isolation complexity beyond R1)
- End-user index picker UI (operator configures indexes via Dataverse + appsettings; users don't pick)
- Search query parity between PCF and Code Page (folded INTO this R1 — see §10 below)
- New AI Search index provisioning automation (operator creates the index in Azure; this project routes to it)
- Migration of historical documents between physical indexes (operator-triggered, separate effort)

---

## 9. Open questions — RESOLVED (round 3 review, 2026-06-05)

All previously-open questions resolved during round-3 design review:

| # | Question | Resolution |
|---|---|---|
| 1 | Tenant default storage location | **Confirmed via code review**: 2-tier — `sprk_aiknowledgedeployment` Dataverse entity (per-tenant, 3 deployment models: Shared/Dedicated/CustomerOwned) → `appsettings.AiSearch.{KnowledgeIndexName, DiscoveryIndexName, RagReferencesIndexName}` (env-level fallback, default `spaarke-knowledge-index-v2`). See `IKnowledgeDeploymentService.cs` + `appsettings.template.json:207-214`. |
| 2 | BFF allowed-index validation | **YES, validate.** Static allow-list in `appsettings.AiSearch.AllowedIndexes` for R1. Mismatches → 400 `INDEX_NOT_ALLOWED`. |
| 3 | Backfill execution mechanism | **PowerShell `.ps1` scripts run from the Claude Code project as one-time operations.** Idempotent + resumable; no scheduled jobs. |
| 4 | Drift audit cadence | **One-time at deploy.** Run alongside backfill. Not recurring. |
| 5 | Dataverse setup verification | **Verified via MCP** (see §4.2 "Important MCP-verified reality"). Schema fields exist on BU + Matter; BU values are NULL and field mappings DON'T currently include container/index. Project must populate BU values AND create the missing attributemap rows + the missing `businessunit → sprk_event` entitymap. |
| 6 | PCF version | **Current baseline: v1.1.73** (packed, awaiting import). This project produces **v1.1.74**. |
| 7 | Document orphans | **Out of scope** — dev/test data, no value in handling them now. Backfill logs + skips. |
| 8 | Re-indexing API | **Out of scope for R1.** When operators correct an index assignment on an existing record, its documents stay indexed where they were originally indexed (acceptable in dev/test). Production re-indexing API is a future epic. |
| 9 | Container → index map maintenance | **Out of scope for R1** — hardcoded in backfill script (2 entries today, ~5 max foreseeable). Promotion to config entity is future concern. |
| 10 | Container-id parameter in search request | **Resolved via BFF trace**: `SearchFilterBuilder.cs` has no container/drive filter. Multiple containers can share one index, distinguished by `tenantId` + `parentEntityType` + `parentEntityId`. Client sends only `searchIndexName`. |
| 11 | Inheritance mechanism | **Resolved via MCP**: declared as Dataverse Entity Relationship Field Mappings. Existing mappings DON'T currently cover container/index (they only carry the security-BU lookup + name). This project must CREATE the missing attributemap rows. |

All §9 items resolved. Ready for promotion to `spec.md`.

---

## 10. Folded scope (formerly separate work)

### 10.1 Search-result parity (was: "fix Open button result-set match")
Since this project changes the PCF→Code Page envelope and the search request bodies anyway, fold in the parity fix:
- PCF passes `query`, `scope`, `entityId`, `threshold`, `searchMode`, `fileTypes`, `dateFrom`, `dateTo`, `tags`, `associatedOnly` in the navigateTo envelope
- Code Page `parseUrlParams` reads them all
- Code Page `App.tsx` seeds `filters` + `selectedTags` state before auto-search
- Stop the `void initialScope; void initialEntityId;` discards in `App.tsx`
- Net: PCF Open icon → Code Page modal shows the EXACT same result set the PCF was showing (same query + scope + filters + tags + container + index)

### 10.2 (removed)
Originally folded "wizard sets Document.sprk_containerid" here. **Removed after design review**: `sprk_containerid` on Document is blank by Spaarke convention (canonical field is `sprk_graphdriveid`, which the wizard already sets correctly). No fix needed.

---

## 11. Phase outline (informs WBS in spec.md)

| Phase | Work | Acceptance |
|---|---|---|
| A — Spaarke Create Wizard extensions | Extend 5 parent-record wizards (`CreateMatterWizard`, `CreateProjectWizard`, `CreateInvoiceWizard`, `CreateWorkAssignmentWizard`, `CreateEventWizard`) + `DocumentUploadWizard` to read `sprk_searchindexname` from BU (or parent record) and include it in the create payload. Fix latent `sprk_containerid` gap in `CreateProjectWizard`. | New records created via wizards have both `sprk_containerid` AND `sprk_searchindexname` set. MCP re-verification confirms recent post-deploy Matter / Project / Invoice / WorkAssignment / Event records have both fields populated. |
| A.5 — Operator BU value setup | Populate `sprk_searchindexname` on `Spaarke Demo` BU (`spaarke-knowledge-index-v2`) and `Spaarke` BU (`spaarke-file-index`) per §5.0 | MCP query confirms values populated; wizards read non-NULL values when creating new records |
| B — BFF resolver | `IKnowledgeDeploymentService.GetSearchClientAsync` signature extension (`indexName?` param) + `appsettings.AiSearch.AllowedIndexes` allow-list + request DTO additions + `400 INDEX_NOT_ALLOWED` ProblemDetails + unit tests | Resolver picks explicit index when supplied; validates against allow-list; falls back cleanly to existing `sprk_aiknowledgedeployment` → appsettings chain when `indexName` empty |
| C — (folded into A) | DocumentUploadWizard extension is one of the 6 wizards extended in Phase A | — |
| D — PCF v1.1.74 | One new bound manifest property (`searchIndexName`); SemanticSearchApiService sends it; NavigationService envelope includes it + filter parity (§10.1) | PCF on a Protected Matter searches the protected index; Open icon shows matching result set |
| E — Code Page | `parseUrlParams` extended; App.tsx wires `initialScope` + `initialEntityId`; hooks include `searchIndexName` in request bodies | Code Page modal launched from PCF shows the exact same documents |
| F — Backfills | Container→index map (§5.1) baked into script + Parent-record backfill + Document backfill + drift audit report | Empty fields filled from existing-Document evidence + map; explicit values preserved; report flags drift; halts loudly on unmapped container |
| G — Operator docs + runbook | Runbook content (§6); operator setup for new PCF manifest property on hosting forms; container→index map maintenance procedure | Operator can self-serve Protected Matter setup, BU changes, drift handling, new-container onboarding |
| H — Deploy + UAT | Coordinated deploy: BU value setup (A.5) → BFF (B) → wizards (A) → PCF v1.1.74 (D) → Code Page (E) → backfill (F) | Smoke + parity tests pass; Protected Matter walkthrough verified |

---

## 12. Risks

| Risk | Mitigation |
|---|---|
| One of the 5 wizards skipped during extension (e.g., we miss CreateInvoiceWizard) | Spec checklist enumerates all 5 explicitly; verification: after deploy, create one record via each wizard and MCP-query the new record for both fields populated. Backfill is the safety net. |
| CreateProjectWizard `sprk_containerid` latent bug not fixed alongside the index extension | Explicitly called out as G2; spec task includes fixing it. Skip = production Projects continue with NULL container after deploy. |
| Non-wizard create paths (Power Automate, direct API, mobile) leave fields empty | Backfill catches them; BFF falls back to tenant default in the meantime. Acceptable in dev/test (§9 round-3 resolution). |
| Operator forgets to populate BU `sprk_searchindexname` values before wizards ship | Phase A.5 in deploy sequence (§11) puts BU value setup BEFORE wizard ship. Runbook (§6) calls it out as pre-deploy. |
| Backfill on large data sets times out | Page execution; resumable script; checkpoint per N records |
| Backfill encounters a `sprk_graphdriveid` not in the §5.1 map | Halt loud with surfaceable error; operator extends the map and re-runs — better than silently mapping to a wrong default |
| BU field typo → all new records get bad index name | BFF validates against allow-list; helpful error surfaces fast |
| Old records routed to deprecated indexes | Drift audit + operator-aware migration; coexistence is the default per INV-3 |
| Deploy order matters | Deploy plan in §Phase H; backward-compatible at every step (server tolerates client not yet sending fields) |
| Orphan Documents (no parent reference, no `sprk_graphdriveid`) | Backfill logs + skips; flagged for manual review |
| Cross-cutting code-page change requires republish of every Custom Page hosting the PCF | Document in operator runbook; existing `Deploy-AllDataGridConsumers.ps1` pattern for atomic republish |
| Document.sprk_containerid stays blank — surprises a future developer who expects it populated | Add operator runbook note: "Document container reference is `sprk_graphdriveid`, not `sprk_containerid`. The latter is intentionally blank." |

---

## 13. Decision log (during design phase)

| Date | Decision | Rationale |
|---|---|---|
| 2026-06-05 | Rejected BU-change auto-sync (Alternative B) | Coexistence is the desired model (INV-3); migration is operator-triggered, not automatic |
| 2026-06-05 | Trust client-supplied `indexName` at BFF (with allow-list validation) instead of BFF re-reading record | Faster; equally safe given Azure AI Search has its own auth |
| 2026-06-05 | Fold search-result parity into this project (§10.1) | Same envelope work; doing them together avoids two PCF versions |
| 2026-06-05 | (reviewer) **No Dataverse plugins** — extend existing Spaarke inheritance mechanism in-kind | Spaarke platform convention does not use plugins |
| 2026-06-05 | (reviewer) **No Power Automate** — same reasoning | Spaarke platform convention does not use PA flows |
| 2026-06-05 | (reviewer) **Canonical Document container field is `sprk_graphdriveid`**, not `sprk_containerid` | `sprk_containerid` on Document is blank by convention; `sprk_graphdriveid` is already wired through every code path; introducing `sprk_containerid` on Document would create a duplicate |
| 2026-06-05 | (reviewer) **Backfill sources from existing-data evidence**, not from BU | BU's `sprk_containerid` has been changed; sourcing from BU would produce wrong values for historical records. Parent records derive container from mode of child Documents' `sprk_graphdriveid`; Documents derive index from their own `sprk_graphdriveid` via a hardcoded map (§5.1) |
| 2026-06-05 | (reviewer) Backfill HALTS on unmapped container | Silent default would hide misalignment; loud halt forces operator to extend the §5.1 map deliberately |
| 2026-06-05 | (reviewer) Removed Document.sprk_containerid fix from folded scope | Not needed — canonical Document container field is `sprk_graphdriveid` |
| 2026-06-05 | (reviewer) **Inheritance mechanism = Dataverse Entity Relationship Field Mappings** on businessunit→{matter, project, invoice, workassignment, event} 1:N | Confirmed by user. Same mechanism already populates `sprk_containerid`; extending it for `sprk_searchindexname` is one mapping rule per relationship. Wizard handles its own programmatic creates. |
| 2026-06-05 | (reviewer) **Backfill tiebreaker = majority/mode of child Documents' `sprk_graphdriveid`** | Confirmed by user (vs. "most recent" or "null + flag for review") |
| 2026-06-05 | (verified in BFF trace) **No container/drive filter exists in `SearchFilterBuilder`** | Index can host multiple containers' documents; scope is by tenant + parent-entity. PCF only sends `searchIndexName`. |
| 2026-06-05 | (round 3, MCP-verified) **Existing entity field mappings DON'T currently inherit container/index** | The 4 existing `businessunit→parent` entitymaps only map securitybu + name. The `sprk_matter→sprk_document` entitymap only maps matter lookup + name. The `businessunit→sprk_event` entitymap is MISSING entirely. This project must CREATE the missing attributemap rows and the new event entitymap. |
| 2026-06-05 | (round 3) **Tenant default = 2-tier (Dataverse + appsettings)** | `IKnowledgeDeploymentService` consults `sprk_aiknowledgedeployment` entity first (3 deployment models), then falls back to `appsettings.AiSearch.{KnowledgeIndexName, DiscoveryIndexName, RagReferencesIndexName}` |
| 2026-06-05 | (round 3) **BFF allow-list validation YES** | Static `appsettings.AiSearch.AllowedIndexes` in R1. 400 INDEX_NOT_ALLOWED on miss. |
| 2026-06-05 | (round 3) **Backfill = one-time PowerShell `.ps1` from Claude Code project** | Idempotent, resumable; no scheduled jobs |
| 2026-06-05 | (round 3) **Drift audit = one-time at deploy** | Not recurring; reviewed once |
| 2026-06-05 | (round 3) **PCF v1.1.74 = this project's release** | Baseline is v1.1.73 (already packed) |
| 2026-06-05 | (round 3) **Orphan Documents OUT OF SCOPE** | Dev/test data; no value in handling now |
| 2026-06-05 | (round 3) **Re-indexing API OUT OF SCOPE for R1** | Records keep documents in original index after operator correction; production re-indexing is a later epic |
| 2026-06-05 | (round 3) **Container→index map maintenance OUT OF SCOPE for R1** | Hardcoded in backfill script (2 entries today, ~5 max foreseeable) |
| 2026-06-05 | (MCP-verified) **BU `sprk_searchindexname` values are NULL today**; operator setup is a prerequisite step | Spaarke Demo → `spaarke-knowledge-index-v2`; Spaarke → `spaarke-file-index` |
| 2026-06-05 | (round 4, data-trace verified) **REVERSAL: inheritance mechanism is Spaarke Create Wizards, NOT field mappings** | MCP showed 10/10 recent Matters have `sprk_containerid` populated but Projects don't. Trace led to `CreateMatterWizard/matterService.ts:216`: `entity['sprk_containerid'] = this._containerId;`. Sitemap "+ New" is intercepted by `sprk_wizard_commands.js` ribbon command → launches CreateMatterWizard Code Page → wizard sets fields before create. OOB attributemaps don't cascade container; Spaarke FieldMappingProfile is scoped to regarding-selection. Wizards are the canonical cascade. |
| 2026-06-05 | (round 4) **Both existing mechanisms remain untouched** — OOB attributemaps continue cascading `securitybu + name`; Spaarke FieldMappingProfile continues powering "Matter to Event" / "Project to Event" regarding | Three orthogonal layers: OOB platform (security_bu + name), Spaarke custom mapping (regarding-selection enrichment), Spaarke create wizards (container/index defaults). Each is the right primitive for its scope. |
| 2026-06-05 | (round 4) **CreateProjectWizard latent bug discovered**: doesn't set `sprk_containerid`. Fold the fix into this project | Project records have NULL `sprk_containerid`; new Documents under them have to fall back to user-BU at upload-time. Wizard pre-set is the right place to fix it. |

---

## 14. Next steps

1. **Review this design.md** — find disagreements, missing invariants, unclear architecture, wrong assumptions about the current code
2. **Resolve open questions** (§9) — at minimum #3 (tenant default storage location) and #5 (backfill mechanism) before spec
3. **Promote design → spec.md** — translate this into AI-implementable requirements (FR-*, NFR-*, acceptance criteria)
4. **Run `/project-pipeline`** — generate plan.md, CLAUDE.md, ~30-40 POML tasks
5. **Execute on a new worktree** — separate from R1 cleanup and the v1.1.73 PCF UI-tweak branch
