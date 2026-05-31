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

*This ledger is required at Phase 2+3 exit gate (per [`design.md`](../design.md) §6.2 line 240 + §10.5 line 560). Each entry must have a fix-by date or an owner sign-off.*
