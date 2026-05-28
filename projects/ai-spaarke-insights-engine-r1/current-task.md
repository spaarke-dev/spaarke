# Current Task — Spaarke Insights Engine, Phase 1

> **Status**: none (Wave 1 complete; awaiting next task / wave commit)
> **Last Updated**: 2026-05-28
> **Project state**: Phase 1 Wave 1 COMPLETE — task 001 (D-P1 InsightArtifact envelope POCOs) + task 002 (D-P17 IInsightGraph stub) both finished

---

## Active task

**No active task in this session.** Wave 1 closed; main session will batch the wave commit.

---

## Last completed tasks (Wave 1)

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

Wave 1 complete. Wave 2 (infrastructure provisioning) unlocks next — pick D-P2 (`insights-index` schema + Bicep) or D-P3 (`sprk_precedent` Dataverse entity) from [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md). Both are parallel-safe to each other.

---

## Progress tracking

| State | Count |
|---|---|
| ✅ Completed | 2 (001, 002) |
| 🔄 In progress | 0 |
| 🔲 Pending | 15 |
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
