# Task 051 — "Create Summary Memo" Toolbar: Generate-docx Download + Email Memo — Execution Notes

> FR-14, design Lens 2 #6. Rigor: FULL. Model: sonnet @ high. Step mode: directional. Depends on 050 (committed).

## 1. Seams located (Step 0)

### 1.1 Toolbar extension seam

`ComposeFormatToolbar.tsx` — the **persistent, single-row document command toolbar** (NOT the
selection-triggered `ComposeAiToolbar` BubbleMenu, which is a different surface for clause-level AI
actions). `ComposeFormatToolbar` already hosts the sibling "Review Summary" / "Review Notes" icon
toggles, both gated on `hasReview` and rendered only when their handler props are threaded — the exact
pattern this task's "Create Summary Memo" dropdown reuses. Host wiring chain (mirrors `onOpenDocument`/
`onRefreshProfile`, every existing toolbar action): `ComposeWorkspace.tsx` (owns fetch/download/dialog
logic, `bffBaseUrl` + `authenticatedFetch` + `state.sessionId`) → `ComposeEditor.tsx`'s `reviewSummary`
prop object (pure forwarder — added `onGenerateMemo`/`onEmailMemo`/`isMemoActionInFlight` fields
alongside the existing `hasFindings`/`findings`/`onToggle`) → `ComposeFormatToolbar.tsx` (renders the
dropdown). `ComposeAiToolbar.tsx` was read and explicitly NOT used — its file header confirms the
"Email" split-menu was REMOVED there per UAT round-8 #6 and that it dispatches through the AI-action
Binding/bindingId mechanism, an unrelated concern (clause-level AI, not a persistent document-level
export tool).

### 1.2 Summary-Page rendering precedent

`ComposeSummaryPageGenerator.cs` (nda-r1 task 041) + `ComposeDocumentRenderer.AppendSection` — the
precedent cited in the task brief. Read in full: it is a **pure, deterministic template/count**
transformer producing `ComposeBlock`s from a ledgered result, with **zero AI dispatch**. Its `AppendSection`
entry point appends to an EXISTING `.docx` (an appendix), which does not fit this task — the memo is its
OWN standalone downloadable file. The correct sibling entry point is
`ComposeDocumentRenderer.SynthesizeDocument(ComposeContentModel, author)` — the SAME engine's
**from-scratch** authoring path (task 026, E1 born-in-editor), which already produces well-formed,
Word-openable `.docx` bytes from a `ComposeContentModel`. `DocxAnnotationWriter` — confirmed RETIRED
(task 036) — was never referenced.

### 1.3 EmailComposer prefill contract

`SendEmailDialog` (`src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailDialog.tsx`)
— the canonical ADR-045 dialog wrapper. Props used: `open`, `onClose`, `mode="compose"`,
`initialSubject`, `initialBody`, `initialBodyFormat="HTML"`, `authenticatedFetch`, `bffBaseUrl`,
`onSent`. Confirmed exported from the package root (`@spaarke/ui-components` re-exports the
`EmailComposer/wrappers/SendEmailDialog` — the engine's canonical wrapper — over a legacy same-named
component per an explicit override in `components/index.ts`). The dialog owns its own Fluent `Dialog`
lifecycle and NEVER auto-sends — the user must click Send inside the composer.

### 1.4 050's read path — "how to read the memo back"

Read `notes/050-execution-notes.md` in full. Confirmed: 050 shipped ONLY the CREATE path
(`POST /api/ai/chat/sessions/{sessionId}/review-memo`, `ReviewMemoAssembler`,
`AnalysisResultPersistence.PersistReviewMemoAsync`, `IAnalysisDataverseService.CreateAnalysisOutputAsync`).
**No read method existed** on `IAnalysisDataverseService` for `sprk_analysisoutput` — confirmed by
reading the interface + both implementations (`DataverseServiceClientImpl` — the SDK/`ServiceClient`
class actually wired to `IAnalysisDataverseService` via `GraphModule.cs:69`; `DataverseWebApiService` —
the REST class, which implements the same composite `IDataverseService` interface for `IEventDataverseService`/
`IFieldMappingDataverseService` but is NOT the live path for analysis reads). Added the smallest read
primitive: `GetLatestAnalysisOutputByNameAsync(analysisId, name)` — one method, implemented in BOTH
concrete classes (both must satisfy the composite interface; only `DataverseServiceClientImpl` is
actually exercised at runtime for this call).

## 2. Render-from-persisted — proof

Both toolbar actions derive from the **same** server read (`AnalysisResultPersistence.GetReviewMemoWithMetadataAsync`),
which itself deserializes the **exact JSON** `PersistReviewMemoAsync` (050) wrote to
`sprk_analysisoutput.sprk_value` — no client-side re-derivation, no second LLM call, no fabricated
"before/after" text:

- **Generate memo (.docx)**: `GET .../review-memo/docx` → `AnalysisResultPersistence.GetReviewMemoWithMetadataAsync`
  → `ReviewMemoDocumentBuilder.Build(memo, documentName, analysisName)` → `ComposeDocumentRenderer.SynthesizeDocument`.
  `ReviewMemoDocumentBuilderTests.Build_MemoWithTwoSections_EmitsTitleMetadataAndTableMatchingRecordExactly`
  asserts every table cell equals the persisted `ReviewMemoSection` field **verbatim** (`Location`/`Before`/
  `After`/`Why`/`StandardRef`).
- **Email memo**: `GET .../review-memo` (JSON) → `buildReviewMemoEmailBody`/`buildReviewMemoEmailSubject`
  (client, `reviewMemoFormatting.ts`) — pure presentation formatting over the SAME response shape, no
  content invention.
- Both GETs 404 identically (`NoMemoProblem()`, shared helper) when no record is persisted yet — the ONE
  negative-path source of truth for both actions.

## 3. Server changes (§10 Placement Justification)

**Extends the EXISTING `ReviewMemoEndpoints.cs`** (050's file) with two GET siblings
(`GET .../review-memo`, `GET .../review-memo/docx`) — same feature ("the Review Summary Memo"), same
one-feature-per-file convention, same compound `Analysis:Enabled && DocumentIntelligence:Enabled` gate
(inherited automatically — no new `MapGroup`/registration). Refactored `GenerateReviewMemo`'s session/
HostContext resolution into a shared `ResolveBoundAnalysisIdAsync` helper (§11 reuse) used by all three
handlers — **behavior-preserving**: the original tenant-check → sections-check → session-lookup order for
`GenerateReviewMemo` is unchanged (verified by the pre-existing 050 tests still passing unmodified).

New surface, minimized:
- **1 new file**: `Services/Ai/ReviewMemo/ReviewMemoDocumentBuilder.cs` — pure `ComposeContentModel`
  builder (mirrors `ComposeSummaryPageGenerator`'s posture: deterministic template, zero AI).
- **1 new Dataverse interface method** (+2 implementations, both required by the composite interface):
  `GetLatestAnalysisOutputByNameAsync`.
- **1 new read method** on the existing `AnalysisResultPersistence`: `GetReviewMemoWithMetadataAsync`
  (reuses `_analysisService.GetAnalysisAsync` + `_documentService.GetDocumentAsync` — both ALREADY
  injected — for the doc/analysis display metadata; no new dependency added to the class).
- **2 new endpoint handlers** in the existing file; **0 new DI registrations** (`ComposeDocumentRenderer`
  was already an unconditional singleton, `Infrastructure/DI/ComposeModule.cs:42`).
- **No new NuGet package.**

Per CLAUDE.md §11 (component justification): **Existing** — `AnalysisResultPersistence` +
`IAnalysisDataverseService` + `ComposeDocumentRenderer` (from-scratch authoring path) all already existed
for adjacent purposes. **Extension** — yes on all three; no new service/abstraction was introduced.
**Cost of doing nothing** — without the read primitive, 050's persisted memo is write-only; nothing could
ever read it back, defeating the ADR-015 "survives DELETE /sessions" rationale 050 itself cites.

## 4. Client changes

- `ComposeFormatToolbar.tsx`: new "Create Summary Memo" `Menu` dropdown (trigger + Generate/Email
  `MenuItem`s), gated on `hasReview` (same gate as the sibling Review toggles) AND at least one handler
  wired — pure forwarder, owns no fetch logic (mirrors every other toolbar control in this file).
- `ComposeEditor.tsx`: three new optional fields on the existing `reviewSummary` prop object
  (`onGenerateMemo`/`onEmailMemo`/`isMemoActionInFlight`); threaded straight through to
  `ComposeFormatToolbar`. Zero new props at the `ComposeEditor` top level — the touch is additive and
  minimal, staying inside the seam the task brief flagged as acceptable ("if the toolbar seam genuinely
  lives there... keep the touch minimal/additive"). `ComposeEditor.tsx` is NOT touched by the concurrent
  task 033 — no contention.
- `ComposeWorkspace.tsx`: owns the actual logic — `handleGenerateMemo` (blob download via a temporary
  `<a download>` element, filename parsed from `Content-Disposition`), `handleEmailMemo` (reads the JSON,
  formats the body/subject, opens the dialog), a shared `fetchReviewMemo` helper, a `memoActionMessage`
  MessageBar banner (negative-path / failure surface, mirrors the existing `composeDraftError` banner
  pattern), and the `<SendEmailDialog>` mount (unconditional, controlled via its own `open` prop — mirrors
  `ComposeConflictDialog`'s mounting convention in the same file).
- **New file**: `reviewMemoFormatting.ts` — mirror-first types (`ReviewMemoSection`/`ReviewMemoDocument`/
  `ReviewMemoReadResponse`, field-for-field matching the server contract) + two pure formatters
  (`buildReviewMemoEmailSubject`, `buildReviewMemoEmailBody`). Sibling of the existing
  `composeResultFormat.ts` — no new "formatting library" surface.
- Inline styles inside the email HTML body (not Fluent v9 tokens) are a deliberate, documented exception:
  the HTML travels through Graph `sendMail` to arbitrary external email clients, which do not resolve CSS
  custom properties — ADR-021 governs in-app UI, not outbound email markup.

## 5. Negative path (no memo record yet)

Both server GETs return 404 ProblemDetails with the message "No Review Summary Memo has been generated
for this session's Analysis yet. Generate the review memo first." The client's `handleGenerateMemo` /
`handleEmailMemo` catch the 404 (`response.status === 404`) and set `memoActionMessage` to the same
honest copy — rendered in a dismissible-on-retry `MessageBar` — **never** a downloaded empty/corrupt file
and **never** an EmailComposer opened with blank content. Server-tested directly
(`GetReviewMemo_NoMemoPersistedYet_Returns404WithGenerateFirstMessage`,
`GetReviewMemoDocx_NoMemoPersistedYet_Returns404_NeverAnEmptyDownload` — the latter also asserts the
response Content-Type is NOT the docx MIME type).

## 6. Tests

### 6.1 New server tests

- `tests/unit/domain/ReviewMemo/ReviewMemoDocumentBuilderTests.cs` (3 tests, pure domain — ADR-038 §2
  path #6): sections render exactly, zero-section defensive path, missing-name metadata line.
- `tests/integration/contract/Api/Ai/ReviewMemoEndpointContractTests.cs` — extended the EXISTING 050
  fixture (+8 tests): `GetReviewMemo` happy path (200 + memo + analysis name), 404 no-memo, 404
  session-not-found; `GetReviewMemoDocx` happy path (200 + correct Content-Type + non-empty PK-zip-magic
  bytes), 404 no-memo never returns the docx content type. Added `ComposeDocumentRenderer` singleton to
  the fixture DI (the one new dependency the docx handler needs).

### 6.2 New client tests

- `ComposeFormatToolbar.test.tsx` (+7 tests): hidden with no review, hidden with no handlers, trigger
  visible, opening reveals both items and each fires its own handler independently, an unwired item
  renders `aria-disabled` (Fluent `MenuItem` is a `<div role="menuitem">`, not a native button — jest-dom's
  `toBeDisabled()` does not recognize `aria-disabled`; fixed to assert the attribute directly),
  in-flight spinner disables the trigger, global `disabled` prop disables the trigger.

### 6.3 Build/test results (exact)

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors**, pre-existing warnings only.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — **9638 passed / 1 failed / 101 skipped** (full suite,
  9740 total across two runs — the ONE failure,
  `NdaReviewDispatchEvalTests.NdaReviewBinding_ResolvesThroughTheRealRoutingService_ForBothClickAndTextPathCases`,
  is a pre-existing flaky live-eval test unrelated to ReviewMemo — it failed a DIFFERENT count (2, then 1)
  across two consecutive runs with zero code changes between them, the signature of flakiness, not a
  regression). The 19 ReviewMemo-specific tests (11 pre-existing 050 + 8 new) pass 19/19 in isolation.
- `npm run typecheck` (Spaarke.Compose.Components) — **clean, 0 errors**.
- `npx jest` (Spaarke.Compose.Components, full suite) — **15 failed / 843 passed / 858 total** after my
  change vs **15 failed / 836 passed / 851 total** on a `git stash`-verified baseline (identical failing
  test names in both runs — confirmed via stash/pop comparison). The 7-test delta is exactly the new
  `ComposeFormatToolbar` tests. **Zero regressions.** The 15 failures are the project's pre-documented,
  pre-existing gap (POML explicitly calls this out as "not yours").
  - **One incidental fix required and applied**: 4 pre-existing `ComposeWorkspace.*.test.tsx` files
    (`saveOpLogPreservation`, `search`, `imports`, `bornInEditorSave`) mock `@spaarke/ui-components`
    without `SendEmailDialog`. My new **unconditional** `<SendEmailDialog>` mount (controlled via its own
    `open` prop, mirroring `ComposeConflictDialog`) made those incomplete mocks throw ("Element type is
    invalid... undefined"). Added a one-line `SendEmailDialog: () => null` stub to each of the 4 mocks —
    the minimal, correct fix (extend the incomplete test double, not change well-reasoned production
    code). Verified this eliminated exactly those crashes (22 → 15 failed, matching the documented
    baseline exactly).
  - ESLint could not run (`npm run lint`) — pre-existing ESLint 9 flat-config migration gap in this
    package (`ESLint couldn't find an eslint.config.(js|mjs|cjs) file`), unrelated to and out of scope
    for this task. `tsc --noEmit` (clean) + the full Jest run are the quality signal used instead.

## 7. UI tests — deferred

`ui-tests` in the POML (live download, live EmailComposer open, dark mode) require a deployed
environment and a completed live review to seed a real persisted memo record — explicitly deferred to
tasks 060 (deploy)/061 (e2e), per the task brief's own instruction. Not attempted here.

## 8. Publish size (§10 NFR-01)

`dotnet publish -c Release src/server/api/Sprk.Bff.Api/` → `deploy/api-publish/`:
- Raw (incl. PDBs): 145.13 MB / 251 files
- PDBs: 2.12 MB / 4 files
- Raw (excl. PDBs): 143.01 MB
- **Compressed (zip, incl. PDBs): 48.25 MB**

Baseline (050, 2026-07-31): 48.24 MB. **Delta: +0.01 MB** — effectively zero, no NuGet package added
(confirmed via `git diff --stat -- '*.csproj' '*.json'` — empty). Well under the ≥+5 MB single-task
escalation threshold and the ≤60 MB hard ceiling.

## 9. CVE check

`dotnet list package --vulnerable --include-transitive` — same 5 pre-existing HIGH advisories on
`System.Security.Cryptography.Xml` 8.0.3 documented by task 050. **Not introduced by this task** — no
`.csproj` changed.

## 10. Deviations / escalations

None required a hard stop or a §6.5 ADR-conflict path. One scope decision, documented for the reviewer:

- **Read-only toolbar, not create-on-click**: the toolbar's two actions are pure READERS of the persisted
  050 record — they do NOT themselves assemble + POST a fresh memo from live disposition state. This
  reading of the POML is deliberate: the brief's own phrasing ("how to READ the memo back", "prefer the
  smallest read surface") and the negative acceptance criterion's wording ("**generate** the review/memo
  **first**" — implying a prior, separate generation step) both point at a READ-only toolbar. Building the
  client-side disposition-assembly logic (walking `ComposeCommentThread`/`AnchoredAnnotation` state to
  reconstruct the exact `{sectionRef, quotedText, afterText, ...}` list 050's POST expects) would have been
  a significant, unscoped expansion beyond "wiring shipped engines to 050's content — fully specified"
  (the task's own `model-tier-reason`), and no knowledge file/pattern for that assembly was cited in the
  POML. If a future task wants the toolbar to ALSO trigger memo (re)generation in one click, the natural
  seam is `handleGenerateMemo`/`handleEmailMemo` in `ComposeWorkspace.tsx` gaining a POST-then-GET step —
  additive, no rearchitecting.

## 11. Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Generate memo downloads a .docx whose sections match the persisted memo record exactly | PASS | `ReviewMemoDocumentBuilderTests.Build_MemoWithTwoSections_...`; `GetReviewMemoDocx_MemoPersisted_Returns200WithWordprocessingContentType`. Live browser download deferred to 060/061 (UAT). |
| 2 | Email memo opens EmailComposer with body + subject prefilled; the user must act to send | PASS (wiring) / DEFERRED (live UI) | `handleEmailMemo` → `SendEmailDialog` (ADR-045 canonical, never auto-sends); live open deferred to 060/061 (UAT). |
| 3 | Negative: no memo record yet → clear "generate first" state, never an empty export | PASS | `GetReviewMemo_NoMemoPersistedYet_Returns404WithGenerateFirstMessage`; `GetReviewMemoDocx_NoMemoPersistedYet_Returns404_NeverAnEmptyDownload`; client `memoActionMessage` banner. |
| 4 | BFF publish ≤60 MB reported; builds green | PASS | §8 above — 48.25 MB (Δ+0.01 MB); `dotnet build` 0 errors. |

## 12. Files touched

**Server**: `src/server/api/Sprk.Bff.Api/Api/Ai/ReviewMemoEndpoints.cs` (extended),
`src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisResultPersistence.cs` (extended),
`src/server/api/Sprk.Bff.Api/Services/Ai/ReviewMemo/ReviewMemoAssembler.cs` (extended, +1 record),
`src/server/api/Sprk.Bff.Api/Services/Ai/ReviewMemo/ReviewMemoDocumentBuilder.cs` (NEW),
`src/server/shared/Spaarke.Dataverse/IAnalysisDataverseService.cs` (extended),
`src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` (extended),
`src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` (extended).

**Client**: `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeFormatToolbar.tsx` (extended),
`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (extended, additive),
`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (extended),
`src/client/shared/Spaarke.Compose.Components/src/widgets/reviewMemoFormatting.ts` (NEW).

**Tests**: `tests/unit/domain/ReviewMemo/ReviewMemoDocumentBuilderTests.cs` (NEW),
`tests/integration/contract/Api/Ai/ReviewMemoEndpointContractTests.cs` (extended),
`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeFormatToolbar.test.tsx` (extended),
`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.{saveOpLogPreservation,search,imports,bornInEditorSave}.test.tsx`
(incidental mock-completion fix, 1 line each).
