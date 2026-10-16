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
import { OntologyAdminTab } from '@spaarke/ui-components';
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

export const OntologyAdminWorkspaceWidget: React.FC<WorkspaceWidgetProps> = () => {
  const styles = useStyles();
  return (
    <div className={styles.host} data-testid="ontology-admin-workspace-widget">
      <OntologyAdminTab />
    </div>
  );
};

export default OntologyAdminWorkspaceWidget;
