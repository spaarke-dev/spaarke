/**
 * RulesArea - the Rules area of the Ontology admin tab (task 100; spec FR-57): the list, and the rule page one click in.
 *
 * Passed to `OntologyAdminTab` as its `rules` slot, so task 103's Classification area sits beside it without touching it. The rule
 * wizard (task 102) opens IN-APP from here, over the list or the page: "New rule" on the list, "New version" on a rule's page. It is
 * offered only when the host passes an `authoringService`; without one the area is the read-only list and page of task 100.
 *
 * When the wizard closes after writing, the list (or the page) is reloaded so what was written shows at once.
 */

import * as React from 'react';
import { useUiScale } from '../../../hooks/useUiScale';
import { RuleWizard, versionModeOf } from '../RuleWizard';
import type { MatterPicker, OntologyAuthoringService, RuleWizardMode, RuleWizardResult } from '../RuleWizard';
import { RuleList } from './RuleList';
import { RulePage } from './RulePage';
import type { OntologyRulesService, RuleDetail } from './rulesService';

export interface RulesAreaProps {
  service: OntologyRulesService;
  /** The rule wizard's service. When omitted the area offers no way to write a rule. */
  authoringService?: OntologyAuthoringService;
  /** The `--sprk-ui-scale` the host applies to its theme. Defaults to the app's display-size setting. */
  uiScale?: number;
  /** Task 102b: the rule wizard's matter picker (as the caller), for limiting a rule about matters to one matter. */
  matterPicker?: MatterPicker;
}

export const RulesArea: React.FC<RulesAreaProps> = ({ service, authoringService, uiScale, matterPicker }) => {
  const [policyId, setPolicyId] = React.useState<string | undefined>(undefined);
  const [wizard, setWizard] = React.useState<RuleWizardMode | undefined>(undefined);
  // Bumped when the wizard wrote something, so the list or page behind it mounts afresh and reads the new state.
  const [refresh, setRefresh] = React.useState(0);
  const appScale = useUiScale().uiScale;

  const closeWizard = React.useCallback((result: RuleWizardResult | null) => {
    setWizard(undefined);
    if (result !== null) {
      setRefresh(n => n + 1);
    }
  }, []);

  const openNewVersion = React.useCallback((detail: RuleDetail) => setWizard(versionModeOf(detail)), []);

  // Back to the list reloads it (RuleList mounts afresh), so a switch or a retire shows at once.
  return (
    <>
      {policyId === undefined ? (
        <RuleList
          key={`list-${refresh}`}
          service={service}
          onOpen={setPolicyId}
          onNewRule={authoringService === undefined ? undefined : () => setWizard({ kind: 'new' })}
        />
      ) : (
        <RulePage
          key={`page-${refresh}`}
          service={service}
          policyId={policyId}
          onBack={() => setPolicyId(undefined)}
          onNewVersion={authoringService === undefined ? undefined : openNewVersion}
        />
      )}
      {authoringService !== undefined && wizard !== undefined ? (
        <RuleWizard
          service={authoringService}
          mode={wizard}
          uiScale={uiScale === undefined ? appScale : uiScale}
          matterPicker={matterPicker}
          onClose={closeWizard}
        />
      ) : null}
    </>
  );
};

RulesArea.displayName = 'RulesArea';
