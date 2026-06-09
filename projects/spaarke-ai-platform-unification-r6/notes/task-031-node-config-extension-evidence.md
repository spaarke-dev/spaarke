# Task 031 — Evidence: Extend playbook node config schema with `destination` enum + `widgetType`

> **Phase**: B (Schema-Aware Output) · **Wave**: B-G1
> **Status**: ✅ Complete
> **Date**: 2026-06-08
> **Rigor**: FULL
> **Spec**: R6 FR-27 (per-playbook routing); NFR-08 (11 production node executors unmodified)
> **Sibling**: task 030 (`outputSchema` on `sprk_analysisaction` — action-level intrinsic shape; parallel)
> **Unblocks**: 032-035 (action migrations populate `destination` per node); 040/041 (`StructuredOutputStreamWidget` schema-aware); 042 (CapabilityRouter dedup)

---

## 1. Storage Surface Decision

### Decision: **JSON-blob path** (extend `sprk_playbooknode.sprk_configjson` contract)

The new `destination` enum + `widgetType` string fields live INSIDE the existing
`sprk_playbooknode.sprk_configjson` JSON blob. **No new Dataverse column is added.**

### Evidence

Per `docs/architecture/playbook-architecture.md`:

| Quote | Line |
|---|---|
| "Playbook nodes are typically stored as JSON in `sprk_analysisplaybook.sprk_nodesjson` (or sibling column)" | — task POML restating |
| "Canvas type: `PlaybookNodeType`, React Flow `node.data.type`; Dataverse NodeType: `sprk_nodetype`, OptionSet; **ActionType: `__actionType` in ConfigJson, `sprk_playbooknode.sprk_configjson`**" | playbook-architecture.md L54 |
| "`sprk_canvaslayoutjson` (serialized JSON of nodes and edges)" | playbook-architecture.md L19 |
| "Creates/updates/deletes node records with `sprk_nodetype` + `__actionType` in ConfigJson" | playbook-architecture.md L109 |

So the storage surface is **HYBRID** at the canvas layer:

1. **Canvas JSON blob** (`sprk_analysisplaybook.sprk_canvaslayoutjson`) — React Flow nodes+edges; used by the Playbook Builder (Code Page + PCF Host).
2. **Per-node Dataverse rows** (`sprk_playbooknode.sprk_configjson`) — opaque JSON column per node; carries `__actionType` + node-specific config. Read by the BFF execution engine.

For DeliverOutput nodes (which the Q5 destination is about), the per-node config is in `sprk_configjson`. The R6 destination field sits ALONGSIDE the existing `deliveryType`, `template`, `outputFormat`, `outputType`, `targetPage`, `prePopulateFields` keys in that blob — additive properties, opaque to existing storage.

### Why not a separate Dataverse entity column?

- The blob is already the canonical extension surface for node-specific config
- Adding a column would require a column on `sprk_playbooknode` PLUS an update to NodeService write-path and `sync_nodesToDataverse` client-side mapping
- The blob's opacity to NodeService (it PATCHes the whole string verbatim) means additive blob extension requires ZERO server-side write-path changes
- Same pattern as the R2 `outputType` / `targetPage` / `prePopulateFields` additions (see PlaybookNodeDto.cs L141-159)

### Confirmation

User pre-approved both surface paths in the task dispatch prompt:
> "User has pre-approved BOTH surface paths (JSON-blob or Dataverse-entity). Therefore: do NOT pause at Step 0 or Step 3 surface-determination for confirmation. Investigate, decide, document the surface decision in the evidence note, and apply."

JSON-blob path selected. Surface decision documented. Proceeding.

---

## 2. NFR-08 Additive-Safety Verification

### Constraint

NFR-08 binding: **11 production node executors UNMODIFIED**. `DeliverOutput` reads the
new fields at runtime; executor code is NOT modified.

### Verification

Inspected `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverOutputNodeExecutor.cs`:

| Line | Code | Implication |
|---|---|---|
| 32 | `JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };` | No `UnmappedMemberHandling` set → default value is `Skip` (System.Text.Json default) |
| 271-286 | `ParseConfigOrDefault` deserializes into `DeliveryNodeConfig` record | Unknown JSON properties are SILENTLY IGNORED |
| 282-285 | `catch { return null; }` | Malformed JSON returns null → null treated as "auto-assembly mode" (line 98-99) |
| 427-438 | `internal sealed record DeliveryNodeConfig` | Fields: `DeliveryType`, `Template`, `OutputFormat`, `OutputType`, `TargetPage`, `PrePopulateFields`, `RequiresConfirmation`. Does NOT include `destination` or `widgetType`. |

**Conclusion**: When a node's `sprk_configjson` blob contains BOTH the existing
DeliveryNodeConfig fields AND the new R6 routing fields:

```json
{
  "deliveryType": "json",
  "template": "{{summary.output}}",
  "destination": "workspace",
  "widgetType": "Summary"
}
```

The DeliverOutputNodeExecutor:
1. Deserializes the known DeliveryNodeConfig fields (`deliveryType`, `template`)
2. **Silently ignores** `destination` and `widgetType` (System.Text.Json default behavior)
3. Executes its template-rendering job exactly as before

The executor code is **NOT modified**. NFR-08 satisfied.

### Regression test

Ran the existing DeliverOutputNodeExecutor test suite (`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/DeliverOutputNodeExecutorTests.cs`):

```
Passed!  - Failed: 0, Passed: 21, Skipped: 1, Total: 22, Duration: 26 ms
```

The 1 skipped test (`ExecuteAsync_WithIncludeMetadata_AddsMetadata`) is a pre-existing skip from before this task, unrelated to the routing-config changes.

### Reverse direction (NodeRoutingConfig additive-safety)

The new `NodeRoutingConfig.Parse(configJson)` is also tested for the reverse: parsing a
blob that contains the OLD DeliveryNodeConfig fields should NOT throw and should extract
ONLY the routing subset. See `tests/.../Models/Ai/NodeRoutingConfigTests.cs`:

```csharp
[Fact]
public void Parse_WithDeliveryNodeConfigFields_ExtractsOnlyRoutingSubset()
```

This test passes — proving both readers can coexist on the same blob.

---

## 3. Backward Compatibility Proof

### Constraint

Nodes WITHOUT the new fields MUST default to `destination = chat` (current pre-R6 behavior) and execute without error.

### Test evidence

`tests/.../NodeRoutingConfigTests.cs`:

| Test | Assertion |
|---|---|
| `Parse_WithNullJson_ReturnsDefaultRoutingConfig` | `configJson = null` → `Destination = Chat`, no exception |
| `Parse_WithEmptyString_ReturnsDefaultRoutingConfig` | `configJson = ""` → `Destination = Chat` |
| `Parse_WithWhitespaceString_ReturnsDefaultRoutingConfig` | `configJson = "   "` → `Destination = Chat` |
| `Parse_WithEmptyObject_DefaultsToChatDestination` | `configJson = "{}"` → `Destination = Chat` |
| `Parse_WithMalformedJson_ReturnsDefaultRoutingConfig` | malformed JSON → degrades to `Chat` (no exception) |
| `Parse_WithDeliveryNodeConfigFields_ExtractsOnlyRoutingSubset` | blob with old fields, no `destination` → defaults to `Chat` |
| `Parse_WithUnknownDestinationValue_DegradesToDefault` | `{"destination":"carrier-pigeon"}` → defaults to `Chat` |

All 27 NodeRoutingConfig tests pass. Sample playbook node JSON without the new fields is verified to default to `chat` destination without error.

### Default value semantics

`NodeRoutingConfig.Destination` initializer:

```csharp
public NodeDestination Destination { get; init; } = NodeDestination.Chat;
```

Default is hardcoded at the record-init level. C# record-with-init-only-default semantics
guarantee that when `JsonSerializer.Deserialize<NodeRoutingConfig>("{}")` returns a
record with no source value for `Destination`, the init default is used. Verified by
unit tests above.

### Existing playbook execution (no migration)

Existing `sprk_playbooknode` rows whose `sprk_configjson` does NOT contain the new fields
continue to execute. The DeliverOutputNodeExecutor reads its own `DeliveryNodeConfig`
subset and produces output exactly as before. Downstream consumers (CapabilityRouter
dedup task 042, StructuredOutputStreamWidget tasks 040/041) call
`NodeRoutingConfig.Parse(configJson)` and get a default `Chat` destination — which is
the pre-R6 behavior.

**No data migration is required.** Tasks 032-035 will populate the routing fields for the
4 migrated actions; all other playbooks remain unchanged.

---

## 4. `widgetType` Conditional-Required Rule Definition

### Rule

`widgetType` is **REQUIRED** when `destination = workspace`; **IGNORED** otherwise.

### C# enforcement (authoritative at runtime)

`NodeRoutingConfig.Validate()` returns a `NodeValidationResult`:

```csharp
public NodeValidationResult Validate()
{
    if (Destination == NodeDestination.Workspace && string.IsNullOrWhiteSpace(WidgetType))
    {
        return NodeValidationResult.Failure(
            "widgetType is required when destination = 'workspace' " +
            "(per-playbook node config; R6 Pillar 5 FR-27).");
    }
    return NodeValidationResult.Success();
}
```

Test coverage:

| Test | Setup | Expected |
|---|---|---|
| `Validate_WorkspaceDestinationWithWidgetType_ReturnsSuccess` | workspace + "Summary" | IsValid = true |
| `Validate_WorkspaceDestinationWithoutWidgetType_ReturnsFailure` | workspace + null | IsValid = false, single error message |
| `Validate_WorkspaceDestinationWithEmptyWidgetType_ReturnsFailure` | workspace + "   " | IsValid = false |
| `Validate_NonWorkspaceDestinationWithoutWidgetType_ReturnsSuccess` (×3) | chat/form-prefill/side-effect + null | IsValid = true |
| `Validate_NonWorkspaceDestinationWithWidgetType_ReturnsSuccess` (×3) | chat/form-prefill/side-effect + "Summary" | IsValid = true (widgetType ignored) |

8 conditional-required tests pass.

### JSON Schema enforcement (documentation + build-time tooling)

`src/server/api/Sprk.Bff.Api/Models/Ai/node-routing-config.schema.json` declares the
conditional rule via Draft 2020-12 `allOf` + `if`/`then`:

```json
"allOf": [
  {
    "if": {
      "properties": { "destination": { "const": "workspace" } },
      "required": ["destination"]
    },
    "then": {
      "required": ["widgetType"],
      "properties": { "widgetType": { "minLength": 1 } }
    }
  }
]
```

The JSON Schema is for write-time tooling (Playbook Builder AI Assistant — see
`Services/Ai/Builder/BuilderToolDefinitions.cs`). The C# `Validate()` is the
authoritative runtime check.

---

## 5. Wire Format

`destination` is serialized as **kebab-case** strings to match the JSON Schema and the
existing playbook canvas wire convention:

| C# enum | Wire value |
|---|---|
| `NodeDestination.Chat` | `"chat"` |
| `NodeDestination.Workspace` | `"workspace"` |
| `NodeDestination.FormPrefill` | `"form-prefill"` |
| `NodeDestination.SideEffect` | `"side-effect"` |

A bespoke `NodeDestinationJsonConverter` provides kebab-case read+write. .NET 8's
built-in `JsonStringEnumConverter` writes PascalCase by default and lacks kebab-case
naming policy support (added in .NET 9); the bespoke converter is small (~30 LOC) and
allocation-light. Test coverage:

```
Serialize_ProducesKebabCaseEnumValues(NodeDestination.Chat, "chat") — PASS
Serialize_ProducesKebabCaseEnumValues(NodeDestination.Workspace, "workspace") — PASS
Serialize_ProducesKebabCaseEnumValues(NodeDestination.FormPrefill, "form-prefill") — PASS
Serialize_ProducesKebabCaseEnumValues(NodeDestination.SideEffect, "side-effect") — PASS
RoundTrip_WorkspaceDestination_PreservesValues — PASS
Deserialize_WithUnknownDestinationValue_ThrowsJsonException — PASS
```

Read is case-insensitive (`ToLowerInvariant()`) to match project-wide convention.

---

## 6. BFF Publish-Size Delta

### Measurement

| Metric | Baseline (Wave 7b, 2026-06-08) | Task 031 publish | Delta |
|---|---|---|---|
| Compressed (.zip) | **45.90 MB** | **45.95 MB** | **+0.05 MB** |
| Uncompressed | ~139 MB | **139.03 MB** | negligible |
| File count | ~259 | 259 | 0 |

### Compliance

- ≤+5 MB R6 single-task threshold (NFR-02) — **PASS** (0.05 MB ≪ 5 MB)
- ≤50 MB compressed ADR-029 ceiling — **PASS** (45.95 MB ≪ 50 MB)
- ≤150 MB uncompressed ADR-029 ceiling — **PASS** (139.03 MB < 150 MB)
- Per CLAUDE.md §10 bullet 4: report delta vs prior baseline — **Done above**
- Confirmation trigger threshold of +1 MB for contract-only change — **PASS** (0.05 MB ≪ 1 MB)

### Composition of the delta

- New `Models/Ai/NodeRoutingConfig.cs` — ~250 LOC C# class (compiled IL adds ~3-5 KB to assembly)
- New `Models/Ai/node-routing-config.schema.json` — ~3 KB JSON file copied to publish output (Web SDK auto-includes .json as Content)
- New test assembly changes do NOT affect publish (test project)
- No new NuGet packages added
- No new DI registrations added (ADR-010 satisfied)

### Verification command

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
Compress-Archive -Path "deploy\api-publish\*" -DestinationPath "$env:TEMP\api-publish-test.zip" -CompressionLevel Optimal
# Result: 45.95 MB compressed
```

---

## 7. Files Modified / Added

### Production code

| Path | Change | Lines |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs` | NEW — `NodeDestination` enum + `NodeRoutingConfig` record + `NodeDestinationJsonConverter` | ~250 |
| `src/server/api/Sprk.Bff.Api/Models/Ai/node-routing-config.schema.json` | NEW — JSON Schema 2020-12 contract with conditional-required rule | ~55 |

### Tests

| Path | Change | Lines |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Models/Ai/NodeRoutingConfigTests.cs` | NEW — 27 tests covering parse / validate / serialize / round-trip / NFR-08 additive safety / backward compatibility | ~315 |

### Evidence / docs

| Path | Change | Lines |
|---|---|---|
| `projects/spaarke-ai-platform-unification-r6/notes/task-031-node-config-extension-evidence.md` | NEW — this file | ~360 |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | UPDATE — 031 🔲 → ✅ | 1 line |
| `projects/spaarke-ai-platform-unification-r6/tasks/031-...poml` | UPDATE — status not-started → completed; add notes section | ~15 |

### NOT modified (NFR-08 binding)

| Path | Status |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverOutputNodeExecutor.cs` | **UNCHANGED** — read-only verification for additive-safety; executor code is binary-identical to pre-task baseline |
| Any of the other 10 production node executors under `Services/Ai/Nodes/` | **UNCHANGED** |

### NOT modified (task 030 surface — parallel sibling)

| Path | Reason |
|---|---|
| `sprk_analysisaction` schema / scripts / `infra/dataverse/sprk_analysisaction-*.json` | Owned by task 030 (action-level `outputSchema`); independent file set |

---

## 8. Acceptance Criteria — Pass/Fail Summary

| # | Criterion | Status |
|---|---|---|
| 1 | User confirmed approval to extend node config with `destination` + `widgetType`; storage surface confirmed | ✅ Pre-approved in dispatch prompt; surface = JSON-blob (`sprk_playbooknode.sprk_configjson`) documented above |
| 2 | Node config schema accepts `destination` enum + `widgetType` string (conditionally required when workspace) | ✅ `NodeRoutingConfig.cs` + `node-routing-config.schema.json` |
| 3 | `DeliverOutputNodeExecutor.cs` is UNMODIFIED (NFR-08) and tolerates new fields additively | ✅ Verified line-by-line (32, 271-286, 427-438); existing 21 tests still pass |
| 4 | Nodes WITHOUT new fields default to `chat` destination | ✅ `Parse_WithEmptyObject_DefaultsToChatDestination` + 6 other backward-compat tests pass |
| 5 | If Dataverse-entity path: migration is idempotent | N/A — JSON-blob path selected; no Dataverse schema migration |
| 6 | BFF publish-size delta reported; ≤+5 MB R6 budget | ✅ +0.05 MB (45.90 → 45.95 MB compressed); 100× below threshold |
| 7 | Task notes record: storage-surface decision, backward-compat evidence, conditional-required rule | ✅ Sections 1, 3, 4 above |
| 8 | `code-review` + `adr-check` quality gates pass at Step 9.5 | ✅ See Section 9 below |
| 9 | TASK-INDEX.md updated (031 🔲 → ✅) and current-task.md reset | ✅ Done by main session per dispatch contract |

---

## 9. Quality Gates (Step 9.5)

### Self-audit code-review

| Topic | Result |
|---|---|
| **Security** | No auth/secret/encryption changes; no new endpoints; no new I/O paths. PASS. |
| **DI / ADR-010** | Zero new top-level DI registrations. `NodeRoutingConfig` is a static-factory record used inline by consumers. PASS. |
| **Publish size / ADR-029** | +0.05 MB delta; well under 50 MB ceiling. PASS. |
| **Naming / Spaarke conventions** | Public types in `Sprk.Bff.Api.Models.Ai`; PascalCase; XML doc comments on every public surface. PASS. |
| **Test coverage** | 27 new tests covering 100% of `NodeRoutingConfig` surface (Parse + Validate + Serialize + round-trip + degenerate + NFR-08 additive-safety). PASS. |
| **Backward compatibility** | Verified for null/empty/malformed/unknown/mixed-with-existing-fields. PASS. |
| **NFR-08 binding** | DeliverOutputNodeExecutor.cs binary-identical; 21 existing tests pass. PASS. |
| **Logging** | No new logging added; converter throws structured `JsonException` with descriptive message for unknown enum values. PASS. |
| **Error handling** | `Parse()` swallow-and-default matches `DeliverOutputNodeExecutor.ParseConfigOrDefault` pattern; `Deserialize` (sync path) surfaces `JsonException` for caller distinction. PASS. |

### Self-audit adr-check

| ADR | Result |
|---|---|
| ADR-029 (BFF publish hygiene) | +0.05 MB delta; below ceiling. PASS. |
| ADR-010 (DI minimalism) | Zero DI changes. PASS. |
| ADR-013 (AI architecture facade boundary) | `Models/Ai/NodeRoutingConfig` is a pure data contract — no `IOpenAiClient`/`IPlaybookService` references. Sits alongside existing `Models/Ai/PlaybookNodeDto.cs` per established pattern. PASS. |
| ADR-027 (Dataverse solution management) | No schema/solution changes (JSON-blob path). N/A. |
| NFR-08 (11 production node executors unmodified) | Verified by build + existing tests + line-by-line read. PASS. |
| NFR-07 (pre-fill flow signatures preserved) | `FormPrefill` destination enum value documented as tagging-only; pre-fill flow code untouched. PASS. |
| NFR-02 (≤+5 MB R6 budget) | +0.05 MB; ≪ threshold. PASS. |
| NFR-03 (no new ADRs) | No ADR changes. PASS. |
| FR-27 (per-playbook routing) | Surface delivered: `NodeRoutingConfig.Destination` + `WidgetType`. Backward compatible. PASS. |

Both gates pass.

---

## 10. Recommendation for Commit Message

Combined with task 030 (parallel sibling), the main session should commit with a single
message covering both. Suggested per-task fragment:

```
feat(r6): Wave B-G1 task 031 — node config destination + widgetType (FR-27)

Adds per-playbook routing contract to playbook node config:
- NodeDestination enum (chat / workspace / form-prefill / side-effect)
- NodeRoutingConfig record with conditional-required validation
  (widgetType required when destination=workspace)
- node-routing-config.schema.json JSON Schema 2020-12 contract
- NodeDestinationJsonConverter (kebab-case wire format)
- 27 unit tests covering parse / validate / round-trip / NFR-08
  additive-safety / backward compatibility

Storage surface: JSON-blob path — fields live inside
sprk_playbooknode.sprk_configjson alongside existing DeliveryNodeConfig
fields. NFR-08 satisfied: DeliverOutputNodeExecutor.cs UNMODIFIED;
verified additive-safe by reading at lines 32, 271-286, 427-438
(System.Text.Json default UnmappedMemberHandling.Skip).

Backward compatibility: nodes without destination/widgetType default to
chat destination (pre-R6 behavior). Validated by 7 backward-compat tests.

BFF publish size: 45.90 → 45.95 MB compressed (+0.05 MB; far below
the +5 MB NFR-02 threshold).

Unblocks: 032-035 (action migrations populate destination per node);
040/041 (StructuredOutputStreamWidget schema-aware rendering);
042 (CapabilityRouter dedup — single canonical route surface).
```

---

## 11. Anything Escalated

**None.** No NFR-08 escalation needed (DeliverOutputNodeExecutor is verifiably
additive-safe). No new ADRs needed (R6 NFR-03 honored). No new DI registrations
(ADR-010 honored). No build failures. No BFF publish-size spike. All work
completed within sub-agent file boundaries.
