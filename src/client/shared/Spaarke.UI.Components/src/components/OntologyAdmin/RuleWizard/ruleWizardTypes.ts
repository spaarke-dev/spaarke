/**
 * ruleWizardTypes - what opens the rule wizard (spaarke-ontology-platform-r1 task 102).
 *
 * A new rule starts empty. A NEW VERSION starts from the rule's page: its identity is fixed, and its wording and plan are carried over
 * as a starting point. The CONDITION is not carried over: the BFF returns no raw rule body (owner decision D-149), so the admin
 * rebuilds the condition from the catalog.
 */

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
  scope: { tenant: string | null; matter: string | null };
}

export type RuleWizardMode = NewRuleMode | NewVersionMode;

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
    scope: { tenant: detail.scope.tenant, matter: detail.scope.matterName ?? detail.scope.matterId },
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
  };
}
