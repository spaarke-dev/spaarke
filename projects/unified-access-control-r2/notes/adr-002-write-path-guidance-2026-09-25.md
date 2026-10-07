# ADR-002 Review → Guidance for unified-access-control-r2

> **Date**: 2026-09-25
> **From**: ADR-002 plugin review (owner-approved; root CLAUDE.md §6.5 Path C + clarification)
> **Status**: Guidance, not yet task-ified. Owner decisions needed are marked 🔔.
> **Source of truth**: `docs/adr/ADR-002-no-heavy-plugins.md` + `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` — currently on branch `work/adr-002-server-side-write-path` in the main checkout (**not yet on master**; merge master after it lands).

---

## 1. What was decided (and why it matters to this project)

- **Your D-1 ("we do not use Dataverse plugins") is confirmed and now formal.** No plugins at all — the old "thin plugin with exception approval" path is gone, replaced by reopen criteria that require §6.5 sign-off.
- **The actual defect the review found is the one this project keeps running into:** invariants enforced **only on the client** (wizards, PCF), so every other write path skips them. The fix is the **Server-Side Write-Path rule**:

| Rule | Meaning for UAC-r2 |
|---|---|
| **WP-1** One server owner per invariant | `CoreAncestorResolver` is the owner for the core-ancestor stamp (registry **I-1**). Secure-project child isolation (**I-2**) has **no owner** yet. |
| **WP-2** Client previews, never solely enforces | Do **not** close the 10 unstamped client create paths by copying `deriveCoreAncestorStamps` into more wizards — that multiplies the defect. |
| **WP-3** Invariant tables written via BFF | The unstamped paths are fixed by moving those creates to BFF endpoints (see §4 — a separate project). |
| **WP-4** Security invariants inline, same request | Stamp/isolation set in the create request, never via a later queue hop. |
| **WP-5** Non-product writes → async fix-up + reconciliation | OOB forms, customer flows, and raw imports are corrected by an idempotent, fill-only job reusing `CoreAncestorResolver`. |
| **WP-6** **Security fails closed** | A missing or stale stamp or flag must **reduce** access, never widen it. This is the binding constraint for the items below. |

Places in this project's docs that list "plugin" as an option should now point at WP-4/5/6 instead: `design.md:595` (child ownership), `notes/task-051-client-restamp.md:~201` (native clear), `notes/finding-secure-transition-container-migration.md:~76`, `unified-access-control-cascade.md:77`, and INV-7's "plugins + wizard" wording at `design.md:~502`.

---

## 2. Findings from this project's code, mapped to the rule

| # | Finding (2026-09-25 sweep) | Fail mode | Rule | Recommendation |
|---|---|---|---|---|
| A | **Native clear of the regarding field leaves a stale ancestor stamp, which over-grants** (`notes/task-051-client-restamp.md:192-203`) | 🔴 **fail OPEN** | WP-6 | **Highest priority.** First, a read-side guard: the evaluator ignores an ancestor stamp when the record's regarding lookup is empty or inconsistent, so it fails closed immediately. Then a WP-5 fix-up (re-stamp or clear on regarding change). |
| B | `IsSecure: row.sprk_issecure == true` treats NULL as not-secure (`ExternalParticipationService.cs:605`); NULL on 5 of 10 live rows (`design.md:532`) | 🔴 **fail OPEN** | WP-6 | 🔔 Owner decision: treat NULL as `Unreadable` (secure + restricted), matching the existing unreadable path (`:529-534`), plus a backfill so NULLs become explicit. |
| C | **Secure-project child isolation is not implemented and not owned** (`design.md:581-603`; `spaarke-secure-project-r1` covers container isolation only) | 🔴 **fail OPEN** for native model-driven reads | WP-1/4/6 | 🔔 Recommend UAC-r2 own **I-2**. (1) **Read side now**: the evaluator vetoes broad and inherited principals for children whose core ancestor is a secure project. (2) **Write side**: set ownership and business unit at create through the BFF write path (reuse `RecordOwnershipResolver` from word-add-in-r1 task 080 when it merges), plus a WP-5 reconciliation for non-product writes. |
| D | 10 client-only create paths write through `Xrm.WebApi` without a stamp (todoService, eventService, invoiceService, workAssignmentService, reportCardService, CreateAnalysisWizardWidget, useInlineTodoCreate, ConnectionsWriteHandler, useSprkMemoRepository, EventDetailSidePane) | Fail closed (the record is invisible, not leaked), provided task 055 keeps its no-fallback rule | WP-2/3 | **Don't fix in UAC-r2 by adding client stamping.** These move to BFF creates in the write-path consolidation project (§4). Interim: keep task 055's fail-closed rule exactly as specified. |
| E | No stamp reconciliation; `Backfill-CoreAncestorStamps.ps1` never run live | Fail closed | WP-5 | Run the backfill once live. Longer term, replace the PowerShell re-implementation with a scheduled job (ADR-036) that calls `CoreAncestorResolver`, so there is **one** implementation of the rule, not two. |
| F | `ExternalAccessReconciliationJob` registered `enabled:false` + report-only | — | WP-5 | This is the reference WP-5 pattern (fail-closed read + async fix-up). 🔔 Decide the enable plan. |

---

## 3. Housekeeping that touches this project's files

- The ADR branch corrected the comment at `src/solutions/EventDetailSidePane/src/App.tsx:~648`. It previously claimed a non-existent "SprkPolymorphicResolverPlugin" (FAILURE-MODES AP-12). If UAC-r2 edits that file, expect a small merge conflict and keep the corrected comment.
- The ADR branch **deleted** `src/dataverse/plugins/Spaarke.CustomApiProxy/`, the `EmailProcessingMonitor` PCF, and `scripts/Register-EmailWebhook.ps1`. It also **rewrote** `tests/Spaarke.ArchTests/ADR002_PluginTests.cs` as a repo-wide zero-plugin guard. UAC-r2 references none of these.

---

## 4. Scope boundary: what UAC-r2 should **not** absorb

**Field-mapping consolidation and moving the wizards to BFF creates are a separate project, not UAC-r2.** Reasons:
- UAC-r2 is 85 of 116 tasks in, with PR #950 open.
- Field mapping is a data-default concern spanning every wizard, not an access concern.

That project's scope:
- Promote word-add-in-r1's `RecordCreationService` and `CreateTimeFieldMapping` to the canonical BFF write path.
- Replace the `/push` Copy-only engine with it.
- Add BFF create endpoints and switch the wizards over (this fixes finding D for UAC).
- Build the change-signal fix-up channel.

UAC-r2's contribution is to make sure `CoreAncestorResolver` (and the I-2 owner, if accepted) can be called from that pipeline, and to keep the invariant registry rows I-1, I-2 and I-8 accurate.

---

## 5. 🔔 Owner decisions requested

1. **B**: treat NULL `sprk_issecure` as Unreadable (fail closed) and backfill?
2. **C**: UAC-r2 owns secure-project child isolation (I-2), read side first?
3. **A**: add the read-side stale-stamp guard in this project (recommended, small)?
4. **F**: enable plan for `ExternalAccessReconciliationJob`?
