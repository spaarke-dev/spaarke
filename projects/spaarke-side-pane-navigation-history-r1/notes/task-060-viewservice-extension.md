# Task 060 (Views tab) — ViewService reuse-vs-extend decision

## The gap

The task's escalation trigger reads: *"If ViewService cannot supply the
userquery data grouped as FR-10 requires without a change to its public
contract, STOP and escalate (root §6.5 — reuse-vs-extend decision) rather
than adding a parallel view-querying service."*

`ViewService`'s three existing public methods (`getViews`, `getDefaultView`,
`getViewById`) are ALL scoped by a required `entityLogicalName` parameter —
they answer "give me the views FOR entity X." FR-10 needs the opposite
direction: "give me every view this user owns, across every entity, so I can
group them by entity for display." There is no existing method that can
answer that without knowing entity names up front, and the Navigator Views
tab has no such fixed entity list (unlike Recent's `editedByMeService`/
`monitoredService`, which scan a known, fixed core-entity set).

## Decision: extend, not fork, not block

Per root `CLAUDE.md` §11 ("Component Justification — Default to Reuse": *"Can
I extend the existing instead? If yes → extend"*) and the project's own
NFR-08 constraint ("reuse ViewService.ts to query userquery"), the resolution
is an **additive extension** of `ViewService`, not a human-escalation stop:

- **Existing**: `fetchUserQueries(entityLogicalName)` (private) already knows
  how to query + map `userquery` records — just always with a
  `returnedtypecode eq '{entity}'` filter clause baked in.
- **Extension**: added ONE new public method, `ViewService.getAllUserQueries()`,
  that runs the SAME query with that one filter clause omitted (keeping
  `statecode eq 0` + `querytype eq 0`), and reuses a newly-factored-out
  private mapper (`mapUserQueryToViewDefinition`) shared with the existing
  `fetchUserQueries`. No existing method's signature or behavior changed.
- **Cost of doing nothing**: without this, the Views tab would either (a)
  need to already know every entity a user might have a personal view for
  before it could ask ViewService for anything — impossible, since that's
  exactly what the tab is supposed to discover — or (b) bypass ViewService
  and query `userquery` directly from `ViewsTab.tsx`, which is the parallel
  view-querying service the task explicitly forbids.

This is why the dispatch instructions frame this as "flag the shared-lib
edit" rather than a hard escalation: the change is a minimal, non-breaking,
additive extension of the exact service the constraint says to reuse — not a
fork, and not a case where an ADR-compliant alternative exists that avoids
touching `ViewService` at all.

## What changed (shared-lib)

- `src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts`
  - Added `getAllUserQueries(): Promise<IViewDefinition[]>` (public).
  - Extracted `mapUserQueryToViewDefinition()` (private) out of
    `fetchUserQueries`, now shared by both methods.
  - Not cached (existing `viewCache` is keyed by `entityLogicalName`, which
    doesn't apply to a cross-entity query).
- `src/client/shared/Spaarke.UI.Components/src/services/__tests__/ViewService.test.ts`
  - Added a `getAllUserQueries` describe block (4 tests): cross-entity fetch
    with no `returnedtypecode` filter, never queries `savedquery`, error
    handling returns `[]`, zero-userquery returns `[]`.
- `src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts`
  - Widened `PageInput` with an optional `viewId?: string` field (additive,
    non-breaking) so the Views tab's `navigateTo({pageType:'entitylist',
    entityName, viewId})` call is fully typed through the canonical
    `getXrm()` path, rather than falling back to the untyped `any`-cast
    workaround already present in `LegalWorkspace/WorkspaceGrid.tsx` for the
    same `viewId` gap. Mirrors task 010's `webresourceName` widen precedent.

## Regression verification

- `ViewSelector.tsx` (the only existing consumer of `ViewService`) —
  `ViewSelector.test.tsx` 14/14 still pass unmodified: the `fetchUserQueries`
  refactor (extracting the mapper) is behavior-preserving.
- `xrmContext.test.ts` 28/28 still pass unmodified.
- New `ViewService.getAllUserQueries` tests: 4/4 pass.
- `@spaarke/ui-components` `tsc` build: clean.

No parallel view-querying service was introduced; no new Dataverse query
storage was added. The MODIFY-ONLY/reuse constraint (NFR-08) is honored via
extension.
