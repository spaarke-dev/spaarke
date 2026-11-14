/**
 * InvoicesWidget — outside-counsel "Invoices" widget (task 016). The grid is a thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Invoices (external widget)"
 * `sprk_gridconfiguration` record and the "invoices" BFF module (entity `sprk_invoice`, Tier-2
 * predicate = the caller's accessible-project set matched against `sprk_invoice.sprk_project`).
 *
 * ebilling-r1 task 051 (FR-18, FR-45), SPA-r4 task 010: "Submit invoice" is a command in the grid's own toolbar (the
 * shared grid's generic `hostCommands` slot), not a separate button above it. It is passed only when the server's own
 * answer says the caller can submit: `firms` on GET /api/v1/external/me is sent only when eBilling is on, and lists the
 * caller's own firms (none for a workforce/systemuser caller, who is refused on the external routes and submits from the
 * internal workspace). Absent, null or empty: no command. The gate is a convenience only; the server refuses a
 * submission with no firm (EB-D-223). That is one cheap /me read on mount, never a list read. Navigation comes from the
 * `onNavigate` prop the router-owning host hands the body (App.tsx -> WorkspaceHomePage), so this file imports no
 * router API (the arch guard refuses router-named imports outside App.tsx); without the prop (a test, a surface with no
 * router) the command is simply not offered. The grid binding below is exactly as SPA-r3 left it.
 */
import * as React from 'react';
import { AddRegular } from '@fluentui/react-icons';
import { useCallerContext } from '../hooks/useCallerContext';
import { INVOICE_SUBMIT_PATH } from '../pages/invoiceSubmitLink';
import type { WidgetBodyComponent } from '../registry/PlaceholderWidgetBody';
import { createGridWidgetBody, type GridHostCommands } from './GridWidgetBody';

const CONFIG_ID = '3ff4102c-1092-f111-b8dc-7ced8ddc4a05';

const InvoicesGridBody = createGridWidgetBody(CONFIG_ID);

/** The id of the toolbar command (stable: it is also what the grid reports to `onCommandInvoke`). */
export const SUBMIT_INVOICE_COMMAND_ID = 'submit-invoice';

export const InvoicesWidgetBody: WidgetBodyComponent = props => {
  const { onNavigate } = props;
  // Read only when there is somewhere to navigate. A failed read leaves the context null: no command (the safe default;
  // the submission page itself still works from a direct link and the server decides).
  const me = useCallerContext(onNavigate !== undefined);
  // `firms` is set only when eBilling is on (FR-45) and the caller has none to submit for if empty (a systemuser gets []
  // and is refused on these routes; a deactivated contact gets []).
  const canSubmit = onNavigate !== undefined && me?.firms != null && me.firms.length > 0;

  const hostCommands = React.useMemo<GridHostCommands>(
    () =>
      canSubmit
        ? [
            {
              id: SUBMIT_INVOICE_COMMAND_ID,
              label: 'Submit invoice',
              icon: <AddRegular />,
              onInvoke: () => onNavigate?.(INVOICE_SUBMIT_PATH),
            },
          ]
        : undefined,
    [canSubmit, onNavigate]
  );

  return <InvoicesGridBody {...props} hostCommands={hostCommands} />;
};

export default InvoicesWidgetBody;
