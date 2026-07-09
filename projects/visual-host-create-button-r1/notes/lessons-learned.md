# Lessons Learned — visual-host-create-button-r1

**Project closed**: 2026-07-09
**Scope shipped**: "+" toolbar button on Visual Host → Create wizards for Event / Invoice / Report Card, auto-associated to the host record via ADR-024 `applyResolverFields`, shared `WizardFollowOns` module consolidating 4 previously-duplicated Next-Steps implementations.

## 1. Mid-project scope pivot: KPI Assessment → Report Card

The spec originally targeted a third wizard creating `sprk_kpiassessment` directly. On review (2026-07-08, before task 040 dispatch), the owner determined the maker-meaningful unit is actually `sprk_reportcard` — the parent review artifact that KPI Assessment line-items belong to — and retargeted the wizard mid-project.

**Why this worked cleanly**: `sprk_reportcard` was already fully ADR-024 resolver-ready (zero schema delta needed for the resolver itself, vs. the original KPI target which needed schema work). The registry key was renamed (`kpi-assessment` → `report-card`) with `ENTITY_TO_WIZARD_KEY` mapping both `sprk_reportcard` and `sprk_kpiassessment` to the same wizard, so the fallback-from-entity path stays robust even for a chart definition still pointing at the old entity.

**Takeaway**: when a spec's chosen target entity turns out to be a line-item of a more maker-meaningful parent record, it's worth pausing before Phase C/D build to re-confirm the target — the cost of the pivot here was near-zero because it happened before the wizard was built, not after.

## 2. An owner-ratified exception is not the same as skipping consolidation

Task 023 (migrate `CreateWorkAssignmentWizard` onto shared `WizardFollowOns`) initially looked like it left two local follow-on steps un-migrated (`AssignWorkStep.tsx`, `CreateFollowOnEventStep.tsx`), which read like an incomplete migration. Investigation traced actual call sites and found these were NOT duplicates despite sharing a component name with the shared steps: the local "Assign Work" step feeds extra field-groups into the SAME `createWorkAssignment(...)` call (in-flight field extension, not a second record), while the shared `AssignWorkFollowOnStep` creates an independent NEW child record for other wizards. The owner ratified this as a CLAUDE.md §6.5 Path A exception after seeing the concrete call-site evidence.

**Takeaway**: "the same component name appears twice" is not proof of duplication — trace the actual call site and what record it creates before either merging away or accepting a divergence. A ratified exception needs the concrete trace, not just a plausible-sounding rationale.

## 3. Dialog sizing: don't trust a component's own internal default when the hosting mode changed

`WizardShell.tsx` has its own internal default size (`95vw`/`70vh`) — used only when a wizard renders its OWN Dialog (non-embedded mode). VisualHost's "+" button uses `embedded={true}`, meaning `WizardShell` skips its own Dialog and relies entirely on the HOST's `DialogSurface` sizing. The first fix attempt matched `WizardShell`'s internal default, reasoning it was "the standard" — but UAT screenshots proved this was visibly too large compared to the actual repo-wide standard.

The real standard turned out to live in `src/client/webresources/js/sprk_wizard_commands.js`'s `DIALOG_OPTIONS` constant: `60% × 70%`, explicitly commented "standardized... per UAT feedback E-03" — a ribbon-launch JS webresource, not the React component tree at all.

**Takeaway**: when a component is used in a fundamentally different hosting mode than its own defaults assume (`embedded=true` vs. self-hosted Dialog), that component's own defaults are not evidence of "the standard" — go find where the ACTUAL cross-cutting convention is enforced (here: a ribbon command file that a UAT-feedback comment explicitly named as canonical) rather than trusting the nearest plausible-looking constant.

## 4. Field-mapping inheritance: deletion-as-collateral-damage is a real failure mode

UAT surfaced that parent→child field inheritance (e.g. Matter's Assigned Attorney 1 auto-populating on a child Event/Invoice/Report Card) worked for Report Card but not Invoice or Event. Investigation (git archaeology + live Dataverse queries + reading a sibling project's design.md) found: a working client-side field-mapping engine was built in Feb 2026 (`events-and-workflow-automation-r1`), then deleted in July 2026 as what looked like pure collateral damage of an unrelated PCF's retirement (`set-regarding-and-field-mapping-resolver-r1`, deleting `AssociationResolver`) — that project's own design.md explicitly listed "automatic cascade-on-parent-save" as a non-goal, meaning creation-time cascade was never actually re-built to survive the deletion, only update-time push via a still-live BFF endpoint.

This was scoped OUT of this project (per spec's explicit non-goal) and spun into a new project (`set-regarding-and-field-mapping-resolver-r2`) with a design.md proposing a CLAUDE.md §6.5 Path B amendment (creation-time cascade back in scope; update-time cascade's original overwrite-risk concern stays valid).

**Takeaway**: when a PCF or module is retired, explicitly audit what OTHER surfaces silently depended on code inside it — "retire X because Y made it redundant" can be true for the module's primary use case while still deleting a secondary capability (here: creation-time cascade) that nothing else was providing. A deletion PR's scope note ("no automatic cascade — deferred") is a useful paper trail for exactly this kind of later investigation.

## 5. A silent no-op fallback (`EMPTY_SEARCH`) can hide a wiring gap for a long time

Two of the three UAT-reported "lookup returns no results, zero console errors" bugs (Event's Assign Work Matter Type + Practice Area fields) traced to the exact same root cause: `CreateRecordWizard.tsx`'s shared "Assign Work" follow-on card silently falls back to `EMPTY_SEARCH = () => Promise.resolve([])` when a wizard's config omits `searchMatterTypes`/`searchPracticeAreas`. `CreateEventWizard.tsx` never set either key. Because the fallback never throws, this bug produced ZERO console errors — exactly matching what the owner observed, and exactly the kind of failure mode that's invisible to `tsc`, ESLint, and unit tests (nothing is actually broken at the type level; the config object is just incomplete).

A second, unrelated bug was found as a side effect while fixing the first: `CreateEventWizard.tsx`'s own local, duplicated `searchOrganizationsAsLookup` queried a nonexistent field (`sprk_name` instead of the real `sprk_organizationname`) — a drift that had crept in because this wizard maintained its own copy of search-helper logic instead of reusing the already-correct canonical implementation (`matterService.ts`, already re-exported via `workAssignmentService.ts` and already used by `CreateInvoiceWizard.tsx`).

**Takeaway**: a silent optional-config-key fallback that "just works" (returns empty instead of erroring) is a reasonable defensive default for a shared component, but it means a caller's *omission* is indistinguishable from a caller's *intentional choice* to have no matter-type search. When one sibling wizard (Invoice) wires a config key correctly and another (Event) doesn't, that's a strong signal to check for copy-paste drift across "thin wrapper" wizards built from the same template — and reusing canonical service functions instead of maintaining parallel local copies would have prevented both bugs at once.

## 6. One UAT lookup bug remains genuinely unexplained after two full investigation passes

Invoice's "Vendor Organization" field returns no results with zero console errors, same symptom class as #5 above — but this field's wiring, unlike Event's, checked out completely: correct field/entity names (verified live), data exists, the canonical `matterService.ts` helper is correctly imported and called, `dataService` is correctly plumbed all the way from `VisualHostRoot.tsx`. No code-level defect found across two full passes.

Filed as [Issue #587](https://github.com/spaarke-dev/spaarke/issues/587) rather than guessed at — the owner explicitly chose to defer this to a session with live browser (`--chrome`) access instead of accepting a low-confidence fix, since the remaining hypotheses (does `onSearch` fire at all; what does the Network tab actually show; is there a security-role/privilege gap specific to this one entity) all require live-environment observation this session couldn't produce.

**Takeaway**: when every code-level hypothesis is exhausted and a bug still reproduces, the honest move is to say so explicitly and file it for live-environment diagnosis — not to keep guessing at "one more theory" or claim a fix without verification.

## Process notes

- Live browser UAT (2 rounds, with screenshots) caught real bugs that static code review + Dataverse-record-creation verification (task 050's original verification method) had missed entirely — the dialog-sizing bug and both lookup bugs were only visible via actual browser interaction.
- `/test-diet` at wrap-up (per CLAUDE.md's project-close gate) reviewed all 163 tests touched by this project and found 0 scaffolding — a good signal that ADR-038's build-vs-maintain discipline was followed during implementation, not retrofitted at the end.
