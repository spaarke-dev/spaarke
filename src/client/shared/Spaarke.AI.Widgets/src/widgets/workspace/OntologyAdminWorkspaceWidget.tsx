/**
 * OntologyAdminWorkspaceWidget - the "Ontology admin" workspace tab (spaarke-ontology-platform-r1 task 104; D-22, FR-56).
 *
 * A thin host over the shared `OntologyAdminTab` (`@spaarke/ui-components`). It is registered ONLY by `registerOntologyAdminWidget`
 * in `register-workspace-widgets.ts`, which `ontologyAdminGate.ts` calls only when the capability probe says the caller can author
 * policies. The probe decides what is shown; each admin write route authorizes itself as the caller (Dataverse enforces the role).
 *
 * EXTENSION POINTS: task 100 passes its Rules area as `rules` and task 103 its Classification area as `classification`, built
 * here from `useAiSession()` (the authenticated fetch and BFF base URL their routes need), the way `WorklistWorkspaceWidget`
 * builds the worklist service.
 *
 * `data` / `isLoading` / `error` from `WorkspaceWidgetProps` are unused: the areas read their own routes once mounted.
 */

import * as React from 'react';
import { makeStyles } from '@fluentui/react-components';
import type { MatterPicker } from '@spaarke/ui-components';
import {
  ClassificationArea,
  OntologyAdminTab,
  RulesArea,
  createClassificationService,
  createOntologyAuthoringService,
  createOntologyRulesService,
  searchLookupTarget,
} from '@spaarke/ui-components';
import { useAiSession } from '../../providers/useAiSession';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

const useStyles = makeStyles({
  // The Console's workspace tab scrolls as a whole; the admin areas are document-like pages.
  host: {
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
    overflowY: 'auto',
  },
});

/** The table a rule's matter limit names (task 102b, D-165): the subject it applies to and the table the picker searches. */
const MATTER_TABLE = 'sprk_matter';

export const OntologyAdminWorkspaceWidget: React.FC<WorkspaceWidgetProps> = () => {
  const styles = useStyles();
  const { authenticatedFetch, bffBaseUrl } = useAiSession();
  // Task 100: the Rules area reads and writes through the BFF as the caller.
  const rulesService = React.useMemo(
    () => createOntologyRulesService({ fetch: authenticatedFetch, baseUrl: bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  // Task 102: the rule wizard (New rule, New version) writes through the authoring routes as the caller.
  const authoringService = React.useMemo(
    () => createOntologyAuthoringService({ fetch: authenticatedFetch, baseUrl: bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  // Task 103: the Classification area reads and writes through the BFF as the caller.
  const classificationService = React.useMemo(
    () => createClassificationService({ fetch: authenticatedFetch, baseUrl: bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  // Task 102b (D-165): the rule wizard's matter picker, offered for a rule about matters. It searches matters by name AS THE CALLER
  // (Xrm.WebApi) through the shared lookup search the record header uses; only the chosen id reaches the BFF, which reads the matter
  // again as the caller and refuses a limit on any other subject.
  const matterPicker = React.useMemo<MatterPicker>(
    () => ({ subject: MATTER_TABLE, search: (query: string) => searchLookupTarget(MATTER_TABLE, query) }),
    []
  );

  return (
    <div className={styles.host} data-testid="ontology-admin-workspace-widget">
      <OntologyAdminTab
        rules={<RulesArea service={rulesService} authoringService={authoringService} matterPicker={matterPicker} />}
        classification={<ClassificationArea service={classificationService} />}
      />
    </div>
  );
};

export default OntologyAdminWorkspaceWidget;
