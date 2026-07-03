/**
 * MatterHeaderHost — top-level React wrapper for the virtual PCF.
 *
 * Mirrors VisualHost's `VisualHostHost.tsx` pattern (verified working in
 * production 2026-05). Owns the concerns the PCF class no longer touches:
 *   - Wraps `<MatterHeaderView>` in a `<FluentProvider>` so Fluent v9 CSS
 *     variables (`--colorNeutralBackground1`, `--shadow16`,
 *     `--borderRadiusMedium`, …) are injected into portal-rendered
 *     surfaces (`Popover`, `Tooltip`, `Menu`, `Dialog`).
 *
 * Why the wrapper is required despite `control-type="virtual"`:
 *   Platform-library Fluent theming is applied on the PCF's own root DOM
 *   element. Portal-rendered surfaces (like the `<AiSummaryPopover>`'s
 *   `<PopoverSurface>`) mount to `document.body` — OUTSIDE the PCF root — so
 *   CSS variables defined only on the PCF root do NOT reach them. Result:
 *   the popover renders without background, border-radius, or shadow. Our
 *   own `<FluentProvider>` fixes this because Fluent v9's `applyStylesToPortals`
 *   default (true) explicitly injects theme vars into portal subtrees. This
 *   was the root cause of the v1.0.4..v1.0.10 "no background / no shadow"
 *   symptom that ate five iteration rounds of live QA.
 *
 * Theme selection: v1.0.11 ships with `webLightTheme` unconditionally. Dark
 * mode + high contrast can be added by porting VisualHost's
 * `providers/ThemeProvider.ts` (152 LOC) into a separate task; the immediate
 * blocker is the portal-vars issue, which `webLightTheme` alone resolves.
 *
 * @see docs/architecture/../architecture/PORTAL-THEME.md — pattern context
 * @see src/client/pcf/VisualHost/control/VisualHostHost.tsx — canonical
 */

import * as React from 'react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MatterHeaderView } from './MatterHeaderView';

export interface IMatterHeaderHostProps {
  recordId: string;
  title?: string;
  showVersion?: boolean;
}

export const MatterHeaderHost: React.FC<IMatterHeaderHostProps> = ({ recordId, title, showVersion }) => {
  return (
    <FluentProvider theme={webLightTheme} style={{ width: '100%' }}>
      <MatterHeaderView recordId={recordId} title={title} showVersion={showVersion} />
    </FluentProvider>
  );
};
