# Task 034 — F-c records bridge decision

> **Rigor**: FULL · **Model tier**: sonnet @ high · **Step mode**: DIRECTIONAL
> **Gate**: 033 complete
> **Written**: before any results-list implementation code, per the POML's step-1 ordering and the
> orchestrator's binding correction #3.

---

## Decision: **(b) documents-only for r1.** The text-seeded second call to `POST
/api/ai/search/records` is deferred, not shipped, in this task.

---

## 1. The two options, restated (finding F-c)

- **(a)** Derive a text seed from the open document and issue a second call to
  `POST /api/ai/search/records`, accepting that it is a *text* query, not true document-content
  vector similarity.
- **(b)** Ship documents-only in r1 and defer records.

Both are pre-authorized by the POML. This note records why **(b)** was chosen.

## 2. Evidence gathered before deciding

- **`GET /api/ai/visualization/related/{documentId}`** (`src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs:48`)
  returns **documents only**. Matter/Project/Invoice appear as parent **hub nodes**
  (`CreateParentHubNode`, built from the source document's own Dataverse lookups —
  `GetHardcodedRelationshipsAsync`, re-located by symbol in
  `src/server/api/Sprk.Bff.Api/Services/Ai/Visualization/VisualizationService.cs`), not as vector
  matches on document content.
- **`POST /api/ai/search/records`** (`src/server/api/Sprk.Bff.Api/Api/Ai/RecordSearchEndpoints.cs:40`)
  is query-**text** driven and accepts no source document —
  `RecordSearchRequest.Query` (`src/server/api/Sprk.Bff.Api/Models/Ai/RecordSearch/RecordSearchRequest.cs:21`)
  is a required, `[StringLength(1000)]`-capped string; `RecordTypes` is restricted to
  `sprk_matter | sprk_project | sprk_invoice`
  (`RecordEntityType.ValidTypes`). There is no "seed by document id" or "seed by embedding" input on
  this route — a caller must supply text.
- **Getting a text seed from the open Word document is not a solved problem in this codebase
  today.** `WordAdapter.ts` (`src/client/office-addins/shared/adapters/WordAdapter.ts:313`) reads the
  document as a **compressed `.docx` binary** via `Office.context.document.getFileAsync(Compressed)`
  for the save flow — there is no existing seam that extracts plain body text for search-query
  purposes (`grep -n "body.getText\|Word.run" shared/adapters/WordAdapter.ts` returns nothing). Every
  candidate seed source has a real problem:
  - The document **title/filename** is the only text already available cheaply, but it is frequently
    generic (`Document1.docx`, a dated filename) and would produce a low-signal or misleading query
    far more often than a genuinely related record name.
  - The **full document body** would need a NEW `Word.run(context => context.document.body.text)`
    call (not present anywhere in this add-in yet), plus a truncation strategy to fit the 1000-char
    cap — and truncating raw prose to 1000 chars is not the same operation as the embedding-based
    similarity task 032/033 already hardened for the documents route; it is closer to "search on
    whatever fit," which is exactly the kind of result the POML's second escalation trigger names
    ("the text seed produces results so unrelated that showing them would mislead").
  - Neither candidate seed has been validated against real Matter/Project/Invoice names in this
    worktree (no live Office host, no live AI Search index reachable here — see §4 of the main task
    report). Shipping (a) now means shipping an **unvalidated** ranking signal under a UI label that
    tells the user "these are similar."

## 3. Why (b), not (a)

Applying CLAUDE.md §11's three-question template to the second BFF call this task would otherwise add:

1. **Existing** — `POST /api/ai/search/records` already exists and already does text-driven hybrid
   search over Matter/Project/Invoice. Nothing new needs to be built on the server side to call it.
2. **Extension** — Calling it is definitionally possible (it is a public route), but *making it mean
   something* requires a text-seed extraction step this codebase does not have, and that step's
   quality cannot be verified in this environment (no live host, no live index). "Can I extend the
   existing" is technically yes for the network call and **no** for the missing client-side text
   extraction and its unverified relevance.
3. **Cost of doing nothing (i.e., of choosing (b))** — Users see documents-only results in r1; a
   genuinely related Matter/Project/Invoice is not surfaced in Find until a later task ships a
   validated seed strategy. That is a real but bounded gap: the parent hub node (already present on
   the documents response) still shows the document's OWN matter/project/invoice, just not
   surfaced as a "similar record." Nothing breaks; nothing is silently wrong.

Set against that, the cost of shipping (a) now is asymmetric: a records grouping seeded by an
unvalidated derived-text query, sitting under a UI section a user will read as "related records,"
next to a documents list that is *itself* currently blocked on a separate, more serious finding (see
the main task escalation — the documents route has no paging mechanism at all, which blocks the
lazy-scroll implementation ADR-051 requires). Compounding an already-escalated primary list with a
second, differently-paginated, unvalidated-relevance section is exactly the kind of scope-under-risk
CLAUDE.md §11 and the POML's second escalation trigger warn against ("STOP and escalate rather than
shipping a records section the team cannot explain").

**This is not "neither option is defensible"** — (b) is defensible on its own; it is a real, if
narrower, feature. The POML's second escalation trigger (fires only when *neither* option is
defensible) does not fire here.

## 4. What this means for implementation (once unblocked)

- `FindResultsList` renders **documents only** from the per-row-authorized
  `GET /api/ai/visualization/related/{documentId}` surface (task 032's hardening).
- Parent hub nodes, when present in that response, are **labeled distinctly** as "this document's
  [Matter/Project/Invoice]" — never merged into or presented as a similarity match (constraint
  already required for either F-c option).
- No call to `POST /api/ai/search/records` is added by this task.
- If the operator later authorizes a follow-on task to add (a), it MUST (i) define exactly what text
  is used as the seed and where it comes from (title vs. body vs. a purpose-built extraction), (ii)
  state plainly in the UI/notes that those results are a text-query result, not document-content
  vector matches, per this task's own constraint, and (iii) reconcile that route's *real* offset
  paging (`RecordSearchOptions.Offset`, 0–1000, confirmed by reading
  `RecordSearchService.cs:174-176` and `:421`) against the documents route's *complete absence* of
  paging — the two lists cannot share one `useLazyResults` page-advance model as written today.
