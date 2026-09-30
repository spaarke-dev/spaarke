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

---

# RESULTS — implemented 2026-09-30

Commits: `7b7688f5c` (gaps b + c) · `73c858f28` (gap a) · `22f6ac55e` (code-review fixes).

## Verified before implementing — the producer for (b) + (c) already existed

The plan above assumed that threading `savedContext.documentId` into `FindView` would fix (c). That is only
true if something WRITES that field on save. Checked: `App.tsx` `SaveView.onComplete` does, on **both hosts**
(added by task 036 / FR-15 for Send Email). So (b) + (c) needed a consumer, not a producer. Had the field
been unwritten, the thread would have been a silent no-op.

It also exposed a comment that had gone false: `FindView`'s header said *"no client flow threads a
just-completed save's documentId back into `App.savedContext` for either host today"*. True when task 033
wrote it; task 036 made it false. Corrected in place (not deleted), so the closure is visible.

## (b) + (c) — one branch in `resolveFindState`

A completed save's id is an identity source of LAST resort, applied to exactly the two outcomes that mean
*"we do not know of a record"*: `new`, and `undefined` (Outlook). **Deliberately not** to `conflict` /
`indeterminate` / `denied` / `error` — those are honest refusals that identity resolution went wrong, and
papering over them with a save id would hide a real defect.

## Outlook decision — FR-16 is NOT amended (constraint 5)

Outlook's Find tab now reaches a useful state: after a save it shows the index state and Run Index, then
results. Before a save, *"Save this email to Spaarke"* is the honest state — there is genuinely nothing to
find yet. No Graph call, no manifest change, so escalation trigger 2 does not fire. The noun comes from the
`canGetSender` **capability** (an item with a sender is an email), not `hostType` (NFR-10).

## (a) — the records bridge

| Half | Source | Authorization |
|---|---|---|
| Documents | `GET /api/ai/visualization/related/{id}` (unchanged) | source-doc auth + task 032 per-row trim |
| Records | `POST /api/ai/search/records` | `RecordSearchAuthorizationFilter` + `AuthorizeRowsAsync` |

Composed client-side in `hooks/useFindRecordMatches.ts`. **No new BFF route** — trigger 1 does not fire.

**Why this reverses task 034's documents-only decision.** 034 (`notes/034-records-bridge-decision.md`)
declined because every seed it considered was bad: the title is often generic (`Document1.docx`) and there is
no body-text extraction seam. Both objections were correct. It never considered the **AI profile**:
`sprk_filekeywords` is a purpose-built extraction, which is what a query should be made of. Seed precedence
keywords → TL;DR → summary; **no seed is ever invented** from the title (a pending or empty profile shows a
caption instead). 034 §4's three conditions for a later attempt are met: (i) the seed is defined exactly;
(ii) the UI states *"Matched on this document's AI keywords — not a content-similarity match"*; (iii) the
paging mismatch is reconciled below.

**Named "Matching records", hook named `useFindRecordMatches`.** First named `useRelatedRecords` — one letter
from the existing `useRelatedRecord`, which is the document's OWN FR-09 record card. Different concept, same
type labels; renamed before anything depended on it. The UI heading changed for the same reason: the hub
section directly above already shows *this document's* records.

## 🔔 ADR-051 — a deliberate reading of its "page fullness" rule (CLAUDE.md §6.5 — owner decision)

- **Rule**: *"load progressively via an IntersectionObserver on a bottom sentinel + a page-fullness hasMore
  rule (full page ⇒ more; short/empty page ⇒ end)"*.
- **Conflict**: `AuthorizeRowsAsync` REMOVES rows the caller cannot read, so a page of 25 search hits with 3
  unreadable arrives as 22. The route's own remarks call such a page *"indistinguishable from nothing
  matched"*. The literal rule would stop after page 1 whenever page 1 held one unreadable record — the exact
  *"shows only the first page"* failure ADR-051 exists to prevent.
- **What shipped**: *non-empty ⇒ more, empty ⇒ end*; the offset advances by the **requested** window (the
  pre-filter search window), never by the surviving count, which would re-request evaluated rows. Cost: one
  extra request at the true end.
- **Proposed path**: **A** (project-scoped exception) — ADR-051 is right in general; its premise just does not
  hold for a server-trimmed route. **B** worth considering later: amend ADR-051 to say *"page fullness, unless
  the source trims rows server-side"*, since every per-row-authorized route will meet this.
- **Rejected**: C (comply literally) — it reintroduces the bug the ADR prevents.
- **Accepted limitation**: a page on which EVERY row is unreadable also arrives empty, so paging stops there
  even if readable rows follow. That errs toward showing too little, never toward showing what the caller
  cannot read.
- Switching back is one line — and it is seeded, so a change in either direction is caught by CI.

## The negative authorization test had to be WRITTEN, not cited

The records half's whole authorization claim rests on `RecordSearchEndpoints.AuthorizeRowsAsync` — and
**nothing tested it**. `RecordSearchEndpointsTests` covers DTO shape only; the one row-trim test in the suite
is for the visualization route's same-named sibling. Added
`tests/integration/contract/Api/Ai/RecordSearchRowAuthorizationContractTests.cs`, driving the **shipped**
handler (`PostRecordSearch` `private` → `internal` — the precedent `VisualizationEndpoints.GetRelatedDocuments`
set for exactly this reason). It asserts the unreadable record's id AND name are absent, the readable one
survives, `TotalCount` does not leak the dropped row, and **both** rows were evaluated as the caller.

## Code review (Step 9.5) — one Critical, found and fixed

**C-1 — records could stop paging at page 1 permanently.** The hook runs in `FindView`; its sentinel is
rendered by `FindResultsList`, which mounts only after the DOCUMENTS request loads. If records answered first,
the observer effect ran against a sentinel that did not exist yet and bailed — and an object ref gave it
nothing to re-run on. A realistic ordering: record search is one query, visualization is five plus a vector
search. Fixed with a callback ref held in state. `useLazyResults` never had this because it is called in the
same component that renders its sentinel — which is also why the first tests missed it.

| Finding | Decision |
|---|---|
| C-1 paging race | **Fixed** + regression test (fails on the pre-fix hook, passes on the fix) |
| W-1 a documents failure hides records that loaded | **Accepted, documented** — the error is shown honestly; records degraded, not wrong |
| W-2 no `<justification>` for the new hook | **Fixed** — written into the 077 POML |
| W-3 publish size owed (§10) | Measured — see Verification |
| S-1 fallbacks said "related" | Fixed |
| S-2 stale `FindView` header row | Fixed |
| S-3 late response after unmount | Fixed — generation invalidated in effect cleanup |
| S-4 auto-fetch while the sentinel stays visible | Accepted — bounded (~11 requests) by the 360 px scroll area |
| S-5 / S-6 caption wording; extract `RecordsGroup` if it grows | Accepted |

## Seeds — every new test proven able to fail

| Seed | Turned red |
|---|---|
| Bypass `AuthorizeRowsAsync` in the shipped handler | the server negative test — *"a record the caller cannot read must never reach the Find tab"* |
| Literal page-fullness `hasMore` | the short-page paging test only |
| Advance offset by the RETURNED count | the short-page paging test only |
| Disable the save-id override | exactly the 3 post-save / Outlook tests |
| Pre-fix hook (object ref) | the late-sentinel regression test |

Every seed reverted to a byte-identical file.

## Test scope — the closed set, plus two justified extras

In scope: the records bridge + its negative case; the Outlook decision; the post-save transition; lazy scroll
over mixed results. Two extras, one line each:
- *offset-cap test* — without it the 41st page request is a 400 surfaced to the user at the end of a long list.
- *no-seed empty-state test* — prevents the UI claiming "no matching records" when none were searched.

**Not tested, accepted**: the stale-query generation guard (a seed change mid-flight).

The new jest suite is registered in `ci-gated-suites.txt` — the gate is an allow-list, so an unregistered suite
would run locally and never in CI. Gated suites 56 → 57.

## Verification (real output)

| Gate | Result |
|---|---|
| Gated jest (the PR gate) | **57 / 57 suites, 765 tests** — 56 → 57, the new suite registered in `ci-gated-suites.txt` |
| Find suites after the code-review fix | 57 / 57 tests (FindView 37 · FindResultsList 13 · useFindRecordMatches 7) |
| ESLint `--max-warnings 0` | clean — after fixing 4 warnings of mine (a real missing `itemNoun` effect dependency + 3 empty mock methods) |
| tsc | **68** — the pinned baseline exactly, 0 in any touched file (was 70 mid-task; fixed) |
| Build `Sprk.Bff.Api` + tests | 0 warnings / 0 errors |
| ArchTests | **333 / 333** — run because `RecordSearchEndpoints.cs` is scanned by source guards |
| Record-search tests (new + DTO + visualization sibling) | 44 / 44 |
| Full `Sprk.Bff.Api.Tests` | **13,004 passed / 0 failed / 56 skipped** (12 m 48 s) — reconciles EXACTLY: 13,003 baseline + 1 new test |
| Publish size (§10) | merge-base `9f938336e` **45.67 MB** → branch **45.67 MB** = **0.00 MB**; **215 files both sides**; `Compress-Archive` both sides, short paths `C:\wt077m` / `C:\wt077b`, both worktrees fresh. Base is the MERGE-BASE, not `origin/master` (1 commit ahead by then), so the delta is this task alone |
| CVE | no vulnerable packages; no package added |

## Acceptance criteria — honest status

| # | Criterion | Status |
|---|---|---|
| 1 | Reproduce-first, all three behaviours | ✅ top of this note |
| 2 | Documents AND records, records per-row authorized; bridge stated; negative test | ✅ bridge section above; negative test written (none existed) and seeded |
| 3 | Outlook's Find reaches a useful state, or FR-16 amended | ✅ reaches it after a save; FR-16 NOT amended |
| 4 | A document saved this session becomes findable; Run Index reachable | ✅ tested both hosts |
| 5 | Lazy scroll per ADR-051 with the mixed set | ✅ implemented + C-1 fixed — ⚠️ the §6.5 page-fullness reading **awaits the owner** |
| 6 | Dark mode per ADR-021; no hard-coded colours | ⚠️ **code-verified only** — Fluent semantic tokens throughout, no hex; a VISUAL check needs a live Office host |
| 7 | All jest suites green | ✅ 57 / 57 (the criterion said 56; this task added the 57th) |
| 8 | Test scope | ✅ closed set + two extras, each justified above |

**The three `<ui-tests>` are UNVERIFIED** — no Word host here. Deferred to live UAT, the same precedent as tasks
013/021/026/033/034/037. Recorded as ⚠️ *complete with escalation* rather than ✅ for exactly two reasons: the
ADR-051 decision is the owner's, and the UI has not been seen running.

## Not deployed

Owner decision 2026-09-30: no redeploy now. None of this is on `spaarke-bff-dev` or the add-in SWA yet — and the
shared dev BFF is currently missing this project's routes anyway (`notes/042-uat-round2-2026-09-30.md`).

