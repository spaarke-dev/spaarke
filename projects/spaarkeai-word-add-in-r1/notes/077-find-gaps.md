# Task 077 — FR-16's three gaps: reproduce-first, and the design that avoids both escalations

> **Date**: 2026-09-28
> **Task**: `tasks/077-find-view-three-gaps.poml` · FULL · opus @ xhigh · directional
> **Status of this note**: reproduce-first evidence + design decisions recorded BEFORE implementation, so the
> reasoning survives independently of the code.

---

## Criterion 1 — reproduce-first, recorded verbatim from the source

### (a) Documents only

`FindResultsList.tsx` renders documents; parent hubs appear in a labelled *"not a similarity match"* section.
The cause is discovery finding **F-c**, confirmed in code:

- `GET /api/ai/visualization/related/{documentId}` — documents only.
- `POST /api/ai/search/records` — has **real** per-row authorization, but is **query-text driven and accepts no
  source document**. `RecordSearchRequest` (`Models/Ai/RecordSearch/RecordSearchRequest.cs`) requires
  `Query` (string, ≤1000) + `RecordTypes` (≥1). There is no document-seed field.

### (b) Outlook can never leave state 1

`FindView.tsx:101` — `resolveFindState` maps `documentIdentity === undefined` → `{ kind: 'no-document' }`.

The reason Outlook is always `undefined` is **not** a `hostType` conditional — and that matters, because the
design is right. `App.tsx:207-209`:

```ts
const [documentIdentity, setDocumentIdentity] = useState<DocumentIdentityState | undefined>(() => {
  return hostAdapter.getCapabilities().canGetDocumentUrl ? 'checking' : undefined;
```

So identity resolution is **capability-gated on `canGetDocumentUrl`** (NFR-10, correctly), and Outlook simply
cannot produce a document URL — an email is not a document with a URL.

### (c) No post-save transition

Two sites, and the code already concedes it. `App.tsx:711` sets the id on save:

```ts
if (docId) {
  setSavedContext(prev => ({ ...prev, documentId: cleanGuid(docId) }));
}
```

with a comment stating *"Outlook has no such resolution path at all, so a completed save is the ONLY place it
ever learns the id."* But `App.tsx:748` passes **only** `documentIdentity` to `FindView`:

```tsx
<FindView
  {...(documentIdentity !== undefined ? { documentIdentity } : {})}
```

`savedContext.documentId` is never threaded in. `FindView`'s own doc comment (`:88-90`) admits it: *"no client
flow threads a just-completed save's documentId back into `App.savedContext` for either host today —
pre-existing, not introduced here."*

---

## 🔑 The finding that collapses (b) into (c)

**(b) and (c) are one fix, not two.** Outlook learns a `documentId` from exactly one place — a completed save.
So Outlook's Find tab is not *permanently* dead; it is dead **until the user saves**, and today it stays dead
*even after* saving, purely because of (c).

Fix (c) and Outlook's tab becomes useful exactly when it should be. Before a save, *"Save this document to
Spaarke"* is the **honest** state for Outlook, not a dead end — there is genuinely nothing to find yet.

**This avoids escalation trigger 2.** No Graph call, no manifest change, no new capability, and no FR-16
amendment: the tab reaches a useful state through a fix this project already owns. The only residual is copy —
for Outlook the string should say *email*, not *document*.

## 🔑 The records bridge — two existing calls, no new endpoint

**This avoids escalation trigger 1**, which would have fired on a new BFF route (and correctly: F-c left the
question open rather than presuming one).

The seed text the record search needs **already exists client-side**. `hooks/useDocumentProfile.ts` reads
`sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords` and `sprk_documenttype` for the resolved
`sprk_document`, and returns `DocumentProfileFields { summary, tldr, keywords, … }`. The Save tab's Profile
section (FR-07) already renders them.

So the bridge is:

| Half | Call | Authorization |
|---|---|---|
| Documents | `GET /api/ai/visualization/related/{documentId}` (unchanged) | source-document auth + the per-row trim task 032 added |
| Records | `POST /api/ai/search/records`, seeded with the document's own **keywords** (falling back to `tldr`, then `summary`) | `.AddRecordSearchAuthorizationFilter()` + `AuthorizeRowsAsync` |

Composed client-side, so no route is added and no endpoint changes shape.

**Why `/api/ai/search/records` satisfies the authorization constraint, verified rather than assumed**:
`RecordSearchEndpoints.cs:41` applies `.AddRecordSearchAuthorizationFilter()`, rows are trimmed by
`AuthorizeRowsAsync` (`:162`, `:242`), and `:115-123` carries a **forcing function** that refuses the request
outright if the filter is ever detached:

> *"if someone detaches AddRecordSearchAuthorizationFilter, this … Refusing."*

That is exactly the property the constraint demands — and the reason the records half must come from this path
and not be bolted onto the documents call.

**Why keywords first, not the summary**: keywords are a short comma-separated list, which is closer to a search
query than a paragraph of prose; a 1000-character summary would dilute a hybrid semantic+keyword query. The
fallback chain exists because `sprk_filekeywords` is empty until profiling completes, and Find must still work
for a document whose profile is `Pending`.

**Honest limitation to record with the design**: this is *similar records by the document's own topic*, not
*records vector-similar to the document's content*. Nothing in the current index supports the latter, and
manufacturing it would need the new endpoint the trigger forbids. FR-16 says "documents and records"; this
delivers records related to the document's subject matter, via an authorized path. If the owner intended true
cross-entity vector similarity, that is a separate, larger piece of work and should be said out loud rather
than implied by a UI that looks the same.

---

## Implementation plan

1. **(c) + (b)** — thread the post-save id into `FindView` and use it as an identity source of last resort.
   Narrowest, clearest cause, and it unlocks Outlook.
2. **(a)** — add the records half to the Find results, reusing `useDocumentProfile` for the seed and
   `useLazyResults` for scroll (ADR-051: no pager).
3. Outlook copy — *email* rather than *document* in the no-document state.
4. Tests: the records negative-authorization case, the post-save transition, mixed-result lazy scroll.

*(Implementation follows this note; results appended below.)*
