# Wave 7 — AnalysisQueryTools → AnalysisQueryHandler migration

**Status**: COMPLETE
**Date**: 2026-06-08
**Agent**: Wave 7 sub-agent (AnalysisQueryTools migration)
**Worktree branch**: `work/spaarke-ai-platform-unification-r6`

## Summary

Migrated the legacy hardcoded `AnalysisQueryTools` class (chat-only) to a typed `IToolHandler` implementation per R6 Pillar 2. The legacy class exposed two LLM functions (`GetAnalysisResult`, `GetAnalysisSummary`); the new handler exposes a single tool with a `method` discriminator parameter, dispatching to the same underlying `IAnalysisOrchestrationService.GetAnalysisAsync` projection logic. The legacy `.cs` file was deleted; the legacy registration in `SprkChatAgentFactory.ResolveTools` was removed; the FR-11 data-driven block now surfaces the handler at chat-session start via the SYS- seed row.

## Files modified

| File | Action |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/AnalysisQueryHandler.cs` | NEW — IToolHandler implementation |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/AnalysisQueryTools.cs` | DELETED — legacy class replaced by handler |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Removed legacy `--- AnalysisQueryTools ---` registration block (lines 786-814); replaced with retirement marker comment. Updated doc-comment at line 679. |
| `infra/dataverse/sprk_analysistool-analysis-query-row.json` | NEW — single seed row with `method` enum schema |
| `scripts/Seed-TypedHandlers.ps1` | Added entry `"AnalysisQueryHandler" = …` to `$RowFiles` map |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/AnalysisQueryHandlerTests.cs` | NEW — 25 tests (4 contract + per-handler) |

## Handler class location

`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/AnalysisQueryHandler.cs`

- `HandlerId = nameof(AnalysisQueryHandler)` → `"AnalysisQueryHandler"`
- `SupportedToolTypes = [ToolType.Custom]`
- `SupportedInvocationContexts = InvocationContextKind.Both` (FR-12)
- Constructor injects `IAnalysisOrchestrationService` (scoped DI) + `ILogger<AnalysisQueryHandler>` (no captured state; resolved at execution time per R6 conventions)

## Seed row(s) — single row, descriptive toolcode

| File | sprk_name | sprk_toolcode | sprk_handlerclass | Context |
|---|---|---|---|---|
| `infra/dataverse/sprk_analysistool-analysis-query-row.json` | `SYS-Analysis Query` | `ANALYSIS-QUERY` | `AnalysisQueryHandler` | `Both (100000002)` |

## Method-dispatch decision — Option (b)

**Chosen**: Option (b) — ONE row + `method` enum discriminator parameter in the JSON Schema.

**Rationale (stop-and-surface analysis preserved here)**:

The task prompt recommended Option (a) — TWO rows sharing one handler class — but the shared `Seed-TypedHandlers.ps1` uses `sprk_handlerclass` as the upsert key with a `SYS-%` name safety filter. Two rows with the same handler class would collide on the first-match query in `Find-ExistingRow`; the second deploy attempt would PATCH the first row instead of creating a new one, leaving only ONE row deployed.

Option (a) would have required modifying the shared seed-script's upsert logic to use a composite key (e.g., `sprk_handlerclass + sprk_toolcode`). That script is also being edited by 3 sibling Wave 7 agents in this same wave — coordinated script changes carry merge-conflict risk and the contract is unlikely to be fully decided in a single wave.

Option (b) — `method` enum discriminator — preserves the same LLM information surface (the model picks `"GetAnalysisResult"` vs `"GetAnalysisSummary"` from a constrained `enum` with per-value description, equivalent to picking between two distinct tool names) while:

- Keeping the seed script's upsert key unchanged (no shared-file edit-race risk)
- Producing one cleanly-described tool with a clear method enum in the schema
- Mapping naturally to the handler's internal dispatch (one `switch` on the method string)
- Behavioral parity with the legacy `AnalysisQueryTools` (same text output formats preserved verbatim)

After this task landed, a sibling Wave 7 agent (TextRefinementHandler) selected Option (a) with 3 rows via method-discriminator and refactored the script's upsert key to `sprk_toolcode + sprk_handlerclass`. The script comment block was updated to document the new contract, but the single-row case (this handler) remains valid — the `Find-ExistingRow` function continues to locate the unique row by either key. Either option is now supported in the seed script.

## SprkChatAgentFactory.cs diff summary

- **Lines removed** (~29 lines): The entire `--- AnalysisQueryTools ---` try/catch block (constructor + 2 `AIFunctionFactory.Create` calls + warning/skip paths)
- **Lines added** (~8 lines): A retirement-marker comment block in the same slot explaining that the data-driven FR-11 block below now surfaces the handler
- **Other**: doc-comment at line 679 updated to remove `AnalysisQueryTools` from the "ungated tools" list and add a note about the Wave 7 migration

Sibling tool blocks (`DocumentSearchTools`, `KnowledgeRetrievalTools`, `TextRefinementTools`, `WorkingDocumentTools`, …) are UNCHANGED.

## Deployment evidence (Spaarke Dev)

```
$ ./scripts/Seed-TypedHandlers.ps1 -OnlyHandler AnalysisQueryHandler

Seeding R6 Pillar 2 typed handler sprk_analysistool rows
  Environment : https://spaarkedev1.crm.dynamics.com
  Rows        : AnalysisQueryHandler
  Preview     : False

--- AnalysisQueryHandler (ANALYSIS-QUERY → AnalysisQueryHandler) ---
  No existing row — POSTing new sprk_analysistool
  Created with sprk_analysistoolid = 8e33860b-3d63-f111-ab0c-70a8a53ec687

Done.
```

MCP `read_query` verification:

```json
[
  {
    "sprk_analysistoolid": "8e33860b-3d63-f111-ab0c-70a8a53ec687",
    "sprk_name": "SYS-Analysis Query",
    "sprk_toolcode": "ANALYSIS-QUERY",
    "sprk_handlerclass": "AnalysisQueryHandler",
    "sprk_availableincontexts": 100000002,
    "sprk_availableincontextsname": "Both"
  }
]
```

## Test results

- AnalysisQueryHandler tests: **25 / 25 pass** in ~2.3 s
- AutoDiscoveryVerificationTests: 6 / 6 pass (handler discovered by assembly scan)
- Broader `Services.Ai.Handlers` + `Services.Ai.Chat` regression suite: **974 pass, 0 fail, 3 pre-existing skips**
- Build: 0 errors, 16 warnings (all pre-existing, unrelated)

## BFF publish-size delta

- Measured compressed publish size after migration: **45.89 MB**
- Baseline (per CLAUDE.md, R6 starting point): ~45.65 MB
- **Delta: +0.24 MB** (well below the ≤+0.5 MB per-handler target and far below the ≤+5 MB R6 NFR-01 budget)

Note: The delta likely also includes earlier R6 + sibling Wave 7 code that has landed in this worktree since the 45.65 baseline was recorded. The pure incremental delta from this single migration is essentially zero (handler code replaces an equivalent-size legacy tool class plus its registration block).

## ADR compliance check

| ADR | Verdict |
|---|---|
| ADR-010 (DI minimalism) | ✅ Auto-discovered via `ToolFrameworkExtensions.AddToolHandlersFromAssembly`; ZERO new top-level DI lines |
| ADR-013 (AI architecture / facade boundary) | ✅ Handler lives under `Services/Ai/Handlers/`; no `Services/Ai/PublicContracts/` changes |
| ADR-014 (per-tenant caching) | ✅ Handler validates `TenantId` on both paths; tenant isolation enforced upstream in `IAnalysisOrchestrationService.GetAnalysisAsync` |
| ADR-015 (AI data governance) | ✅ Telemetry surface is handler name + outcome + IDs + duration only. Per-test scan for known confidential markers (e.g., `ConfidentialClientName-12345`) confirms no leak in log capture |
| ADR-029 (publish hygiene) | ✅ +0.24 MB measured, within all limits |
| NFR-04 (no Microsoft Agent Framework) | ✅ Zero `Microsoft.Agents.*` references introduced |

## Coordination notes

- Sibling Wave 7 agents previously halted to surface infrastructure decisions (KnowledgeRetrieval, VerifyCitations). AnalysisQueryTools migration proceeded because the prompt explicitly identifies it as the "no citations, no SSE — safe" trivial case (confirmed by `notes/wave-07-knowledge-retrieval-migration.md`).
- No edits collided with sibling work. The `Seed-TypedHandlers.ps1` was already edited by the TextRefinement sibling between handler implementation and deploy; my single-row map entry merged cleanly under both the old and new upsert-key conventions in the script.

## Follow-ups (out of scope here)

- The retired legacy registration in `SprkChatAgentFactory.cs` left a retirement-marker comment for traceability. A future closeout task could decide whether to delete the marker once all 10 chat tools have migrated.
- The `PlaybookCapabilities.cs` doc-comment at line 30 still references the legacy class name `AnalysisQueryTools` purely as a descriptive label — left untouched to avoid colliding with the parallel `capability-filtering` work (R2 task 047). Update there if/when that work touches the file.
