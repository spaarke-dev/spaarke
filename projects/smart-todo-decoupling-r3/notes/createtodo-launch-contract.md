# CreateTodoWizard launch-context contract

> **Project**: smart-todo-decoupling-r3
> **Authored**: 2026-06-08 (task 032)
> **Spec**: [FR-16](../spec.md) — `CreateTodoWizard` includes a skippable `AssociateToStep`; launch context determines pre-fill.
> **Related ADRs**: [ADR-024 Polymorphic Resolver Pattern](../../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)

---

## Purpose

`CreateTodoWizard` is reused across three surface entry points. Each entry point has a different parent-record context. This document is the **binding contract** for what each launcher MUST pass for the `initialRegarding` prop. Task 032 wires the prop end-to-end; tasks 040 and 070 consume the contract.

The wizard's `initialRegarding` prop type is `IInitialRegarding` (alias of `AssociationResult`):

```typescript
interface AssociationResult {
  entityType: string;   // Dataverse logical name (e.g., 'sprk_matter', 'sprk_communication')
  recordId: string;     // GUID (lowercased, no braces — normalised inside the wizard)
  recordName: string;   // Display name (used in the AssociateToStep selected-record card)
}
```

When `initialRegarding` is supplied, the `AssociateToStep` opens with that record pre-selected. The user may still change the selection (re-open the lookup) or clear it. If the user clears it, the wizard creates a standalone todo per FR-16.

---

## Entry-point matrix

| # | Launch context        | `initialRegarding` value                                                 | Owning task | Status        |
|---|-----------------------|--------------------------------------------------------------------------|-------------|---------------|
| 1 | Kanban "Add To Do"    | `undefined` (no pre-fill — user picks if any)                            | this task   | ✅ wired       |
| 2 | Parent-form ribbon    | `{ entityType: <parent-logical-name>, recordId, recordName }`            | task 040    | 📝 specified  |
| 3 | Outlook "Create To Do"| `{ entityType: 'sprk_communication', recordId, recordName }`             | task 070    | 📝 specified  |

---

## 1. Kanban "Add To Do" (SmartTodo Code Page)

**Where**: `src/solutions/SmartTodo/src/components/AddTodoBar.tsx` (kanban entry surface).

**Rationale**: The kanban view is a personal task list, not scoped to a parent record. There is no implicit regarding — the user explicitly chooses one inside the wizard (or skips, creating a standalone todo).

**Wiring**:

```tsx
import { CreateTodoWizard } from '@spaarke/ui-components';
import { KANBAN_WIZARD_INITIAL_REGARDING } from './AddTodoBar';

// Inside the kanban host:
<CreateTodoWizard
  open={wizardOpen}
  onClose={closeWizard}
  dataService={dataService}
  navigationService={navigationService}
  initialRegarding={KANBAN_WIZARD_INITIAL_REGARDING}  // = undefined; explicit per FR-16
  authenticatedFetch={authenticatedFetch}
  bffBaseUrl={bffBaseUrl}
  resolveSpeContainerId={resolveSpeContainerId}
/>
```

**Acceptance criteria**:
- `AssociateToStep` opens with the dropdown showing the default (`Matter`) and **no record selected**.
- The step is skippable (Skip button advances to the next step).
- Skip path → `TodoService.createTodo(form, null)` → standalone `sprk_todo` row with all 11 lookups and 4 resolver fields null.

---

## 2. Parent-form ribbon (Matter / Project / Event / Communication / WorkAssignment / Invoice / Budget / Analysis / Organization / Contact / Document)

**Where**: Dataverse ribbon `ButtonScript`, added by task 040. The button appears on each of the 11 parent entity main forms (eligible per `TODO_REGARDING_TARGETS`).

**Rationale**: When the user clicks "Add To Do" on a Matter detail page, the wizard MUST pre-fill the regarding to that Matter. The user can change to a different parent type (rare) or clear (also rare) inside the wizard, but the common path is: pre-filled → fill title/notes/dates → Finish.

**Wiring** (Phase 5 — task 040 will produce this code):

```typescript
// Ribbon ButtonScript pseudo-code (TypeScript runtime via WebResource):
async function openCreateTodoFromMatterRibbon(primaryControl: Xrm.PageInstance): Promise<void> {
  const entityType = primaryControl.data.entity.getEntityName();   // 'sprk_matter'
  const recordId   = primaryControl.data.entity.getId().replace(/[{}]/g, '').toLowerCase();
  const recordName = primaryControl.data.entity.getPrimaryAttributeValue();

  // Open the SmartTodo wizard dialog (web-resource-hosted Code Page) with launch context:
  const initialRegarding = { entityType, recordId, recordName };

  // The receiving Code Page reads initialRegarding from page query-string or
  // window.parent.* postMessage handshake, then passes it to <CreateTodoWizard>:
  //
  //   <CreateTodoWizard
  //     ...
  //     initialRegarding={initialRegarding}
  //   />
}
```

**Acceptance criteria**:
- `AssociateToStep` opens with the dropdown set to the parent entity type AND the selected-record card showing the launch record.
- The "Clear" button on the selected-record card works (user may clear and skip).
- The "Select Record" button re-opens the lookup (user may swap).
- The 11 parent types each work — covered by the all-11 regression test in `todoService.test.ts` (line 293 onward).

**Open considerations for task 040**:
- The ribbon ButtonScript MUST URL-encode any record-name characters that break query strings (esp. `&`, `#`).
- The launch should pre-resolve the BFF base URL + container ID per the host's environment (do NOT hardcode).
- If the parent entity is NOT one of the 11 `TODO_REGARDING_TARGETS`, the button MUST be hidden (ribbon `EnableRule` query).

---

## 3. Outlook add-in "Create To Do" (email-read context)

**Where**: `src/solutions/Outlook/...` (Outlook taskpane / ribbon), added by task 070.

**Rationale**: Per FR-27, the Outlook ribbon's "Create To Do" action operates on the currently-read email. If the email is not already a `sprk_communication`, the existing email-save flow runs first to create one. Then the wizard MUST pre-fill the regarding to that `sprk_communication`.

**Wiring** (Phase 8 — task 070 will produce this code):

```typescript
// Outlook taskpane TypeScript (Office.js context):
async function openCreateTodoFromOutlookRibbon(): Promise<void> {
  const communicationId = await ensureCommunicationExistsForCurrentEmail();
  // ensureCommunicationExistsForCurrentEmail: saves the email if not already saved;
  // returns the sprk_communication GUID (lowercased, no braces).

  const subject = Office.context.mailbox.item?.subject ?? 'Email';
  const initialRegarding = {
    entityType: 'sprk_communication',
    recordId: communicationId,
    recordName: subject,
  };

  // Render the wizard in the taskpane (or open as dialog):
  //   <CreateTodoWizard
  //     ...
  //     initialRegarding={initialRegarding}
  //   />
}
```

**Acceptance criteria**:
- `AssociateToStep` opens with dropdown set to `Communication` AND the selected-record card showing the email subject.
- The pre-filled `recordId` matches the GUID returned by the save-email flow (or the existing match if the email was already saved).
- Test: confirm `applyResolverFields` is invoked with `entityType === 'sprk_communication'` and the four resolver fields populated atomically (per ADR-024).

**Open considerations for task 070**:
- Office.js does not expose a true `INavigationService` — task 070 may need a no-op adapter (the user is unlikely to re-pick the regarding inside the wizard since it was just pre-filled). The wizard tolerates a no-op `openLookup` that returns `[]`.
- The `authenticatedFetch` and `bffBaseUrl` props must come from the Outlook-add-in auth context (MSAL-backed), not Xrm.

---

## Invariants (apply to all three entry points)

These are binding across launchers and are asserted by the test suite:

1. **When `initialRegarding` is `undefined`** — the wizard's `AssociateToStep` is empty and skippable. Skip → standalone todo (all 11 lookups null, all 4 resolver fields null).
2. **When `initialRegarding` is provided** — the wizard's `AssociateToStep` shows the selected-record card with the matching `recordName`, the dropdown is set to the matching `entityType`, and `context.association` (passed to `onFinish`) carries the same triple.
3. **The user is never locked in** — even when pre-filled, the user MAY clear or change the selection. The contract is "pre-fill", not "force".
4. **`applyResolverFields` is invoked exactly once on Finish** with the final triple (per ADR-024). If the user cleared the pre-fill, the create path goes through the standalone branch.
5. **No legacy `sprk_event` / `sprk_eventtodo` / `sprk_todoflag` fields** are written by any launcher (NFR-12 / OS-1).

---

## Test coverage

Located in `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/__tests__/initialRegarding.test.tsx`:

| Test                                                                  | Covers   |
|-----------------------------------------------------------------------|----------|
| `kanbanEntry_noInitialRegarding_associateStepEmptyAndSkippable`        | row 1    |
| `parentFormEntry_initialRegardingMatter_associateStepPreFilledToMatter`| row 2    |
| `outlookEntry_initialRegardingCommunication_associateStepPreFilled`    | row 3    |

The all-11-targets end-to-end is covered by `todoService.test.ts`
(`populatesAllFifteenRegardingFields_forEachOfElevenTargets`).
