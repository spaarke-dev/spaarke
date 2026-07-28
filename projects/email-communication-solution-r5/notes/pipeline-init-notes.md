# Pipeline Init Notes — `email-communication-solution-r5`

> Written 2026-07-27 by `/project-pipeline`. Records decisions + known-benign validator output from initialization.

## Task generation

20 POMLs authored across 6 phases (0–5) by 5 parallel sub-agents, one per phase-group. All grounded against live code (paths verified before authoring). `scripts/Validate-TaskPoml.ps1`: **20 scanned, 0 errors, PASS**.

## Intentional validator warnings (7) — do NOT re-litigate at code-review

`Validate-TaskPoml.ps1` emits 7 advisory warnings that are **expected and correct** by design:

| Tasks | Warning | Why it is intentional |
|---|---|---|
| 020, 021, 022, 023 | "adds NEW surface (role='new') but no `<justification>`" | These are **Layer-1 logic EXTRACTION** tasks. Per spec §11 (spec line 133) + design (design line 188), extracting existing PCF logic into shared cores is **COMPLETE-class (extraction of existing code), not net-new surface → no §11 row required**. The new files are relocated existing logic, not new behavior/scope. The validator heuristic keys on the `role='new'` shared-module files; the §11 scope-creep gate does not apply to refactors (root CLAUDE.md §11: "Tasks that ONLY modify… refactor… do NOT require justification"). |
| 020, 022 | "frontend task has no `<ui-tests>`" | Pure **React-agnostic logic** extraction — no visual surface to drive. UI-bearing sibling tasks 021 (`AttachmentList`) + 023 (`TrackingFieldTrio`) DO carry `<ui-tests>`. |
| 090 | "adds NEW surface (role='new') but no `<justification>`" | Wrap-up docs task; the `role='new'` file is `notes/lessons-learned.md`. Docs are §11-exempt. |

All NEW **product** surfaces DO carry concrete `<justification>` blocks: 010 (`eml-render` endpoint), 030 (`EmailCardList`), 032 (reading-pane shell), 040 (shared `EmailWorkspace`), 042 (code page), 041 (widget) — copied from the spec's §11 New-Components table with concrete cost-of-doing-nothing.

## Design-authority path correction

The stale `ConnectionsEditor` stub that task 020 MUST NOT reuse lives at `src/client/code-pages/CommunicationPage/.../ConnectionsEditor.tsx` (NOT under `src/client/shared/`). Corrected in task 020 during authoring.

## Empirical finding folded into task 002 (FR-17)

`IncomingCommunicationProcessor.cs` already treats `ArchiveIncomingOptIn` as default-on when null (`!= false`), and `CommunicationAccount.cs` documents "Defaults to true if not set." FR-17 may therefore be primarily **contract-pinning + test coverage** rather than a behavioral flip. Task 002 instructs the executor to characterize the current default empirically (Step 1) before editing, and make the minimal change.

## Coordination reminders

- `/conflict-check` before EVERY BFF PR (002, 010) and shared-lib PR (`@spaarke/communication-components`, `@spaarke/ui-components`).
- Sequence r5 merge **after** `email-communication-solution-r4` (owns the `Services/Communication/**` + `EmailComposer` + 4 Communication PCFs r5 extends).
