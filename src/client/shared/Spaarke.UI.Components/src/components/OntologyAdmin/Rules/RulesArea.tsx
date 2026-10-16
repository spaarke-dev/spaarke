/**
 * RulesArea - the Rules area of the Ontology admin tab (task 100; spec FR-57): the list, and the read-only rule page one click in.
 *
 * Passed to `OntologyAdminTab` as its `rules` slot, so task 103's Classification area sits beside it without touching it. The
 * rule wizard (task 102) opens in-app from here when it lands; it is not part of this task.
 */

import * as React from 'react';
import { RuleList } from './RuleList';
import { RulePage } from './RulePage';
import type { OntologyRulesService } from './rulesService';

export interface RulesAreaProps {
  service: OntologyRulesService;
}

export const RulesArea: React.FC<RulesAreaProps> = ({ service }) => {
  const [policyId, setPolicyId] = React.useState<string | undefined>(undefined);

  // Back to the list reloads it (RuleList mounts afresh), so a switch or a retire shows at once.
  return policyId === undefined ? (
    <RuleList service={service} onOpen={setPolicyId} />
  ) : (
    <RulePage service={service} policyId={policyId} onBack={() => setPolicyId(undefined)} />
  );
};

RulesArea.displayName = 'RulesArea';
