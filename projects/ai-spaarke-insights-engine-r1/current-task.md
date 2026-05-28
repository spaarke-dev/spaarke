# Current Task — Spaarke Insights Engine, Phase 1

> **Status**: ✅ idle — task 011 just completed
> **Last Updated**: 2026-05-28
> **Project state**: Wave 1 complete; Wave 2 in progress (task 010 in parallel, task 011 done)

---

## Active task

**none** — ready for next task pickup. Task 010 (D-P2 Bicep) was previously active; task 011 (D-P3 sprk_precedent entity) just completed in parallel and is recorded under "Last completed tasks" below.

### Environment context (loaded for this task)
- Subscription: `484bc857-3802-427f-9ea5-ca47b43db0f0` (Spaarke Dev), tenant `a221a95e-...`, user `ralph.schroeder@spaarke.com`
- RG: `spe-infrastructure-westus2` (westus2)
- AI Search: `spaarke-search-dev` (standard, 2 replicas / 1 partition; identity:none — need to enable SystemAssigned for vectorizer if used)
- AIServices (OpenAI): `spaarke-openai-dev` in eastus; `text-embedding-3-large` deployed (3072 dim) ✓
- Key Vault: `sprkspaarkedev-aif-kv`
- App Insights: `spe-insights-dev-67e2xz`; Log Analytics: `spe-logs-dev-67e2xz`
- Existing UAMI (for BFF, not for Functions): `mi-bff-api-dev` — task creates a NEW per-tenant UAMI
- Existing indexes on `spaarke-search-dev`: discovery-index, knowledge-index, playbook-embeddings, spaarke-invoices-dev, spaarke-knowledge-index{,-v2}, spaarke-knowledge-shared, spaarke-rag-references, spaarke-records-index — `spaarke-insights-index` does NOT exist ✓
- Reference schema (3072-dim vector profile + HNSW + semantic config): `spaarke-rag-references` — used as structural template

### Tooling versions
- az CLI 2.77.0, Bicep CLI 0.41.2 — both functional

---

## Last completed tasks

**Task 011 — D-P3 (entity) sprk_precedent Dataverse entity + relationships** ✅ (2026-05-28)
- Rigor: STANDARD (Dataverse schema; quality gates inside dataverse-create-schema skill)
- Solution: created `spaarke_insights` (Spaarke publisher, prefix `sprk`) — new solution in Spaarke Dev
- Entity: `sprk_precedent` (LogicalName, SchemaName=sprk_Precedent, EntitySetName=sprk_precedents); primary name `sprk_name`
- Fields (8 custom): sprk_patternstatement (Memo 4000), sprk_status (Picklist→sprk_precedentstatus), sprk_reviewerby (Lookup→systemuser), sprk_reviewdate (DateOnly), sprk_effectivenessscore (Decimal 0-1, Phase 1.5+), sprk_clusterdefinition (Memo 2000), sprk_samplesize (Integer), sprk_producedby (String 200)
- Option set `sprk_precedentstatus` (global, 5 values): Tentative(100000000) / Confirmed(100000001) / Under Drift Review(100000002) / Deprecated(100000003) / Retired(100000004)
- N:N relationships: `sprk_precedent_matter` (supporting matters), `sprk_precedent_related` (self) — both created
- N:N DEFERRED: `sprk_precedent_observation` — sprk_observation entity does not exist in Phase 1 (will land with D-P11)
- Views: `Active Precedents` (default), `Tentative Precedents`, `Confirmed Precedents`, `Precedents Under Drift Review`, `Deprecated Precedents`
- Form: default Main form ("Information") added to solution
- Solution exported + unpacked to `src/solutions/spaarke_insights/` (27 files)
- Scripts created: `scripts/Deploy-PrecedentEntity.ps1` (idempotent, 5-phase pattern), `scripts/Deploy-PrecedentViewsAndForm.ps1`
- All 6 acceptance criteria PASS via Web API verification (queryable, fields present, 5 status values, N:Ns, views, solution export)

**Task 002 — D-P17 IInsightGraph interface + stub** ✅ (2026-05-28)
- Files: `Services/Insights/Graph/{IInsightGraph,InsightVertex,InsightEdge,GraphTraversalSpec,StubInsightGraph}.cs` + `Infrastructure/DI/InsightsModule.cs` + `Program.cs` registration + `tests/.../StubInsightGraphTests.cs`
- Tests: 9/9 pass (standalone verifier — DI resolution + 7 method NotImplementedException assertions, all with "Phase 1.5" + "SPEC §3.3" message check); test project Sprk.Bff.Api.Tests still has pre-existing compile errors unrelated to this task
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — 0 errors, 17 pre-existing warnings (none in new files)
- SPEC §3.5.4 forbidden-imports grep: clean (zero matches in `Services/Insights/Graph/`)
- Quality gates: skipped per STANDARD rigor; design discipline still applied (Zone B isolation, ADR-010 seam justification, D-09 no-Gremlin-leak)
- Preserves D-P17 swap path — CosmosNoSqlInsightGraph is first Phase 1.5 deliverable per SPEC §3.3
- Judgment: created new `InsightsModule` (Zone B) rather than extending Zone A `AnalysisServicesModule` — keeps §3.5 facade boundary visible in DI composition; ADR-010 §Exceptions permits interface seams when swap-path is real (it is)

**Task 001 — D-P1 InsightArtifact envelope POCOs** ✅ (2026-05-28)
- Files: `Models/Insights/{InsightArtifact,EvidenceRef,DeclineResponse}.cs` + `tests/.../InsightArtifactTests.cs`
- Tests: 7/7 pass (standalone runner); test project Sprk.Bff.Api.Tests has pre-existing compile errors unrelated to this task
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` clean — zero new warnings
- SPEC §3.5.4 forbidden-imports grep: clean
- Quality gates: code-review ✅ / adr-check ✅
- Foundation type for D-P3, D-P4, D-P6, D-P10, D-P11, D-P14, D-P15 (all downstream tasks consume this envelope)

---

## Next action

Wave 1 complete. Wave 2 (infrastructure provisioning) unlocks next — pick D-P2 (`spaarke-insights-index` schema + Bicep) or D-P3 (`sprk_precedent` Dataverse entity) from [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md). Both are parallel-safe to each other.

---

## Progress tracking

| State | Count |
|---|---|
| ✅ Completed | 3 (001, 002, 011) |
| 🔄 In progress | 0 |
| 🔲 Pending | 14 |
| ⏭️ Deferred (Phase 1.5+) | — see SPEC §3.3 |

---

## Context recovery

If a session is compacted or interrupted, this file is the entry point for recovery:

1. Read this file to see active task state
2. Read [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) for progress
3. Read [SPEC.md](SPEC.md) §3.1 for canonical deliverable list
4. Read [CLAUDE.md](CLAUDE.md) for project-scoped instructions
5. Read root [CLAUDE.md](../../CLAUDE.md) §4 for the mandatory task-execute protocol
6. Invoke `task-execute` for whatever task is `in_progress` (or pick the next 🔲 from the index)

---

## Decision log (per task)

### Task 002 (D-P17) — completed 2026-05-28

- **New `InsightsModule` vs extending `AnalysisServicesModule`**: chose new module. AnalysisServicesModule is Zone A (freely imports `IOpenAiClient`, `IPlaybookService`, `Microsoft.Extensions.AI`); §3.5 facade boundary mandates Zone B Insights code be wired separately so the boundary is visible in DI. ADR-010 §Exceptions permits new interfaces when there's a true seam — D-P17 IS a true seam (Phase 1 stub ↔ Phase 1.5 Cosmos impl).
- **Singleton lifetime for `IInsightGraph`**: future Cosmos impl will hold a `CosmosClient` which is itself thread-safe and intended to be reused; stub is stateless so lifetime is moot in Phase 1.
- **`StubInsightGraph` marked `internal sealed`**: nothing outside the assembly should depend on the concrete type — only `IInsightGraph` via DI. Sealed prevents test subclassing tricks that would obscure the swap intent.
- **`InternalsVisibleTo Sprk.Bff.Api.Tests` already present** on `Sprk.Bff.Api.csproj` so tests can reference the internal stub type for assertion-against-concrete in `BeOfType<StubInsightGraph>()`.
- **Records (positional/init-only) for all DTOs** — immutable, value equality, smaller boilerplate, matches task 001's choice for `InsightArtifact`. `IReadOnlyDictionary<,>` + `IReadOnlyList<>` for collections preserves immutability through the interface surface.
- **Named traversal discipline (D-09)**: `GraphTraversalSpec` deliberately exposes `EdgeTypeFilter`, `MaxHops`, `TargetVertexTypeFilter` as plain lists/ints — NOT Gremlin step syntax fragments or Cosmos SQL strings. This is what makes a NoSQL ↔ Gremlin implementation swap a contained refactor.
- **Pre-existing test infrastructure breakage continues** (same 7 errors as task 001 reported). Worked around by writing a standalone console verifier that exercises every interface method through the DI-registered stub; all 9 assertions passed. The shipped `StubInsightGraphTests.cs` will run cleanly once the unrelated test-project breakage is fixed.

### Task 001 (D-P1) — completed 2026-05-28

- Use C# **records** (immutable) over classes — matches POML "Record types" wording, cleaner serialization, no accidental mutation in pipelines.
- **`PrecedentArtifact`** does NOT carry `confidence` on the wire (matches SPEC §3.4.2 worked example `"confidence": null`; Precedents are SME-confirmed per D-46) and adds **`Status`** (lifecycle state per design §2.1 + SPEC §3.4.2 `"status": "confirmed"`).
- **`Value.Raw`** typed as `JsonElement` — preserves arbitrary JSON shapes (string enum, integer, nested Precedent object) without custom converters.
- **Polymorphism**: `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` + `[JsonDerivedType]` for each tier — standard System.Text.Json idiom; no custom converter needed.
- **Pre-existing test infrastructure breakage** (EmbeddingMigrationService / AppOnlyDocumentAnalysisJobHandler / EmailAnalysisJobHandler types missing from Sprk.Bff.Api) — verified pre-existing by stashing my changes and confirming build still fails with same 7 errors. Out of scope for task 001; should be tracked separately as test-cleanup work.
