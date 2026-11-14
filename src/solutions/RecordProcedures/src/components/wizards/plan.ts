/**
 * What a wizard choice does, as pure functions of the row the server returned (ebilling-r1 task 084b). A choice becomes a PLAN: the ordered
 * calls to task 089's routes. The page resolves nothing; the plan only says which override rows to remove and which to create.
 *
 * Why a plan can hold two calls: 089's assign and exclude routes refuse a second row for the same subject (409 `assignment.duplicate`: "Remove
 * that override first"), and there is no route that edits a row. Changing a record's existing override is therefore remove, then create.
 * {@link executePlan} stops at the first refusal and says how many calls had already succeeded.
 *
 * Task 084c adds "Change for this record" to the rule wizard: a `changeRule` step is the one call to `rule-overrides/change`. A rule that
 * already has this record's Exclude gets it removed first (089 keeps one row per record and rule: a change of it is 409 `assignment.duplicate`).
 * Fix round 1 (F1-1): a rule already changed on this record is changed again the same way: its change is removed, then the BASE rule (the
 * `Changed` mark's `basePolicyId`) is changed.
 */

import type { OverrideView, ProcedureItem, RuleItem, RuleMark } from '../../services/recordProceduresApi';
import type { AssignMode, OverridesApi, WriteFailure } from '../../services/overridesApi';
import type { CoreRoute } from '../../services/recordProceduresApi';
import type { AllowedSteps } from '../../services/model';
import { formFor } from './ruleForms';

export type PlanStep =
  | { kind: 'remove'; overrideId: string }
  | { kind: 'assign'; procedureId: string; mode: AssignMode; pinnedVersionId?: string; reason?: string }
  | { kind: 'excludeRule'; policyId: string; reason: string; firmId?: string }
  /** `ruleBody` is the whole new body of the rule's type, as JSON text (the route's request member). */
  | { kind: 'changeRule'; policyId: string; reason: string; firmId?: string; ruleBody: string };

export interface Plan {
  steps: PlanStep[];
}

export const NO_CHANGE: Plan = { steps: [] };

const same = (a: string | null | undefined, b: string | null | undefined): boolean => (a ?? '').trim() === (b ?? '').trim();

// ─────────────────────────────────────────────────────────────────────────────
// Procedures
// ─────────────────────────────────────────────────────────────────────────────

/** `parent`: As set on the parent. `follow`: Follow latest. `pin`: Pin a version. `exclude`: Exclude here. `remove`: Remove from this record. */
export type ProcedureChoice = 'parent' | 'follow' | 'pin' | 'exclude' | 'remove';

/** This record's own override row of the Procedure (distance 0), when it has one. */
export const ownOverride = (p: ProcedureItem): OverrideView | undefined => p.overrides.find(o => o.distance === 0);

/** The rows set on the records this one is filed under (distance above 0). */
export const parentOverrides = (p: ProcedureItem): OverrideView[] => p.overrides.filter(o => o.distance > 0);

/** `ProcedureAppliesTo.AssignedRecordsOnly` (ProcedureService.cs): the server sends the enum member name, `procedure.AppliesTo.ToString()`. */
export const APPLIES_TO_ASSIGNED_ONLY = 'AssignedRecordsOnly';

/** A Procedure that reaches a record only through an assignment row (its `AppliesTo` is AssignedRecordsOnly). */
const isManual = (p: ProcedureItem): boolean => p.appliesTo === APPLIES_TO_ASSIGNED_ONLY;

/** What the record does now, in the wizard's own choices (the prototype's `currentOf`). */
export function currentProcedureChoice(p: ProcedureItem): ProcedureChoice {
  const own = ownOverride(p);
  if (own) return own.mode === 'Pinned' ? 'pin' : own.mode === 'Excluded' ? 'exclude' : 'follow';
  return parentOverrides(p).length > 0 ? 'parent' : 'follow';
}

/** Which choices the wizard offers for this row. Every one of them has a route; none is a Change. */
export function procedureChoices(p: ProcedureItem): ProcedureChoice[] {
  const own = ownOverride(p);
  const parents = parentOverrides(p);
  const out: ProcedureChoice[] = [];
  if (parents.length > 0) out.push('parent');
  out.push('follow', 'pin');
  // Excluding a Procedure only this record's own assignment brings in is the same as removing it.
  if ((!isManual(p) || p.sources.some(s => s.kind !== 'ManualOwn')) || own?.mode === 'Excluded') out.push('exclude');
  if (isManual(p) && own && parents.length === 0) out.push('remove');
  return out;
}

/** The server's limit on a reason, after trimming (`MaxReasonChars` in ProcedureAssignmentService.cs; `CheckReason` answers 400 above it). */
export const MAX_REASON_CHARS = 2000;

/** A reason the route accepts: not blank, and within the server's limit. */
export const reasonValid = (reason: string | undefined): boolean => {
  const n = (reason ?? '').trim().length;
  return n > 0 && n <= MAX_REASON_CHARS;
};

export interface ProcedureInput {
  versionId?: string;
  reason?: string;
  /**
   * The ids of the versions a pin accepts (Active or Superseded, per task 081's read). A pin is planned only for one of these: the plan
   * removes this record's own row first, and a POST the server will certainly refuse (a Retired version) would leave the pin lost.
   * Absent means the versions are not known yet, so no pin can be planned.
   */
  pinnable?: readonly string[];
}

const pinnableVersion = (input: ProcedureInput): boolean =>
  !!input.versionId && !!input.pinnable?.some(id => id.toLowerCase() === input.versionId!.toLowerCase());

/** Whether the choice's own inputs are complete (a pin names a pinnable version and a reason, an exclude a reason; reasons within the limit). */
export function procedureInputValid(choice: ProcedureChoice, input: ProcedureInput): boolean {
  if (choice === 'pin') return pinnableVersion(input) && reasonValid(input.reason);
  if (choice === 'exclude') return reasonValid(input.reason);
  return true;
}

/**
 * The calls for one choice. An empty plan means the choice changes nothing (the wizard then does not let the user go on).
 * Follow latest keeps a row only where the row does work: a Procedure assigned only here, or one that overrides a parent's pin or exclusion
 * (the prototype's `setProcedureMode`); anywhere else the record returns to how its source applies the Procedure, which is the row's removal.
 */
export function planProcedure(p: ProcedureItem, choice: ProcedureChoice, input: ProcedureInput): Plan {
  const own = ownOverride(p);
  const parents = parentOverrides(p);
  const removeOwn: PlanStep[] = own ? [{ kind: 'remove', overrideId: own.rowId }] : [];

  switch (choice) {
    case 'parent':
    case 'remove':
      return { steps: removeOwn };

    case 'follow': {
      const keepsRow = (isManual(p) && !!own && parents.length === 0) || parents.some(o => o.mode !== 'FollowLatest');
      if (!keepsRow) return { steps: removeOwn };
      if (own?.mode === 'FollowLatest') return NO_CHANGE;
      return { steps: [...removeOwn, { kind: 'assign', procedureId: p.id, mode: 'FollowLatest' }] };
    }

    case 'pin': {
      if (!procedureInputValid('pin', input)) return NO_CHANGE;
      if (own?.mode === 'Pinned' && own.pinnedVersion?.id === input.versionId && same(own.reason, input.reason)) return NO_CHANGE;
      return { steps: [...removeOwn, { kind: 'assign', procedureId: p.id, mode: 'Pinned', pinnedVersionId: input.versionId, reason: input.reason!.trim() }] };
    }

    case 'exclude': {
      if (!procedureInputValid('exclude', input)) return NO_CHANGE;
      if (own?.mode === 'Excluded' && same(own.reason, input.reason)) return NO_CHANGE;
      return { steps: [...removeOwn, { kind: 'assign', procedureId: p.id, mode: 'Excluded', reason: input.reason!.trim() }] };
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Assign a Procedure (task 084d-5b)
// ─────────────────────────────────────────────────────────────────────────────

/** "On this record" for a Procedure not yet on it: `follow` (Follow latest) or `pin` (Pin a version). */
export type AssignChoice = 'follow' | 'pin';

/**
 * The one call that assigns a Procedure the record does not have yet: Follow latest (`{ procedureId, mode }`), or Pin a version with a
 * reason (`{ procedureId, mode, pinnedVersionId, reason }`), whose version must be one the route accepts (`input.pinnable`). Nothing to remove:
 * the assignable list never offers a Procedure the record already has a row for. An incomplete pin plans nothing.
 */
export function planAssign(procedureId: string | null | undefined, choice: AssignChoice, input: ProcedureInput): Plan {
  if (!procedureId) return NO_CHANGE;
  if (choice === 'follow') return { steps: [{ kind: 'assign', procedureId, mode: 'FollowLatest' }] };
  if (!procedureInputValid('pin', input)) return NO_CHANGE;
  return { steps: [{ kind: 'assign', procedureId, mode: 'Pinned', pinnedVersionId: input.versionId, reason: input.reason!.trim() }] };
}

// ─────────────────────────────────────────────────────────────────────────────
// Rules
// ─────────────────────────────────────────────────────────────────────────────

/**
 * `parent`: As set on the parent. `keep`: Run the published rule. `exclude`: Exclude here. `change`: Change for this record (task 084c).
 * `changed`: the state of a rule changed on this record (what it does now); it is no choice the wizard offers, so a wizard on such a rule
 * opens with nothing selected.
 */
export type RuleChoice = 'parent' | 'keep' | 'exclude' | 'change' | 'changed';

const distanceOf = (m: RuleMark): number => m.distance ?? 0;
const markOf = (r: RuleItem, kind: string, own: boolean): RuleMark | undefined => r.marks.find(m => m.kind === kind && (distanceOf(m) === 0) === own);

/** This record's own Exclude of the rule, and this record's own Change of it (an override row of this record), when there are some. */
export const ownExclude = (r: RuleItem): RuleMark | undefined => markOf(r, 'Excluded', true);
export const ownChange = (r: RuleItem): RuleMark | undefined => markOf(r, 'Changed', true);

/** True when a record this one is filed under excludes or changes the rule (P-3). */
export const inheritedOverride = (r: RuleItem): RuleMark | undefined => markOf(r, 'Excluded', false) ?? markOf(r, 'Changed', false);

export const isBaseRetired = (r: RuleItem): boolean => r.marks.some(m => m.kind === 'BaseRetired');

/** The row id of the override that removing "the published rule's" exception means: this record's own Exclude, else its own Change. */
export function ownRuleOverrideId(r: RuleItem): string | null {
  return ownExclude(r)?.overrideRowId ?? ownChange(r)?.overrideRowId ?? null;
}

export function currentRuleChoice(r: RuleItem): RuleChoice {
  if (ownExclude(r)) return 'exclude';
  if (inheritedOverride(r)) return 'parent';
  // A rule changed on this record does not run as published, so "Run the published rule" is not the current state (it removes the change).
  if (ownChange(r)) return 'changed';
  return 'keep';
}

export interface RuleInput {
  reason?: string;
  /** A firm id, or '' / undefined for every firm. */
  firmId?: string;
  /** Change only: the new body as JSON text, once the form is complete and differs from the published values. Absent: nothing to send. */
  ruleBody?: string;
}

/**
 * Whether "Run the published rule" can be chosen, and why not. P-3: a rule a parent excluded (or changed) cannot be re-enabled on a child.
 * A rule changed on THIS record is removed through its override row (the same removal route); a row with no override row to name (a retired
 * base rule's record rule) has nothing to remove here.
 */
export function keepBlocker(r: RuleItem): string | null {
  const inherited = inheritedOverride(r);
  if (inherited) {
    return `Not available: the parent's rule ${inherited.kind === 'Excluded' ? 'exclusion' : 'change'} applies to this record. Change it on the parent.`;
  }
  if (isBaseRetired(r) && !ownRuleOverrideId(r)) return 'The base rule is retired, so there is nothing published to run. No override row is available here; see 084c.';
  return null;
}

/** Whether "Exclude here" can be chosen, and why not. A rule changed on this record is excluded only after its change is removed. */
export function excludeBlocker(r: RuleItem): string | null {
  // A change made on a parent cannot be undone here (P-3), so "Run the published rule first" would be a dead end: the way out is on the parent.
  if (!ownChange(r) && markOf(r, 'Changed', false)) return 'Changed on the parent; change it there.';
  if (ownChange(r) || r.recordRule) return 'This rule is changed on this record. Run the published rule first, then exclude it.';
  return null;
}

/** Why a rule whose base rule is retired offers no Change (084c fix round 1, F1-7): checked before the type, which such a row does not carry. */
export const BASE_RETIRED_NO_CHANGE = 'Not available: the published rule is no longer in force, so there is nothing to change. Remove the change instead.';

/** Why a rule changed here cannot be changed again: the read did not name the published rule, or the change's own row (an older BFF). */
export const RECHANGE_NOT_NAMED = 'This rule is already changed on this record, and the published rule it replaces is not named here. Run the published rule first, then change it again.';

/**
 * Whether "Change for this record" can be chosen, and why not (task 084c):
 *   - a record rule whose base rule is retired has nothing published to change (084c fix round 1, F1-7: said before the type, which it lacks);
 *   - a type this page has no form for, or whose form says it is not changed (a rate card rule: pin the Procedure version; a type with no
 *     values), offers no Change;
 *   - P-3: a rule a parent excluded or changed is changed on the parent;
 *   - a rule already changed on this record (084c fix round 1, F1-1) is changed again by removing its change and changing the BASE rule, so
 *     its `Changed` mark must name the base rule (`basePolicyId`) and its own row (`overrideRowId`).
 */
export function changeBlocker(r: RuleItem): string | null {
  if (isBaseRetired(r)) return BASE_RETIRED_NO_CHANGE;
  const form = formFor(r.ruleType);
  if (!form) return `Not available: this page has no form for the rule type ${r.ruleType ?? '(unknown)'}.`;
  if (form.noChange) return form.noChange;
  const inherited = inheritedOverride(r);
  if (inherited) return `Not available: the parent's rule ${inherited.kind === 'Excluded' ? 'exclusion' : 'change'} applies to this record. Change it on the parent.`;
  if (ownChange(r) || r.recordRule) {
    const own = ownChange(r);
    if (!own?.basePolicyId || !own.overrideRowId) return RECHANGE_NOT_NAMED;
  }
  return null;
}

/** The policy a change names: the base rule for a rule changed here (084c fix round 1, F1-1), else the rule itself. */
export const changeTargetPolicyId = (r: RuleItem): string => ownChange(r)?.basePolicyId ?? r.policyId;

/** O-20: a rule that names one firm can only be excluded (or changed) for that firm. */
export const fixedFirmId = (r: RuleItem): string | null => r.firmScope?.id ?? null;

export function ruleInputValid(choice: RuleChoice, input: RuleInput): boolean {
  return choice !== 'exclude' || reasonValid(input.reason);
}

export function planRule(r: RuleItem, choice: RuleChoice, input: RuleInput): Plan {
  const own = ownExclude(r);
  switch (choice) {
    case 'parent':
    case 'changed':
      return NO_CHANGE;

    case 'keep': {
      if (keepBlocker(r)) return NO_CHANGE;
      const id = ownRuleOverrideId(r);
      return id ? { steps: [{ kind: 'remove', overrideId: id }] } : NO_CHANGE;
    }

    case 'change': {
      if (changeBlocker(r) || !reasonValid(input.reason) || !input.ruleBody) return NO_CHANGE;
      const firmId = fixedFirmId(r) ?? (input.firmId || undefined);
      // This record's own override of the rule (its Exclude, or its earlier change) is removed first: 089 keeps one row per record and rule,
      // so a change over it is refused as a duplicate. An own Exclude whose row id the server did not name cannot be removed.
      if (own && !own.overrideRowId) return NO_CHANGE;
      const ownId = ownRuleOverrideId(r);
      const removeOwn: PlanStep[] = ownId ? [{ kind: 'remove', overrideId: ownId }] : [];
      return { steps: [...removeOwn, { kind: 'changeRule', policyId: changeTargetPolicyId(r), reason: input.reason!.trim(), firmId, ruleBody: input.ruleBody }] };
    }

    case 'exclude': {
      if (excludeBlocker(r) || !ruleInputValid('exclude', input)) return NO_CHANGE;
      const firmId = fixedFirmId(r) ?? (input.firmId || undefined);
      if (own && same(own.reason, input.reason) && (own.firm?.id ?? '').toLowerCase() === (firmId ?? '').toLowerCase()) return NO_CHANGE;
      const removeOwn: PlanStep[] = own?.overrideRowId ? [{ kind: 'remove', overrideId: own.overrideRowId }] : [];
      // An own Exclude whose row id the server did not name cannot be replaced: the create would be refused as a duplicate.
      if (own && !own.overrideRowId) return NO_CHANGE;
      return { steps: [...removeOwn, { kind: 'excludeRule', policyId: r.policyId, reason: input.reason!.trim(), firmId }] };
    }
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Which kinds of call a choice needs (task 084g fix round 1, review B1)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The kinds of call a choice's plan can contain, from the row and the choice alone (never from the inputs): `remove` (the removal of this
 * record's own override, and its preview), `create` (an assignment or a rule exclusion) and `change` (a rule's change, task 084c). Every step of every plan the choice can make is
 * one of these (`planWithin` checks it; a test runs it over every fixture row, choice and input). The panel says which kinds the lists'
 * answers allow (`AllowedSteps`); a choice that needs a kind they do not allow is disabled with the reason, and only a wizard with no
 * choice left that makes a call is read-only.
 */
export interface ChoiceNeeds {
  remove: boolean;
  create: boolean;
  change: boolean;
}

const NEEDS_NOTHING: ChoiceNeeds = { remove: false, create: false, change: false };

/** What a Procedure choice's plans need: a removal exactly when the record has its own override row, a create when a row is saved. */
export function procedureChoiceNeeds(p: ProcedureItem, choice: ProcedureChoice): ChoiceNeeds {
  const own = ownOverride(p);
  const parents = parentOverrides(p);
  switch (choice) {
    case 'parent':
    case 'remove':
      return { remove: !!own, create: false, change: false };
    case 'follow': {
      const keepsRow = (isManual(p) && !!own && parents.length === 0) || parents.some(o => o.mode !== 'FollowLatest');
      if (!keepsRow) return { remove: !!own, create: false, change: false };
      if (own?.mode === 'FollowLatest') return NEEDS_NOTHING;
      return { remove: !!own, create: true, change: false };
    }
    case 'pin':
    case 'exclude':
      return { remove: !!own, create: true, change: false };
  }
}

/** What a rule choice's plans need. A choice its own blocker refuses plans nothing, so it needs nothing. */
export function ruleChoiceNeeds(r: RuleItem, choice: RuleChoice): ChoiceNeeds {
  switch (choice) {
    case 'parent':
    case 'changed':
      return NEEDS_NOTHING;
    case 'keep':
      return keepBlocker(r) ? NEEDS_NOTHING : { remove: !!ownRuleOverrideId(r), create: false, change: false };
    case 'exclude': {
      if (excludeBlocker(r)) return NEEDS_NOTHING;
      const own = ownExclude(r);
      if (own && !own.overrideRowId) return NEEDS_NOTHING;
      return { remove: !!own?.overrideRowId, create: true, change: false };
    }
    case 'change': {
      // A change saves no exclusion of its own: it is its own route and its own right. It removes this record's own override of the rule
      // first, its Exclude or its earlier change (084c fix round 1).
      if (changeBlocker(r)) return NEEDS_NOTHING;
      const own = ownExclude(r);
      if (own && !own.overrideRowId) return NEEDS_NOTHING;
      return { remove: !!ownRuleOverrideId(r), create: false, change: true };
    }
  }
}

/** Whether a choice makes any call at all. */
export const needsAny = (needs: ChoiceNeeds): boolean => needs.remove || needs.create || needs.change;

/**
 * Whether a wizard is read-only: some offered choice makes a call, and none of those is allowed. A row none of whose choices makes a call
 * (each refused by its own blocker, e.g. a rule changed on the parent) is NOT read-only: there is nothing the answers deny, and the choices
 * give their own reasons (084g fix round 1 re-check, R1).
 */
export function noAllowedCall<T>(offered: readonly T[], needsOf: (choice: T) => ChoiceNeeds, blockerOf: (choice: T) => string | null): boolean {
  const making = offered.filter(c => needsAny(needsOf(c)));
  return making.length > 0 && !making.some(c => blockerOf(c) === null);
}

export const NOT_ALLOWED_BOTH = 'You can view this, but not change it.';
export const NOT_ALLOWED_REMOVE = "Not available to you: it removes this record's own override, which you cannot do here.";
export const NOT_ALLOWED_CREATE = 'Not available to you: you cannot save an override on this record.';
export const NOT_ALLOWED_CHANGE = 'Not available to you: you cannot change a rule on this record.';

/** Why a choice is not available from what the answers allow, or null when it is. */
export function allowedBlocker(needs: ChoiceNeeds, allowed: AllowedSteps): string | null {
  const noRemove = needs.remove && !allowed.remove;
  const noCreate = needs.create && !allowed.create;
  const noChange = needs.change === true && !allowed.change;
  if ([noRemove, noCreate, noChange].filter(Boolean).length > 1) return NOT_ALLOWED_BOTH;
  if (noRemove) return NOT_ALLOWED_REMOVE;
  if (noCreate) return NOT_ALLOWED_CREATE;
  if (noChange) return NOT_ALLOWED_CHANGE;
  return null;
}

/** True when every step of the plan is of a kind the choice was said to need. */
export function planWithin(plan: Plan, needs: ChoiceNeeds): boolean {
  return plan.steps.every(s => (s.kind === 'remove' ? needs.remove : s.kind === 'changeRule' ? needs.change === true : needs.create));
}

// ─────────────────────────────────────────────────────────────────────────────
// Running a plan
// ─────────────────────────────────────────────────────────────────────────────

export interface PlanRun {
  ok: boolean;
  /** How many calls succeeded before the run stopped (all of them when `ok`). */
  completed: number;
  /** The refusal that stopped the run. */
  failure?: WriteFailure;
  /** The `Removed` answers, for the success screen. */
  removed: Array<{ ruleSwitchedOff: boolean; closeComplete: boolean; affectedText: string | null; message: string | null }>;
  /** The `RuleChanged` answers (task 084c): what the close of the base rule's findings did, for the success screen. */
  changed: Array<{ closeComplete: boolean; affectedText: string | null; message: string | null }>;
}

/** Runs the plan's calls in order and stops at the first refusal. */
export async function executePlan(api: OverridesApi, core: CoreRoute, recordId: string, plan: Plan): Promise<PlanRun> {
  const run: PlanRun = { ok: true, completed: 0, removed: [], changed: [] };
  for (const step of plan.steps) {
    const result =
      step.kind === 'remove'
        ? await api.removeOverride(core, recordId, step.overrideId)
        : step.kind === 'assign'
          ? await api.assignProcedure(core, recordId, { procedureId: step.procedureId, mode: step.mode, pinnedVersionId: step.pinnedVersionId, reason: step.reason })
          : step.kind === 'changeRule'
            ? await api.changeRule(core, recordId, { policyId: step.policyId, reason: step.reason, firmId: step.firmId, ruleBody: step.ruleBody })
            : await api.excludeRule(core, recordId, { policyId: step.policyId, reason: step.reason, firmId: step.firmId });
    if (!result.ok) return { ...run, ok: false, failure: result.failure };
    if (step.kind === 'remove') run.removed.push(result.value as PlanRun['removed'][number]);
    if (step.kind === 'changeRule') run.changed.push(result.value as PlanRun['changed'][number]);
    run.completed += 1;
  }
  return run;
}

/** The override a plan removes first, whose preview the Impact step shows (null when the plan removes nothing). */
export const removalOf = (plan: Plan): string | null => {
  const step = plan.steps.find(s => s.kind === 'remove');
  return step && step.kind === 'remove' ? step.overrideId : null;
};
