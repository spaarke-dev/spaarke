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

### DEF-002 — Compose fidelity-wideners home (indentation/paragraph-style/section-break survival — fast-follow)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-08-17 |
| **Source** | task 090 wrap-up (spec Owner Clarifications — "fidelity-wideners home decided at wrap-up"); R6 defer-register §C |
| **GitHub Issue** | [#777](https://github.com/spaarke-dev/spaarke/issues/777) |

**Description**

R7 is deliberately editor-UX-only and does not touch the render-on-save fidelity engine (ADR-049). The Compose
**fidelity wideners** — making Word formatting features SURVIVE the save round-trip instead of degrading loudly —
need a concrete named home so they don't rot as ledger entries. This DEF names that home: a fast-follow project
**`spaarkeai-compose-fidelity-wideners-r1`** (sequence AFTER `spaarkeai-compose-templates-r8`), carrying the R6
defer-register §C evidence.

**Concrete failure mode (§11)**: opening a routine, real NDA and saving it through Compose silently flattens
~84 indentations (`indentation-dropped ×84`) and ~85 paragraph styles (`paragraph-style-flattened ×85`) — measured
on the Corteva NDA UAT (2026-08-06). The output document loses its visual structure. On a legal-drafting surface
this is a named, measured regression on a shipped document class, not "future flexibility".

Front-of-queue wideners (by UAT warning volume): indentation survival (×84), paragraph-style survival (×85),
section-break survival (×6, == R6 **D5** page borders / page-break flow). Lower tier: tab-flattened ×5,
line-break ×5, heading-direct-numbering ×4, drawing/embedded ×5, custom-style-linked numbering, localized heading
ids, `hMerge`/`tblLayout` typed carry, bookmarks+internal links, typed move-revisions + table-revision carry,
`pageBreakBefore` tri-state, field-result box text + SmartArt.

**Entry-points**

- `projects/spaarkeai-compose-r7/notes/r6-defer-register-consolidated.md` §C — widener table + UAT volumes (the evidence carried forward)
- `.claude/adr/ADR-049-compose-shadow-document.md` — render-on-save canonical model; `src/server/api/Sprk.Bff.Api/Services/Compose/**` (SaveAsync render path)
- `docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md` — fidelity architecture

**Suggested approach**

Stand up `spaarkeai-compose-fidelity-wideners-r1` via `/design-to-spec` → `/project-pipeline`; prioritize
indentation + paragraph-style (the ×84/×85 volumes) first (section-break folds in R6 D5). Each widener is a
typed-carry addition to the render-on-save model with a seam test proving survival on the Corteva-NDA corpus.

**Estimated effort**: multi-widener project; indentation + paragraph-style survival ≈ the first milestone (~13 wideners total, decreasing frequency).
**Blockers**: none technical; needs portfolio scheduling. Owner worst-offender corpus rows 4–8 + Corteva-NDA-as-corpus-row-4 (needs confidentiality sign-off) strengthen the FR-08 fidelity gate.
**Related**: R6 D5 (page borders) subsumed by section-break survival; ADR-049; sequence after templates-r8.

---

### DEF-003 — Compose save-identity prod-safety (self-heal dupes, retroactive dedup tool, runtime key-health probe)

| Field | Value |
|---|---|
| **Status** | Open (partial — #1 + #4a shipped, rest deferred) |
| **Urgency** | next-round |
| **Filed** | 2026-08-17 |
| **Source** | dev UAT 2026-08-17 — Compose save 500s traced to a `Failed` `sprk_graphitemid_uk` key over 417 duplicate `sprk_document` rows |
| **GitHub Issue** | [#781](https://github.com/spaarke-dev/spaarke/issues/781) |

**Description**

Follow-on hardening for R7 FR-07(d) (atomic upsert on `sprk_graphitemid_uk`). The atomic-upsert dedup is
*preventive* and has an unmet precondition: the unique key can only be `Active` when `sprk_document.sprk_graphitemid`
is already unique. `spaarkedev1` carried **105 duplicated graphitemids / 417 excess rows** (the mis-scoped D1
debt — spec said "5"), so the key sat `Failed` and every save through the promote path threw a raw 500
(`not defined as keys / Not Active`, and `Found multiple records`). The preventive dedup literally could not run
because the duplicates it prevents blocked its own key.

**Shipped now (post-R7):**
- **#1 graceful error** — `ComposeEndpoints.Save` maps the two identity-key fault signatures → actionable 409/503 ProblemDetails (not an opaque 500). *Needs BFF deploy to take effect.*
- **#4a deploy-verification** — `scripts/Verify-ComposeIdentityKey.ps1` asserts the key is `Active` (run post-deploy / CI). Verified `Active` in dev 2026-08-17 after the cleanup.

**Deferred (this DEF):**
- **#2 self-heal on "found multiple"** — promote catch resolves a duplicated graphitemid deterministically (pick canonical, update, orphan rest) so touching a dup heals it.
- **#3 retroactive dedup admin tool** — package the one-time dedupe+reactivate flow (run by hand for dev) as a repeatable operation for prod.
- **#4b runtime key-health probe** — startup IHostedService asserting the key is `Active` (needs new Dataverse key-metadata retrieval in the BFF).

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:2769` — `PromoteIfEphemeralAsync` upsert + catch (#2 site)
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` Save handler — the shipped #1 catch
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:1954` — `UpsertAsync`
- `scripts/Verify-ComposeIdentityKey.ps1` — shipped #4a (basis for #4b)
- `src/server/api/Sprk.Bff.Api/Services/Documents/ContentDedupDetector.cs` — the *content*-dedup tool (from `sdap-file-duplication-detector-r1`); distinct axis from identity dedup

**Estimated effort**: ~1–2 days. BFF hot-path → §10 gates apply.
**Blockers**: none technical. Prod MUST have `sprk_graphitemid_uk` `Active` before Compose ships there (run #4a).
**Related**: DEF-001/#776, DEF-002/#777; the D1 deferral (spec Out-of-Scope) was mis-scoped and is the root cause.

---

## Issues (ISS)

_None filed._
