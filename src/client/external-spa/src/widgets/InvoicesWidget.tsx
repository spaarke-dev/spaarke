/**
 * InvoicesWidget — outside-counsel "Invoices" widget (task 016). The grid is a thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Invoices (external widget)"
 * `sprk_gridconfiguration` record and the "invoices" BFF module (entity `sprk_invoice`, Tier-2
 * predicate = the caller's accessible-project set matched against `sprk_invoice.sprk_project`).
 *
 * ebilling-r1 task 051 (FR-18, FR-45): a "Submit invoice" action above the grid opens the submission page. It is shown
 * only when the server's own answer says eBilling is on: the availability probe (`probeInvoiceSubmission`) hides it on a
 * 503 `ebilling.disabled`, and nothing else hides it. This is the ONLY change to this file for task 051; the grid
 * binding below is exactly as SPA-r3 left it.
 */
import * as React from 'react';
import { UNSAFE_NavigationContext, useNavigate } from 'react-router-dom';
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { AddRegular } from '@fluentui/react-icons';
import { probeInvoiceSubmission } from '../api/web-api-client';
import { INVOICE_SUBMIT_PATH } from '../pages/InvoiceSubmitPage';
import type { WidgetBodyComponent } from '../registry/PlaceholderWidgetBody';
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '3ff4102c-1092-f111-b8dc-7ced8ddc4a05';

const InvoicesGridBody = createGridWidgetBody(CONFIG_ID);

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    width: '100%',
    minWidth: 0,
  },
  actions: {
    display: 'flex',
    justifyContent: 'flex-end',
    flexShrink: 0,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  grid: {
    flex: '1 1 auto',
    minHeight: 0,
    minWidth: 0,
  },
});

/** The action itself. Rendered only inside a router (it navigates), and only once the probe has not said "off". */
const SubmitInvoiceAction: React.FC = () => {
  const navigate = useNavigate();
  return (
    <Button appearance="primary" icon={<AddRegular />} onClick={() => navigate(INVOICE_SUBMIT_PATH)}>
      Submit invoice
    </Button>
  );
};

export const InvoicesWidgetBody: WidgetBodyComponent = props => {
  const styles = useStyles();
  // Is a router mounted above this widget? In the app it always is (App.tsx owns the one router); a unit test that renders
  // the grid body alone has none, and `useNavigate` would throw there. Read from the navigation context, which is null
  // outside a router: the router hook whose name says so (`useInRouterContext`) is refused outside App.tsx by the arch
  // guard that keeps every router under the external-host provider (ExternalSpaGridViewSelectorGuardTests).
  const inRouter = React.useContext(UNSAFE_NavigationContext) != null;
  // Hidden until the server has answered, and for good if it answers "disabled" (FR-45). An outage or a refusal is not
  // "disabled": the action stays, and the submission page shows what the server says.
  const [showSubmit, setShowSubmit] = React.useState(false);

  React.useEffect(() => {
    if (!inRouter) return;
    let cancelled = false;
    void probeInvoiceSubmission().then(answer => {
      if (!cancelled) setShowSubmit(answer !== 'disabled');
    });
    return () => {
      cancelled = true;
    };
  }, [inRouter]);

  return (
    <div className={styles.root}>
      {inRouter && showSubmit && (
        <div className={styles.actions}>
          <SubmitInvoiceAction />
        </div>
      )}
      <div className={styles.grid}>
        <InvoicesGridBody {...props} />
      </div>
    </div>
  );
};

export default InvoicesWidgetBody;
