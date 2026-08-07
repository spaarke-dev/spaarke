/**
 * InvoicesWidget — outside-counsel "Invoices" widget (task 016). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Invoices (external widget)"
 * `sprk_gridconfiguration` record and the "invoices" BFF module (entity `sprk_invoice`, Tier-2
 * predicate = the caller's accessible-project set matched against `sprk_invoice.sprk_project`).
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '3ff4102c-1092-f111-b8dc-7ced8ddc4a05';

export const InvoicesWidgetBody = createGridWidgetBody(CONFIG_ID);

export default InvoicesWidgetBody;
