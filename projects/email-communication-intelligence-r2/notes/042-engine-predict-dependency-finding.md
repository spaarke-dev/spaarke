# Task 042 — dependency/scope finding (STOP before implementing the engine-predict criteria)

**Date**: 2026-08-06 · **Rigor**: FULL · **Model**: opus (session) / task-tier sonnet·high · **Outcome**: escalation (POML `<escalation><trigger>` fired + CLAUDE.md §6/§6.5). **No code written.**

## TL;DR

Task 042's three core acceptance criteria (#1 engine-predicted pre-selection, #2 ribbon quick-save to the
**predicted** record, #4 no-prediction fallback) all require the add-in to obtain the **Association Engine's
prediction for the currently-open email**. That capability **does not exist in the add-in path and cannot be
built within task 042's declared client-only scope** — it is exactly what task **043** (unify user-upload with
capture — engine + dedup; deps 021, 024) establishes. The candidate model also cannot be reproduced identically
without adding a cross-package dependency + two BFF endpoints. Both conditions in the POML's own
`<escalation><trigger>` are met. Surfacing for an operator sequencing/scope decision rather than silently forking
the candidate model or silently building BFF endpoints outside this task's scope.

## What the code actually shows (verified this session)

### 1. The add-in save path never runs the Association Engine
- `useSaveFlow.ts` → `POST /api/office/save` → `OfficeService.SaveAsync` → creates a **`sprk_document`**
  (document-centric SPE-pointer), **not** a `sprk_communication` with association/triage/provenance. (Confirmed
  independently in the **task 041 finding**, `notes/041-intake-folder-scope-finding.md`.) There is **no engine
  prediction anywhere in the add-in** today.

### 2. `derivePrimaryReview` consumes persisted provenance that only exists AFTER capture
- The code-page candidate model = `derivePrimaryReview(associationProvenanceJson, associationStatus, filed, denorm)`
  ([`provenance.ts:775`](../../../src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts)),
  fed from the **stored `sprk_communication` row** (`sprk_associationprovenance` JSON etc.). For an email not yet
  captured (the normal add-in case), there is no provenance to feed it.
- `CandidateTrace` (the persisted provenance schema, `AssociationProvenance.cs:57`) has **no `targetName`** — so the
  code page and a hypothetical add-in reuse are in the identical position on names (both fall back to `targetId`).
  → The candidate model *would* be reproducible identically **iff** the add-in could feed `derivePrimaryReview`.

### 3. The only engine-preview endpoint needs a stored communication GUID we can't obtain
- `POST /api/communications/{id:guid}/suggest-associations` (`CommunicationEndpoints.cs:235`, `SuggestAssociationsAsync`)
  is READ-ONLY and returns `SuggestAssociationsResponse` (1:1 with `ProvenanceDoc` candidates). But it requires a
  **stored communication `{id:guid}`**.
- To get that GUID from the open email the add-in needs `internetMessageId → communicationId`. The client service
  `communicationLookupService.ts` already consumes `GET /api/office/communications/by-message-id/{id}` — but that
  endpoint **does not exist server-side** (grep of `Api/*.cs`: only `suggest-associations` matches; the service's
  own header comment says "not yet implemented server-side as of smart-todo-decoupling-r3 task 070").

### 4. The add-in doesn't import the shared candidate model
- `office-addins/package.json` depends only on `@spaarke/auth` — **not** `Spaarke.Communication.Components`. So
  `derivePrimaryReview` is not importable without adding a cross-package dependency (which pulls the components lib
  into the add-in's separate webpack build to reuse one pure function).

### 5. The picker isn't even rendered today
- `SaveFlow.tsx` **imports `EntityPicker` but never renders it**; association is fully optional (`isValid = true`),
  pre-filled only from `getLastAssociation()` (sessionStorage last-used). So "pre-select the picker" first requires
  *rendering* a picker into the shipped save flow — a real gap, but orthogonal to the engine-predict blocker.

## Why the POML `<escalation><trigger>` fires (both clauses)

> "If the engine's candidate endpoint returns no prediction … fall back to the picker with no pre-selection …
>  If `derivePrimaryReview` output cannot be reproduced identically in the add-in (model divergence from the code
>  page), STOP and escalate per CLAUDE.md §6 rather than forking a second candidate model."

- There is **no candidate endpoint** the add-in can call for the open email (§3 above: the lookup endpoint is
  missing; the save path isn't the engine). Not "returns no prediction" — the path doesn't exist.
- `derivePrimaryReview` **cannot be reproduced identically** without (a) a new `by-message-id` BFF endpoint, (b)
  reachable engine suggestions for that communication, and (c) importing the shared module. Doing the candidate
  logic any other way = forking the model, which the trigger explicitly forbids.

## Why this is out of task 042's scope (governance)

- **Declared scope**: 042 `<parallel-reason>` = "Client add-in + ribbon (Dataverse solution) surface — **isolated
  from the BFF work**"; `<deps>041</deps>` only. 041 delivered **no** by-message-id/suggest endpoint (it was
  re-scoped to config + folded mechanism-2 into 043).
- **§10 BFF Hygiene**: `by-message-id` (and any office-scoped suggest-for-email) are new BFF endpoints on a
  hot-path surface — require Placement Justification + a BFF-touching task. Building them silently here violates the
  task's client-only contract and §10.
- **Task ordering defect**: 042's engine-predict criteria depend on the capability task **043** establishes
  ("unify user-upload with capture — engine + dedup", deps 021✅/024✅). 042 is sequenced *before* 043 in the plan
  (W4: 041→042 · 043), so as authored it cannot satisfy #1/#2/#4 until 043 (or a dedicated suggest-for-email
  endpoint) lands. Criterion #3 (dedup of a re-saved email) likewise inherits FR-C1 from 043/021.

## Recommended operator options

- **(A) Re-sequence: do 043 first, then 042.** 043 makes the add-in path run the engine (and gives a
  communication + provenance to predict from); 042's pre-select + quick-save-to-predicted then become a thin,
  in-scope client layer over it. **Recommended.** Requires 043's deps (021✅, 024✅ — both landed) so 043 is
  runnable now.
- **(B) Expand 042 to include the BFF suggest-for-email path.** Add `GET /api/office/communications/by-message-id/{id}`
  + reach `suggest-associations` (or a new office-scoped preview), add `Spaarke.Communication.Components` (or its
  pure `provenance.ts`) as an add-in dep, then wire `derivePrimaryReview`. This turns 042 into a BFF+client task
  (Placement Justification, publish-size/CVE gate, its own hot-path decl) — larger than the authored client-only
  scope.
- **(C) Split 042: ship the non-engine slice now; defer engine-predict to post-043.** Deliverable now with no
  engine: (i) render the existing `EntityPicker` into `SaveFlow` with the existing **last-used** pre-fill; (ii)
  implement `quickSave` (#234) to file via `/api/office/save` using the last-used/selected association +
  authenticatedFetch; (iii) ribbon button (gated). Then a follow-up task adds engine-predicted pre-selection after
  043. **Note**: this does NOT satisfy criteria #1/#2 as written ("engine-predicted" ≠ "last-used") — it would ship
  the *mechanical* quick-save + picker and explicitly re-scope the engine-predict criteria to the follow-up. Only
  valid with operator sign-off on the re-scope.

## What was NOT done (and why)

- No code written. Forking the candidate model (trigger-forbidden), silently adding BFF endpoints (out of scope +
  §10), and silently downgrading "engine-predicted" → "last-used" while marking 042 ✅ (redefining acceptance
  criteria) were all considered and rejected.

## OPERATOR DECISION (2026-08-06): Option B — expand 042 to include the BFF path

The operator selected **Option B**: 042 becomes a **BFF + client + ribbon** task (no longer client-only). Scope now
includes a new office-scoped BFF endpoint that resolves `internetMessageId → communication → engine suggestions`,
so the add-in can pre-select via the shared `derivePrimaryReview` and quick-save to the predicted record.

**Governance consequences (now binding for this task):**
- **§10 BFF Hygiene / Placement Justification** — the new endpoint lives on the existing **Office** endpoint group
  (`Api/Office/…`), reusing the existing evaluate-only engine path (`CommunicationService.ReconstructEnvelopeAsync`
  + `IncomingAssociationResolver.EvaluateAsync`) — **NOT** a new engine or a forked candidate model (§11 reuse).
  It reaches the engine via existing services already registered for the Communication module (no new AI-facade
  dependency; ADR-013 untouched). Placement = Office group because the caller is the Outlook add-in on the office
  auth path; it does message-id resolution the Communication group has no need for.
- **/conflict-check** run before the BFF PR (BFF hot-path); project INDEX.md already declares BFF=Y.
- **Publish-size ≤60 MB + no new HIGH CVE** verified on the BFF touch; no new NuGet expected.
- **Candidate-model reuse (escalation trigger honored)** — the add-in imports the REAL shared `derivePrimaryReview`
  (adds the shared candidate-model dependency); it reconstructs a `ProvenanceDoc` from the endpoint's
  `SuggestAssociationsResponse` and calls the shared function. No fork.
- **No-prediction fallback (criterion #4)** preserved: endpoint 404 / empty candidates → picker with no
  pre-selection; quick-save with no prediction opens the taskpane instead of auto-filing a guess.

## COMPLETION (Option B shipped, 2026-08-06)

**What shipped** (BFF + client + tests; ribbon = gated tail):

1. **BFF** — `GET /api/office/communications/by-message-id/{internetMessageId}/suggestions`
   (`Api/Office/CommunicationsEndpoints.cs`, `GetSuggestionsByMessageIdAsync` + `CommunicationSuggestionsResponse`
   DTO + shared `QueryCommunicationByMessageIdAsync` helper). Resolves the email → captured `sprk_communication`
   → runs the **same read-only evaluate path** as the Communication-group `suggest-associations`
   (`CommunicationService.ReconstructEnvelopeAsync` + `IncomingAssociationResolver.EvaluateAsync` +
   `SuggestAssociationsResponse.FromDecision`). **No fork, no new engine, no AI-facade dep.** 404 when not captured
   (FR-B2 fallback). NOTE (corrects the finding above): `by-message-id` **already existed** in `Api/Office/` — only
   the suggestions step was net-new.
2. **Client pre-select** — `communicationSuggestionsService.fetchEnginePreSelection` reconstructs a `ProvenanceDoc`
   from the endpoint and calls the **shared `derivePrimaryReview`** (imported via a webpack-alias / tsconfig-paths /
   jest-mapper to the pure `provenance.ts` source — the `@shared` pattern; ADR-045 no-fork honored). `SaveFlow` now
   **renders the `EntityPicker`** (previously imported-but-unrendered) and pre-selects the predicted record, with a
   "Suggested by Spaarke from this email" hint; a user selection wins over a late prediction.
3. **Client quick-save (#234)** — `outlook/commands/index.ts` `quickSave` bootstraps auth+apiClient in the commands
   context, reads the email, fetches the prediction, and files to the predicted record via `POST /api/office/save`
   (`quickSaveHelpers.buildEmailSaveRequest`, idempotency key in body). **No prediction → opens the taskpane, never
   auto-files a guess** (criterion #4 / ADR-015 spirit).

**Verification**: BFF build 0-err; **8/8 office contract tests** (added 401 + 404-fallback); add-in production build
exit 0 (taskpane + commands bundles compile, shared alias resolves); **10/10 client tests** (6 service incl. real
`derivePrimaryReview`, 4 quick-save helper); publish **48.32 MB** compressed incl PDBs (baseline 48.30 → Δ≈0, ≤60 MB);
**no new HIGH CVE**; no new NuGet. Step 9.5: ADR-028/021/045/013/010/§10 checks clean.

**Acceptance criteria**: #1 (engine-predicted pre-selection via shared model) ✅ · #2 (one-click quick-save to
predicted) ✅ · #3 (re-save dedup — inherits FR-C1 server-side; unchanged) ✅ · #4 (no-prediction → no pre-selection /
no auto-file) ✅ · #5 (Fluent v9 dark tokens — EntityPicker + hint use semantic tokens) ✅ · #6 (add-in builds; ribbon
imports cleanly — **ribbon is the gated tail, see below**).

**Known limitation (surfaced for reviewer)**: the persisted provenance schema (`CandidateTrace`) carries **no record
name**, so the pre-selected chip shows the record's GUID as its name with the record number as secondary info — this
is **identical to the code-page candidate model** (the escalation trigger required reproducing `derivePrimaryReview`
exactly). A follow-up could resolve display names via entity search; out of scope here (would diverge from the code
page / add a lookup).

**GATED TAIL — ribbon quick-save button (criterion #6, live env)**: adding the ribbon button points at the finished
`quickSave` command via the `ribbon-edit` skill (export the Communication ribbon solution → add the button → import
to `spaarkedev1`). The **import touches a live Dataverse environment** — held for operator go-ahead per the
"no silent live-env mutation" rule. The command code is complete and built; only the solution import remains.

**Coverage note**: the Office.js command *glue* (`readEmailContext`/`ensureBootstrapped`/notifications/
`showAsTaskpane`) has no unit test (the add-in has no Office-command test harness); the *testable* logic (request
build, idempotency, prediction→file decision, no-prediction fallback) is covered by `quickSaveHelpers.test.ts` +
`communicationSuggestionsService.test.ts`.

## Evidence pointers

- `src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts` (save path; last-used pre-fill)
- `src/client/office-addins/shared/taskpane/components/SaveFlow.tsx` (EntityPicker imported, not rendered)
- `src/client/office-addins/shared/taskpane/services/communicationLookupService.ts` (consumes unimplemented by-message-id)
- `src/client/office-addins/outlook/commands/index.ts:31` (quickSave #234 stub)
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs:235` (`suggest-associations`, needs stored GUID)
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AssociationProvenance.cs:57` (CandidateTrace — no targetName)
- `src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts:775` (`derivePrimaryReview`)
- `projects/email-communication-intelligence-r2/notes/041-intake-folder-scope-finding.md` (add-in path ≠ engine; 043 owns unification)
