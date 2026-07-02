# 06 — Update Record executor: Choice fields declared as `type:"string"`

> **Priority**: MEDIUM — data quality + authoring UX
>
> **Source**: R7 W12 Document Upload wizard UAT, 2026-07-02
>
> **Scope**: Cross-cutting — every playbook that uses `UpdateRecord` to write a Choice column has the same trap

## What happened

During Document Upload wizard smoke, the Profile Document playbook (`18cf3cc8-02ec-f011-8406-7c1e520aa4df`) reached the Update Record node with this fieldMappings entry:

```json
{"field":"sprk_documenttype","type":"string","value":"{{output_aiAnalysis.output.sprk_documenttype}}"}
```

`sprk_documenttype` on `sprk_document` is a **Choice** column (values `Contract=100000000`, `Invoice=100000001`, ... `Other=100000012`). The AI returns the label ("Contract", "Invoice", etc.). The mapping declared `type:"string"`, so `UpdateRecordNodeExecutor.CoerceFieldValue` fell into the String branch and passed the string label through to Dataverse. Dataverse returned **500 Internal Server Error** on the PATCH.

## R7 unblock

Data-only PATCH: dropped `sprk_documenttype` from the Update Record node's fieldMappings. The other 6 fields still update (summary, tl;dr, keywords, extractorganization, extractpeople, filetype). Doc profile completes; the Choice field just isn't set. Applied 2026-07-02 via `mcp__dataverse__update_record`.

## The underlying gap

`UpdateRecordNodeExecutor.CoerceFieldValue` already supports Choice types when the mapping declares:

```json
{
  "field": "sprk_documenttype",
  "type": "choice",
  "options": { "Contract": 100000000, "Invoice": 100000001, ... },
  "value": "{{output_aiAnalysis.output.sprk_documenttype}}"
}
```

But nothing in the authoring surface enforces this. Playbooks authored via canvas + AI-schema wire everything as `type:"string"` because that's what the AI's output looks like. Result: Choice writes silently fail at execution time, only surfacing in UAT.

## Three tracks for R5 to consider

### Track A — Authoring-time gate (playbook builder)

When a maker maps an AI output field to a Dataverse column, the canvas UI looks up the target column's metadata:
- Column is Choice → force `type:"choice"` and pre-populate the `options` map from the metadata
- Column is Boolean/Number/Lookup → coerce type accordingly
- Column is text → `type:"string"` (default)

Prevents the trap at authoring time. Requires the PlaybookBuilder to have Dataverse metadata access (may already via the schema endpoint added in Wave 3).

### Track B — Runtime auto-coercion in `UpdateRecordNodeExecutor`

When `type:"string"` mapping fails PATCH with 500, retry with metadata-driven coercion:
- Look up column metadata
- If column is Choice, try to match rendered value against the option-set labels (case-insensitive) → coerce to numeric option value
- If column is Boolean → parse
- Etc.

More robust but more code + one round-trip on failure. Duplicates authoring intelligence at runtime.

### Track C — AI prompt discipline

Update the Action JPS for Profile Document (`bb356968-ebe9-f011-8406-7ced8d1dc988`) to return numeric option values instead of labels — e.g., have the AI emit `sprk_documenttype: 100000000` instead of `sprk_documenttype: "Contract"`.

Fragile — requires every Choice-writing Action to encode the option-set constants in its prompt; brittle to Dataverse schema changes.

## Recommendation for R5

Track A (authoring-time) is the durable fix. Track B is a good defensive-second layer to catch bugs Track A misses. Track C is a stopgap for existing playbooks that authored before Track A shipped.

## Test cases

1. Author a playbook via canvas that maps to `sprk_documenttype`. Verify the canvas forces `type:"choice"` and pre-fills options from metadata.
2. Execute the playbook end-to-end. Verify the Choice value is written correctly (round-trip: read back the record, confirm the enum value matches).
3. Sweep other Choice fields on `sprk_document` (`sprk_classification`, `sprk_filesummarystatus`, `sprk_documentstatus`, `sprk_sourcetype`, `sprk_relationshiptype`, etc.) — same pattern applies.
4. Sweep other entities used by Update Record nodes (Matter, Project, Todo, etc.).

## References

- Executor: [`src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs) — `CoerceFieldValue` at line 417
- Playbook node (patched 2026-07-02): `sprk_playbooknode = 0fa4e8db-b216-f111-8343-7c1e520aa4df` (Profile Document → Update Record)
- Target entity metadata: `sprk_document` — verify via `mcp__dataverse__describe`
- Wave 3 schema endpoint: `GET /api/ai/playbook-builder/executor-config-schemas` (may already expose column-metadata; check whether canvas consumes)
