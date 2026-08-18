/**
 * newTaskLauncher — "+ New Task" OOB create-form wiring (spec FR-10, task 030;
 * host-record auto-associate added by UAT defer-issue D-8, 2026-08-17).
 *
 * Extracted from `SmartTodoApp.tsx` into its own service module (mirrors the
 * existing `queryHelpers.ts` / `TodoRegardingUpdateBuilder.ts` service-layer
 * convention) so it can be unit-tested WITHOUT pulling `SmartTodoApp.tsx`'s
 * full import graph (Header, SmartToDo, SearchFilter, Toolbar, TodoContext,
 * `@spaarke/auth`, ...) into the test module graph.
 *
 * ADR-050 Path A exception (per spec.md ADR Tensions + CLAUDE.md §6.5): this
 * reuses the OOB `Xrm.Navigation.navigateTo` main-form CREATE surface via the
 * existing `navigateToEntityRecordSurfaceAsync()` launcher
 * (`@spaarke/ui-components` WorkspaceShell/wizardLaunchers.ts) — NOT a
 * proprietary `SprkModal`/`FormModal` config. `SprkModal` does not govern OOB
 * `navigateTo` dialogs (see `docs/standards/MODAL-DECISION-CRITERIA.md`).
 *
 * ── Regarding pre-association (D-8) ──────────────────────────────────────────
 * When the Smart To Do surface is embedded in a record context (e.g. a Matter
 * form) and the user clicks "+ New Task", the new To Do MUST open with that
 * host record pre-associated as its regarding, so the RegardingResolver control
 * on the To Do form completes the 5 denormalized resolver fields on load.
 *
 * Two regarding SOURCES, host-record preferred:
 *   1. **Host record** (`resolveHostRegardingRecord`) — reads the hosting form's
 *      current record via the shell `Xrm.Page.data.entity.getEntityReference()`
 *      (frame-walked through `getXrm`). Present when the Code Page is embedded
 *      as a web resource on a record form OR opened as a dialog over one.
 *   2. **Launch context** (`resolveRegardingSource`) — the existing URL-param
 *      pre-seed (`openTodos.regardingFilter` from VisualHost drill-through /
 *      `createTodo.initialRegarding`). Kept working as an additional source.
 * A source only qualifies when its entity type is one of the 12 canonical
 * `sprk_todo` regarding targets (`TODO_REGARDING_CATALOG`).
 *
 * Two DELIVERY mechanisms, both applied (belt-and-suspenders, per the
 * 2026-08-17 MS-Learn researcher finding):
 *   • `defaultValues` three-key lookup convention — the DETERMINISTIC primary.
 *     `{lookup}` = GUID, `{lookup}name` = display name, `{lookup}type` = target
 *     logical name (MS Learn "Set column values using parameters passed to a
 *     form"). Directly names `sprk_regardingmatter` (etc.), which is a
 *     single-target lookup → documented-supported ground. All 12 catalog
 *     lookups are present on the To Do main form (hidden cells, task 013) so
 *     `getAttribute(lookup)` on the form resolves them.
 *   • `createFromEntity` — the relationship-attribute-mapping "create from
 *     parent" seam (same mechanism as a subgrid "+ New"). Secondary; fills
 *     nothing when no mapping is configured, hence paired with the direct
 *     `defaultValues` shape above.
 *
 * The 4th resolver field `sprk_regardingrecordtype` (a lookup to
 * `sprk_recordtype_ref`, keyed by entity-type NAME not the record's GUID) is
 * deliberately NOT pre-seeded here — the RegardingResolver control resolves it
 * on the form. This pre-seed gives the form a head start; it does not need to
 * be complete.
 *
 * ⚠ LIVE-VERIFICATION REQUIRED (no live Dataverse in this session — see
 * `projects/smart-todo-r5/notes/uat-newtask-autoassociate.md`):
 *   (a) that the three-key `data` convention pre-fills `sprk_regardingmatter`
 *       on the real deployed To Do main form (documented-supported, unverified
 *       against the live form); and
 *   (b) that `Xrm.Page.data.entity.getEntityReference()` yields the true host
 *       record in the ACTUAL embed configuration (web-resource-on-form vs
 *       dialog-over-form) with no stale-context false positive on standalone
 *       full-page loads.
 * The plain-create fallback (no defaultValues, no createFromEntity) is
 * unconditional, so a mis-fire degrades to today's behavior, never an error.
 *
 * @see projects/smart-todo-r5/tasks/030-new-task-oob-mainform-modal.poml
 * @see projects/smart-todo-r5/notes/uat-newtask-autoassociate.md
 */

import {
  navigateToEntityRecordSurfaceAsync,
  TODO_REGARDING_CATALOG,
} from '@spaarke/ui-components';
import type { ILaunchContext } from '../hooks/useLaunchContext';
import { getXrm } from './xrmProvider';

/** Entity logical name for the sprk_todo OOB create form (spec FR-10). */
const TODO_ENTITY_NAME = 'sprk_todo';

/** Dialog title passed to `navigateToEntityRecordSurfaceAsync`. */
const NEW_TASK_DIALOG_TITLE = 'New To Do';

/**
 * Narrow shape shared by all regarding branches this module reads
 * (`openTodos`'s `regardingFilter`, `createTodo`'s `initialRegarding`, and the
 * host-record read) — all carry `{entityType, recordId, recordName?}`.
 */
interface IRegardingSource {
  entityType: string;
  recordId: string;
  recordName?: string;
}

/**
 * The current user's CONTACT (the `sprk_todo.sprk_assignedto` lookup targets the
 * OOB `contact` table, resolved from systemuser via `contact.sprk_systemuser` —
 * see `useCurrentContactId`). Passed by the caller so a new To Do opens with
 * "Assigned To" pre-filled to the current user (smart-todo-r5 UAT 2026-08-17,
 * item #1 — client default at form launch; NOT the Field Mapping Framework,
 * which is for wizard creates that inherit from a parent record).
 */
export interface INewTaskAssignee {
  /** The current user's `contactid` GUID (bare, no braces). */
  contactId: string;
  /** The contact's `fullname`, for the form's lookup display text. */
  contactName?: string;
}

/**
 * Merge the current-user "Assigned To" default into a `defaultValues` map using
 * the same three-key lookup convention the regarding pre-seed uses (MS Learn —
 * "Set column values using parameters passed to a form"): `{lookup}` = id,
 * `{lookup}name` = display, `{lookup}type` = target logical name. The
 * `sprk_assignedto` lookup targets `contact`. Uses the LOWERCASE logical name
 * (the three-key form convention), NOT the PascalCase `sprk_AssignedTo`
 * navigation property required by the Web API `@odata.bind` create path.
 * Creates the map if the regarding pre-seed produced none.
 */
function applyAssigneeDefault(
  defaultValues: Record<string, unknown> | undefined,
  assignee: INewTaskAssignee | undefined,
): Record<string, unknown> | undefined {
  if (!assignee?.contactId) return defaultValues;
  const next = defaultValues ? { ...defaultValues } : {};
  next['sprk_assignedto'] = assignee.contactId;
  next['sprk_assignedtotype'] = 'contact';
  if (assignee.contactName) {
    next['sprk_assignedtoname'] = assignee.contactName;
  }
  return next;
}

/** `true` when `entityType` is one of the 12 canonical sprk_todo regarding targets. */
function isSupportedRegardingTarget(entityType: string | undefined): boolean {
  return !!entityType && TODO_REGARDING_CATALOG.some((c) => c.entityType === entityType);
}

/**
 * Resolve the regarding context the Code Page was LAUNCHED with, preferring
 * the `openTodos` branch's `regardingFilter` (an ACTIVE Kanban filter — the
 * more likely scenario when the user is mid-session and clicks "+ New Task")
 * over the `createTodo` branch's `initialRegarding` (present only on the very
 * first render, before `LaunchCreateTodoWizardHost` consumes it for the
 * Outlook/parent-ribbon wizard flow this module does NOT touch).
 */
function resolveRegardingSource(
  launchContext: ILaunchContext | undefined,
): IRegardingSource | undefined {
  if (launchContext?.action === 'openTodos') {
    return launchContext.regardingFilter;
  }
  if (launchContext?.action === 'createTodo') {
    return launchContext.initialRegarding;
  }
  return undefined;
}

/**
 * Read the HOST record the Smart To Do surface is embedded in / opened over,
 * via the shell form context (`Xrm.Page.data.entity.getEntityReference()`,
 * frame-walked through `getXrm`).
 *
 * Returns `{entityType, recordId, recordName}` for a SAVED host record, or
 * `undefined` when there is no reachable form context (standalone full-page
 * load, Vite dev, jsdom) or the host record has no id yet. Entity-type gating
 * to the 12 catalog targets happens centrally in `resolvePreferredRegarding`
 * (this function returns the raw host reference so the decision stays in one
 * place). All paths defensive — never throws.
 *
 * ⚠ `Xrm.Page` is shell-global; a mis-configured standalone launch could in
 * principle surface a stale prior form here. Callers gate to catalog targets
 * and this remains an ADDITIONAL, preferred-when-present source — the operator
 * must live-verify the embed configuration (see module doc + the D-8 note).
 */
export function resolveHostRegardingRecord(): IRegardingSource | undefined {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = getXrm() as any;
    const entity = xrm?.Page?.data?.entity;
    if (!entity || typeof entity.getEntityReference !== 'function') {
      return undefined;
    }
    const ref = entity.getEntityReference();
    if (!ref) return undefined;
    const id = typeof ref.id === 'string' ? ref.id.replace(/[{}]/g, '') : '';
    const entityType: string = ref.entityType ?? ref.logicalName ?? '';
    if (!id || !entityType) return undefined;
    return {
      entityType,
      recordId: id,
      recordName: typeof ref.name === 'string' && ref.name.length > 0 ? ref.name : undefined,
    };
  } catch {
    // Non-host environment or Xrm getter threw — no host regarding.
    return undefined;
  }
}

/**
 * Central regarding resolution: HOST record (preferred) then LAUNCH context,
 * each admitted only when its entity type is a supported catalog target.
 */
function resolvePreferredRegarding(
  launchContext: ILaunchContext | undefined,
): IRegardingSource | undefined {
  const host = resolveHostRegardingRecord();
  if (host && isSupportedRegardingTarget(host.entityType)) {
    return host;
  }
  const fromLaunch = resolveRegardingSource(launchContext);
  if (fromLaunch && isSupportedRegardingTarget(fromLaunch.entityType)) {
    return fromLaunch;
  }
  return undefined;
}

/**
 * Build the flat `data` pre-seed dictionary for the sprk_todo OOB create form
 * from a resolved regarding source. Uses the MS-Learn three-key lookup
 * convention for the entity-specific lookup PLUS the two plain-text resolver
 * fields. `sprk_regardingrecordtype` is intentionally omitted (see module doc).
 */
function buildDefaultValuesFromRegarding(regarding: IRegardingSource): Record<string, unknown> {
  const catalogEntry = TODO_REGARDING_CATALOG.find((c) => c.entityType === regarding.entityType)!;
  const lookup = catalogEntry.lookupAttribute;

  const defaultValues: Record<string, unknown> = {
    // Three-key lookup convention: id / name / type (MS Learn — "Set column
    // values using parameters passed to a form"). `type` is optional/harmless
    // for a single-target lookup and covers the polymorphic doc gray-zone.
    [lookup]: regarding.recordId,
    [`${lookup}type`]: regarding.entityType,
    // Plain-text resolver fields (ADR-024) — pre-seeded directly as strings.
    sprk_regardingrecordid: regarding.recordId,
  };
  if (regarding.recordName) {
    defaultValues[`${lookup}name`] = regarding.recordName;
    defaultValues.sprk_regardingrecordname = regarding.recordName;
  }
  return defaultValues;
}

/**
 * Build the `defaultValues` pre-seed map for the sprk_todo OOB create form,
 * best-effort, from the preferred regarding source (host record ▸ launch
 * context). Returns `undefined` when neither source yields a supported
 * regarding target — the caller falls back to a plain create.
 */
export function buildNewTaskDefaultValues(
  launchContext: ILaunchContext | undefined,
  assignee?: INewTaskAssignee,
): Record<string, unknown> | undefined {
  const regarding = resolvePreferredRegarding(launchContext);
  const base = regarding ? buildDefaultValuesFromRegarding(regarding) : undefined;
  return applyAssigneeDefault(base, assignee);
}

/**
 * Open the sprk_todo OOB main form in CREATE mode as a modal (spec FR-10),
 * pre-associated to the host record / launch-context regarding when one is
 * present (D-8), and invoke `onSaved` when the user actually saves (never on
 * cancel/dismiss).
 *
 * REUSES `navigateToEntityRecordSurfaceAsync` (per CLAUDE.md §11 / the task's
 * "MUST reuse" constraint) — no second, parallel `Xrm.Navigation.navigateTo`
 * call site. Passes BOTH the direct `defaultValues` three-key lookup shape AND
 * `createFromEntity` (belt-and-suspenders — see module doc).
 */
export async function launchNewTaskCreateForm(
  launchContext: ILaunchContext | undefined,
  onSaved: () => void,
  assignee?: INewTaskAssignee,
): Promise<void> {
  const regarding = resolvePreferredRegarding(launchContext);
  const defaultValues = applyAssigneeDefault(
    regarding ? buildDefaultValuesFromRegarding(regarding) : undefined,
    assignee,
  );
  const createFromEntity = regarding
    ? { entityType: regarding.entityType, id: regarding.recordId, name: regarding.recordName }
    : undefined;

  const outcome = await navigateToEntityRecordSurfaceAsync({
    entityName: TODO_ENTITY_NAME,
    title: NEW_TASK_DIALOG_TITLE,
    defaultValues,
    createFromEntity,
  });
  if (outcome.savedEntityReference) {
    onSaved();
  }
}
