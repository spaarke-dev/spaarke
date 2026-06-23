# Task 032 — extend PlaybookEmbeddingService with JPS matching metadata

> **Date**: 2026-06-22
> **Author**: Wave 2-B (sub-agent dispatch → main session completion after agent stream-timeout)
> **Verdict**: ✅ **PASS** — FR-10 embedding extension live; 4 new unit tests pass; +1.33 MB publish delta

---

## Summary

`PlaybookEmbeddingService.ComposeContentText` now tolerantly parses the new `sprk_jps_matching_metadata` JSON column (added by task 031) and appends `documentTypes + intents + triggerPhrases` arrays to the embed-input string. Null / missing / malformed JSON falls back to the baseline 4-source composition with a single warning log (ADR-015 compliant — no JSON content leakage).

This is the FR-10 routing-quality improvement: structured JPS metadata now influences vector similarity for queries like "summarize this NDA". The full benchmark validation is task 038.

---

## Embed-input composition (before / after)

**Before** (4 sources, `\n`-separated):
```
{playbookName}
{description}
{triggerPhrases joined with " | "}
{tags joined with ", "}
```

**After** (7 possible sections; only present sections contribute — no blank padding):
```
{playbookName}
{description}
{triggerPhrases joined with " | "}
{tags joined with ", "}
{jps.documentTypes joined with ", "}              ← new, when present
{jps.intents joined with ", "}                    ← new, when present
{jps.triggerPhrases joined with " | "}            ← new, when present
```

Order is deterministic (documentTypes → intents → triggerPhrases) for embedding-cache stability.

---

## Code changes

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookEmbeddingDocument.cs` | Added `JpsMatchingMetadata` property (`string?`, `[JsonIgnore]` — transient input, not persisted to AI Search) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs` | (1) Refactored `ComposeContentText` into 2 overloads (one parses, one takes pre-parsed); (2) Added `ParseJpsMatchingMetadata` tolerant parse helper; (3) Added `ReadStringArray` private helper; (4) Added `JpsMatchingMetadataParse` internal sealed record with `Empty` / `EmptyMalformed` / `HasAny`; (5) `IndexPlaybookAsync` logs parse counts (debug) or malformed warning (warning, no content) per ADR-015 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingServiceComposeContentTextTests.cs` | 4 new xUnit cases (null / full / malformed / partial) |

---

## Acceptance criteria results

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `PlaybookEmbeddingService` reads `sprk_jps_matching_metadata`; composed embed-input includes `documentTypes + intents + triggerPhrases` when JSON is well-formed | ✅ |
| 2 | Malformed/null JSON path returns baseline composition without exception | ✅ — tested (case 3) |
| 3 | Unit tests for all 4 cases pass | ✅ 4/4 in 5 ms |
| 4 | `dotnet build` + `dotnet test` exit 0 | ✅ 0 errors, 16 pre-existing warnings |
| 5 | BFF publish delta within NFR-01 thresholds | ✅ +1.33 MB (well under +5 MB escalation; 13.92 MB under 60 MB ceiling) |
| 6 | code-review + adr-check exit 0 | ✅ (see Quality Gates below) |

---

## Test results

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PlaybookEmbeddingServiceComposeContentText"
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 5 ms
```

Regression — Phase 1 stable-ID migration suite still green:
```
dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ --filter "FullyQualifiedName~Phase1StableIdMigration"
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 190 ms
```

---

## Publish-size measurement

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-032/
Compress-Archive deploy/api-publish-032/* deploy/api-publish-032.zip -CompressionLevel Optimal
→ 46.08 MB compressed
```

| Baseline (pre-task) | Post-task | Delta | NFR-01 ceiling | Headroom |
|---|---|---|---|---|
| 44.75 MB (current-task.md pre-rebase) | 46.08 MB | **+1.33 MB** | 60 MB | 13.92 MB |

**Note**: The +1.33 MB delta likely includes some post-rebase baseline drift from `origin/master` work (Daily Update Service r2.2 commits replayed under our branch), not just task 032 changes. The pure FR-10 add is ~5 KB of source — most of the apparent delta is environmental.

Either way: under the +5 MB per-task escalation threshold per CLAUDE.md §10 / NFR-01.

---

## Quality gates (inline code-review + adr-check)

### CLAUDE.md §11 Component Justification

- **Existing**: `PlaybookEmbeddingService.ComposeContentText` is the canonical embed-input composer. JSON parsing for Dataverse Memo content has no existing helper in the BFF codebase (closest neighbor is ad-hoc `JsonDocument.Parse` usage in unrelated services).
- **Extension**: Yes — extended via overload (one parses, one consumes pre-parsed). The new `ParseJpsMatchingMetadata` helper + `ReadStringArray` private + `JpsMatchingMetadataParse` internal record all live in the same service file. No new public surface.
- **Cost-of-doing-nothing**: FR-10 acceptance benchmark ("summarize this NDA" returns Summarize-NDA top-1, task 038) would fail; embeddings would miss the structured `documentTypes`/`intents`/`triggerPhrases` signal needed for routing precision; the field added in task 031 would have no consumer.

### ADR compliance

| ADR | Check | Verdict |
|---|---|---|
| ADR-013 (AI facade boundary) | All new code in `Services/Ai/PlaybookEmbedding/`; no PublicContracts crossings | ✅ |
| ADR-014 (AI caching) | Cache-key derivation participates in `ComposeContentText`; new content fingerprints in. No new cache TTL changes needed | ✅ |
| ADR-015 (Data governance — tier-1 logging) | Warning log uses playbook ID only, NEVER the JSON content; debug log uses counts only | ✅ — verified in code |
| ADR-029 (BFF publish hygiene) | +1.33 MB delta < +5 MB threshold; no `<PublishTrimmed>` / `<PublishAot>` added | ✅ |
| ADR-010 (DI minimalism) | No new DI registration; helpers are static internal | ✅ |

### Other checks

- **Backward compat**: Tested via case 1 (null JPS → baseline 4-section composition) ✅
- **Deterministic ordering**: Tested via case 2 (assert documentTypes index < intents index < triggerPhrases index in composed string) ✅
- **No exception bubble**: Tested via case 3 (malformed JSON → no throw, baseline fallback) ✅
- **Partial JPS handling**: Tested via case 4 (only documentTypes populated → 1 extra section, not 3) ✅

---

## Sub-agent dispatch notes

A sub-agent (`general-purpose` type, ID `add50cf866bdb5c3c`) was dispatched for this task and made high-quality progress on the code changes (added the model property, refactored the service, added the parse helper and `ReadStringArray`) but exhausted its 33 tool calls / API stream-timed-out before:
- Defining the `JpsMatchingMetadataParse` record (build was broken)
- Writing the unit tests
- Measuring publish size
- Updating TASK-INDEX / current-task.md
- Writing this evidence note
- Committing

The main session diagnosed the build break (2× `CS0246: type ... could not be found`), added the missing record, wrote the 4 unit tests inline, ran build+test+publish+regression, and completed the task end-to-end. The agent's earlier work was kept verbatim — only the missing record was added. No re-implementation needed.

**Process note for future autonomous mode**: 33 tool calls is the soft limit for `general-purpose` agents on FULL-rigor BFF tasks that include build+test+publish loops. Future dispatches should chunk this into 2 sub-agents OR have the main session do build+test+publish after the agent returns the code.

---

## Phase 2 Wave 2-B closeout

- Task 032 ✅ — embedding extension live, tested, published-size-clean
- Task 033 🚫 BLOCKED — see `notes/handoffs/033-block-send-to-index-button.md` (owner decision pending)
- Wave 2-B is partially complete; Wave 2-C (tasks 034 drift job, 035 admin view, 036 validation gate) can proceed since their 033 dependency was removed.

---

## Related artifacts

- POML: `tasks/032-extend-playbookembeddingservice-embed-input.poml`
- Schema doc consumed: `architecture/jpsmatchingmetadata-schema.json` (task 031)
- Field reference (actual logical name): `sprk_jps_matching_metadata` per task 031 evidence
- Block ref for sibling task: `notes/handoffs/033-block-send-to-index-button.md`
