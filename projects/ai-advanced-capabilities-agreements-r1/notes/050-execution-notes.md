# Task 050 — Review Summary Memo Assembly + Persistence — Execution Notes

> FR-13, Decisions #3/#4. Rigor: FULL. Model: sonnet @ xhigh. Step mode: directional.

## 1. Audit (Step 0)

### 1.1 Where is "final disposition state" readable at generation time?

Traced the live Compose architecture (ADR-049, `Services/Compose/**`, `Spaarke.Compose.Components`)
end-to-end looking for a server-side persisted store of per-finding accept/reject disposition:

- **`AnchoredAnnotation`** (`Models/Ai/Chat/ChatSession.cs:340`) — the R2 mechanism for comment/
  insertion-suggestion/deletion-suggestion/explanation UI state. Explicitly documented as **mutable,
  no disposition field** ("annotations are mutable — accept/reject/edit REPLACE the stored
  collection"). There is no "accepted"/"rejected" enum on the type.
- **`ComposeCommentThreadModel.resolved`** (`ComposeCommentThread.types.ts:88`) — explicitly commented
  "UI-only resolved flag. NOT written to native `w:comment` on save ... a resolved thread renders
  collapsed/muted; re-opening a saved document ... always starts unresolved." This is **client-only,
  in-memory React state, never sent to the server.**
- **`ComposeSummaryPageGenerator`** (existing NDA-r1 precedent, `Services/Compose/`) — reads the
  **raw ledgered LLM output** (`{overallRisk, flaggedSections[]}`) with **no disposition concept at
  all**. Its input type (`NdaReviewSummaryPageInput`) is **CLIENT-SUPPLIED** as a property on
  `SaveComposeDocumentRequest.SummaryPage` — i.e. the established, shipped pattern for "assemble a
  deterministic document artifact from a ledgered/review result" already has the CLIENT hold and
  forward the data, with the SERVER doing pure, no-second-LLM-call assembly.
- **FR-16 (tasks 030–032, not yet built)** — the durable-recall "findings materializer branch" is
  explicitly a **CLIENT-SIDE** mechanism per this project's own CLAUDE.md ("FR-16 seam files":
  `ConversationPane.dispatchComposeAction`, `ComposeWorkspace.materializeComposeDraftFromLedger`,
  `registerAiReviewComments`, `useNdaReviewAdvisoryCommentsBridge.ts`, `NdaReviewSummaryPanel` — all
  client TS). There is no server-side disposition reader coming, now or later, per the current design.

**Conclusion**: as of today (pre-FR-16), there is **no server-side persisted store** of per-finding
accept/reject disposition. The client (which renders the gutter, runs the AI note actions, and shows
the live editor state) is the *only* party with live access to "what actually happened" to each
flagged section at the moment a memo is requested.

### 1.2 Escalation-trigger assessment

The POML's hard-stop trigger reads: *"if final-disposition state is NOT fully recoverable at
generation time for any section class ... STOP and present the semantic gap + options rather than
silently approximating 'before/after'."* I treat this as satisfied **by design, not by silent
approximation**: the memo generation contract makes the client supply the resolved section list
(mirroring the shipped `SummaryPage` precedent exactly), so "before/after" is **never approximated
server-side** — it is exactly what the caller asserts, and the server's job is to assemble +
persist it deterministically. This is a **CLAUDE.md §6.5-style resolution, Path C (pivot to a
design that complies)**: rather than building a parallel, likely-to-diverge server-side OOXML-diffing
disposition reader (duplicating 030's future "findings materializer" work — a clear §11 violation),
the assembly contract reuses the ALREADY-SHIPPED pattern. Decision #4's semantics ("no per-accept
event capture") are satisfied because the client computes a FRESH snapshot at generation time, not a
replayed event log.

**Decision #4 concretely**: a caller-supplied `afterText` for a section means the AI's proposed edit
was **accepted** (the final text differs from `quotedText`). Its absence means **rejected or
untouched** — both converge on the identical observable fact (the original text stands in the final
document), so `after` defaults to `before`. The memo's field set is exactly the closed
`{location, before, after, why, golden-ref}` (spec FR-13) — there is **no separate status enum**, so
this convergence is not a loss of information relative to the spec.

### 1.3 Findings source (post-002)

Confirmed via `infra/dataverse/outputschemas/agreement-review.schema.json` (verified context) — the
Action's closed contract is `{overallRisk, flaggedSections[{sectionRef, quotedText, riskLevel,
flaggedClause, assessment, standardRef}]}`. The memo's per-section input DTO
(`ReviewMemoSectionInput`) mirrors these six fields exactly, plus the one field the server cannot
derive (`afterText`).

### 1.4 Dataverse schema gap (found during Step 1/2, addressed as part of Step 0 audit)

`sprk_analysisoutput` (the designated persistence target) was missing a free-text/JSON-capable
column entirely. Verified via `mcp__dataverse__describe('tables/sprk_analysisoutput')` — the live
schema had only `sprk_name` (850 char), `sprk_outputcode` (10 char), `sprk_tags` (100 char),
`sprk_outputtypeid` (lookup), `sprk_availableadhoc` (bit), `sprk_analysisid` (lookup). **No
`sprk_value` field existed**, even though `AnalysisOutputEntity.Value` → `sprk_value` is written by
BOTH existing callers of `CreateAnalysisOutputAsync` (`AnalysisResultPersistence.StoreDocumentProfileOutputsAsync`
and `AppOnlyAnalysisService.cs:659`) — confirmed by `mcp__dataverse__read_query` erroring
`'sprk_analysisoutput' entity doesn't contain attribute with Name = 'sprk_value'`. Both existing
callers wrap the create in a best-effort try/catch that **swallows** the resulting Dataverse fault as
a WARNING log — so this was a **latent, pre-existing bug**, not something this task introduced, just
surfaced because task 050 is the first caller that actually needs the write to succeed.

**Resolution (CLAUDE.md §6.5 Path C — completing an already-coded contract, not an ADR conflict)**:
added the missing `sprk_value` (Multiline Text, MaxLength 200,000) column to `sprk_analysisoutput` via
Dataverse Web API (`POST EntityDefinitions(...)/Attributes` + `PublishXml`), following the
`dataverse-create-schema` skill's documented pattern. Verified post-add via
`mcp__dataverse__describe` — `sprk_value MULTILINE TEXT` now present. This is a MINIMAL, additive
column (not a new entity — the POML explicitly says "no new entity"), and it fixes the SAME latent
bug for Document Profile / AppOnlyAnalysisService too, not scope introduced by the memo feature.
Also seeded ONE new `sprk_aioutputtype` row ("Review Summary Memo", code `REVMEMO`,
id `94f3df87-0c8d-f111-8077-7ced8ddc4a05`) as available categorization reference data — **NOT**
hardcoded into C# (its GUID is environment-specific; `AnalysisResultPersistence.PersistReviewMemoAsync`
leaves `OutputTypeId` null and categorizes by `sprk_name` instead, to stay portable across
environments). No `infra/dataverse/**` files were touched (hard boundary respected — this was a live
Dataverse Web API change, not a repo schema-manifest edit).

## 2. Placement decision (§10 / §11)

**§10 BFF Hygiene**: extends the existing "session-scoped AI endpoint" family
(`ChatEndpoints`/`AnalysisEndpoints` siblings at `/api/ai/chat/sessions/{sessionId}/*` and
`/api/ai/analysis/*`). New file `Api/Ai/ReviewMemoEndpoints.cs` (not appended to the already
1400+-line `AnalysisEndpoints.cs`) — matches the existing one-feature-per-file convention. Mapped
**inside the same `Analysis:Enabled && DocumentIntelligence:Enabled` compound gate** as
`MapAnalysisEndpoints()` (bff-extensions.md §F.1 asymmetric-registration rule): this endpoint's
`AnalysisResultPersistence` dependency is registered only when that gate is ON
(`AnalysisServicesModule.cs:705`), so mapping it unconditionally would 500 instead of 404/503 under
the compound-OFF branch. No new NuGet package. Follows ADR-001 (Minimal API), ADR-008 (endpoint
filter — `AddAiAuthorizationFilter`), ADR-019 (ProblemDetails).

**§11 reuse-first**:
1. **Existing** — `sprk_analysisoutput` (entity) + `IAnalysisDataverseService.CreateAnalysisOutputAsync`
   (KEEP-list persistence path) + `AnalysisResultPersistence` (the designated integration point per
   the task brief) + the `SaveComposeDocumentRequest.SummaryPage` client-supplies-ledgered-data
   pattern (`ComposeSummaryPageGenerator` precedent).
2. **Extension** — yes on all three: ONE new method on `AnalysisResultPersistence`
   (`PersistReviewMemoAsync`), ONE new pure assembler class (`Services/Ai/ReviewMemo/ReviewMemoAssembler.cs`
   — a NEW sibling file per the task brief's explicit suggestion, not an edit to the
   conflict-sensitive `AnalysisResultPersistence.cs` beyond the one additive method), ONE new endpoint
   file reusing `ChatSessionManager`/`AnalysisResultPersistence` verbatim.
3. **Cost of doing nothing** — without this, the agreement review has no exportable "what changed and
   why" business deliverable, and (per ADR-015/FR-13's core rationale) the review evidence is entirely
   lost the moment `DELETE /sessions/{id}` erases the Cosmos ledger.

## 3. Memo shape

Persisted verbatim as ONE JSON body in `sprk_analysisoutput.sprk_value` (self-contained per ADR-015 —
no Cosmos/ledger back-references, no timestamps):

```json
{
  "schemaVersion": "review-memo-v1",
  "overallRisk": "High",
  "sectionCount": 2,
  "sections": [
    {
      "location": "Section 4.2, para 2 (p. 3)",
      "before": "Confidential Information means information marked Confidential in writing.",
      "after": "Confidential Information means any information disclosed, whether or not marked.",
      "why": "Materially narrower than the standard.",
      "flaggedClause": "The clause defines Confidential Information only as information marked in writing.",
      "standardRef": "B5 - Use & disclosure obligations",
      "riskLevel": "High"
    }
  ]
}
```

Structured Dataverse fields alongside the JSON body: `sprk_name` = "Review Summary Memo" (the
categorization/display signal), `sprk_analysisid` (the 1:N FK — sentinel-aware resolved from
`session.HostContext.EntityId`).

## 4. Decision #4 semantics — evidence

See `tests/unit/domain/ReviewMemo/ReviewMemoAssemblerTests.cs` —
`Assemble_AcceptedRejectedAndUntouchedSections_ProducesCorrectBeforeAfterPerSection` exercises exactly
the task's named scenario (1 accepted + 1 rejected + 1 untouched) and asserts: accepted → `after` ≠
`before`; rejected → `after` == `before` (the discarded suggestion leaves the original standing);
untouched → `after` == `before` (identical observable outcome to rejected, by design — no separate
status enum exists in the FR-13 shape).

## 5. Conflict mitigation note (`multi-container-multi-index-r1`)

Per the task brief, the stale `multi-container-multi-index-r1` branch (dormant since 2026-06-10)
carries an unmerged 71-line refactor of `AnalysisResultPersistence.cs`. This task's edit to that file
is kept to the ABSOLUTE minimum: one `using` statement + one new method + one new private const,
appended at the end of the class, touching NO existing lines. Risk of a merge conflict with that
stale branch (if it's ever revived) is minimized to a simple "both add near the end of the file"
append conflict, not a semantic collision.

## 6. Publish size

`dotnet publish -c Release src/server/api/Sprk.Bff.Api/` → `deploy/api-publish/`:
- Raw (incl. PDBs): 145.11 MB / 251 files
- PDBs: 2.12 MB / 4 files
- Raw (excl. PDBs): 142.99 MB
- **Compressed (zip, incl. PDBs): 48.24 MB**

Baseline (root CLAUDE.md §10, as of 2026-07-08): ~49.63 MB incl. PDBs. **Delta: -1.39 MB** (well
under the ≥+5 MB single-task escalation threshold; well under the ≤60 MB hard ceiling). No new NuGet
package was added by this task (confirmed via `git diff --stat -- '*.csproj'` — the only `.csproj`
change is the test project's `<Compile Include>` addition for the `tests/unit/domain/**` KEEP path;
`Sprk.Bff.Api.csproj` itself is untouched).

## 7. CVE check

```
dotnet list package --vulnerable --include-transitive
```
Reports 5 HIGH-severity advisories on `System.Security.Cryptography.Xml` 8.0.3. **Pre-existing —
NOT introduced by this task**: verified via `git diff --stat -- '*.csproj'` that `Sprk.Bff.Api.csproj`
has zero changes; this task added no `<PackageReference>` anywhere. This finding pre-dates task 050
and is out of this task's scope to remediate (no server-side cryptography code was touched).

## 8. Test project change

`tests/unit/domain/**` (ADR-038 §2 path #6, pure domain logic) existed only as a README placeholder
("Bulk move pending", zero compiled files) — `Sprk.Bff.Api.Tests.csproj` had NO `<Compile Include>`
glob reaching that path (only `contract`/`regression`/`seam`/`tenant` were wired). Added ONE new
`<Compile Include="..\domain\**\*.cs" LinkBase="DomainTests" />` ItemGroup, mirroring the four
existing sibling globs exactly — this is the first backfill of that KEEP category and benefits any
future domain test placed there, not just this task's.

## 9. Deviations / escalations

None required a hard stop. Two consequential-but-resolved findings are documented above (§1.4 schema
gap; §1.1/1.2 disposition-read placement) — both resolved via CLAUDE.md §6.5 Path C (pivot to a
design/fix that complies) with full rationale, not silently swept aside.

## 10. Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | 1 accepted + 1 rejected + 1 untouched → correct {location,before,after,why,golden-ref} | ✅ PASS | `ReviewMemoAssemblerTests.Assemble_AcceptedRejectedAndUntouchedSections_ProducesCorrectBeforeAfterPerSection` |
| 2 | Memo persists to sprk_analysisoutput under right Analysis; JSON parses | ✅ PASS | `ReviewMemoEndpointContractTests.GenerateMemo_SessionBoundToAnalysisWithSections_Returns201AndPersistsUnderCorrectAnalysis` + live MCP round-trip proof (test rows created + verified + cleaned up) |
| 3 | Survives DELETE /sessions/{id} | ✅ PASS (by architecture) | `ChatSessionManager.DeleteSessionAsync` (read, not modified) only touches Redis + Cosmos + archives (not deletes) `sprk_aichatsummary` — never touches `sprk_analysis`/`sprk_analysisoutput` |
| 4 | No completed review → ProblemDetails, persists nothing | ✅ PASS | `ReviewMemoEndpointContractTests.GenerateMemo_NoSections_Returns400AndPersistsNothing` |
| 5 | BFF publish ≤60 MB; build+tests green | ✅ PASS | §6/§7 above; 9625 passed / 0 failed / 101 skipped (pre-existing skips) |
