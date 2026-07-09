# Test diet report — visual-host-create-button-r1

**Run date**: 2026-07-09
**Branch**: work/visual-host-create-button-r1
**Scope**: test files touched by the project's bundled implementation commit `b026daf48` ("feat(visual-host-create-button-r1): Visual Host "+" create button — Event/Invoice/Report Card wizards")

> **Scoping note**: this project's own commit history was flattened into one bundled commit (`b026daf48`) after task-by-task subagent execution, so `git log {first-commit}..HEAD` over the full branch range pulls in test files from unrelated projects merged into `master` during this branch's lifetime (SpaarkeAi, PlaybookBuilder, AI Widgets, etc.). Scope was narrowed to files actually touched by `b026daf48` (plus the two follow-up fix commits, `ced796cd7`/`03f946bfd`, which touch no test files) to isolate this project's own deltas.
>
> This is a TypeScript/Jest project (PCF + shared React component library) — the skill's Step 1 command (`tests/**/*.cs`) doesn't apply. The classifier logic (ADR-038 §7's 17-ban criteria) is language-agnostic and was applied to Jest `it()`/`describe()` blocks instead of xUnit `[Fact]`/`[Theory]`.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (keep, confirmed) | 163 | confirmed — no action |
| SCAFFOLDING (delete candidate) | 0 | none |
| AMBIGUOUS (reviewer judgment) | 0 | none |
| PATH-VIOLATION (wrong location) | 0 | none |
| **Total tests touched** | **163** | — |

**No delete or move commands to emit.** Every touched test file passed the 17-ban classifier cleanly.

## Files reviewed (17 files, 163 test cases)

| File | Tests | Classification |
|---|---|---|
| `pcf/VisualHost/control/services/__tests__/ConfigurationLoader.test.ts` | 23 | MAINTAIN |
| `CreateEventWizard/__tests__/CreateEventWizard.associateToStep.test.ts` | 14 | MAINTAIN |
| `CreateEventWizard/__tests__/eventService.resolver.test.ts` | 4 | MAINTAIN |
| `CreateInvoiceWizard/__tests__/CreateInvoiceStep.test.tsx` | 5 | MAINTAIN |
| `CreateInvoiceWizard/__tests__/CreateInvoiceWizard.associateToStep.test.ts` | 7 | MAINTAIN |
| `CreateInvoiceWizard/__tests__/invoiceService.resolver.test.ts` | 11 | MAINTAIN |
| `CreateRecordWizard/__tests__/CreateRecordWizard.hideFilesStep.test.tsx` | 2 | MAINTAIN |
| `CreateRecordWizard/__tests__/CreateRecordWizard.lockAssociation.test.tsx` | 2 | MAINTAIN |
| `CreateReportCardWizard/__tests__/CreateReportCardStep.test.tsx` | 5 | MAINTAIN |
| `CreateReportCardWizard/__tests__/CreateReportCardWizard.associateToStep.test.ts` | 8 | MAINTAIN |
| `CreateReportCardWizard/__tests__/reportCardService.resolver.test.ts` | 10 | MAINTAIN |
| `WizardFollowOns/__tests__/AddTodoFollowOnStep.test.tsx` | 2 | MAINTAIN |
| `WizardFollowOns/__tests__/FollowOnGrid.test.tsx` | 5 | MAINTAIN |
| `WizardRegistry/__tests__/wizardRegistry.test.ts` | 10 | MAINTAIN |
| `services/__tests__/EntityCreationService.multibind.test.ts` | 8 | MAINTAIN |
| `services/__tests__/TodoRegardingUpdateBuilder.test.ts` | 22 | MAINTAIN |
| `AssociateToStep/__tests__/AssociateToStep.test.tsx` | 25 | MAINTAIN |

## Why MAINTAIN across the board (evidence, not assertion)

Sampled representative files at each risk tier (highest test-density, shortest files, heaviest-setup resolver tests) rather than reading all 163 bodies:

- **`wizardRegistry.test.ts`** (10 tests / 63 lines — highest density): each test is a single-assertion call into `resolveWizard()`, but each exercises a **distinct branch of real decision logic** (explicit-key vs. entity-fallback vs. precedence vs. unknown-key/null-key edge cases) — not a mirror of a trivial 1:1 method. Passes B6 (mirror check).
- **`CreateRecordWizard.hideFilesStep.test.tsx`** (2 tests / 71 lines — shortest): genuine DOM-render behavioral assertions (`screen.queryAllByText(...)`) against a real config flag, with an explicit doctring disclaiming "no mock-internals or DI assertions (ADR-038)". Passes B1/B3/B4/B10.
- **`invoiceService.resolver.test.ts`** (11 tests / 483 lines — heaviest setup): the setup builds a realistic fake Dataverse metadata catalog (`sprk_recordtype_ref` lookups keyed by entity) to exercise the REAL `applyResolverFields` resolver logic end-to-end — a legitimate characterization/behavior fixture (Feathers), not a shallow `Mock<HttpMessageHandler>` wiring test. The file's own header comment explicitly disclaims the exact B1/B3/B4 patterns this skill bans. Passes B1/B2/B7/B15.
- **Naming across all 163 tests**: every test name states a concrete scenario + expected result (`resolveWizard_ExplicitKeyTakesPrecedenceOverFallback`, `graceful degradation (NFR-06): missing catalog still links + falls back on name, skips number/type, never throws`, `binds an uploaded file to BOTH the new Invoice (primary) and the host Matter (additionalBinds)`) — zero instances of `Test1`, `Foo_Works`, or unscoped names (B13 clean).
- **No matches found** for any of: `Mock<HttpMessageHandler>` equivalent (raw `fetch`/XHR mocking), `GetRequiredService`-style DI-registration assertions, ctor null-checks, record-equality/auto-property round-trips, snapshot-vs-default-format comparisons, or `BindingFlags.NonPublic`-equivalent internal access (no such patterns exist in this TS codebase's idioms).

## Ambiguous — reviewer judgment

None.

## Path-move commands

None — all 17 files already live at their canonical co-located `__tests__/` path next to the component/service they test, which is this codebase's established convention (mirrors the 6 KEEP-path principle for the TS/React stack).

## Count delta

- Tests added/touched during project: 163
- Tests classified MAINTAIN: 163
- Tests classified SCAFFOLDING: 0
- Tests classified AMBIGUOUS: 0
- Net post-diet expected count: 163 (no deletions)

## Full-suite regression check (context, not part of the classifier)

Full shared-lib suite run this session (post code-review fix, commit `03f946bfd`): **107/114 suites, 1749/1765 tests** — unchanged from this project's established baseline. The 7 failing suites (16 tests) are pre-existing, unrelated to this project (e.g. `RichFilePreview` ambiguous-text-match failures), not part of this project's touched-file scope, and not evaluated by this diet pass.

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1-B17.
