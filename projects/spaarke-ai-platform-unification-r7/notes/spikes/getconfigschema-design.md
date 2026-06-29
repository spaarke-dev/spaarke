# GetConfigSchema() — Signature + Schema DTO Design

> **Task**: R7-030 (Wave 3, FR-16, STANDARD rigor — design only)
> **Status**: design complete; drives tasks 031 (interface change), 032 (33 executor impls), 033 (BFF endpoint), 083 (Wave 8 UI consumption)
> **Author**: Claude Code via `task-execute`, 2026-06-28
> **Coordination note**: design references `ExecutorType` (post-Wave-2-rename name). Wave 2 task 022 is in flight renaming `ActionType` → `ExecutorType` across the BFF. Task 031 (which actually adds `GetConfigSchema()` to `INodeExecutor`) blocks on Wave 2 task 023 (the parallel interface rename `SupportedActionTypes` → `SupportedExecutorTypes`) — both interface edits land in one commit.

---

## 1. Method signature

```csharp
/// <summary>
/// Returns the typed configuration schema this executor reads from
/// <see cref="PlaybookNodeDto.ConfigJson"/>. Used by the Playbook Builder
/// canvas (Wave 8 FR-23) to render typed forms instead of free-text JSON.
/// </summary>
/// <remarks>
/// MUST be a pure, deterministic method (same return every call). MUST be
/// safe to invoke at any time after construction (no DI dependencies
/// consulted, no I/O). Implementations return a singleton-cached instance.
/// Placeholder executors (no maker-editable fields) return
/// <see cref="ExecutorConfigSchema.Empty"/>.
/// </remarks>
ExecutorConfigSchema GetConfigSchema();
```

**Decisions**:
- **Sync** (not `Task`). Schemas are static descriptors; no async work.
- **No parameters.** The schema is per-executor-type, not per-node-instance.
- **Non-nullable return.** Placeholder = empty schema, never `null`.
- **Cache once.** Executors store the schema in a `private static readonly` field built once. Repeated calls return the same reference.

---

## 2. Schema DTO records — C# shape

All records live in a new file:

**Placement**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ExecutorConfigSchema.cs` (alongside `INodeExecutor.cs`).

```csharp
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Typed configuration schema returned by <see cref="INodeExecutor.GetConfigSchema"/>.
/// Drives Playbook Builder canvas form rendering (Wave 8 FR-23).
/// </summary>
public sealed record ExecutorConfigSchema(
    [property: JsonPropertyName("executorTypeName")] string ExecutorTypeName,
    [property: JsonPropertyName("executorTypeValue")] int ExecutorTypeValue,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("fields")] IReadOnlyList<ConfigSchemaField> Fields)
{
    /// <summary>Empty schema — used by executors with no maker-editable config.</summary>
    public static ExecutorConfigSchema Empty(ExecutorType type, string description) =>
        new(type.ToString(), (int)type, description, Array.Empty<ConfigSchemaField>());
}

/// <summary>
/// Single field in an executor's config schema.
/// JSON-serialized into the schema endpoint payload for canvas form rendering.
/// </summary>
public sealed record ConfigSchemaField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] SchemaFieldType Type,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("default")] object? Default = null,
    [property: JsonPropertyName("enumValues")] IReadOnlyList<string>? EnumValues = null);

/// <summary>
/// Allowed kinds of config fields. Forward-compat — new kinds added at the
/// end. Canvas falls back to read-only JSON view on unknown values (see §7).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchemaFieldType
{
    String  = 0,
    Number  = 1,
    Boolean = 2,
    Object  = 3,
    Array   = 4,
    Enum    = 5
}
```

**Naming rationale**:
- `ExecutorConfigSchema` matches the rename pattern (`ExecutorType` not `ActionType`)
- `ConfigSchemaField` is the field descriptor; not nested under `ExecutorConfigSchema` so the canvas TypeScript side can `import { ConfigSchemaField }` cleanly
- `SchemaFieldType` enum (not magic strings) — enforces type safety on the C# side; serializes to `"String"` / `"Number"` etc. via `JsonStringEnumConverter` so the TS side reads strings

---

## 3. JSON shape served by endpoint (TypeScript equivalent)

PlaybookBuilder Code Page consumes:

```typescript
interface ExecutorConfigSchema {
    executorTypeName: string;    // "AiCompletion"
    executorTypeValue: number;   // 1
    description: string;         // "Prompt-only structured LLM completion"
    fields: ConfigSchemaField[];
}

interface ConfigSchemaField {
    name: string;                                          // "templateParameters"
    type: "String" | "Number" | "Boolean" | "Object" | "Array" | "Enum";
    required: boolean;
    description: string;
    default?: string | number | boolean | object | null;   // optional
    enumValues?: string[];                                 // present only when type==="Enum"
}

interface SchemaRegistryResponse {
    schemas: ExecutorConfigSchema[];                       // one entry per executor (33 today)
}
```

**Wire example** (AiCompletion):

```json
{
  "executorTypeName": "AiCompletion",
  "executorTypeValue": 1,
  "description": "Prompt-only structured LLM completion (R7 FR-12/FR-13).",
  "fields": [
    {
      "name": "templateParameters",
      "type": "Object",
      "required": false,
      "description": "Key→value map substituted into {{var}} bindings in the JPS prompt instruction section.",
      "default": null
    },
    {
      "name": "promptSchemaOverride",
      "type": "Object",
      "required": false,
      "description": "Per-node override merged into the Action's base JPS prompt schema (FR-25). Same shape as the Action's SystemPrompt JPS object.",
      "default": null
    }
  ]
}
```

**Wire example** (placeholder — `Start`):

```json
{
  "executorTypeName": "Start",
  "executorTypeValue": 33,
  "description": "Canvas anchor; pass-through with no execution logic.",
  "fields": []
}
```

---

## 4. Placeholder / empty schema convention

For the 28 executors without rich maker-editable config (Start, ReturnResponse, DeliverOutput, Condition, Parallel, Wait, etc.), the implementation returns:

```csharp
public ExecutorConfigSchema GetConfigSchema() =>
    ExecutorConfigSchema.Empty(ExecutorType.Start, "Canvas anchor; pass-through with no execution logic.");
```

**Wire**: `{ "executorTypeName": "Start", "executorTypeValue": 33, "description": "...", "fields": [] }`

Canvas behavior: when `fields.length === 0`, render no config form (collapsed config section with a "no configuration required" hint). Distinguishes placeholder from "we forgot to define" — the empty array IS the contract.

Wave 8 task 084 implements the 5 priority executors with rich schemas: **AiCompletion**, **AiAnalysis**, **AiEmbedding**, **EntityNameValidator**, **DeliverComposite**. Wave 8 task 085 supplies empty schemas for the remaining 28.

---

## 5. Nested-object handling (e.g., AiCompletion's `promptSchemaOverride`)

Two candidate strategies were considered:

| Strategy | Pros | Cons |
|---|---|---|
| **A. Flat with `SchemaFieldType.Object`** (chosen) | Single round-trip; canvas renders a sub-JSON editor (Monaco-style) for `Object`-typed fields; preserves authoring flexibility for arbitrarily shaped JPS overrides | Loses recursive type info — canvas cannot render nested typed forms |
| **B. Recursive `Fields` on `ConfigSchemaField`** | Full nested typed forms | Premature complexity — the only known nested cases (JPS overrides, scope filters) are arbitrarily shaped by design; nested typed forms would over-constrain authoring |

**Decision**: Strategy A. `SchemaFieldType.Object` signals to the canvas "render a JSON sub-editor here." If a future need emerges for typed nested objects, add a `nestedFields?: ConfigSchemaField[]` optional property to `ConfigSchemaField` — additive, non-breaking.

---

## 6. Endpoint contract

```
GET /api/ai/playbook-builder/executor-config-schemas
```

| Aspect | Decision |
|---|---|
| **Path** | Confirmed `/api/ai/playbook-builder/executor-config-schemas` (matches existing `/api/ai/playbook-builder/{operation}` kebab-case convention in `AiPlaybookBuilderEndpoints.cs`; pluralized noun consistent with "this returns N records") |
| **Method** | `GET` (first GET on this group — all existing endpoints are POST for command operations; this is the first query) |
| **Response** | `200 OK` with `{ "schemas": ExecutorConfigSchema[] }` (envelope, not raw array — leaves room for `{ "schemas": [...], "version": "..." }` later) |
| **Order** | Schemas ordered by `ExecutorTypeValue` ascending (deterministic for diff-friendly testing and predictable canvas grouping) |
| **Auth** | Same as other PlaybookBuilder endpoints — `RequireAuthorization()` on the group; no special role |
| **Caching** | No server-side cache initially. Client caches in-memory for the session (schemas change only across deployments). Response includes no `Cache-Control` header in v1 — revisit if measured payload grows past ~50KB |
| **Payload size estimate** | 33 executors × ~6 fields × ~200 bytes ≈ 40KB worst case; trivial |
| **Errors** | Endpoint cannot fail in normal operation — registry is constructed at DI-time. If somehow empty, return `200` with `{ "schemas": [] }` rather than 500 (better degradation for canvas) |

**Wave 3 task 033 implementation sketch**: handler injects `INodeExecutorRegistry`, calls `GetAllExecutors()`, projects each to `GetConfigSchema()`, returns ordered envelope.

---

## 7. Forward-compat strategy

**Adding a new `SchemaFieldType`** (e.g., `Date`, `Duration`, `Currency`):
- Append to enum tail — never insert in middle (numeric values are wire contract)
- Canvas TS union widens: `"String" | "Number" | ... | "Date"`
- Existing canvas builds that don't know `"Date"` fall through to the unknown-type warning state (FR-27 pattern: read-only JSON view + "unsupported field type — update Playbook Builder Code Page" hint)

**Adding a new field property** (e.g., `validationRegex`, `minLength`):
- Add as optional property on `ConfigSchemaField` (`init` accessor on the record)
- C# serializer omits `null` values when `[JsonIgnore(Condition = WhenWritingNull)]` is applied
- Older canvas builds ignore unknown JSON properties — additive, non-breaking

**Removing a field property**: BREAKING. Requires Wave-style coordinated update (server + canvas same deploy). Avoid; deprecate by marking `Required = false` + setting `Default` first.

**Renaming `ExecutorTypeName`**: BREAKING — canvas keys grouping by it. Avoid; treat string names as part of wire contract.

The "unknown type" canvas fallback is the SAME pattern documented in spec FR-27 (`unknown executor type` warning state) — reuse the same warning component to keep canvas behavior consistent across "unknown executor" and "unknown field type" cases.

---

## 8. Worked example: AiCompletion full schema

Demonstrates how task 032 will implement `GetConfigSchema()` on `AiCompletionNodeExecutor`:

```csharp
private static readonly ExecutorConfigSchema CachedSchema = new(
    ExecutorTypeName: nameof(ExecutorType.AiCompletion),
    ExecutorTypeValue: (int)ExecutorType.AiCompletion,
    Description: "Prompt-only structured LLM completion (FR-12). Requires Action FK with SystemPrompt + OutputSchemaJson. Prohibits Tool + Document.",
    Fields: new ConfigSchemaField[]
    {
        new(
            Name: "templateParameters",
            Type: SchemaFieldType.Object,
            Required: false,
            Description: "Key→value map substituted into {{var}} bindings in the JPS prompt instruction section.",
            Default: null),
        new(
            Name: "promptSchemaOverride",
            Type: SchemaFieldType.Object,
            Required: false,
            Description: "Per-node override merged into the Action's base JPS prompt schema (FR-25). Same shape as the Action's SystemPrompt JPS object.",
            Default: null)
    });

public ExecutorConfigSchema GetConfigSchema() => CachedSchema;
```

## 9. Worked example: Placeholder (Start)

```csharp
private static readonly ExecutorConfigSchema CachedSchema =
    ExecutorConfigSchema.Empty(
        ExecutorType.Start,
        "Canvas anchor; pass-through with no execution logic.");

public ExecutorConfigSchema GetConfigSchema() => CachedSchema;
```

---

## 10. Open questions (none blocking task 031)

- **Q (deferred)**: should `Description` support markdown for tooltips in canvas? Decision: not in v1. Plain text only; the canvas can wrap in a tooltip component. Markdown adds XSS surface for a marginal UX gain.
- **Q (deferred)**: should the endpoint return `{ schemas, version: <git-sha> }` so the canvas can detect server upgrades? Decision: not in v1. Client-side session cache + page-reload-on-deploy is sufficient. Revisit if cache invalidation becomes an operator pain point.

---

## 11. Backward-compatibility with existing `sprk_configjson`

`sprk_configjson` on `sprk_playbooknode` continues to be the runtime carrier of node configuration. `GetConfigSchema()` is a **read-side contract** for the Builder canvas — it does NOT change how executors read config at runtime. Executors continue to deserialize `ConfigJson` into private config records (see `EntityNameValidatorNodeConfig` for pattern).

The schema is the **maker contract** (what fields the canvas presents). The `ConfigJson` payload is the **runtime contract** (what executors deserialize). They MUST stay aligned — task 032's per-executor PR template includes a checklist item "schema field names match config record `[JsonPropertyName]` attributes" to keep them locked.

---

## 12. Acceptance criteria reflected in this design

| POML AC | Where addressed |
|---|---|
| Document defines `GetConfigSchema()` method signature | §1 |
| Document defines `ExecutorConfigSchema` + `ConfigFieldDescriptor` records with JSON serialization names | §2 |
| Document enumerates allowed `Type` values | §2 (enum) + §3 (TS union) |
| Document specifies placeholder/empty schema shape | §4 + §9 |
| Document specifies endpoint payload aggregation | §6 (response envelope; ordering by `ExecutorTypeValue`) |
| Document specifies forward-compat for unknown types | §7 |
| Document confirms endpoint URL OR raises as Open Question | §6 — confirmed; consistent with existing group |
| Document includes 1-2 worked examples (rich + placeholder) | §8 + §9 |

---

*Drives tasks 031 (interface change), 032 (implement on 33 executors), 033 (BFF endpoint), 034 (xUnit tests for endpoint), 035 (architecture doc update), 083 (Wave 8 UI consumption).*
