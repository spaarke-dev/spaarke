/**
 * WorkAssignmentsWidget — outside-counsel "Work Assignments" widget (task 016). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Work Assignments (external widget)"
 * `sprk_gridconfiguration` record and the "work-assignments" BFF module (entity
 * `sprk_workassignment`, Tier-2 predicate = the caller's accessible-project set matched against
 * `sprk_workassignment.sprk_regardingproject`).
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '42f4102c-1092-f111-b8dc-7ced8ddc4a05';

export const WorkAssignmentsWidgetBody = createGridWidgetBody(CONFIG_ID);

export default WorkAssignmentsWidgetBody;
