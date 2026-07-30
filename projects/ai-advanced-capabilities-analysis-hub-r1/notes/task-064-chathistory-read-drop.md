# Task 064 — sprk_chathistory Read Drop + Write Cleanup + NDA Hook Generalization

> **Task**: 064 (W6 final retirement task) · **Date**: 2026-07-29
> **Spec**: §13.5 / FR-22 · **ADR**: ADR-040 Path A (Cosmos = transcript store-of-record, Dataverse = anchor + outputs)

## A. Read drop (GET/save, and export incidentally)

`AnalysisDocumentLoader.GetOrReloadFromDataverseAsync` is the SHARED "lite" loader behind all
three surviving readers: `POST /{id}/save` (`SaveWorkingDocumentAsync`), `GET /{id}`
(`GetAnalysisAsync`), and `POST /{id}/export` (`ExportAnalysisAsync`). It no longer calls
`DeserializeChatHistory` or reads `record.ChatHistory` — the `AnalysisInternalModel.ChatHistory`
field simply stays at its default `[]` for all three.

**Per-consumer assessment** (confirms none needed the read):

| Consumer | Uses `analysis.ChatHistory`? | Disposition |
|---|---|---|
| `SaveWorkingDocumentAsync` (`/save`) | No — only `analysis.WorkingDocument` | Drop cleanly, no behavior change |
| `ExportAnalysisAsync` (`/export`) | No — `ExportContext` never referenced `ChatHistory`, even before this change (uses `WorkingDocument`/`FinalOutput`/`DocumentName`/`DocumentId`/`StartedOn`) | Drop cleanly — the read was already dead weight for this consumer |
| `GetAnalysisAsync` (`GET /{id}`) | Yes — mapped into `AnalysisDetailResult.ChatHistory` in the response | **Decision below** |

### Decision: GET /{id} does NOT repoint the transcript to Cosmos — it drops it

The task's acceptance criteria say GET "returns the anchor + outputs (transcript from Cosmos)".
Read literally, this describes the ARCHITECTURE split (ADR-040 Path A: transcript lives in Cosmos,
Dataverse is anchor + outputs) — not a mandate that this specific endpoint re-fetch a Cosmos
transcript inline. Verification before deciding:

- Grepped every client call site for `GET /api/ai/analysis/{analysisId}` (the wire route this
  endpoint serves) across `src/solutions/**` and `src/client/**` — **no live client calls it**.
  `AnalysisDetailResult.ChatHistory` has zero consumers today.
- The actual live transcript-access path clients use is
  `GET /api/ai/chat/sessions/by-analysis/{analysisId}` (task 031,
  `IChatDataverseRepository.GetSessionsByAnalysisAsync`), consumed by `WorkspacePane.tsx` and
  `AnalysisHubWidget.tsx`. This is the Cosmos-backed session-discovery path ADR-040 intends.

Given no consumer reads it and a dedicated Cosmos-backed replacement already exists and is live,
repointing `GetAnalysisAsync` to ALSO fetch a Cosmos transcript would be unused complexity — a
§11 Component Justification failure (cost-of-doing-nothing is not concrete: nothing breaks by
leaving it empty). `AnalysisDetailResult.ChatHistory` stays on the wire contract (empty array) for
back-compat rather than being removed, since removing a public response field is a larger contract
change than this task's scope ("keep-with-changes", not a contract migration).

**Escalation guard did not fire**: no consumer was left without transcript access — the escalation
trigger ("if dropping the read leaves a consumer without transcript access that Cosmos does not
yet serve") did not apply because (a) no consumer reads via this path, and (b) Cosmos-backed
access already exists via a different, already-shipped endpoint.

## B. Write cleanup (062 hand-off, completed)

Per `notes/task-062-handoff-to-064.md`, once every `sprk_chathistory` READER was confirmed dropped
(step A above), the per-turn WRITE became provably dead:

- Removed `ChatEndpoints.cs` ~972-982: the `if (session.HostContext?.EntityType ==
  "sprk_analysisoutput" ...) { ... workingDocumentService.UpdateChatHistoryAsync(...) }` block, and
  the now-unused `IWorkingDocumentService workingDocumentService` parameter from
  `SendMessageAsync`.
- Removed `IWorkingDocumentService.UpdateChatHistoryAsync` (interface) and
  `WorkingDocumentService.UpdateChatHistoryAsync` (impl).
- Removed the 3-file `sprk_chathistory` column plumbing:
  - `DataverseServiceClientImpl.cs` — dropped `"sprk_chathistory"` from the `ColumnSet` + the
    `ChatHistory = entity.GetAttributeValue<string>(...)` line.
  - `DataverseWebApiService.cs` — dropped `sprk_chathistory` from the `$select` + its JSON parse.
  - `Models.cs` — removed `AnalysisEntity.ChatHistory` property entirely (repo-wide grep confirmed
    zero remaining readers/writers of this property after the above).
- `AnalysisResultPersistence` class: kept (per hand-off note — still used by
  `UpdateWorkingDocumentAsync`/`FinalizeAnalysisAsync`/`SaveToSpeAsync`/export telemetry). Its
  chat-history wrapper was already removed by task 062; nothing further to do here.
- **Negative check honored**: `Services/Insights/Observations/ObservationMirrorMapper.cs` (writes
  `sprk_chathistory` on a DIFFERENT entity — the Insights observation mirror) was left completely
  untouched. Confirmed via repo-wide grep before and after the edits.

No escalation fired for Part B — the write's sole reader (step A) was dropped in the SAME task, so
the write was provably dead with no ambiguity.

## C. NDA-hook generalization → work-type

Investigated two candidate locations for "NDA-hardcoded conversation hooks":

1. **`CreateAnalysisWizardWidget.tsx`** (the hub/wizard task 040/041 surface) — already fully
   work-type-parameterized (`sprk_worktype`/`workTypeValue`/`workTypeLabel`, defaults to Agreement
   Review). No NDA hardcoding found here — nothing to generalize.
2. **`ConversationPane.tsx`'s `handleNdaClassified`** — found the actual hardcoded gate:
   `if (docType !== "nda" || !data.fileId) return;` — this decided whether the "Review an NDA"
   Suggested-Next-Steps card renders, using a hardcoded `"nda-review"` consumerType and a
   hardcoded `"Review an NDA"` card label.

**Generalization applied** (mechanical, low-risk — see `localActionChips.ts`):

- Added `DOCUMENT_REVIEW_CAPABILITIES: DocumentReviewCapability[]` — a small table
  `{ docType, consumerType, cardLabel }`, today containing ONE entry (`nda` →
  `nda-review` → `"Review an NDA"`, i.e., byte-identical to the prior hardcoded behavior).
- Added `getDocumentReviewCapability(docType)` — table-driven lookup, case-insensitive, returns
  `null` for unregistered/absent docTypes (preserves the negative case from task 022 acceptance
  criterion 3).
- `ConversationPane.tsx`: `handleNdaClassified` now calls `getDocumentReviewCapability(data.docType)`
  instead of the string comparison; `ndaReviewFile` state widened with `cardLabel`; a new
  `ndaReviewConsumerType` state (declared before the `ndaReviewBindingId` memo that consumes it)
  replaces the hardcoded `"nda-review"` consumerType in the capability-discovery lookup; the
  Suggested-Next-Steps chip and the no-bindingId fallback message both read the matched
  capability's label instead of a hardcoded string.
- **A second work-type is a ONE-LINE registry addition** (e.g., `{ docType: "agreement",
  consumerType: "agreement-review", cardLabel: "Review this Agreement" }`) — no other code
  change required. It is currently NOT added because no such Action/Binding exists in the
  Dataverse catalog yet; capability discovery safely resolves an unregistered consumerType to a
  `null` bindingId (existing fallback path, unchanged), so adding the registry entry ahead of the
  Action would be inert, not broken — left out here to avoid implying a capability that doesn't
  exist yet.

**What was deliberately NOT touched** (deep NDA coupling, noted per "keep scope reasonable"):

- `isNdaReviewResult` (`useNdaReviewAdvisoryCommentsBridge.ts`) — already work-type-agnostic: it
  matches on OUTPUT SHAPE (`{overallRisk, flaggedSections[]}`), not on which docType triggered the
  dispatch. No change needed — a future "agreement-review" Action producing the same output shape
  would already flow through this bridge correctly.
- Hook/file/variable NAMES (`useNdaReviewRunProgress`, `NdaReviewProgressModal`,
  `useNdaReviewAdvisoryCommentsBridge`, `ndaReviewFile`, `LOCAL_CHIP.ndaReview`, etc.) — these are
  cosmetic NDA-specific naming, not behavioral hardcoding. Renaming them would cascade into ~15
  existing test files (`NdaReviewProgressModal.test.tsx`, `useNdaReviewRunProgress.test.ts`,
  `useNdaReviewAdvisoryCommentsBridge.test.ts`, several `ConversationPane.*.test.tsx`) for a
  purely cosmetic gain. Deferred — a future task can rename these IF/when a second work-type's
  review capability actually ships and the "Nda"-prefixed names become confusing.

## D. 3-pane fold + shared widgets — verified, not rebuilt

Confirmed (no changes made — verification only, per task's own instruction):

- `ThreePaneShell.tsx` imports and renders the shared `ThreePaneLayout` from `@spaarke/ui-components`
  (line ~61 import, ~819 render) — the 3-pane behavior is already folded into SpaarkeAi.
- `FindingsWidget` is registered as a context-pane widget via
  `Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` (dynamic import), consumed by
  SpaarkeAi's `ContextPaneController.tsx` (`"findings" → "sources-citations"` stage mapping).
- `AnalysisEditorWidget` is registered as a workspace widget via
  `Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`
  (`WIDGET_TYPE.AnalysisEditor`, dynamic import from `@spaarke/ai-outputs`).
- `NdaReviewSummaryPanel` is wired into the Compose editor via `ComposeWorkspace.tsx` /
  `ComposeCommentGutter.tsx` / `ndaClauseLocation.ts` (Compose is one of SpaarkeAi's three panes).
- None of these three widgets' contracts/props were touched by this task.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — 0 errors (23 pre-existing warnings, none new).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — 9036 passed / 8 failed / 101 skipped. All 8
  failures confirmed PRE-EXISTING via `git stash` baseline re-run (3 in
  `AnalysisEndpointsExecuteDispatchContractTests` — a route-metadata DI-inference error unrelated
  to any file this task touched; 5 in `Services/Communication/*` — a different module entirely).
  Zero new failures introduced.
- `npx jest src/components/conversation` (SpaarkeAi) — 48 suites / 433 tests passed (up from
  47/426 — the 7 new tests in `localActionChips.test.ts`).
- Publish size: **47.51 MB compressed** — matches the project's recorded rolling baseline exactly;
  no measurable delta (this task only removed code/columns). Well under the 55 MB
  architecture-review threshold and the 60 MB hard ceiling.
- CVE check: `dotnet list package --vulnerable --include-transitive` shows one pre-existing HIGH
  advisory group on `System.Security.Cryptography.Xml` (transitive) — NOT introduced by this task
  (no packages were added or changed).
- `/conflict-check`: no active worktree has uncommitted/unmerged changes overlapping the touched
  BFF files or `ConversationPane.tsx`/`localActionChips.ts` (checked `ai-advanced-capabilities-nda-r1`,
  `ai-advanced-capabilities-agreements-r1`, `ai-spaarke-insights-engine-widgets-r1`,
  `spaarkeai-assistant-enhancements-r1`, `spaarkeai-compose-r2..r5`,
  `spaarke-notification-spine-r1`). `spaarke-ai-architecture-redesign-r2` (the `Services/Ai/`
  sole-owner) has no active worktree — already merged.

## Deferred finding (out of task 064's scope, noted for a future cleanup task)

`AnalysisContextBuilder.BuildContinuationPrompt` / `BuildContinuationPromptWithContext`
(`Services/Ai/AnalysisContextBuilder.cs`) appear to have **no remaining production caller** — their
only caller was the legacy `/continue` endpoint's `ContinueAnalysisAsync`, which task 062 already
deleted. This wasn't named in the 062 hand-off, so it was left untouched here rather than folded
into this task's scope (avoids uncontrolled expansion beyond the hand-off's explicit removal list).
Flagging for a follow-up dead-code sweep.
