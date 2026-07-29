# Task 040 — Per-type creation wizard: deviations + scoping decisions

> Documented per task-execute Step 8 ("Document any deviation"). No `<escalation>` trigger fired
> (the Field Mapping engine expressed both "associate-to" and "next-steps" without any net-new
> mapping-type or expression work) — these are scoping/interpretation decisions, not ADR conflicts.

## 1. "Run the analysis" — scoped to status flip + file-tab load, not a new AI session

Task 040's declared dependencies are 011 (client `sprk_analysis` type) and 012 (regarding resolver)
ONLY. The session↔Analysis binding + two-tier session model + fork-on-analysis AI-execution
plumbing is built by other tasks in this project (020–025) and is explicitly out of scope here.

`CreateAnalysisWizardWidget`'s `onFinish` therefore:
- Sets `sprk_analysisstatus = 1` (`InProgress`) on create — a real, verifiable state transition.
- Dispatches the EXISTING, already-registered `document-viewer` workspace widget via
  `PaneEventBus` `widget_load` (`workspace` channel) with the resolved `documentId` — this is the
  literal "file loads to a workspace tab" acceptance criterion, using zero new registrations.

No new BFF session-start endpoint or client session-bootstrap contract was invented. If a later
task (020–025 downstream, or a wrap-up task) needs the wizard to also kick off a bound chat
session, that wiring should compose with this widget's existing `onFinish` (the analysisId is
available there) rather than duplicating record-creation logic.

## 2. "Access" interpreted as the OOB Dataverse Owner field

`sprk_analysis` has no dedicated `sprk_access`-style column in the verified schema
(`notes/schema-prerequisites.md` — no such field listed; task 011's `ISprkAnalysisRecord` does not
model one either). Per CLAUDE.md §11 ("avoid speculative fields"), no new column was invented.
"Access" is implemented as the standard Dataverse `ownerid` lookup (Step 2's "Access" `LookupField`,
searching `systemuser` via `data.searchUsers`) — the existing OOB security-role/BU access model.
Bound via `ownerid@odata.bind` when a user is picked; omitted (defaults to created-by) otherwise.

## 3. `CreateRecordWizard` extension: `existingRecordPicker` (Step 1 "select existing Document")

The shared `CreateRecordWizard`'s built-in "Add file(s)" step only supports upload
(`FileUploadZone`) — no existing precedent supported "select an existing record" instead. Per
ADR-012 ("do NOT fork a parallel wizard implementation") this task added a small, additive,
opt-in config surface (`ICreateRecordWizardConfig.existingRecordPicker` +
`IFinishContext.selectedExistingRecord`) rather than forking a parallel wizard shell — mirroring
the precedent set by `hideFilesStep` / `followOnCards` (both added by prior tasks for the same
reason: a genuine per-wizard need the shared shell didn't yet support). Every other consumer of
`CreateRecordWizard` is unaffected (the new config key is optional and defaults to no-op).

Covered by a new sibling test file:
`src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/__tests__/CreateRecordWizard.existingRecordPicker.test.tsx`.

## 4. Step 2 combines Access + Associate-To + Name + Description in ONE step

`CreateRecordWizard`'s built-in `config.associateToStep` renders the regarding picker as its OWN
step, PREPENDED before the files step — using it would have produced 4 steps
(`Associate To → Add file(s) → Info → Next Steps`), not the 3 the spec (FR-12) and acceptance
criteria enumerate. Instead, `AssociateToStep` is rendered directly (in `variant="compact"`) inside
the wizard's custom `infoStep`, alongside Name / Description / Access — producing exactly
`Add file(s) → Analysis Details → Next Steps`.

## 5. `workTypeValue`/`workTypeLabel` — raw values, not the `AnalysisWorkTypeId` union

`CreateAnalysisWizardWidget` lives in the shared `Spaarke.AI.Widgets` package. The semantic
`AnalysisWorkTypeId` union + `SprkAnalysisWorkType` enum (task 011) live in
`src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts` — a SOLUTION-owned file that depends on this
shared package, not the reverse (ADR-012). The widget therefore accepts a raw Choice integer
(`data.workTypeValue`, default `100000000` = Agreement Review) + a display label
(`data.workTypeLabel`) instead of importing the solution's semantic type. The caller (hub/entry —
tasks 030/050, both living in SpaarkeAi) resolves `AnalysisWorkTypeId` → the numeric value before
passing it in.

## 6. `register-workspace-widgets.ts` — NOT touched

Per the orchestrator's explicit parallel-execution constraint, task 030 (Analysis hub widget, runs
AFTER this task) owns `register-workspace-widgets.ts`. `CreateAnalysisWizardWidget` is exported
(default + named) from its own file for the hub (030) and entry-routing (050) to register/consume
— it deliberately does NOT self-register, avoiding the registration-collision risk called out in
the task brief.

## 7. Field-Mapping-driven "Send Email" — enrich, then fall back

Unlike "Create To Do" (whose `TodoService.createTodo` already calls `applyFieldMappings`
internally — task 021, zero extra wiring needed here), `EntityCreationService.sendEmail` is a
fire-and-forget BFF Communication-send call with no configurable target Dataverse record to
enrich. To make "Send Email" genuinely Field-Mapping-driven without inventing a new mapping type,
`onFinish` calls `applyFieldMappings({ sourceEntity: 'sprk_analysis', targetEntity:
'sprk_communication', payload: {} })` against a scratch payload; any `subject`/`description`
fields a configured profile writes override the user-entered subject/body, and the call gracefully
no-ops (falls back entirely to user input) when no profile is configured for that entity pair —
the expected default state for most tenants.
