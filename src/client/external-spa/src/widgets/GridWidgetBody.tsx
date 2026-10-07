/**
 * GridWidgetBody — the thin shared-grid (`configId=…`) wrapper factory every outside-counsel
 * workspace widget resolves to (task 016). Per constraints ("a widget = a sprk_gridconfiguration
 * record + a thin wrapper + a registry entry"), each of the five widget files under this directory
 * is a one-line call to `createGridWidgetBody(CONFIG_ID)` — this file is the ONE shared
 * implementation, not five hand-rolled grids (§11).
 *
 * Deliberately mounts the shared grid (through `<ExternalDataGrid>`) rather than `<DataGridPageShell>` — the shell injects a
 * GLOBAL `html/body { overflow:hidden; height:100% }` CSS reset intended for a standalone Custom
 * Page's own document (see DataGridPageShell.tsx file header: "the shell's canonical Custom Page
 * mount"). This SPA embeds the grid as ONE TAB inside the larger workspace shell (tab strip +
 * pinned Quick Start + assistant dock) — injecting that reset globally would fight the shell's own
 * scroll/layout chrome. Theme resolution mirrors what the shell does internally
 * (`resolveCodePageTheme` + `setupCodePageThemeListener`, the SAME utility `main.tsx` already uses
 * for the app's ambient theme) so the grid's own popover/menu PORTALS (which render outside the
 * React tree, via `applyStylesToPortals`) still resolve dark/light/Teams correctly (ADR-021).
 *
 * Read-only (NFR-01): no `onCreateNew` / write-path override is wired — the framework's default
 * command-bar `+ New` action opens `Xrm.Navigation.openForm`, which no-ops on this Xrm-free host
 * (there is no `window.Xrm`); each grid config also sets `commandBar.showDefaultCommands` behavior
 * via its `sprk_configjson` where relevant. Row-open likewise defaults to `Xrm.Navigation.navigateTo`,
 * which console-warns and no-ops without `Xrm` present — a graceful degrade (no crash, no write),
 * consistent with "widgets are READ-ONLY (broker)". See notes/task-016-deviations.md.
 *
 * NO VIEW SELECTOR (unified-access-control-r2 task 157, owner round 4 item 7). That is a SECURITY setting, not a
 * cosmetic one. The shared grid enforces it itself inside this SPA (DataGridExternalHost: the provider at the
 * app root and the constant this SPA's Vite build defines), whatever props reach it, and it then never requests
 * the saved-query list. As defence in depth the grid is also mounted through `<ExternalDataGrid>`, which forces
 * `showViewSelector={false}`. With the selector on, the grid offered the entity's INTERNAL MDA main views
 * (`/savedqueries/{entity}`), and the BFF had to admit every column those views project. With it off, the
 * grid only ever runs its own `sprk_gridconfiguration` FetchXML, so each external module's server column
 * allow-list (ExternalAccessModule.cs) is that config's columns plus the scope and /record default columns,
 * and nothing more. Turning the selector back on would make every sibling view error with
 * DV_FETCHXML_COLUMN_NOT_PERMITTED. The arch guard ExternalSpaGridViewSelectorGuardTests asserts the provider
 * mount and the build constant, and (defence in depth) refuses any import of the shared grid outside the
 * wrapper's own file and any use of `<ExternalDataGrid>` here other than as a JSX tag.
 */
import * as React from 'react';
import { makeStyles, webLightTheme, type Theme } from '@fluentui/react-components';
import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components/utils/themeStorage';
import { gridDataverseClient } from '../services/gridDataverseClient';
import { ExternalDataGrid } from './ExternalDataGrid';
import type { WidgetBodyComponent, WidgetBodyProps } from '../registry/PlaceholderWidgetBody';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    width: '100%',
    minWidth: 0,
  },
});

/** Resolves + live-tracks the ambient light/dark/Teams theme (same source as the app shell). */
function useAmbientTheme(): Theme {
  const [theme, setTheme] = React.useState<Theme>(() => resolveCodePageTheme() ?? webLightTheme);
  React.useEffect(() => setupCodePageThemeListener(next => setTheme(next ?? webLightTheme)), []);
  return theme;
}

/**
 * Builds a widget-body component bound to one `sprk_gridconfiguration` record id. The returned
 * component ignores the incoming `title`/`description` props (the shared grid framework renders its
 * OWN header from `sprk_configjson.display.title`) — matches `WidgetBodyComponent`'s contract so it
 * plugs into `widgetRegistry.ts`'s `lazyLoader` unchanged.
 */
export function createGridWidgetBody(configId: string): WidgetBodyComponent {
  const GridWidgetBody: React.FC<WidgetBodyProps> = () => {
    const s = useStyles();
    const theme = useAmbientTheme();
    return (
      <div className={s.root}>
        <ExternalDataGrid configId={configId} dataverseClient={gridDataverseClient} theme={theme} />
      </div>
    );
  };
  GridWidgetBody.displayName = `GridWidgetBody(${configId})`;
  return GridWidgetBody;
}
