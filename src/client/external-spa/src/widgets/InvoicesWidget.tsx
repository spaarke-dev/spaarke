/**
 * InvoicesWidget — outside-counsel "Invoices" widget (task 016). The grid is a thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Invoices (external widget)"
 * `sprk_gridconfiguration` record and the "invoices" BFF module (entity `sprk_invoice`, Tier-2
 * predicate = the caller's accessible-project set matched against `sprk_invoice.sprk_project`).
 *
 * ebilling-r1 task 051 (FR-18, FR-45): a "Submit invoice" action above the grid opens the submission page. It is shown
 * only when the server's own answer says the caller can submit: `firms` on GET /api/v1/external/me is sent only when
 * eBilling is on, and lists the caller's own firms (none for a workforce/systemuser caller, who is refused on the
 * external routes and submits from the internal workspace). Absent, null or empty: no action. That is one cheap /me read
 * on mount, never a list read. Navigation comes from the `onNavigate` prop the router-owning host hands the body
 * (App.tsx -> WorkspaceHomePage), so this file imports no router API (the arch guard refuses router-named imports
 * outside App.tsx); without the prop (a test, a surface with no router) the action is simply not shown. The grid binding
 * below is exactly as SPA-r3 left it.
 */
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { AddRegular } from '@fluentui/react-icons';
import { useCallerContext } from '../hooks/useCallerContext';
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

export const InvoicesWidgetBody: WidgetBodyComponent = props => {
  const styles = useStyles();
  const { onNavigate } = props;
  // Read only when there is somewhere to navigate. A failed read leaves the context null: no action (the safe default;
  // the submission page itself still works from a direct link and the server decides).
  const me = useCallerContext(onNavigate !== undefined);
  // `firms` is set only when eBilling is on (FR-45) and the caller has none to submit for if empty (a systemuser gets []
  // and is refused on these routes; a deactivated contact gets []).
  const canSubmit = onNavigate !== undefined && me?.firms != null && me.firms.length > 0;

  return (
    <div className={styles.root}>
      {canSubmit && (
        <div className={styles.actions}>
          <Button appearance="primary" icon={<AddRegular />} onClick={() => onNavigate?.(INVOICE_SUBMIT_PATH)}>
            Submit invoice
          </Button>
        </div>
      )}
      <div className={styles.grid}>
        <InvoicesGridBody {...props} />
      </div>
    </div>
  );
};

export default InvoicesWidgetBody;
