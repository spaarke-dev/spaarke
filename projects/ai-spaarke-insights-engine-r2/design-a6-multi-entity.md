# Wave A6 — Multi-Entity Subject Design

> **Project**: Spaarke Insights Engine Phase 1.5 (r2)
> **Task**: 015 (Wave A6)
> **Status**: Authored 2026-06-02
> **Author**: task-execute / Wave A6
> **Feeds**: Wave D5 (034 — per-entity `ILiveFactResolver`), Wave D6 (035 — index scope migration), Wave D4 (033 — per-area routing)
> **Resolves**: Q-A6-1 (resolver registration pattern), Q-D6-1 (index scope shape)

---

## 1. Context + scope

Phase 1 r1 ships a single-entity subject model: `matter:{guid}` only. r1's `DataverseLiveFactResolver` (`src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/DataverseLiveFactResolver.cs`) hard-codes:
- `MatterSubjectScheme = "matter:"`
- `MatterEntityName = "sprk_matter"`
- 5 supported predicates pegged to `sprk_matter` columns (`attorney`, `client`, `matterType`, `opposingCounsel`, `currentMatterFacts`)
- A single composition root registration (`InsightsModule` → `services.AddSingleton<ILiveFactResolver, DataverseLiveFactResolver>()`)
- A single `scope.matterId` field on `spaarke-insights-index`

Phase 1.5 (r2) requires multi-entity subjects per spec.md FR-03 + SC-03:

> FR-03 — `POST /api/insights/ask` with `subject: "project:<guid>"` or `subject: "invoice:<guid>"` resolves live facts from the correct Dataverse entity and returns the appropriate Inference.

This design doc resolves the five Wave A6 questions captured in spec.md:
1. Subject parser shape (validation, scheme catalog)
2. `ILiveFactResolver` registration pattern (Q-A6-1)
3. `spaarke-insights-index` scope shape evolution (Q-D6-1)
4. Phase 1 backward-compat / migration plan (NFR-08)
5. Per-entity resolver responsibilities (matter, project, invoice)

The design is **constraint-led**: ADR-010 DI minimalism (≤15 non-framework registrations is already pressured per ADR-010 Phase 5 baseline), and NFR-08 (Phase 1 Observations with `scope.matterId` only stay queryable).

---

## 2. Subject parser shape

### 2.1 Format

```
<scheme>:<entityId>
```

- `<scheme>` — lower-case ASCII, matches `[a-z][a-z0-9-]*`, MUST be a registered scheme (see §2.3)
- `<entityId>` — Dataverse GUID, MUST parse via `Guid.TryParse` to non-empty Guid (consistent with r1)

**Phase 1.5 schemes**: `matter`, `project`, `invoice` (initial).
**Reserved for Phase 2+**: `document`, `client`, `contract`.

### 2.2 Parsed shape

```csharp
public readonly record struct ParsedSubject(string EntityType, Guid EntityId)
{
    public string ToSubjectString() => $"{EntityType}:{EntityId}";
}

public interface ISubjectParser
{
    bool TryParse(string subject, out ParsedSubject parsed, out string error);
    ParsedSubject Parse(string subject); // throws ArgumentException on invalid
}
```

The parser is a stateless pure component (no DI dependencies aside from a config-driven scheme catalog injected via Options pattern — see §2.3).

### 2.3 Config-driven scheme catalog

To avoid hard-coding scheme names in C# (which would force a code deploy to add `client:` or `contract:` later), schemes are catalog-driven via `IOptions<SubjectSchemeCatalogOptions>`:

```jsonc
// appsettings.json
{
  "Insights": {
    "Subject": {
      "Schemes": [
        { "name": "matter",  "dataverseEntity": "sprk_matter",   "resolverKey": "matter"  },
        { "name": "project", "dataverseEntity": "sprk_project",  "resolverKey": "project" },
        { "name": "invoice", "dataverseEntity": "sprk_invoice",  "resolverKey": "invoice" }
      ]
    }
  }
}
```

The parser validates `<scheme>` against the catalog at runtime. Unknown schemes throw `UnknownSubjectSchemeException` → LiveFactNode surfaces as node-level `InvalidConfiguration` (consistent with r1's existing `LiveFactNotSupportedException` plumbing).

This catalog is also consumed by the resolver registration pattern (§3) and the index scope migration plan (§4).

### 2.4 Error handling

| Failure | Exception | LiveFactNode surface |
|---|---|---|
| Empty / whitespace subject | `ArgumentException` | `InvalidConfiguration` |
| Missing `:` separator | `InvalidSubjectFormatException` | `InvalidConfiguration` |
| Unknown scheme (not in catalog) | `UnknownSubjectSchemeException` | `InvalidConfiguration` |
| Invalid GUID after scheme | `InvalidSubjectFormatException` | `InvalidConfiguration` |
| Subject resolves but Dataverse row not found | (no exception; resolver returns null) | `InternalError` "Subject not found" (existing r1 behavior preserved) |

All authoring errors surface immediately at playbook authoring time, not silently at runtime. Consistent with r1's existing graceful-degradation pattern.

---

## 3. Resolver registration pattern (Q-A6-1)

### 3.1 Decision: Option (a) — `IDictionary<string, ILiveFactResolver>` keyed by entity type

**Picked**: Option (a) — `IDictionary<string, ILiveFactResolver>` keyed by entity-type name (e.g., `"matter"`, `"project"`, `"invoice"`).

This is the default already assumed in spec.md §Assumptions ("Per-entity resolver registration pattern — defaults to option (a)").

### 3.2 Options considered

| Option | Pros | Cons | DI cost (matter + project + invoice) |
|---|---|---|---|
| **(a) `IDictionary<string, ILiveFactResolver>` keyed by entity-type** | Simplest; direct lookup; minimal DI surface | Requires factory registration; less polymorphic | 1 factory registration + 3 concrete registrations = **4 lines** |
| (b) `ILiveFactResolverRegistry` service that routes | Encapsulates lookup logic; testable seam | Extra abstraction layer; another interface | 1 registry + 3 concrete = **4 lines** + 1 interface |
| (c) `IEnumerable<ILiveFactResolver>` + `CanResolve(string)` iteration | Most polymorphic; "Open/Closed"-friendly | Linear scan per call (cheap but unnecessary); risk of multiple resolvers claiming the same scheme | 3 lines (just registrations) + iteration logic in consumer |

### 3.3 Why (a) wins

**Three reasons:**

1. **ADR-010 DI minimalism**: Option (a) ties for fewest registrations with (b); option (c) is technically fewest but pushes iteration logic into every consumer (which is itself an ADR-010 anti-pattern — concrete dispatch logic creeps into multiple places).

2. **r1 precedent already uses dictionary-keyed resolver pattern elsewhere** (e.g., `NodeExecutorRegistry` auto-discovery — see ADR-013-ai-architecture.md and spec.md §"DI minimalism per ADR-010 — node executors register via existing NodeExecutorRegistry auto-discovery"). Re-using a known pattern lowers cognitive load.

3. **Explicit registration matches the config-driven scheme catalog**: each scheme in the catalog has a `resolverKey` field; the registration code reads the catalog and registers `dict[resolverKey] = serviceProvider.GetRequiredService<TResolverType>()`. This means **adding a scheme = (a) add a catalog row + (b) implement the resolver class + (c) register it** — no changes to consumers, no changes to the parser.

### 3.4 Registration shape (Wave D5 implementation)

```csharp
// In InsightsModule.cs (existing module, extends r1 registration):

services.Configure<SubjectSchemeCatalogOptions>(
    configuration.GetSection("Insights:Subject"));

// Concrete resolvers — each scoped (matches r1 DataverseLiveFactResolver lifetime)
services.AddScoped<MatterLiveFactResolver>();
services.AddScoped<ProjectLiveFactResolver>();
services.AddScoped<InvoiceLiveFactResolver>();

// Dictionary registration — keyed by scheme name
services.AddScoped<IReadOnlyDictionary<string, ILiveFactResolver>>(sp =>
    new Dictionary<string, ILiveFactResolver>(StringComparer.OrdinalIgnoreCase)
    {
        ["matter"]  = sp.GetRequiredService<MatterLiveFactResolver>(),
        ["project"] = sp.GetRequiredService<ProjectLiveFactResolver>(),
        ["invoice"] = sp.GetRequiredService<InvoiceLiveFactResolver>(),
    });

// Subject parser — singleton (stateless)
services.AddSingleton<ISubjectParser, SubjectParser>();
```

**DI net-cost**: 5 lines (parser + dictionary factory + 3 concrete resolvers). Removes 1 line (r1's single `AddSingleton<ILiveFactResolver, DataverseLiveFactResolver>`). **Net: +4 registrations.**

This fits well within ADR-010's ≤15 non-framework target for net Phase 1.5 D5+E2+E1 additions (per NFR-05).

### 3.5 Backward-compat with LiveFactNode

`LiveFactNode` (the existing Phase 1 consumer of `ILiveFactResolver`) changes minimally:

```csharp
// r1 (single resolver):
public sealed class LiveFactNode(ILiveFactResolver resolver, ...)
{
    public async Task<NodeResult> ExecuteAsync(...)
    {
        var fact = await resolver.ResolveAsync(subject, predicate, tenantId, ct);
        ...
    }
}

// r2 (dispatch via dictionary):
public sealed class LiveFactNode(
    IReadOnlyDictionary<string, ILiveFactResolver> resolvers,
    ISubjectParser parser,
    ...)
{
    public async Task<NodeResult> ExecuteAsync(...)
    {
        if (!parser.TryParse(subject, out var parsed, out var err))
            return NodeResult.InvalidConfiguration(err);

        if (!resolvers.TryGetValue(parsed.EntityType, out var resolver))
            return NodeResult.InvalidConfiguration($"No resolver registered for scheme '{parsed.EntityType}'");

        var fact = await resolver.ResolveAsync(subject, predicate, tenantId, ct);
        ...
    }
}
```

The `ILiveFactResolver` interface signature **does NOT change** — r1's `Task<FactArtifact?> ResolveAsync(string subject, string predicate, string tenantId, CancellationToken ct)` continues to apply per-entity. The dispatcher is the only new concept.

---

## 4. `spaarke-insights-index` scope shape (Q-D6-1)

### 4.1 Decision: Option (c) Hybrid — keep `scope.matterId` + add `scope.entityType` + `scope.entityId`

**Picked**: Option (c) — Hybrid. Preserves Phase 1 backward-compat (NFR-08) while enabling multi-entity Observations for Wave D6 onward.

This is the approach already pre-staged in spec.md §Risks ("hybrid backward-compat (keep scope.matterId + add scope.entityType/entityId)") and task POML 015 step 5.

### 4.2 New + existing `Scope` fields

`Sprk.Bff.Api.Models.Insights.Scope` (`InsightArtifact.cs`) gets two new optional fields. Note: the `Scope` record already carries `MatterId`, `ClientId`, `PracticeArea`, `Jurisdiction`, `Year`; the migration only adds two new fields and is purely additive.

```csharp
public sealed record Scope
{
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    [JsonPropertyName("matterId")]
    public string? MatterId { get; init; }     // r1 — KEPT for backward-compat

    [JsonPropertyName("entityType")]
    public string? EntityType { get; init; }   // r2 NEW — scheme name (e.g., "matter", "project", "invoice")

    [JsonPropertyName("entityId")]
    public string? EntityId { get; init; }     // r2 NEW — GUID string of the entity

    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }     // r1 — KEPT

    [JsonPropertyName("practiceArea")]
    public string? PracticeArea { get; init; } // r1 — KEPT

    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; init; } // r1 — KEPT

    [JsonPropertyName("year")]
    public int? Year { get; init; }            // r1 — KEPT
}
```

### 4.3 `spaarke-insights-index` field additions

The Azure AI Search index `infra/insights/schemas/spaarke-insights-index.index.json` adds two filterable fields under the nested `value.raw.scope` ComplexType:

```jsonc
{
  "name": "entityType",
  "type": "Edm.String",
  "searchable": false,
  "filterable": true,
  "retrievable": true,
  "sortable": false,
  "facetable": true
},
{
  "name": "entityId",
  "type": "Edm.String",
  "searchable": false,
  "filterable": true,
  "retrievable": true,
  "sortable": false,
  "facetable": false
}
```

These are **optional** (nullable) — Phase 1 Observations omit them entirely; they remain queryable via `scope.matterId` filter. Phase 1.5 Observations populate both: for matter subjects, **both** `matterId` AND `entityType="matter"`/`entityId=<guid>` are set (dual-writing) — for backward-compat with any Phase 1 RAG queries that filter by `scope.matterId`.

### 4.4 Writer behavior (Wave D5 + D6)

| Subject scheme | Writes `scope.matterId` | Writes `scope.entityType` | Writes `scope.entityId` |
|---|---|---|---|
| `matter:` | ✅ (Phase 1 compat) | ✅ "matter" | ✅ matter GUID |
| `project:` | ❌ (null) | ✅ "project" | ✅ project GUID |
| `invoice:` | ❌ (null) | ✅ "invoice" | ✅ invoice GUID |

This means `scope.matterId` becomes a **scheme-specific convenience field for matter subjects**, and `scope.entityType` + `scope.entityId` are the **canonical generalized fields** going forward.

### 4.5 Reader behavior

Wave E1 RAG search (and any other consumer that filters by scope) implements **dual-read logic**:

```csharp
// Pseudocode for a matter-scoped query:
filter = parsedSubject.EntityType == "matter"
    ? $"scope/matterId eq '{matterId}' or (scope/entityType eq 'matter' and scope/entityId eq '{matterId}')"
    : $"scope/entityType eq '{entityType}' and scope/entityId eq '{entityId}'";
```

This guarantees a Phase 1 Observation written with only `scope.matterId` is still findable when querying for `matter:{guid}` after Wave D6 ships, and Phase 1.5 matter Observations written with both fields are also findable.

---

## 5. Migration plan (NFR-08 — Phase 1 Observations stay queryable)

### 5.1 Index migration approach

**Index re-create required** (per spec.md §Assumptions: "Index re-create required for Wave D6 migration; coordinate with infra team during D6 task; new fields nullable; Phase 1 Observations remain queryable.")

Azure AI Search does NOT support adding fields to an existing index in-place if they need to be filterable+facetable. The migration path is:

1. **D6.1**: Create new index `spaarke-insights-index-v2` with the new schema (matter + project + invoice support).
2. **D6.2**: Re-index Phase 1 Observations into v2 (back-fill: for each Observation, set `scope.entityType = "matter"` + `scope.entityId = scope.matterId`). Phase 1 Observations were tenant-scoped + matter-scoped, so the back-fill is mechanical.
3. **D6.3**: Switch readers + writers to v2 in a single deploy. The writer change is in the universal-ingest playbook → `IInsightsAi.WriteObservation` → AI Search SDK client. The reader change is in Wave E1's RAG retriever + any LiveFactNode that queries the index.
4. **D6.4**: Delete `spaarke-insights-index-v1` after a defined soak period (1 sprint default).

### 5.2 Coordination touchpoints

- **Infra team**: provisions v2 index (manual run of `infra/insights/scripts/Deploy-InsightsIndex.ps1` with v2 name parameter).
- **Wave E1 (RAG endpoint)**: reads from v2; dual-read filter logic per §4.5.
- **Wave D5 (per-entity resolvers)**: writes to v2 with new scope fields. Resolvers themselves don't write to the index — the playbook's IndexWriter does.
- **Phase 1 r1 deployment**: continues to read from v2 with no code changes (the matterId-only filter still works via dual-read logic in v2 readers).

### 5.3 Rollback plan

If v2 has a corruption issue surfaced during deploy verification:
1. Re-point readers + writers back to v1 (one config change in `appsettings.json`).
2. Investigate v2 corruption offline.
3. Re-attempt v2 cutover after fix.

Phase 1 v1 remains available throughout the soak period (D6.4), giving an emergency rollback target.

### 5.4 Phase 1 r1 Observation backward compatibility (NFR-08 verification)

**Test in D6**: after migration, replay a Phase 1 RAG query that filters by `scope.matterId` against v2 and verify it returns the same Observations as it did against v1. This is the NFR-08 acceptance test.

---

## 6. Per-entity resolver responsibilities

Each per-entity resolver implements the same `ILiveFactResolver` interface as r1's `DataverseLiveFactResolver` — but reads from a different Dataverse entity and supports a different predicate set.

### 6.1 `MatterLiveFactResolver` (existing r1 resolver, renamed)

**Refactor scope**: rename `DataverseLiveFactResolver` → `MatterLiveFactResolver`. Behavior **unchanged**. Subject scheme `matter:`. Reads `sprk_matter`. Predicates: `attorney`, `client`, `matterType`, `opposingCounsel`, `currentMatterFacts`.

Internal constant `MatterSubjectScheme = "matter:"` is removed (the dispatcher handles subject parsing now). All other code stays the same; r1's `BuildLookupFact`, `BuildCompositeFact`, `BuildFact` helpers are preserved.

### 6.2 `ProjectLiveFactResolver` (new)

**Subject scheme**: `project:`
**Dataverse entity**: `sprk_project`
**Predicates** (initial set — extend as project-cost-prediction or project-status playbooks land):

| Predicate | Source field | Shape |
|---|---|---|
| `projectName` | `sprk_name` | plain string |
| `projectManager` | `sprk_projectmanager` (lookup → contact) | `{id, name}` |
| `client` | `sprk_externalaccount` (lookup → account) | `{id, name}` |
| `projectStatus` | `sprk_status` (option set) | plain string (status text) |
| `currentProjectFacts` | composite | composite object containing the above |

**Behavior parity with r1**: confidence = 1.0; null-on-missing-row; `LiveFactNotSupportedException` on unsupported predicate; `producedBy.id = "dataverse://sprk_project"`.

### 6.3 `InvoiceLiveFactResolver` (new)

**Subject scheme**: `invoice:`
**Dataverse entity**: `sprk_invoice`
**Predicates** (initial set):

| Predicate | Source field | Shape |
|---|---|---|
| `invoiceNumber` | `sprk_invoicenumber` | plain string |
| `invoiceTotal` | `sprk_total` (decimal) | numeric (USD by default; currency override via Phase 2) |
| `invoiceStatus` | `sprk_status` (option set) | plain string |
| `relatedMatter` | `sprk_matter` (lookup → sprk_matter) | `{id, name}` (LINK to matter resolver — see §6.4) |
| `currentInvoiceFacts` | composite | composite object containing the above |

**Behavior parity**: confidence = 1.0; null-on-missing-row; same exception handling.

### 6.4 Inter-entity references

Some predicates on one entity refer to another (e.g., `invoice.relatedMatter`). The resolver returns the `EntityReference` `{id, name}` shape; it does NOT recurse into the referenced entity. If a playbook needs facts from both invoice AND its related matter, the playbook itself adds two `LiveFactNode` instances with different subjects (`invoice:<guid>` then `matter:<related-matter-guid>`).

This keeps each resolver simple and aligned with r1's pattern.

### 6.5 Zone B placement (per CLAUDE.md / SPEC §3.5)

All three resolvers live in `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/` (consistent with r1's `DataverseLiveFactResolver`). They consume `IGenericEntityService` from `Spaarke.Dataverse` only. **Zero AI-internal imports** (no `IOpenAiClient`, no `IPlaybookService`, no `PlaybookExecutionEngine`). The §3.5.4 forbidden-imports grep gate continues to pass.

### 6.6 Testing scope

Per spec.md: `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/LiveFacts/` adds:
- `MatterLiveFactResolverTests` (existing `DataverseLiveFactResolverTests` rename — verifies behavior unchanged)
- `ProjectLiveFactResolverTests` (new)
- `InvoiceLiveFactResolverTests` (new)
- `SubjectParserTests` (new — covers §2.4 error matrix)
- `LiveFactNodeDispatchTests` (new — covers the dispatcher logic in §3.5)

Wave D7 (036) adds synthetic LLM-generated fixtures for project + invoice.

---

## 7. ADR-032 (Null-Object Kill-Switch) applicability

ADR-032 applies if any of the per-entity resolvers are gated behind a feature flag in `InsightsModule.cs` (e.g., `if (multiEntitySubjectsEnabled) { registerProjectResolver(); }`).

**Recommendation for D5**: register all three resolvers unconditionally. The cost is minimal (3 concrete registrations + 1 dict factory + 1 parser singleton). If a feature gate is later judged necessary, apply the P3 pattern — `FeatureDisabledException` thrown by `ProjectLiveFactResolver` / `InvoiceLiveFactResolver` when the gate is off — surfacing as a uniform 503 ProblemDetails via the existing `FeatureDisabledResults.AsFeatureDisabled503()` helper.

Per CLAUDE.md §10 F.1, if a conditional registration is added, the PR reviewer runs the static-scan recipe and selects P1/P2/P3. P2 (Quiet no-op) is **forbidden for query services** like `ILiveFactResolver`.

---

## 8. Acceptance criteria for Wave D5 + D6 implementation

These are the testable outcomes that Wave D5 (034) + D6 (035) must satisfy:

### D5 acceptance (per-entity resolvers)
1. [ ] `matter:<guid>` subject continues to resolve identically to r1 (regression test against r1's 5 predicates).
2. [ ] `project:<guid>` subject resolves and returns a `FactArtifact` with `producedBy.id = "dataverse://sprk_project"`.
3. [ ] `invoice:<guid>` subject resolves and returns a `FactArtifact` with `producedBy.id = "dataverse://sprk_invoice"`.
4. [ ] Subject parser rejects unknown schemes (`client:<guid>`) with `UnknownSubjectSchemeException` → LiveFactNode emits `InvalidConfiguration`.
5. [ ] Subject parser rejects invalid GUIDs (`matter:not-a-guid`) with `InvalidSubjectFormatException` → LiveFactNode emits `InvalidConfiguration`.
6. [ ] DI count check: net Phase 1.5 additions stay within ADR-010 ≤15 non-framework target per NFR-05.
7. [ ] §3.5 forbidden-imports grep passes (zero new Zone B violations).

### D6 acceptance (index migration)
1. [ ] `spaarke-insights-index-v2` deployed with `entityType` + `entityId` filterable fields.
2. [ ] Phase 1 Observations back-filled with `entityType="matter"` + `entityId=<matterGuid>`.
3. [ ] Phase 1 RAG query filtering by `scope.matterId` against v2 returns same Observation set as it did against v1 (NFR-08 verification).
4. [ ] New Phase 1.5 project Observation written via universal-ingest is queryable via `entityType="project"` + `entityId=<guid>` filter.
5. [ ] v1 → v2 cutover is reversible by config change (rollback plan §5.3 exercise).

---

## 9. Decisions log

| ID | Decision | Rationale |
|---|---|---|
| A6-D1 | Subject parser is config-catalog-driven (not hard-coded scheme names in C#) | Adding a scheme should not require a code deploy; future-proofs `client:`, `contract:`, etc. |
| A6-D2 | Q-A6-1 → Option (a) `IDictionary<string, ILiveFactResolver>` | Matches spec.md §Assumptions default; minimal DI surface; reuses r1's `NodeExecutorRegistry`-style pattern |
| A6-D3 | Q-D6-1 → Option (c) Hybrid (keep `scope.matterId` + add `entityType`/`entityId`) | Preserves NFR-08 backward-compat; minimal Phase 1 Observation back-fill cost |
| A6-D4 | Index migration via new index `spaarke-insights-index-v2` + back-fill | Azure AI Search doesn't support in-place schema mutation for filterable fields |
| A6-D5 | r1 `DataverseLiveFactResolver` renamed to `MatterLiveFactResolver`; behavior preserved | Naming consistency with `ProjectLiveFactResolver`, `InvoiceLiveFactResolver`; zero behavior change |
| A6-D6 | All resolvers registered unconditionally (no ADR-032 gate) in D5 default | Cost is minimal; avoids ADR-032 conditional-registration overhead; can revisit if cost grows |
| A6-D7 | Inter-entity references (e.g., `invoice.relatedMatter`) return `EntityReference` only; do NOT recurse | Keeps resolvers simple; playbooks compose multi-entity fact-gathering at the playbook level |
| A6-D8 | Subject parser is `ISingleton`; concrete resolvers are `IScoped` (matches r1 lifetime) | Matches `IGenericEntityService` scoped lifetime in r1 |

---

## 10. Open questions for Wave D5/D6 task authors

These are NOT blockers for design — they are implementation-time decisions:

- **D5-Q1**: For `ProjectLiveFactResolver` predicates, are `projectName`, `projectManager`, `client`, `projectStatus` the right initial set, or do upcoming project-cost-prediction playbook needs warrant additional predicates? *Confirm with playbook authoring SME during D5 task kick-off.*
- **D5-Q2**: For `InvoiceLiveFactResolver`, is `relatedMatter` the right link field (vs. `sprk_relatedmatter` or a different lookup attribute name)? *Verify against Spaarke Dev `sprk_invoice` schema during D5 task kick-off.*
- **D6-Q1**: Soak period for v1 deletion — default 1 sprint; confirm with infra team during D6 coordination.
- **D6-Q2**: Can Phase 1 Observation back-fill run in a single batched re-index, or do we need pagination + throttling? *Test against Spaarke Dev volume during D6 dry-run.*

---

## 11. References

- spec.md §Wave D5, §Wave D6, §Q-A6-1, §Q-D6-1, §NFR-05, §NFR-08, §FR-03, §SC-03, §Assumptions
- design.md §Q-A6-1, §Q-D6-1
- r1 implementation: `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/DataverseLiveFactResolver.cs`
- r1 interface: `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/ILiveFactResolver.cs`
- r1 scope shape: `src/server/api/Sprk.Bff.Api/Models/Insights/InsightArtifact.cs` (Scope record)
- r1 index schema: `infra/insights/schemas/spaarke-insights-index.index.json`
- ADR-010 (DI minimalism)
- ADR-013 (AI Architecture — facade boundary)
- ADR-032 (BFF Null-Object Kill-Switch Pattern)
- CLAUDE.md §3.5 (Zone A/B facade boundary), §10 (BFF Hygiene)

---

*Feeds Wave D5 (034) and Wave D6 (035) — both depend on this design landing first.*
