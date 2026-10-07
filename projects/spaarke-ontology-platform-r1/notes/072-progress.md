# Task 072 - classifier guidance injection (stream D)

Branch `stream/d-072`. Status: complete.

## What the code actually does
- `LookupChoicesResolver` is unchanged for names: the value at `lookup:sprk_triagecategory.sprk_name` is the bare names,
  which `ActionRunner` turns into the schema `enum`. For entities in `GuidanceColumns` (only `sprk_triagecategory`)
  it also reads `sprk_classifierguidance` and stores prompt lines at `LookupChoicesResolver.GuidanceKey(ref)`.
  Guidance is joined to the names already behind the enum, so the prompt cannot offer a category the schema rejects.
- `PromptSchemaRenderer` renders `## Allowed values for '<field>'` from that key. This is needed because TRIAGE-EMAIL is
  `structuredOutput:true`: the renderer's old `- one of:` hint is text-mode only, so in structured mode the model saw
  NO category names in the prompt, only the schema enum.
- Failures: `ai_choices_resolution_failures_total{reference,reason}` with reason `read_failed`, `no_values` (also how
  `ScopeResolverService` surfaces a swallowed HTTP error), `guidance_read_failed`. Guidance failure degrades to bare names.

## Deviations from the POML
1. POML named one file; the change spans resolver + `PromptSchemaRenderer` + `IScopeResolverService`/`ScopeResolverService`
   (new `QueryLookupGuidanceAsync`, which throws instead of swallowing so the failure is observable). Side-key in the
   existing dictionary chosen over a new Render parameter to avoid changing three call paths (ActionRunner, node
   executor, GenericAnalysisHandler).
2. The `triage-email` Action row / JPS is NOT edited (email project owns it); no schema or Dataverse writes made.
3. Guidance text is flattened to one line and capped at 1000 chars (longest authored row is about 540).

## For task 074 (recall gate)
- Prompt now carries tie-breaker text, but the recall measurement must run the REAL path (ActionRunner against
  spaarkedev1 rows), not a replayed prompt.
- `sprk_enabled` is not consulted by the names or guidance reads (only `statecode`); all ten rows are active and enabled today.
- Guidance read costs one extra Dataverse call per triage run.
