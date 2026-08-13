/**
 * ServiceRequestsWidget — INTERNAL-ONLY "Service Requests" widget (task 028). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared implementation.
 * Backed by the "Internal — Service Requests (external widget)" `sprk_gridconfiguration` record and
 * the "service-requests" BFF module (entity `sprk_servicerequest`, Tier-2 predicate =
 * `sprk_requestedby == caller contact`, and server-side fail-closed for the CIAM partner plane).
 *
 * Registered with `planes: ['workforce']` so it never appears on the outside-counsel partner SPA
 * (client gate); the BFF module additionally returns 0 rows for any non-workforce caller (defense in
 * depth — the "internal-only" guarantee does not depend on the client hiding the tab).
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '403e5d37-cb94-f111-b8db-00224835447a';

export const ServiceRequestsWidgetBody = createGridWidgetBody(CONFIG_ID);

export default ServiceRequestsWidgetBody;
