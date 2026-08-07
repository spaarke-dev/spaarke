/**
 * MattersWidget — outside-counsel "Matters" widget (task 016). A thin
 * `createGridWidgetBody(configId)` binding — see `GridWidgetBody.tsx` for the shared
 * implementation. Backed by the "Outside Counsel — Matters (external widget)"
 * `sprk_gridconfiguration` record and the "matters" BFF module (entity `sprk_matter`).
 *
 * D-016-1 (documented Path-A exception — see notes/task-016-deviations.md): the BFF module's
 * Tier-2 predicate is intentionally ALWAYS-EMPTY (fail-closed — sprk_matter has no lookup back to
 * sprk_project in the current schema, and no Contact→Organization affiliation resolution exists to
 * evaluate `sprk_matter.sprk_assignedoutsidecounsel` against the caller). The grid config's
 * `emptyStateMessage` is the VERBATIM R1 `OutsideCounselDashboard` stub copy ("Matter-level
 * workspace access is coming soon.") — R1-parity-preserving (R1 never had a working Matters
 * surface either), not a regression.
 */
import { createGridWidgetBody } from './GridWidgetBody';

const CONFIG_ID = '583a2a33-1092-f111-b8dc-7ced8ddc4a05';

export const MattersWidgetBody = createGridWidgetBody(CONFIG_ID);

export default MattersWidgetBody;
