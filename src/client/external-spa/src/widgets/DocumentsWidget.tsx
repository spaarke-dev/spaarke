/**
 * DocumentsWidget — outside-counsel "Documents" widget (task 016). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Documents (external widget)"
 * `sprk_gridconfiguration` record and the "documents" BFF module (entity `sprk_document`, Tier-2
 * predicate = the caller's accessible-project set matched against `sprk_document.sprk_project`).
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '3af4102c-1092-f111-b8dc-7ced8ddc4a05';

export const DocumentsWidgetBody = createGridWidgetBody(CONFIG_ID);

export default DocumentsWidgetBody;
