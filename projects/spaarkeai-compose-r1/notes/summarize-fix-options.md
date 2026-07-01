# Compose Summarize — Fix Options

> **Author**: 2026-07-01 live-Dev triage (Compose UAT session)
> **Symptom**: `POST /api/compose/action/compose-summarize` returns 200 with
> `success=false`; playbook AI node fails with
> `"Validation failed: AI analysis node requires document context"`.
> **Error code**: `PLAYBOOK_INVOCATION_FAILED`.
> **Reproduces**: every dispatch against a promoted `sprk_document` with
> populated `sprk_graphitemid`, `sprk_graphdriveid`, and `sprk_filename`.

---

## Root cause (proven, 2026-07-01)

Two invocation paths reach the same playbook (Document Summary, GUID
`47686eb1-9916-f111-8343-7c1e520aa4df`) with different request shapes:

### Working: `summarize-file` consumer
`WorkspaceFileEndpoints.SummarizeFileAsync` (line ~333) sends:
```csharp
var request = new PlaybookRunRequest {
    PlaybookId       = playbookId,
    DocumentIds      = [],
    UserContext      = documentText,     // ← extracted DOCX plain text
    Document         = new DocumentContext { ... },
};
```
The AI node reads `UserContext` (or `Document`) → validation passes → node
runs.

### Failing: `compose-summarize` consumer
`ComposeEndpoints.DispatchAction` (line ~634) sends via the refined-ADR-013
facade:
```csharp
var result = await invokePlaybook.InvokePlaybookAsync(
    playbookId, parameters, invocationContext, ct);
```
The facade (`InvokePlaybookAi.cs:65-73`) intentionally builds:
```csharp
var request = new PlaybookRunRequest {
    PlaybookId    = playbookId,
    DocumentIds   = Array.Empty<Guid>(),   // ← always empty
    Parameters    = parameters,             // ← parameters only
    // UserContext = NOT set — no way to pass it through this facade
    // Document    = NOT set — same
};
```
Comment on line 65-67 confirms the design intent: *"the facade does NOT
accept documentIds today — invoke_playbook callers pass parameters only.
The orchestration service interprets an empty documentIds array as 'no
document context' (consistent with the existing M365 Copilot adapter
path)."*

**The facade was built for parameter-only invocations (M365 Copilot). It
was not designed for document-context flows.** Compose is trying to use
it for a use case outside its contract. This is an architectural gap
surfaced by the first document-context consumer to use the facade.

---

## Options

| # | Approach | Complexity | Time | Publish-size impact | ADR-013 posture | R1 suitability |
|---|---|---|---|---|---|---|
| A | Extend the facade + server-side DOCX text extraction | Medium | 2–3 h | +2–3 MB | Clean (path B amendment) | Good |
| B | Client sends extracted text in request body | Low | 30–60 min | 0 MB | Facade unchanged, still narrow | Fastest |
| C | Compose uses `IPlaybookOrchestrationService` directly | Low | 30 min | 0 MB | **Violates ADR-013 refined** | Poor (needs Path A exception) |
| D | Route Compose Summarize to existing `/api/workspace/files/summarize` | Low | 1–2 h | 0 MB | Facade unused | Trade-off (SSE vs aggregated) |
| E | Read pre-extracted text from Dataverse fields | Low | 30–60 min | 0 MB | Facade parameters, cheap | Fragile (depends on prior classification) |

Recommendation ordering (best → worst for R1 close-out): **B → A → D → E → C**

---

## Option A — Extend the facade + server-side extraction

### What changes
1. **Add DOCX text extractor** to BFF (server-side). Options:
   - `DocumentFormat.OpenXml` NuGet (Microsoft, MIT, ~2 MB) — canonical
   - `NPOI` (Apache 2, ~4 MB) — older but Word-format-broader
   - Roll our own zip-parse + `word/document.xml` walker (no dep, ~50 LOC,
     handles paragraphs only — no lists/tables/formatting round-trip)
2. **Extend `IInvokePlaybookAi`** with an overload:
   ```csharp
   Task<PlaybookInvocationResult> InvokePlaybookAsync(
       Guid playbookId,
       IReadOnlyDictionary<string, string>? parameters,
       string? userContext,           // ← NEW
       DocumentContext? document,     // ← NEW (optional)
       PlaybookInvocationContext context,
       CancellationToken cancellationToken = default);
   ```
   Update `InvokePlaybookAi` impl to forward `userContext` → `PlaybookRunRequest.UserContext`.
   Update `NullInvokePlaybookAi` to accept the new overload.
3. **`ComposeEndpoints.DispatchAction`**: after routing resolution, load
   DOCX from SPE via existing `IComposeDocumentService.LoadDocxAsync`,
   extract text, call new overload with the text.

### Pros
- Correct architectural direction — the facade now supports the two use
  cases the codebase actually has (parameter-only, document-context)
- No client changes
- Reusable — any future consumer needing document context inherits the
  extension
- Server-side extraction is trusted, testable, and deterministic

### Cons
- Facade interface change ripples to all `IInvokePlaybookAi` implementers
  (currently 2: real + Null-Object)
- New dep for DOCX text extraction (~2 MB publish-size), or ~50 LOC of
  DIY zip-parse we then own
- Requires an ADR-013 amendment note (Path B per CLAUDE.md §6.5) — the
  amendment is small: refined ADR-013 refers to a specific facade
  contract that we're extending

### When to pick
- If the team wants a clean answer that stays inside the CRUD-side facade
  boundary
- If we expect more document-context consumers in R2 (likely — every AI
  playbook that reads a document goes through this shape)

---

## Option B — Client sends extracted text in request body

### What changes
1. **Client**: `ComposeWorkspace.handleComposeSummarizeRequest` extracts
   plain text from the currently-loaded `docxBytes` using mammoth's
   `convertToHtml` result (strip HTML → text) OR mammoth's
   `extractRawText` API (which returns plain text directly). Store /
   compute once per doc load; re-use per summarize click.
2. **Client**: add `documentText: string` (base64? plain?) field to the
   ComposeActionRequest body.
3. **BFF**: `ComposeActionRequest` DTO gets a new optional `DocumentText`
   field. `BuildScopeParameters` forwards it as `parameters["userContext"]`
   OR `parameters["documentText"]`.
4. **BFF facade**: unchanged. Whether the playbook AI node reads
   `parameters["userContext"]` depends on how the orchestrator wires
   parameter → PlaybookRunRequest — needs verification.

### Pros
- Fastest to ship — no server-side dep, no facade change
- Client already has the DOCX bytes in memory
- No new server-side attack surface (text extraction happens client-side
  where the doc is already trusted)

### Cons
- **Depends on playbook AI node reading a `parameters` key** — verify
  first. If the AI node only reads `PlaybookRunRequest.UserContext` (a
  first-class field, not a parameter dict entry), this option collapses
  and we need to pick another
- Bandwidth: adds ~size-of-plain-text KB to every summarize request
  (typically 10–500 KB). Retry cost multiplies
- Coupling — the playbook contract now leaks to client (client "knows"
  the playbook expects `documentText`)
- Trust — text extraction happens client-side; a compromised client could
  send edited text that doesn't match what's in SPE. Might be OK because
  the AI answer isn't authoritative anyway, but consider

### When to pick
- If we can verify the AI node reads `parameters["userContext"]` (or
  similar) BEFORE committing
- If R1 close-out is time-critical and Option A takes too long
- If the doc size is bounded (< 500 KB typical) so bandwidth isn't a
  real concern

### Verification step BEFORE picking B
Read `PlaybookOrchestrationService` or equivalent — does it hoist
`parameters["userContext"]` (or similar) into `PlaybookRunRequest`
first-class fields? If yes → B works. If no → B needs the same
plumbing as A (facade extension) minus the extraction, meaning B ≈ A
minus a NuGet dep. In that case just pick A.

---

## Option C — Skip the facade; inject `IPlaybookOrchestrationService`

### What changes
1. `ComposeEndpoints.DispatchAction` injects `IPlaybookOrchestrationService`
   directly (in addition to `IInvokePlaybookAi` — or replacing it).
2. Builds `PlaybookRunRequest` with `UserContext + Document` shape (mirror
   `WorkspaceFileEndpoints`).
3. Consumes the SSE event stream, aggregates the terminal text.

### Pros
- Mirrors an existing working path — high confidence it will work
- Fast — 30 minutes of copy-paste-adapt from WorkspaceFileEndpoints

### Cons
- **Violates refined ADR-013**: CRUD code (compose endpoints) MUST NOT
  inject AI internals directly (`IPlaybookOrchestrationService` is
  explicitly on the wrong side of the facade)
- Requires either an ADR Tension declaration (Path A: project-scoped
  exception, but then compose is the second CRUD-code area doing this,
  which sets a bad precedent) OR an ADR amendment (Path B: bigger scope
  than Option A's amendment)
- Same architectural problem A solves — just less cleanly

### When to pick
- Only as a last resort if A and B both fail. Not recommended.

---

## Option D — Route Compose Summarize to existing summarize-file endpoint

### What changes
1. **Client**: When user clicks Summarize, call
   `POST /api/workspace/files/summarize` (existing, working) with the
   `sprk_documentid` instead of calling `/api/compose/action/
   compose-summarize`.
2. **Client**: handle the response — this endpoint may stream SSE (not
   aggregated), so ComposeWorkspace's Summary banner needs to consume
   SSE and append tokens progressively OR wait for terminal event and
   show all at once
3. `sprk_playbookconsumer` row for `compose-summarize` becomes unused —
   remove OR leave for R2 reuse

### Pros
- Zero new server-side work — proven working path
- Same playbook, same result
- Aligns Compose with the rest of the platform (Workspace summarize IS
  the single canonical summarize entry point)

### Cons
- Compose Summarize's response shape changes from aggregated JSON to
  SSE stream. Banner UI needs to handle both, OR we buffer the SSE
  server-side (defeats streaming). Meaningful client-side effort
- The `/api/compose/action/{consumerType}` endpoint becomes stub-only in
  R1. Unclear what future consumer types would use it
- The compose-summarize playbookconsumer row deploys but is unused —
  needs documentation

### When to pick
- If we accept the SSE-vs-aggregated response shape difference
- If we're OK letting the `/api/compose/action/*` surface be R2-only for
  its original purpose
- Good compromise if A takes too long and B's parameter-key assumption
  is wrong

---

## Option E — Read pre-extracted text from Dataverse fields

### What changes
1. `ComposeService.DispatchAction`: before invoking the facade, query
   `sprk_document` by `sprkDocumentId` for
   `sprk_filesummary`, `sprk_extractdocumenttype`, `sprk_extractreference`,
   etc. (fields populated by Phase 4 classification pipeline)
2. Concatenate available fields into a single "document context" string
3. Pass as `parameters["userContext"]` (same caveat as B — depends on
   orchestrator hoisting parameters into first-class request fields)

### Pros
- Fastest if the fields are populated
- No extraction dep
- Zero client changes

### Cons
- **Fragile** — depends on the sprk_document having been through Phase 4
  classification. Freshly-promoted Compose docs may not have these fields
  populated. First-Save from Compose (FR-06 promotion) creates a bare
  sprk_document row — classification runs asynchronously later
- Text quality — these fields are AI-summarized versions of the doc, not
  raw text. Summarizing a summary compounds error
- Same "does the orchestrator hoist parameters into first-class fields"
  dependency as B

### When to pick
- Only if the Phase 4 classification is reliable AND we accept the
  "summary of summary" quality trade-off. Not recommended for R1.

---

---

## VERIFIED 2026-07-01 — Option B collapses; Options A / D are the real choices

Grep of `src/server/api/Sprk.Bff.Api/**/*.cs` for any code path that
hoists `parameters["userContext"]` (or any parameter key) into
`PlaybookRunRequest.UserContext` returned **zero hits**. The
orchestration pipeline treats `parameters` as opaque key/value data that
flows to individual nodes as inputs; it does NOT surface any parameter
key up to first-class `UserContext` / `Document` fields.

**Consequence**: Option B as originally described (client sends
`documentText` as a parameter, no BFF change) **cannot work** — the AI
analysis node reads `PlaybookRunRequest.UserContext` (a first-class
field), not the parameters dictionary.

For Option B to work we'd have to modify the facade to hoist a specific
parameter key into `UserContext` — which is the same code change as
Option A's facade extension. That collapses B → A minus the extraction
step.

**Revised recommendation ordering (best → worst for R1 close-out)**:

1. **Option A** — Extend the facade + server-side DOCX text extraction.
   This is the correct architectural answer AND is now not meaningfully
   more work than any workable alternative. Recommend a DIY zip-parse
   text extractor (~50 LOC) to avoid the +2 MB NuGet cost — DOCX is
   just a zip with `word/document.xml` inside, and the AI node only
   needs prose (walk `<w:t>` elements).

2. **Option D** — Route Compose Summarize through the existing
   `/api/workspace/files/summarize` endpoint. Zero server-side work,
   but the client needs to handle the SSE response shape difference,
   and the /api/compose/action/ surface becomes R2-reserved.

3. **Option C** — ADR violation without amendment. Only pick if A and
   D both fail. Not recommended.

4. **Options B, E** — non-viable in current form.

---

## Timing estimates for the two viable paths

| Task | Option A | Option D |
|---|---|---|
| Add DOCX text extractor (~50 LOC DIY) | 30–60 min | — |
| Facade interface extension (`UserContext`, `Document` optional params) | 30–45 min | — |
| Update `InvokePlaybookAi` + `NullInvokePlaybookAi` implementations | 20 min | — |
| Update `ComposeEndpoints.DispatchAction` to call new overload | 15 min | — |
| Unit tests (new overload + extractor) | 30–60 min | — |
| ADR-013 amendment note (project-close) | 20 min | — |
| Client SSE consumption + progressive rendering | — | 60–90 min |
| Client URL swap + response-shape reshape | — | 30–45 min |
| Compose consumer row status document | — | 15 min |
| **Total** | **~3 hours** | **~2 hours** |

Both are within R1 wrap-up scope. Option A is ~1 hour longer but leaves
the platform in a strictly better place for R2.

**File this option analysis as an addendum to `notes/defer-issues.md`
under a new SC9-live section** once we commit to a path — even if we
pick Option A now, the ADR-013 amendment gets tracked as project-close
work.
