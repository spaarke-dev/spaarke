# Wave 7b — Per-playbook capability filter infrastructure

**Status**: COMPLETE
**Date**: 2026-06-08
**Agent**: Wave 7b dispatch
**Unblocks**: Wave 7c (VerifyCitations re-attempt), Wave 8 (LegalResearch / WebSearch / CodeInterpreter), Wave 9 (WorkingDocumentTools)

## TL;DR

Wave 7b adds the missing per-playbook capability filter to the data-driven block in `SprkChatAgentFactory.ResolveTools()` so capability-gated tools (VerifyCitations, LegalResearch, WebSearch, CodeInterpreter, WriteBack, Reanalyze) can migrate from hardcoded registration to data-driven `sprk_analysistool` rows WITHOUT silently expanding the chat-tool surface to playbooks that don't have the capability.

Change is **additive + backward-compatible**: every existing pre-Wave-7b row has `sprk_requiredcapability = null`, which is interpreted as "always available" — exactly the pre-Wave-7b behavior.

---

## Files changed

### Schema / data layer

- **NEW**: `scripts/Add-AnalysisToolRequiredCapability.ps1` — idempotent PowerShell deployment script for `sprk_requiredcapability` (single-line text, MaxLength 100, nullable) on `sprk_analysistool`.

### BFF code

- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` — added `RequiredCapability` property to `AnalysisTool` DTO with full doc-comment covering enforcement, ADR-018 distinction, and case-insensitive contract.
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisToolService.cs` — three edits:
  - `ListToolsAsync` `$select` clause appends `sprk_requiredcapability`.
  - `GetToolAsync` + `ListToolsAsync` DTO construction wires `RequiredCapability = MapRequiredCapability(entity.RequiredCapability)`.
  - New `MapRequiredCapability(string?)` mapper — null/whitespace → null, otherwise `Trim()` and pass through.
  - `ToolEntity` private record gains `[JsonPropertyName("sprk_requiredcapability")] public string? RequiredCapability { get; set; }`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — three edits:
  - New counter `dataDrivenSkippedCapability` to track skip count + summary log update.
  - Per-row capability check between dedup and HandlerClass validation — calls new helper `IsCapabilityGateSatisfied`.
  - New `internal static bool IsCapabilityGateSatisfied(string? requiredCapability, IReadOnlySet<string> capabilities)` helper near `TryParseMatterId` — null/whitespace → true (always-available), otherwise case-insensitive `Equals` scan over the capability set.

### Documentation

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/HandlerRegistrationConventions.md` — added `sprk_requiredcapability` to the Dataverse row table (point 3) and new "Capability-Gated Tools" section with the 6-tool table, contract bullets, ADR-018 distinction, and migration responsibilities.

### Tests

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisToolDtoTests.cs` — added Wave 7b section (10 tests):
  - DTO default construction → null
  - Round-trip serialization (Theory: 6 canonical capability values + null)
  - `with` expression preserves field
  - `MapRequiredCapability` null / whitespace / known / surrounding-whitespace / unknown (forward-compat)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryToolResolutionTests.cs` — added Wave 7b section (9 tests):
  - Null required capability → always passes
  - Whitespace required capability → always passes (3 InlineData)
  - Matching capability → passes
  - Missing capability → fails
  - Case-insensitive matching (Theory: 5 case-variance pairs)
  - Empty capability set + gated tool → fails
  - Empty capability set + un-gated tool → passes
  - All 6 capability-gated canonical strings round-trip via `PlaybookCapabilities` constants
  - New `InvokeIsCapabilityGateSatisfied` helper (direct call — `internal static` already exposed to test project via `InternalsVisibleTo`).

---

## Column deployment evidence

Idempotent deployment to Spaarke Dev succeeded (first attempt hit a concurrent-publish race condition; retry succeeded). MCP describe confirms the column:

```
DESCRIBE TABLE sprk_analysistool (
  ...
  sprk_jsonschema MULTILINE TEXT,
  sprk_name NVARCHAR(200) NOT NULL,
  sprk_requiredcapability NVARCHAR(100),   <-- NEW (Wave 7b)
  sprk_tags NVARCHAR(100),
  ...
);
```

Backward-compat: all existing rows have `sprk_requiredcapability = null` (column nullable, no value set). No data migration was performed in Wave 7b — Waves 7c / 8 / 9 will set the column on their migrated rows.

---

## Factory filter logic (excerpt)

```csharp
// R6 Wave 7b: per-playbook capability filter.
if (!IsCapabilityGateSatisfied(row.RequiredCapability, capabilities))
{
    dataDrivenSkippedCapability++;
    _logger.LogDebug(
        "[FR-11/Wave-7b] Skipping data-driven tool '{ToolName}' (id={ToolId}) — " +
        "requires capability '{RequiredCapability}' not in current playbook's " +
        "capability set. Tenant={TenantId}.",
        row.Name, row.Id, row.RequiredCapability, tenantId);
    continue;
}
```

The helper:

```csharp
internal static bool IsCapabilityGateSatisfied(
    string? requiredCapability,
    IReadOnlySet<string> capabilities)
{
    if (string.IsNullOrWhiteSpace(requiredCapability))
        return true;

    foreach (var capability in capabilities)
    {
        if (string.Equals(capability, requiredCapability, StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}
```

Counter `dataDrivenSkippedCapability` is included in the existing summary log so operators can observe how many rows the filter withholds per session (ADR-015: count-only telemetry, no row content).

---

## Pattern documentation snippet

The new "Capability-Gated Tools" section in `HandlerRegistrationConventions.md` documents:

- Six canonical PlaybookCapabilities values and their tool mapping
- Filter enforcement contract (where, when, how)
- ADR-018 distinction (authorization filter vs. feature flag — feature flags gate service registrations, this gates schema exposure)
- Migration responsibility checklist for Waves 7c / 8 / 9 (set field, remove hardcoded gate, add positive/negative test)

---

## Test coverage summary

**New tests**: 19 across 2 files (10 DTO/mapper + 9 capability-gate helper)

**Test run**: `dotnet test --filter "FullyQualifiedName~Sprk.Bff.Api.Tests.Services.Ai"` →
**Passed: 3379, Failed: 0, Skipped: 22 (pre-existing skips)**

All existing AI subsystem tests pass unchanged. No regressions.

---

## Design decisions

### 1. Case-insensitive matching

Canonical capability names are lowercase snake_case (`"verify_citations"`). Admins editing the `sprk_requiredcapability` column in Power Apps may type uppercase or mixed-case variants. The helper uses `StringComparison.OrdinalIgnoreCase` for forgiveness.

The today-hardcoded `if (capabilities.Contains(PlaybookCapabilities.X))` calls use `HashSet<string>`'s default comparer (which is set at construction time elsewhere in the codebase to `OrdinalIgnoreCase` — verified by reading `GetPlaybookCapabilitiesAsync` chain). So the data-driven path matches today's case-sensitivity exactly.

### 2. Single-capability requirement (not list)

Wave 7b stores ONE capability per tool — not a list. The hardcoded blocks have exactly this shape (`if (capabilities.Contains(X))` — single check), so a single-string column is sufficient to preserve today's semantics. If a multi-capability requirement ever surfaces (`AND` logic), the column can evolve to a CSV or JSON array WITHOUT breaking the single-string contract (the mapper would split, then check ALL).

Surfacing this so a future maintainer doesn't have to re-derive: no Wave 7c / 8 / 9 tool needs multi-capability gating; the hardcoded `if` blocks all use single-capability `Contains` calls.

### 3. NOT a feature flag (ADR-018 compliance)

ADR-018's distinction: feature flags are kill-switches that gate underlying service registrations and yield a 503 / graceful degradation when off. Wave 7b's `sprk_requiredcapability` is per-tool AUTHORIZATION — analogous to ACL entries — and silently withholds the tool from the LLM's function schema without affecting service registrations. The two compose: a feature-flagged-off LegalResearch service stays unregistered (no Bing Grounding); a per-tool capability gate on a feature-flagged-ON service further restricts which playbooks see the tool.

Documented in the conventions doc and in the DTO doc-comment.

### 4. Column type: single-line text vs option set

Used single-line text (`NVARCHAR(100)`) — NOT a Dataverse option-set / choice. Rationale:

- Forward-compatibility: admins can author rows with future capability names that don't yet exist in `PlaybookCapabilities`. The filter no-ops (no match), effectively withholding the tool until a matching playbook capability is defined.
- Decouples the column from option-set integer codes (the existing `sprk_playbookcapabilities` on the PLAYBOOK side is a global multi-select choice; the TOOL side mirrors its name, not its integer).
- Maker-UI ergonomics: a freeform text column displays the capability name (self-documenting), whereas a tool-side option set would force every new capability addition to be added in two places.

### 5. Null = always-available (default)

Pre-Wave-7b behavior MUST be preserved for existing rows. The 8 typed handlers + AnalysisQuery (Wave 7) + TextRefinement (Wave 7) have NO capability gate. The 6 capability-gated tools (Waves 7c / 8 / 9) will set the column. Null reads as "no gate" — exactly the existing-row semantics.

---

## ADR / NFR check

- **ADR-013**: `AnalysisTool` DTO is in `IScopeResolverService.cs`, NOT in `PublicContracts/`. AI-internal. Confirmed by `Glob` of `Services/Ai/PublicContracts/**/*.cs` — `AnalysisTool` is not exposed.
- **ADR-010**: No new top-level DI registrations. `AnalysisToolService` and `IToolHandlerRegistry` were already registered.
- **ADR-018**: Wave 7b is NOT a feature flag. Per-tool authorization filter on existing tools. ADR-018 distinction documented in DTO doc-comment + conventions doc.
- **ADR-029 / NFR-01**: Compressed publish size 45.9 MB. Pre-Wave-7b baseline ~45.65 MB. Delta +0.25 MB. Well under +5 MB per-task threshold and 60 MB ceiling.
- **NFR-04**: No Microsoft Agent Framework references introduced.

---

## Stop-and-report triggers — none hit

The eight scenarios listed in the prompt were checked:

- `capabilities` collection accessibility: already in scope of the data-driven block (parameter of `ResolveTools`).
- `AnalysisTool` PublicContracts exposure: NOT exposed; AI-internal record in `IScopeResolverService.cs`.
- `PlaybookCapabilities` extension: NOT needed; all 6 capability-gated tools map to existing constants.
- Case-sensitivity subtleties: addressed via `OrdinalIgnoreCase` comparator; verified case-variance tests pass.
- Dataverse column creation: succeeded (after concurrent-publish retry).
- Multi-capability requirement: not surfaced; today's hardcoded `Contains` calls are single-capability.

No stop-and-surface was needed. Wave 7b proceeded to completion.

---

## Next-wave handoff

Wave 7c (VerifyCitations re-attempt) can now:

1. Author `infra/dataverse/sprk_analysistool-citation-verify-row.json` with `sprk_requiredcapability = "verify_citations"`.
2. Migrate `VerifyCitationsHandler` per the Wave 7 STOP-AND-SURFACE design outline.
3. Remove the hardcoded `if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))` block at `SprkChatAgentFactory.cs` lines 1199–1230.
4. Verify via integration test that a playbook without `verify_citations` does NOT see the `verify_citations` function in the agent's tool list.

Wave 8 (LegalResearch / WebSearch / CodeInterpreter) + Wave 9 (WorkingDocumentTools) follow the same pattern — set `sprk_requiredcapability` to the canonical value, remove the hardcoded gate, add positive/negative tests.

Wave 10 (AnalysisExecutionTools / Reanalyze) is being deleted entirely (consolidated into invoke_playbook per plan), so no migration row is needed for `reanalyze` — the capability simply won't be referenced in any migrated row.
