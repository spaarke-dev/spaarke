# Task 032 loader gap + Task 036 bundling proposal

> **Date**: 2026-06-22
> **Author**: Main session (post owner-challenge on Wave 2-C blocker)
> **Status**: 🟡 honest acknowledgment of a gap surfaced by the owner's diagnostic question

---

## The gap surfaced

The owner asked: "the AI Search `playbook-embeddings` index has playbook entries — if there is no endpoint, how was that indexed?" Re-grep showed I was wrong on the endpoint absence (it lives at `POST /api/ai/playbooks/{id}/index`). Tracing further into the indexing path revealed a **second issue with my task 032 work**.

### Task 032's loader gap

Task 032 added `JpsMatchingMetadata` to [`PlaybookEmbeddingDocument`](src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookEmbeddingDocument.cs#L100) and extended `PlaybookEmbeddingService.ComposeContentText` to consume it. But the production loader at [`PlaybookIndexingService.cs:158-168`](src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexingService.cs#L158) builds the document like this:

```csharp
var document = new PlaybookEmbeddingDocument
{
    Id = playbookId,
    PlaybookId = playbookId,
    PlaybookName = playbook.Name,
    Description = playbook.Description ?? string.Empty,
    TriggerPhrases = playbook.TriggerPhrases?.ToList() ?? [],
    RecordType = playbook.RecordType ?? string.Empty,
    EntityType = playbook.EntityType ?? string.Empty,
    Tags = ParseTags(playbook)
    // ❌ Document.JpsMatchingMetadata is NEVER SET — always defaults to null
};
```

And [`PlaybookResponse`](src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookDto.cs#L84) (returned by `IPlaybookService.GetPlaybookAsync`) does not expose `JpsMatchingMetadata` at all.

**Effect**: task 032's embed-input extension is **functionally dormant in production**. The unit tests pass (they set `JpsMatchingMetadata` directly on the document), but real playbooks get indexed with `JpsMatchingMetadata = null` → the embed-input composer's well-tested tolerant-fallback path runs → only the baseline 4 sources contribute. The FR-10 routing-quality improvement does not actually fire.

### Why my completion missed this

When I picked up the sub-agent's stalled work, I focused on:
1. Fixing the missing `JpsMatchingMetadataParse` record (build was broken)
2. Adding the 4 unit tests
3. Running build/test/publish

I did NOT trace the call graph from the endpoint down to where `PlaybookEmbeddingDocument` is constructed in production. The unit tests exercised the new code path with manually-set field values, so the green tests were a false signal of completeness.

This is the same class of mistake the sub-agent made (focus on the immediate code change without verifying the end-to-end path). The owner's diagnostic question forced a deeper look — and the same lens that surfaced the wrong endpoint-block also surfaces this.

---

## Corrected scope for completing the FR-10 + FR-12 work

To make task 032 actually take effect in production AND add task 036's validation gate, the minimum-correct change set is:

| Change | File | Why |
|---|---|---|
| Add `JpsMatchingMetadata` to `PlaybookResponse` | `Models/Ai/PlaybookDto.cs` | New field surfaces from Dataverse fetch |
| Populate `JpsMatchingMetadata` in `IPlaybookService` implementation | (need to locate — likely `Services/Ai/PlaybookService.cs` or similar) | Read `sprk_jps_matching_metadata` column from Dataverse row into response |
| Set `document.JpsMatchingMetadata = playbook.JpsMatchingMetadata` | `PlaybookIndexingService.cs:158-168` | Wire the new field through the loader |
| Add validation gate to `IndexPlaybook` endpoint | `PlaybookEmbeddingEndpoints.cs:53-78` | FR-12: 400 ProblemDetails with `missingFields` array when description / documentTypes / outputDestination empty |
| Add Validation helper service in `Services/Ai/PlaybookEmbedding/` | `PlaybookIndexInputValidator.cs` (new) | Per ADR-013, validation logic lives in `Services/Ai/`; endpoint filter is the thin gate |
| Tests: 5 cases for validation (3 missing-each + all-missing + valid) | `tests/unit/.../Services/Ai/PlaybookEmbedding/PlaybookIndexInputValidatorTests.cs` (new) | STANDARD rigor test obligation |
| Optional: integration test that an indexed playbook with populated `sprk_jps_matching_metadata` actually produces a different embed-input than one without | `tests/integration/...` (extend Phase1StableIdMigrationSuite or new file) | True end-to-end verification — would have caught the loader gap |

Scope estimate: **3–5 hours** (bigger than task 036's POML 2h estimate because we're bundling the task 032 loader fix). All the work is BFF code — autonomous-friendly.

---

## Recommended next step

This is an inflection point. Three honest options:

| Option | Description | When to pick |
|---|---|---|
| **E1** | I implement the bundled fix inline in main session (3-5h of work via Read/Edit/Write/Bash). Risk: substantial context burn; could time out before commit. | If you want progress NOW and accept context risk |
| **E2** | Dispatch a fresh sub-agent with a tight, pre-decomposed prompt covering ONLY the 6 changes above + tests. Risk: sub-agent stall pattern (saw twice today) — but smaller scope than original task 032 dispatch. | If you want progress without burning my context — but accept retry risk |
| **E3** | Stop here, commit checkpoint with this analysis. You decide next session whether to: (a) implement bundled fix, (b) accept task 032 as cosmetic-only for now and pivot to Phase 3, or (c) explicitly defer FR-10 implementation. | If you want clean handoff for an unattended window or fresh session |

**Recommended**: **E2** with a tightly scoped prompt + main session does build/test/publish/commit after the agent returns code. Combines lower context burn with main-session safety net for the final mile.

Pivoting to Phase 3 (option E3 path b) is also defensible — Phase 3 is fully unblocked and orthogonal to this gap.

---

## What's already correct from task 032

For the record, these parts of my task 032 work remain correct even with the loader gap:
- ✅ `JpsMatchingMetadata` property on `PlaybookEmbeddingDocument` (with `[JsonIgnore]` — correct, never persisted)
- ✅ `ParseJpsMatchingMetadata` tolerant helper (null/missing/malformed handling validated by tests)
- ✅ `JpsMatchingMetadataParse` record (Empty / EmptyMalformed / HasAny)
- ✅ `ComposeContentText` extended (7-section composition; deterministic ordering)
- ✅ ADR-015 logging compliance (no JSON content; counts + playbook ID only)
- ✅ Tolerant fallback path (the well-tested behavior IS the path production currently takes — just not by design)
- ✅ Phase 1 regression suite still green
- ✅ +1.33 MB publish delta under thresholds

The work is **correct in isolation**; it just isn't yet wired to the production data source. The fix is small (add 1 field to `PlaybookResponse` + populate it + set it on the document) but requires tracing the `IPlaybookService` implementation.

---

## Related artifacts

- `notes/handoffs/032-bff-publish-delta.md` — original task 032 evidence (now needs amendment)
- `notes/handoffs/wave-2-c-scope-assessment.md` — Wave 2-C blocker analysis with retraction
- Surface this in spec.md follow-ups? (Probably not — it's a task-execution gap, not a spec defect.)
