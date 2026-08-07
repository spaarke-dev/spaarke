/**
 * ProjectsWidget — outside-counsel "Projects" widget (task 016). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Projects (external widget)"
 * `sprk_gridconfiguration` record and the "collaboration" BFF module (task 015, entity
 * `sprk_project`, Tier-2 predicate = the caller's accessible-project set).
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '61711823-1092-f111-b8dc-7ced8ddc4a05';

export const ProjectsWidgetBody = createGridWidgetBody(CONFIG_ID);

export default ProjectsWidgetBody;
