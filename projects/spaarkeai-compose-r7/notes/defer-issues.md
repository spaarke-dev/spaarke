# Deferrals & Issues — spaarkeai-compose-r7

> Source of truth for deferred work + newly-discovered issues. Kept in sync with GitHub Issues on the
> portfolio board (project #2). File via `/project-defer-issue-tracking` (alias `/defer`) — writes BOTH.
> Status lifecycle: Open → In Progress → Done / Won't Fix / Superseded.

---

## Deferrals (DEF)

### DEF-001 — apply-template server-side If-Match/ETag concurrency hardening (FR-12 Item 2, Path A follow-up)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-08-17 |
| **Source** | task 074 (FR-12) investigation — `notes/task-074-notes.md`; ComposeService.cs 372-475 / 1177-1184 / 1482-1485 |
| **GitHub Issue** | [#776](https://github.com/spaarke-dev/spaarke/issues/776) |

**Description**

`ComposeService.ApplyTemplateAsync` (`src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:372`)
downloads the document's CURRENT persisted bytes (T1), merges the firm/matter template chrome in memory,
then writes the result back via a **blind `ReplaceFileContentAsUserAsync` (T2) with NO If-Match
precondition**. A concurrent sibling-tab Save landing between T1 and T2 is therefore overwritten at the
head: the new head SPE version is "template-merged-over-the-older-content" and does NOT include the
concurrent save's edits.

Concrete failure mode: same document open in two tabs; Tab A saves (creates V2) in the sub-second window
while Tab B's apply-template is mid-flight server-side → Tab A's V2 is dropped from the head version. It is
RECOVERABLE (SPE version history retains V2 — the "FR-07 safety net", ComposeService.cs 436-437) but is a
head-clobber, not a clean rejection. Apply is already guarded to a saved / non-dirty document, which
narrows exposure.

Owner approved **Path A** (documented exception, Ralph 2026-08-17): apply-template rides the SAME
server-side concurrency model as save (no client-supplied If-Match, per the DELIBERATE ComposeService
design — "the client cannot assert its own currency", 1177-1184 — and the design-deferred Graph-If-Match
candidate, 1482-1485). This DEF tracks the architecture-consistent server-side hardening for a scoped
follow-up.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:372` — `ApplyTemplateAsync` (T1 download → merge → T2 blind replace @441)
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:1177-1184` + `:1482-1485` — the deliberate client-If-Match rejection + the design-deferred Graph-If-Match candidate
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs:1701` — `ApplyTemplate` endpoint (add a 412 catch → typed ProblemDetails)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` `handleApplyTemplate` (~2062) — add typed-412 handling (reload-and-reapply) once the server emits it
- shared facade: `SpeFileStore.ReplaceFileContentAsUserAsync` — needs an optional `ifMatch` param (Graph `If-Match` → 412)

**Suggested fix** (validate before implementing)

Add an optional `ifMatch` param to the shared `SpeFileStore.ReplaceFileContentAsUserAsync` facade (Graph
`If-Match` header → 412 on mismatch); capture the read-time eTag in `ApplyTemplateAsync` (a metadata read
at T1, as the save path already does ~1196) and pass it as the write precondition; catch the 412 in the
endpoint → typed ProblemDetails; handle the typed 412 client-side ("the document changed in another tab —
reload and re-apply"); migrate `SpeFileStore` test mocks (task-013-class ripple).

**Estimated effort**: ~0.5–1 day (shared-facade change + threading + endpoint + client + test migration).
**Blockers**: none technical. BFF hot-path → §10 gates (Placement Justification, publish ≤60 MB vs 44.96,
CVE, `/conflict-check`) apply to the implementing PR.
**Related**: ADR-007 (SpeFileStore facade), ADR-019 (ProblemDetails), ADR-049 (Compose save path); sister:
the save path's own live-eTag staleness-assert + AnnotationReanchor model.

---

## Issues (ISS)

_None filed._
