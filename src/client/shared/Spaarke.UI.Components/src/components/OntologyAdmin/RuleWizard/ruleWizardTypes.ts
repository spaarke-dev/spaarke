/**
 * ruleWizardTypes - what opens the rule wizard (spaarke-ontology-platform-r1 task 102).
 *
 * A new rule starts empty. A NEW VERSION starts from the rule's page: its identity is fixed, and its wording, plan and matter scope
 * (task 102b) are carried over as a starting point. The CONDITION is not carried over: the BFF returns no raw rule body (owner decision D-149), so the admin
 * rebuilds the condition from the catalog.
 */

import type { ILookupItem } from '../../../types/LookupTypes';
import type { RuleDetail } from '../Rules/rulesService';
import type { AuthoringCatalog } from './authoringService';
import { emptyDraft, QUIET_WINDOW_LANE } from './ruleDraft';
import type { RuleDraft } from './ruleDraft';

export interface NewRuleMode {
  kind: 'new';
}

export interface NewVersionMode {
  kind: 'version';
  policyId: string;
  code: string;
  name: string;
  lane: string;
  /** The version in force, if any. */
  versionNumber: number | null;
  /** Whether the rule is On. A version of an Off rule is published but nothing is re-evaluated until it is turned On. */
  enabled: boolean;
  /** The current version's wording, offered as the starting point of the new one. */
  wording: { headline: string; messageTemplate: string; proposedAction: string };
  /** The current version's plan, by catalog code. */
  actions: string[];
  nextSteps: string[];
  /** Set when the rule was created; a version does not change them. Shown read-only. */
  priority: number | null;
  countsTowardSuppression: boolean;
  quietWindowDays: number;
  /**
   * The scope the rule has now. `matter` is for display (the matter's name, or its id when the name is not readable); `matterId` is
   * what a new version starts with and can change (task 102b, D-165).
   */
  scope: { tenant: string | null; matter: string | null; matterId: string | null };
}

export type RuleWizardMode = NewRuleMode | NewVersionMode;

/**
 * Task 102b (D-165): how the host lets the admin limit a rule to one matter. The host names the subject a matter limit belongs to and
 * searches matters by name AS THE CALLER (the Console: the shared `searchLookupTarget`); the wizard names no table (D-149) and holds no
 * matter list. The BFF stays the judge: it refuses a limit on any other subject (`ontology.admin.scope_subject_mismatch`).
 */
export interface MatterPicker {
  /** The rule subject a matter limit applies to; the picker is offered only for a rule about it. */
  subject: string;
  /** At most a page of matters whose name contains the text, as the caller can read them. */
  search: (query: string) => Promise<ILookupItem[]>;
}

/** The mode that opens the wizard on a new version of the rule a page shows. */
export function versionModeOf(detail: RuleDetail): NewVersionMode {
  return {
    kind: 'version',
    policyId: detail.summary.policyId,
    code: detail.summary.code,
    name: detail.summary.name,
    lane: detail.summary.lane,
    versionNumber: detail.summary.versionNumber,
    enabled: detail.summary.enabled,
    wording: {
      headline: detail.wording.headline ?? '',
      messageTemplate: detail.wording.messageTemplate ?? '',
      proposedAction: detail.wording.proposedAction ?? '',
    },
    actions: detail.plan.actions.map(a => a.code),
    nextSteps: detail.plan.nextSteps.map(a => a.code),
    priority: detail.scope.priority,
    countsTowardSuppression: detail.noise.countsTowardSuppression,
    quietWindowDays: detail.noise.quietWindowDays,
    scope: {
      tenant: detail.scope.tenant,
      matter: detail.scope.matterName ?? detail.scope.matterId,
      matterId: detail.scope.matterId,
    },
  };
}

/**
 * The draft a mode opens with. A new rule is empty. A new version carries its rule's lane and the current wording and plan as the
 * starting point; its CONDITION is empty because the BFF returns no raw rule body (D-149) and the admin rebuilds it from the catalog.
 */
export function initialDraftOf(catalog: AuthoringCatalog, mode: RuleWizardMode): RuleDraft {
  const draft = emptyDraft(catalog);
  if (mode.kind === 'new') {
    return draft;
  }
  return {
    ...draft,
    lane: mode.lane,
    shortHeadline: mode.wording.headline,
    messageTemplate: mode.wording.messageTemplate,
    proposedAction: mode.wording.proposedAction,
    actions: [...mode.actions],
    nextSteps: [...mode.nextSteps],
    quietWindowDays: mode.lane === QUIET_WINDOW_LANE ? String(mode.quietWindowDays) : '',
    // Task 102b: a new version starts with the rule's current scope; publishing it with another (or none) changes the scope.
    matterId: mode.scope.matterId,
    matterName: mode.scope.matter,
  };
}
