/**
 * WorklistWorkspaceWidget.tsx
 *
 * Direct-widget mount for the shared `WorklistWidget` (spaarke-ontology-platform-r1 task 059). Registered as the SpaarkeAi workspace
 * widget type `worklist` in `register-workspace-widgets.ts`.
 *
 * `WorklistWidget` (in `@spaarke/ui-components`) has no Dataverse, Xrm or auth dependency (ADR-012), so this file's ONLY job is to
 * resolve its host slots, mirroring `ReconciliationWorkspaceWidget.tsx`:
 *   - `service`      - the Signal read route (task 038) over the session's authenticated fetch (ADR-028). It is the only source of the
 *                      lists: this widget reads no `sprk_signal` row itself.
 *   - `lookup`       - the decision wizard's Person / Lookup search, from Dataverse (`worklistLookup.ts`).
 *   - `aggregateRow` - task 054's email reconciliation card, whose "Open Email Review" opens the Reconciliation tab through the pane
 *                      event bus (no-op in a bus-less host).
 *
 * The narrative slot is not filled here either: the shared widget asks `service.narrate` for the Assistant's summary of the list's counts
 * (task 062), shows it while no filter is on, and a host-supplied `narrative` would win. This host supplies none.
 * The decision wizard itself is already mounted by the Console shell (`DecisionWizardHost` in `ThreePaneShell`, task 058), so there is
 * nothing to mount in this file.
 *
 * ADR-022: React 19, NOT PCF-safe. ADR-021: tokens only (none are needed here).
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { ReconciliationAggregateCard } from '@spaarke/communication-components';
import {
  WorklistWidget,
  XrmDataverseClient,
  createWorklistService,
  createXrmNavigationService,
  getXrm,
} from '@spaarke/ui-components';
import { useOptionalDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import { useAiSession } from '../../providers/useAiSession';
import type { WorkspaceWidgetProps } from '../../types/widget-types';
import {
  createWorklistLookup,
  createWorklistOpenLookup,
  createWorklistOpenRecord,
  type WorklistLookupDataSource,
} from './worklistLookup';

/** The registered type string of the Email Review (reconciliation) tab this widget's aggregate card opens. */
export const RECONCILIATION_WIDGET_TYPE = 'communications-reconciliation';

const useStyles = makeStyles({
  // The Console's workspace tab scrolls as a whole; the worklist is a document-like page, not a bounded grid.
  host: {
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    overflowY: 'auto',
  },
});

/** `data` / `isLoading` / `error` from `WorkspaceWidgetProps` are unused: the worklist reads its own route once mounted. */
export const WorklistWorkspaceWidget: React.FC<WorkspaceWidgetProps> = () => {
  const styles = useStyles();
  const { authenticatedFetch, bffBaseUrl } = useAiSession();
  const dispatch = useOptionalDispatchPaneEvent();

  const service = React.useMemo(
    () => createWorklistService({ fetch: authenticatedFetch, baseUrl: bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  // Without Xrm (a non-MDA dev shell) the wizard's searches find nothing, and the aggregate card reports its count as Missing.
  const webApi = React.useMemo(() => getXrm()?.WebApi as WorklistLookupDataSource | undefined, []);
  const lookup = React.useMemo(() => (webApi ? createWorklistLookup(webApi) : undefined), [webApi]);
  const dataverseClient = React.useMemo(() => (webApi ? new XrmDataverseClient() : undefined), [webApi]);
  // D-182: the wizard's Person / Lookup fields open the platform side-pane lookup, and its subject opens in a modal. Without Xrm
  // neither exists, and the wizard keeps its inline type-ahead and shows the subject as text.
  const navigation = React.useMemo(() => (webApi ? createXrmNavigationService() : undefined), [webApi]);
  // Task 160: with Xrm.WebApi the pane also reads a picked contact's address (the inquiry's To lookup).
  const openLookup = React.useMemo(
    () => (navigation ? createWorklistOpenLookup(navigation, webApi ?? undefined) : undefined),
    [navigation, webApi]
  );
  const openRecord = React.useMemo(() => (navigation ? createWorklistOpenRecord(navigation) : undefined), [navigation]);

  const openEmailReview = React.useCallback(() => {
    dispatch('workspace', { type: 'widget_load', widgetType: RECONCILIATION_WIDGET_TYPE });
  }, [dispatch]);

  const aggregateRow = React.useMemo(
    () => <ReconciliationAggregateCard dataverseClient={dataverseClient} onOpen={openEmailReview} />,
    [dataverseClient, openEmailReview]
  );

  return (
    <div className={styles.host} data-testid="worklist-workspace-widget">
      <WorklistWidget
        service={service}
        lookup={lookup}
        openLookup={openLookup}
        openRecord={openRecord}
        aggregateRow={aggregateRow}
      />
    </div>
  );
};

export default WorklistWorkspaceWidget;
