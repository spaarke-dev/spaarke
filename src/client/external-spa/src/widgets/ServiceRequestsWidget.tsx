/**
 * ServiceRequestsWidget — "Service Requests" widget (task 028; workforce plane in the client for now). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared implementation.
 * Backed by the "Internal — Service Requests (external widget)" `sprk_gridconfiguration` record and
 * the "service-requests" BFF module (entity `sprk_servicerequest`, Tier-2 predicate =
 * `sprk_requestedby == caller contact`; BFF A7 / r3 task 011: workforce see their own rows, CIAM partners
 * only when ExternalAccess:PartnerServiceRequestsEnabled is on, otherwise 0 rows).
 *
 * Registered with `planes: ['workforce']`, so the client currently hides it from the outside-counsel
 * partner SPA; widening the client plane is task 052. The BFF, not the client gate, is the boundary
 * (NFR-06).
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '403e5d37-cb94-f111-b8db-00224835447a';

export const ServiceRequestsWidgetBody = createGridWidgetBody(CONFIG_ID);

export default ServiceRequestsWidgetBody;
