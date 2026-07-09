# Business-slice determinism check — verdict (Task 003 / FR-P0-03)

> **Project**: spaarke-ai-architecture-redesign-r2
> **Task**: 003 — Business-slice determinism check
> **Decision this settles**: D-M2 / NFR-04 — does the Business slice (host-record identity +
> per-table "schema card" / write contract) qualify for the ContextEnvelope's cache-stable
> prefix, or must it move to a volatile/semi-stable position?

## Verdict: **CONFIRMED DETERMINISTIC — Business slice stays in the stable prefix.**

Both render sites named by the task anchors are pure functions of their inputs. Neither
embeds a timestamp, mints a per-request GUID, or depends on unordered-collection iteration.
NFR-04 is **not** re-scoped; D-M2's placement of Business ahead of the volatile ledger tail
stands as designed.

## What was verified (existing assembly path — ADR-040: no second render path introduced)

### Site 1 — Host-record identity block
`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs:602-707`
(`AppendEntityEnrichmentAsync`, appended via `GetContextAsync`).

- Built from `ChatHostContext` (`EntityType`, `EntityId`, `EntityName`, `PageType`) — all
  caller-/record-supplied, not clock- or randomness-derived.
- The one GUID in the rendered text is the **caller-supplied host record id** (`EntityId`) —
  stable for the same record across turns, not a per-request mint (`Guid.NewGuid()` does not
  appear anywhere in this method or its call path).
- No `DateTime`/`DateTimeOffset` value is embedded in the rendered text (the method's only
  time use, `EstimateTokenCount`/budget checks, is a length calculation, not rendered content).
- The server-side `EntityName` lazy-fetch (`TryResolveEntityNameAsync`, T151) hits Dataverse,
  but for the **same record** it resolves the same `sprk_name` value — the pinning test drives
  this exact path (two independent `PlaybookChatContextProvider` instances, mirroring two
  separate Scoped-per-request renders) and asserts byte-identical output.
- Construction order (`recordPhrase` → `pageSentence` → binding instruction) is a fixed
  string-interpolation sequence, not a dictionary/set iteration — no ordering instability.

### Site 2 — Per-table write-contract description ("hand-mirrored schema card")
`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs:92-134`
(`Metadata.Description` — the `sprk_matter` write-contract text design.md §D-M2 calls out by
name as "currently hand-mirrored across tool descriptions").

- This text is a **compile-time string literal** assigned once to a `readonly` auto-property
  initializer — it is not rebuilt per call, per request, or per instance. Two independent
  handler instances return byte-identical text by construction, and the pinning test proves it.
- No timestamp or GUID substring appears anywhere in the text (verified by regex scan in the
  pinning test, not eyeballing).
- This is precisely the fragility D-M2 flags for the *future* Context Binder (task 053): today
  the schema card is safe only because it is hand-authored and frozen; the Binder's job is to
  assemble it dynamically from live Dataverse metadata **without losing this determinism
  property** — the negative-control tests below exist so that guarantee has a machine-checked
  trip-wire once task 053 replaces the hand-mirrored text with a live render.

## Evidence: the pinning test

`tests/integration/contract/Api/Ai/BusinessSliceDeterminismContractTests.cs` (KEEP path:
endpoint/contract-adjacent render-contract stability; no `Mock<HttpMessageHandler>`, no
DI-registration assertions — ADR-038 compliant):

| Test | Proves |
|---|---|
| `HostIdentityBlock_RenderedTwiceForSameHostContext_IsByteIdentical` | Two independent provider instances rendering the same host record (name resolved via live Dataverse lazy-fetch) produce byte-identical `SystemPrompt`; the enrichment block contains no timestamp and exactly one GUID (the host record id). |
| `HostIdentityBlock_IdOnlyShape_RenderedTwice_IsByteIdentical` | The id-only branch (unresolvable name) is equally byte-identical across two renders. |
| `WriteContractDescription_RenderedFromTwoIndependentInstances_IsByteIdentical` | The hand-mirrored write-contract text is byte-identical across two independent handler instances and carries no timestamp/GUID. |
| `RepeatRenderEqualityCheck_WhenGuidInjectedPerCall_FailsToMatch` | **Negative control (AC5)**: a render that mints a GUID per call fails the same byte-identity assertion used above — proving the real tests would catch this regression, not pass by construction. |
| `RepeatRenderEqualityCheck_WhenTimestampVariesAcrossCalls_FailsToMatch` | **Negative control (AC5)**: a render with a varying embedded timestamp fails the same assertion. |
| `RepeatRenderEqualityCheck_WhenSourceOrderingUnstable_FailsToMatch` | **Negative control (AC5)**: reordering the source column list changes the rendered text — the ordering-instability failure mode NFR-04 names is also caught. |

Result: **6/6 passed.**

```
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

## Build / publish-size / CVE

No production code changed (verification-only task — determinism was already true on the
existing render path; no fix was required). Consequently:

- `dotnet build src/server/api/Sprk.Bff.Api/` — **Build succeeded**, 0 errors (pre-existing
  warnings only, unrelated to this task).
- Publish-size delta: **N/A** — no production file touched, no new package added.
- CVE scan: **N/A** — no new/changed package reference.
- Placement Justification: N/A (no new service/endpoint/DI registration added — a test-only
  addition under an existing KEEP path).

## Note on a pre-existing, unrelated build blocker encountered during verification

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/_Scratch_PromptAssemblyMeasurement.cs` (an
untracked scratch fixture belonging to the **parallel task 002** "Measure-first prompt-assembly
baseline") currently fails to compile (`SessionOutput` object initializer references a `Key`
member that does not exist on the record). This is task 002's own in-flight scratch work
(explicitly self-documented as "Deleted immediately after the measurement run — do not
classify at /test-diet") and out of task 003's scope. To verify this task's build/test without
editing task 002's file, it was temporarily renamed out of the compile path for the single
`dotnet test` run above and restored immediately afterward — no content change, verified via
`git status` (file remains untracked, unmodified). **Flagging for whoever executes/completes
task 002**: fix the `SessionOutput` initializer (drop the `Key` member or use whatever the
actual identifying member is) before that task's own build/test verification step.

## Consumers

- **Task 053 (Context Binder + ContextEnvelope assembly)**: proceeds with Business in the
  stable prefix per D-M2; when the Binder replaces the hand-mirrored write-contract text with
  a dynamic, live-metadata-driven schema-card render, re-run (or extend) this pinning test
  against the new render path — the negative controls above are the trip-wire for the ordering/
  timestamp/GUID failure modes a dynamic render is more exposed to than a frozen string literal.
- **NFR-04 (prompt-cache stability)**: no re-scope needed; ContextEnvelope budget table
  (design.md §D-M2, Business ≤ 1,200 tokens, stable prefix) stands as written.
