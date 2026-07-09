# Task 001 — Add sprk_expression column — Notes

**Completed**: 2026-07-09 · Rigor: STANDARD · Model: sonnet@high (run on Opus session)

## What was done
Added one additive column to `sprk_fieldmappingrule` (spaarkedev1):
- `sprk_expression` — `MemoAttributeMetadata`, **MaxLength 2000**, `Format=Text`, `RequiredLevel=None` (nullable).
- Holds Concat/Template format strings for the field-mapping engine.

## Verification (acceptance criteria)
- `describe(sprk_fieldmappingrule)` → `sprk_expression MULTILINE TEXT` present.
- Metadata API → `{MaxLength: 2000, Required: None}`.
- `sprk_defaultvalue` unchanged (still `NVARCHAR(100)`) — the Default/Constant literal field.
- No existing rule record modified (metadata-only add + PublishXml).

## Method / deviations
- Used the `dataverse-create-schema` **Web API + PowerShell** path (per constraint) rather than MCP `update_table`, because MCP's `multiline text` type does not expose `MaxLength` (design requires 2000). Escalation trigger did NOT fire — no differently-named expression/template column pre-existed.
- The one-shot script lived in scratchpad (`Add-ExpressionColumn.ps1`), not committed to the repo. Rationale: the schema itself is the artifact (lives in Dataverse); this is a single additive column, not a reusable multi-entity deployment. If a repeatable seed script is later wanted, task 030 (seed) is the natural home.
- No plugin, no form script, no PCF (owner constraint honored).

**Unblocks**: 002 (BFF read-layer + DTO extension), 030 (seed attorney matrix).
