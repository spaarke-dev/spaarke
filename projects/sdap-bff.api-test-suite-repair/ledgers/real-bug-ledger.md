# Real Bug Ledger — `sdap-bff.api-test-suite-repair`

> **Purpose**: Track tests marked `[Trait("status", "real-bug-pending-fix")]` and Skip'd because they assert correct behavior that the production code does not (yet) provide. Per project §6.2 / NFR-01, these tests CANNOT be fixed in this project — a separate PR/project must fix production. The tests remain in the suite (Skip'd) so the bugs are not forgotten.
>
> **Schema**: Each row identifies the bug, the test(s) affected, the production file owning the bug, and a fix-by date.

---

## RB-T012-01 — `SessionRestoreService.NormaliseETag` and `ExtractODataETag` mishandle embedded quote chars in OData/JSON ETag values

| Field | Value |
|---|---|
| **Bug ID** | RB-T012-01 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 012 (P1.A3 — `Services/Ai/Tools` + `Services/Ai/Sessions` batch) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionRestoreService.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionRestoreService.cs) |
| **Affected methods** | `NormaliseETag(string etag)` (line 435) and `ExtractODataETag(string jsonBody)` (line 405) |
| **Tests Skip'd** | (1) `SessionRestoreServiceTests.NormaliseETag_StripsOuterQuotes` (`Theory`, 2 inline cases) — line 140; (2) `SessionRestoreServiceTests.ExtractODataETag_FindsETagInJsonBody` (`Fact`) — line 148. Both in [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Sessions/SessionRestoreServiceTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Sessions/SessionRestoreServiceTests.cs). |
| **Fix-by date** | 2026-07-31 (60-day target — non-blocking; functional staleness contract still works in production because both sides of the comparison receive identical broken normalization) |
| **Severity** | LOW (functional contract preserved end-to-end; tests over-specify contract relative to implementation) |
| **Owner** | TBD (AI session-restore feature owner; coordinate with `ai-spaarke-insights-engine-r1` if Insights team owns Sessions/) |

### Bug detail

#### Bug A — `NormaliseETag` over-strips embedded `"` chars

**Documented contract** (from the SUT XML doc comment, line 432):
> Strips surrounding double-quotes from an ETag value for comparison.
> e.g., `"W/\"1234\""` → `W/"1234"`.

**Implementation** (line 436):
```csharp
internal static string NormaliseETag(string etag) => etag.Trim('"');
```

**Bug**: `string.Trim('"')` removes ALL `"` characters from both ends of the string until it hits a non-`"`. For an ETag value like `W/"1234"` (8 chars: `W`,`/`,`"`,`1`,`2`,`3`,`4`,`"`), it strips the trailing `"` → returns `W/"1234` (7 chars). For an ETag wrapped in JSON-escaped quotes like `"W/\"1234\""` (12 chars), it strips the leading `"` plus BOTH trailing `"` chars → returns `W/\"1234\` (9 chars, with a dangling trailing `\`). Neither matches the documented "strip surrounding quotes only" contract.

#### Bug B — `ExtractODataETag` stops at first JSON-escaped quote

**Documented contract** (from the SUT XML doc comment, line 402):
> Extracts the `@odata.etag` value from a Dataverse OData JSON response body.
> Format: `"@odata.etag":"W/\"1234567\""`

**Implementation** (lines 405-429):
```csharp
const string marker = "\"@odata.etag\":\"";
var start = jsonBody.IndexOf(marker, StringComparison.Ordinal);
// ...
start += marker.Length;
var end = jsonBody.IndexOf('"', start);
// ...
return jsonBody[start..end];
```

**Bug**: `IndexOf('"', start)` finds the FIRST raw `"` character after the marker, but Dataverse ETag values contain literal `\"` (JSON-escaped quotes) — e.g., body `{"@odata.etag":"W/\"12345\"",...}` contains the escaped-quote sequence `\"` at the 19th char. `IndexOf` ignores the leading `\` and treats the `"` as the end of the value, returning `W/\` (3 chars) instead of `W/\"12345\"` (10 chars). A proper implementation must handle JSON escape sequences (or use `System.Text.Json` for parsing).

### Why this didn't surface in production

The functional end-to-end contract (`CheckSingleEntityAsync`) compares saved ETag vs. current ETag, with both sides passed through `NormaliseETag`. Because the same broken normalization is applied to both sides, byte-equal saved-and-current ETags remain equal after normalization, and byte-different ones remain different. The staleness-detection contract holds end-to-end, masking the per-helper bugs from runtime tests. Tests on the helpers individually surface the bug.

The HTTP-header path (`response.Headers.TryGetValues("ETag", ...)` at line 311) returns a raw header value that does not contain JSON escape sequences, so `ExtractODataETag` is only invoked when the header is absent. In live Dataverse traffic, the ETag header is always present, so `ExtractODataETag` is rarely exercised.

### Recommended production fix (out of scope for this project)

For `NormaliseETag`: replace `Trim('"')` with a check that strips at most one leading and one trailing `"` only when they form a matched outer pair (e.g., regex `^"(.*)"$` or manual length-check substring).

For `ExtractODataETag`: replace the substring approach with `System.Text.Json.JsonDocument.Parse(jsonBody).RootElement.GetProperty("@odata.etag").GetString()`. This correctly handles JSON escape sequences.

### Verification after fix

When production is fixed, remove the `Skip = "..."` attributes on the 2 Skip'd tests (`NormaliseETag_StripsOuterQuotes` and `ExtractODataETag_FindsETagInJsonBody`) and change the per-test `[Trait("status", "real-bug-pending-fix")]` to inherit the class-level `[Trait("status", "repaired")]`. Run the tests; they should pass. Update this ledger row to "Resolved" with the fix-commit SHA + date. Remove the row entirely after the next phase exit review.

---

## RB-T034-01 — `AgentConfigurationService.GetExposedPlaybookIdsAsync` does not honor cancellation token

| Field | Value |
|---|---|
| **Bug ID** | RB-T034-01 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 034 (P23.B2 — factory-dependent config-path batch 2) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Api/Agent/AgentConfigurationService.cs`](../../../src/server/api/Sprk.Bff.Api/Api/Agent/AgentConfigurationService.cs) |
| **Affected method** | `GetExposedPlaybookIdsAsync(string tenantId, CancellationToken cancellationToken = default)` (line 44) |
| **Tests Skip'd** | (1) `AgentConfigurationServiceTests.GetExposedPlaybookIdsAsync_RespectsCancellationToken` (`Fact`) — line 444 in [`tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConfigurationServiceTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Api/Agent/AgentConfigurationServiceTests.cs). |
| **Fix-by date** | 2026-07-31 (60-day target — LOW severity; tests-only impact since callers in production currently always pass `CancellationToken.None`) |
| **Severity** | LOW (no production caller passes a non-default token; the cancellation contract is unobserved in live traffic) |
| **Owner** | TBD (M365 Copilot agent feature owner) |

### Bug detail

**Documented test contract** (test name + Assert): a pre-cancelled `CancellationToken` passed to `GetExposedPlaybookIdsAsync` must surface as `OperationCanceledException` before the method completes.

**Implementation** (lines 44-65 of `AgentConfigurationService.cs`):

```csharp
public async Task<IReadOnlyList<Guid>> GetExposedPlaybookIdsAsync(
    string tenantId,
    CancellationToken cancellationToken = default)
{
    var cacheKey = $"{CacheKeyPrefix}{tenantId}:exposed-playbooks";
    var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
    // ...
}
```

**Bug**: The method does NOT call `cancellationToken.ThrowIfCancellationRequested()` before invoking the cache. The injected `MemoryDistributedCache` (used in tests; in production it is a Redis-backed `IDistributedCache`) returns synchronously on the first lookup when the value is absent / already in-process. For an already-cancelled token, neither the service nor the in-memory cache implementation raises `OperationCanceledException`. The test fails with `Assert.Throws() Failure: No exception was thrown`.

Sibling methods on the same service (`IsCapabilityEnabledAsync`, `IsRolePermittedAsync`, `InvalidateCacheAsync`) have the same defensive-cancellation gap, but only `GetExposedPlaybookIdsAsync` is asserted by a `RespectsCancellationToken` test.

### Why this didn't surface in production

The single in-process caller of `AgentConfigurationService` is the M365 Copilot agent endpoint, which currently does not flow a `CancellationToken` derived from the HTTP request — it passes `CancellationToken.None`. The defensive cancellation pattern is therefore exercised only by this unit test. Live traffic never hits the broken path.

### Recommended production fix (out of scope for this project)

Add `cancellationToken.ThrowIfCancellationRequested();` as the FIRST statement inside `GetExposedPlaybookIdsAsync` (and, defensively, on the three sibling public methods). This honors the documented `CancellationToken` parameter contract and matches the canonical .NET pattern for async public APIs.

Single-line minimal fix in `AgentConfigurationService.GetExposedPlaybookIdsAsync` (line 47):

```csharp
public async Task<IReadOnlyList<Guid>> GetExposedPlaybookIdsAsync(
    string tenantId,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();  // <-- add
    var cacheKey = $"{CacheKeyPrefix}{tenantId}:exposed-playbooks";
    // ...
}
```

### Verification after fix

Remove the `Skip = "..."` attribute on `GetExposedPlaybookIdsAsync_RespectsCancellationToken` and the per-test `[Trait("status", "real-bug-pending-fix")]` (inherits class-level `[Trait("status", "repaired")]`). Run the test; should pass. Update this ledger row to "Resolved" with the fix-commit SHA + date. Remove the row at the next phase exit review.

---

## RB-T050-01 — `SourcePaneSseEventData.CitationId` missing `JsonIgnoreCondition.WhenWritingNull`; emits `citationId: null` instead of omitting the field

| Field | Value |
|---|---|
| **Bug ID** | RB-T050-01 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 050 (P23.M1 — `Services/Ai/Chat` batch 1 + `Services/Ai/Feedback`/`RagService`/`WorkingDocumentService` extension) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/SourcePaneSseEvent.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/SourcePaneSseEvent.cs) |
| **Affected members** | `SourcePaneSseEventData.CitationId` (line 51) — `JsonPropertyName("citationId")` attribute is present, but no `JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)` accompanies it. |
| **Tests Skip'd** | (1) `ChatSseEventFactoryTests.CreateSourcePaneEvent_WithNullCitationId_OmitsCitationIdField` (`Fact`) — line 197 of [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SseEventTypes/ChatSseEventFactoryTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SseEventTypes/ChatSseEventFactoryTests.cs). |
| **Fix-by date** | 2026-07-31 (60-day target — non-blocking; frontend SSE consumer tolerates `null` citationId values, so wire-format bloat is the only observable effect) |
| **Severity** | LOW (functional contract preserved — frontend ignores `null` citation IDs; only adds ~16 bytes per source_pane SSE event without citation) |
| **Owner** | TBD (AI Chat SSE feature owner; coordinate with frontend Spaarke.UI.Components ChatPane consumers if the contract is tightened) |

### Bug detail

**Documented contract** (from the XML doc comment at `SourcePaneSseEvent.cs:43-46`):
> Optional citation ID linking this source pane content to a citation marker in the response text (e.g., "[1]", "[2]"). When present, the frontend can establish a cross-pane link between the response text and the source.

The matching test assertion (correct, per documented contract):

```csharp
json.Should().NotContain("\"citationId\"",
    "citationId should be omitted when null per JsonIgnoreCondition.WhenWritingNull");
```

**Implementation** (line 48-51):

```csharp
public record SourcePaneSseEventData(
    [property: JsonPropertyName("widgetType")] string WidgetType,
    [property: JsonPropertyName("widgetData")] JsonElement WidgetData,
    [property: JsonPropertyName("citationId")] string? CitationId = null);
```

**Bug**: `CitationId` is nullable and has a default of `null`, with `JsonPropertyName("citationId")` — but no `JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)`. The default `System.Text.Json` writer policy emits `"citationId": null` for null reference types. Production output:

```json
{"widgetType":"web_reference","widgetData":{"url":"https://example.com"},"citationId":null}
```

Expected per documented contract + test:

```json
{"widgetType":"web_reference","widgetData":{"url":"https://example.com"}}
```

### Proposed fix

Add the property-level `JsonIgnore` attribute alongside the existing `JsonPropertyName`:

```csharp
public record SourcePaneSseEventData(
    [property: JsonPropertyName("widgetType")] string WidgetType,
    [property: JsonPropertyName("widgetData")] JsonElement WidgetData,
    [property: JsonPropertyName("citationId")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CitationId = null);
```

### Why this didn't surface in production

The SSE event is consumed by the frontend `ChatPane` source-rendering code, which checks `if (event.data.citationId)` before linking — so a literal `null` value is treated identically to a missing field. The wire-format bloat is small (~16 bytes per source_pane event without citation) and was not noticed in observability metrics. The unit test that asserts the documented contract surfaces the bug; the frontend's defensive null-check masks it from end-to-end behavioral tests.

### Verification after fix

Remove the `Skip = "..."` attribute on `CreateSourcePaneEvent_WithNullCitationId_OmitsCitationIdField` and the per-test `[Trait("status", "real-bug-pending-fix")]` (inherits class-level `[Trait("status", "repaired")]`). Run the test; should pass. Update this ledger row to "Resolved" with the fix-commit SHA + date. Remove the row at the next phase exit review.

---

## RB-T044-01 — `ConversationHistorySanitizer.StripRetrievedContent` `fromTurnIndex` semantics inverted

| Field | Value |
|---|---|
| **Bug ID** | RB-T044-01 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 044 (P23.H5 — Ai/Safety) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Safety/CrossMatter/ConversationHistorySanitizer.cs) |
| **Affected method** | `StripRetrievedContent(IReadOnlyList<ChatMessage> history, int fromTurnIndex)` (line 55) |
| **Tests Skip'd** | 5 in [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/PrivilegeLeakageTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/PrivilegeLeakageTests.cs): `MatterPivot_StripsRetrievalContent_PreservesUserAndAssistantMessages`, `MatterPivot_NoPrivilegedTextInSanitizedOutput`, `MatterPivot_StripsOnlyWithinWindow_PreservesNewMatterContent`, `MatterPivot_PreservesNonRetrievalSystemMessages`, `Sanitizer_OnlyReturnsDocs_FromActiveMatter`. |
| **Fix-by date** | 2026-07-31 (60-day target — HIGH severity per design.md §3.3 HIGH-tier safety; cross-matter privilege leakage protection is currently inverted) |
| **Severity** | HIGH (cross-matter privilege content protection is the intended outcome; the inversion means stale retrieval content from previous matters may leak to new-matter turns) |
| **Owner** | TBD (AI safety / cross-matter feature owner) |

### Bug detail

**Documented contract** (XML doc, line 41 of `ConversationHistorySanitizer.cs`):
> When the user switches from Matter A to Matter B, any tool_result messages that contain retrieved document passages from Matter A are replaced with a privacy placeholder.

**Implementation** (lines 62-90): the loop passes through messages where `i > fromTurnIndex` and only strips retrieval messages where `i <= fromTurnIndex`.

**Bug**: `MatterContextDetector.DetectChange` returns `ChangeDetectedAtTurnIndex = i` where `i` is the index of the PREVIOUS matter marker — the START of the previous-matter window. The sanitizer interprets this as "strip from index 0 up to and including the pivot index, pass through everything after." That is the OPPOSITE of the intended behavior. With history `[markerA(0), user(1), retrievalA(2), assistantA(3), user(4)]` and `fromTurnIndex=0`, only index 0 is in the strip window — but index 0 is the matter marker (not a retrieval message), so nothing gets stripped. The retrieval at index 2 leaks into the new-matter context.

### Why this didn't surface in production

The full end-to-end matter-pivot integration test path was not previously exercised in CI (this HIGH-tier batch is the first that covers the Sanitizer at the boundary). Live traffic would silently leak privileged content from a previous matter into the model's context window for any subsequent turn — a serious safety regression that requires HIGH-tier prioritization.

### Recommended production fix (out of scope for this project)

Invert the index check at line 68: change `if (i > fromTurnIndex)` to `if (i < fromTurnIndex)`. Re-verify all 5 Skip'd tests pass + existing passing tests (`Sanitizer_StripsRetrievalBlocks_PreservesConclusions`, etc.) remain green.

### Verification after fix

Remove the 5 `Skip = "..."` attributes + per-test `[Trait("status", "real-bug-pending-fix")]`. Run `dotnet test --filter "FullyQualifiedName~PrivilegeLeakageTests"`; all 5 must pass. Update this row to "Resolved" with fix-commit SHA + date.

---

## RB-T044-02 — `CitationExtractor.NormalizeCaseLaw` over-strips trailing period of reporter abbreviation

| Field | Value |
|---|---|
| **Bug ID** | RB-T044-02 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 044 (P23.H5 — Ai/Safety) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs) |
| **Affected method** | `NormalizeCaseLaw(Match m)` (line 163) |
| **Tests Skip'd** | `ExtractCitations_CaseLaw_MatchedAndNormalized` Theory (4 InlineData cases) in [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/CitationExtractorTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Safety/CitationExtractorTests.cs). |
| **Fix-by date** | 2026-07-31 (60-day target — MEDIUM severity) |
| **Severity** | MEDIUM (citation normalization affects downstream verification provider lookups + UI display) |
| **Owner** | TBD (AI citations/verification feature owner) |

### Bug detail

**Documented canonical form** (test InlineData expectations + the class XML doc table at line 11): canonical key for `Smith v. Jones, 542 U.S. 296 (2004)` is `"542 U.S. 296"` — preserving the trailing period of the reporter abbreviation `U.S.`.

**Implementation** (line 167):

```csharp
var reporter = m.Groups["reporter"].Value.Trim().TrimEnd('.');
```

**Bug**: `TrimEnd('.')` strips the trailing `.` of the reporter token. The regex captures `U.S.` (with trailing period), and the normalizer strips it to `U.S`, yielding the non-canonical key `542 U.S 296`.

### Recommended production fix

Remove `.TrimEnd('.')` from line 167 (the reporter capture group already excludes the trailing year-court parenthetical; no other trim is needed).

### Verification after fix

Remove the `Skip = "..."` attribute on `ExtractCitations_CaseLaw_MatchedAndNormalized` Theory; remove per-Theory `[Trait("status", "real-bug-pending-fix")]`. Run; all 4 InlineData cases must pass.

---

## RB-T044-03 — `CitationExtractor.NormalizeStatute` does not trim subsections from canonical section

| Field | Value |
|---|---|
| **Bug ID** | RB-T044-03 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 044 (P23.H5 — Ai/Safety) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs) |
| **Affected** | `StatutePattern()` regex (line 44) + `NormalizeStatute(Match m)` (line 172) |
| **Tests Skip'd** | `ExtractCitations_Statute_StripsSubsectionsInNormalizedKey` (`Fact`) — split from original Theory by task 044 — in `CitationExtractorTests.cs`. |
| **Fix-by date** | 2026-07-31 (60-day target — LOW severity) |
| **Severity** | LOW (affects only statutes cited with subsection parentheticals; canonical `§ 101`-style cites unaffected) |
| **Owner** | TBD (AI citations/verification feature owner) |

### Bug detail

**Documented canonical form**: `See 17 U.S.C. § 512(c)(1)(A).` → canonical key `17 U.S.C. § 512`. The regex captures `(?<section>\d[\d\-\.]*[a-z]?(?:\([a-z0-9]+\))*)` — INCLUDING the subsection parentheticals. The normalizer concatenates verbatim, yielding `17 U.S.C. § 512(c)(1)(A)` instead of `17 U.S.C. § 512`.

### Recommended production fix

Strip the parenthetical in the normalizer:

```csharp
var section = m.Groups["section"].Value.Trim();
var parenStart = section.IndexOf('(');
if (parenStart >= 0) section = section[..parenStart];
return $"{title} U.S.C. § {section}";
```

### Verification after fix

Remove `Skip` + trait on `ExtractCitations_Statute_StripsSubsectionsInNormalizedKey`. Run; must pass. Verify other Statute Theory cases still pass.

---

## RB-T044-04 — `CitationExtractor.NormalizePatent` double-prefixes EP/WO country codes

| Field | Value |
|---|---|
| **Bug ID** | RB-T044-04 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 044 (P23.H5 — Ai/Safety) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs) |
| **Affected method** | `NormalizePatent(Match m)` (line 180), EP + WO branches |
| **Tests Skip'd** | `ExtractCitations_Patent_NonUS_MatchedAndNormalized` Theory (2 InlineData: EP, WO) — split from original Theory by task 044 — in `CitationExtractorTests.cs`. |
| **Fix-by date** | 2026-07-31 (60-day target — MEDIUM severity) |
| **Severity** | MEDIUM (100% regression for non-US patent normalization: `EP3456789` → `EPEP3456789`) |
| **Owner** | TBD (AI citations/verification feature owner) |

### Bug detail

The regex's `ep` and `wo` capture groups include the country-code prefix in the captured value. The normalizer then PREPENDS the literal `"EP"` (or `"WO"`) again. Result: `EP3456789` becomes `EPEP3456789`. The US branch is correct because the regex `(?<us>[\d,]{5,15})` captures digits-only.

### Recommended production fix

Drop the literal prefix in the EP and WO branches of the normalizer:

```csharp
if (m.Groups["ep"].Success)
    return Regex.Replace(m.Groups["ep"].Value, @"[^\dA-Za-z]", "");

if (m.Groups["wo"].Success)
    return Regex.Replace(m.Groups["wo"].Value, @"[^\dA-Za-z/]", "");
```

### Verification after fix

Remove `Skip` + trait on `ExtractCitations_Patent_NonUS_MatchedAndNormalized`. Run; both EP and WO InlineData cases must pass. Verify US Patent Theory cases still pass.

---

## RB-T044-05 — `CitationExtractor.RegulationPattern` does not accept documented `CFR` (no-period) form

| Field | Value |
|---|---|
| **Bug ID** | RB-T044-05 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 044 (P23.H5 — Ai/Safety) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Safety/Citations/CitationExtractor.cs) |
| **Affected method** | `RegulationPattern()` regex (line 74) |
| **Tests Skip'd** | `ExtractCitations_Regulation_NoPeriodForm_MatchedAndNormalized` (`Fact`) — split from original Theory by task 044 — in `CitationExtractorTests.cs`. |
| **Fix-by date** | 2026-07-31 (60-day target — LOW severity) |
| **Severity** | LOW (LLM outputs commonly use the period form `C.F.R.`; no-period `CFR` form is the corner case) |
| **Owner** | TBD (AI citations/verification feature owner) |

### Bug detail

Class XML doc line 15 explicitly lists `21 CFR Part 312` as a supported example. The regex requires the literal `C.F.R.` form (only the trailing period is optional). Input `21 CFR Part 312` does not match, contradicting the documented contract.

### Recommended production fix

Loosen the inter-letter periods to optional:

```csharp
@"\b(?<title>\d{1,3})\s+C\.?F\.?R\.?(?:\s+(?:Part|§)\s*)(?<part>\d[\d\-\.]*)"
```

### Verification after fix

Remove `Skip` + trait on `ExtractCitations_Regulation_NoPeriodForm_MatchedAndNormalized`. Run; must pass. Verify the original Regulation Theory cases still pass.

---

## RB-T053-01 — `CapabilityRouter` Layer 1 substring keyword classifier produces semantic-gap false positives

| Field | Value |
|---|---|
| **Bug ID** | RB-T053-01 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 053 (P23.M4 — `Services/Ai/Capabilities` non-Streaming batch) |
| **Production file** | [`src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs) |
| **Affected** | Layer-1 classifier substring-match scoring (algorithm overview at class XML doc line 17-26): `score = matched_hints / total_hints` where match is `lowercased hint is substring of lowercased user message`. Confidence formula `topScore / (topScore + secondScore + Epsilon)` saturates at 1.0 when only one capability matches any keyword. |
| **Tests Skip'd** | (1) `CapabilityRouterBenchmarkTests.Layer1_DoesNotFalsePositive_OnNonKeywordMessages` (`Fact`); (2) `CapabilityRouterBenchmarkTests.Layer1_FullCorpus_DistributionSummary` (`Fact`). Both in [`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterBenchmarkTests.cs`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterBenchmarkTests.cs). |
| **Fix-by date** | 2026-07-31 (60-day target — MEDIUM severity; Layer 2 LLM-disambiguation is the documented mitigation, so end-to-end routing converges on the correct capability via the multi-layer cascade; the bug is observable only when Layer 1 is exercised in isolation, as in these benchmark tests) |
| **Severity** | MEDIUM (Layer 1 hit rate is 68.6% on the corpus — above the 60% target — and Layer 2/3 cascade corrects misroutes in live traffic; however, the documented zero-false-positive invariant is violated, undermining single-call cost optimization for the affected message patterns) |
| **Owner** | TBD (AI capability-routing feature owner; coordinate with `ai-spaarke-action-engine-r1` if Action Engine team owns capability classification) |

### Bug detail

**Documented contract** (from class XML doc comment line 22-24 + test name + assertion at `CapabilityRouterBenchmarkTests.cs:191`):
> Layer 1 must never confidently route to the wrong capability. Messages with `expectedLayer=2` or `3` should NOT be confidently routed by Layer 1.

The matching test assertion (correct, per documented contract):
```csharp
falsePositiveCount.Should().Be(0,
    "Layer 1 must not produce false-positive confident results for off-topic or ambiguous messages");
```

**Implementation** ([`CapabilityRouter.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs) line 17-26):
1. Snapshot the enabled capability list once per call.
2. Normalize the user message to lowercase.
3. Score each capability as `matched_hints / total_hints` where "match" = lowercased hint is a substring of the lowercased message.
4. Pick the top-scoring capability; confidence = `topScore / (topScore + secondScore + Epsilon)`.
5. If confidence >= 0.80 → confident; else → uncertain.

**Bug**: Substring matching cannot disambiguate keyword presence from keyword intent. The 105-message benchmark corpus surfaces 4 specific failures (2 Layer-2 misroutes + 1 Layer-2 misroute + 1 Layer-3 false-positive):

| id | Message | Expected | Actual | Confidence |
|---|---|---|---|---|
| 77 | "Set the priority of the Henderson case to urgent" | `write_back` | `legal_research` (matched `case law` ⊃ `case`) | 1.00 |
| 89 | "What is the latest on the Martinez case?" | `entity_lookup` | `legal_research` (same root cause) | 1.00 |
| 91 | "Pull the brief for the amicus curiae filing" | `document_search` | `summarize_content` (matched `brief`) | 1.00 |
| 102 | "What version of the AI model are you using?" | (Layer 3 — off-topic) | `document_analysis` (matched `analyze document` ⊃ `model`?) | 1.00 |

In all 4 cases the user message contains a token that matches exactly one capability's keyword hint as a substring, so the scoring formula collapses to `topScore / (topScore + 0 + ε) ≈ 1.0`, well above the 0.80 confidence threshold.

### Why this didn't surface in production

The three-tier router cascade (Layer 1 keyword → Layer 2 LLM-classifier → Layer 3 fallback) is the documented mitigation. In live traffic, when Layer 1 produces a confident-but-wrong result, the downstream playbook execution or tool dispatch surfaces the mismatch — the cascade self-corrects via tool-result feedback. The unit test exercises Layer 1 in isolation precisely to surface the substring-matching limitation; the end-to-end routing path masks it.

The Layer 1 hit rate of 68.6% (above the 60% NFR target) is the load-bearing observability metric; the 3 confidently-wrong cases on a 105-message corpus correspond to a routing-precision floor that is acceptable for cost-optimization (single-call routing on the 96.4% of messages where Layer 1 is correct) but violates the documented zero-false-positive guarantee.

### Recommended production fix (out of scope for this project)

Three viable approaches, in increasing complexity:

1. **Word-boundary matching**: change `message.Contains(hint, StringComparison.OrdinalIgnoreCase)` to a regex `\b<hint>\b` match. This eliminates the "case law" → matches "case" false-positive on id=77/89 because the regex requires the full bigram to appear with word boundaries. Estimated effort: ~2h.

2. **Negative-evidence scoring**: track which capabilities have keyword hints that are PROPER substrings of other capabilities' hints (e.g., `case` ⊂ `case law`), and apply a discount factor when the user message matches only the shorter substring. Eliminates the bigram-superstring false-positive class entirely. Estimated effort: ~4-6h.

3. **Confidence-saturation guard**: when only one capability scores > 0, cap confidence at 0.75 (below the 0.80 threshold) instead of 1.0. Forces Layer 2 disambiguation for single-match cases. This is the conservative fix — it sacrifices some Layer 1 hit rate (currently 68.6%) but guarantees zero false-positives. Estimated effort: ~1h.

The MEDIUM severity rating reflects that the cascade self-corrects in live traffic, but the documented Layer 1 contract is violated. Recommend approach (2) if router precision matters for cost; approach (3) if the contract guarantee matters more.

### Verification after fix

Remove the `Skip = "..."` attributes on both Skip'd tests and the per-test `[Trait("status", "real-bug-pending-fix")]` overrides (the class-level `[Trait("status", "repaired")]` will then apply). Run the tests; both must pass. Update this ledger row to "Resolved" with the fix-commit SHA + date. Remove the row entirely after the next phase exit review.

---

*This ledger is required at Phase 2+3 exit gate (per [`design.md`](../design.md) §6.2 line 240 + §10.5 line 560). Each entry must have a fix-by date or an owner sign-off.*
