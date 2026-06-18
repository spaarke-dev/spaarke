/**
 * Test-local mock for `@spaarke/ui-components`.
 *
 * R2 task 019 / NFR-05:
 *   `NarrativeBullet.tsx` imports `MicrosoftToDoIcon` from `@spaarke/ui-components`.
 *   The smoke test transitively mounts that component, so we stub the icon as a
 *   no-op SVG. This keeps the test independent of the @spaarke/ui-components
 *   peer dep (which isn't installed at the daily-briefing-components package level).
 */
import * as React from "react";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const MicrosoftToDoIcon: React.FC<any> = (props) => (
  <svg
    role="img"
    aria-label="Microsoft To Do"
    className={props?.className}
    width={16}
    height={16}
  />
);
