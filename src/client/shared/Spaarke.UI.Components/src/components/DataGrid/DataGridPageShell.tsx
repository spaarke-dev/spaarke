/**
 * `<DataGridPageShell />` — canonical Custom Page mount for the Spaarke
 * DataGrid framework.
 *
 * Replaces the ~50-line FluentProvider + theme listener + URL parser + box-
 * sizing scaffolding that every DataGrid Code Page used to author by hand
 * with a single drop-in component:
 *
 * ```tsx
 * <DataGridPageShell
 *   configId="…"
 *   useUrlParentContext={{ key: 'matterId' }}
 *   sidePaneFilter={{
 *     paneId: 'date-filter',
 *     translator: (payload: DatePanePayload) => [{
 *       attribute: payload.field, operator: 'between', value: [payload.from, payload.to],
 *     }],
 *   }}
 *   onBack={() => window.close()}
 * />
 * ```
 *
 * What the shell handles automatically:
 *  - `FluentProvider` wrap with `applyStylesToPortals={true}` (NFR-03)
 *  - Theme resolution + `setupCodePageThemeListener` (light / dark / tenant)
 *  - Box-sizing CSS reset injection (the contract from
 *    `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §2)
 *  - URL parsing for the common `data=` envelope drill-through pattern
 *    (via `useUrlParentContext`)
 *  - Side-pane filter subscription + translation → `hostFilters`
 *    (via `sidePaneFilter`)
 *  - `XrmDataverseClient` instantiation (override via `dataverseClient` prop
 *    for non-MDA / mock hosts)
 *
 * What the shell does NOT handle (host concerns):
 *  - Side-pane LIFECYCLE (open/close/mutual exclusivity) — use
 *    `DataGridSidePaneOrchestrator` separately when the host owns this
 *  - Custom command handler registration — call `registerCommandHandler`
 *    BEFORE mounting the shell
 *  - Custom `onRecordOpen` overrides (e.g. opening a detail side pane instead
 *    of the configjson default) — pass via the `onRecordOpen` prop
 *
 * For pages that don't fit the shell, mount `<DataGrid>` directly and follow
 * the contract doc — the InvoicesPage / KPI / EventsPage hand-rolled mounts
 * remain valid; the shell is a convenience, not a requirement.
 *
 * @see docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md
 * @see useSidePaneFilter
 * @see DataGrid
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, type Theme } from '@fluentui/react-components';

import { DataGrid } from './DataGrid';
import type { DataGridProps } from './DataGrid';
import type { HostFilterCondition } from './fetchXmlOverlay';
import { useSidePaneFilter, type SidePaneFilterTranslator } from './sidePane';
import { XrmDataverseClient } from '../../services/XrmDataverseClient';
import { resolveCodePageTheme, setupCodePageThemeListener } from '../../utils/themeStorage';
import type { DataGridParentContext } from '../../hooks/useDataGridContext';

// ─────────────────────────────────────────────────────────────────────────────
// Box-sizing CSS reset injection (matches the index.html contract from §2)
//
// Custom Page authors SHOULD put the canonical reset in their index.html (the
// host contract spells it out). This injection is belt-and-suspenders: if a
// page misses the reset for any reason, the shell injects it at mount time
// so the DataGrid's chrome renders correctly.
// ─────────────────────────────────────────────────────────────────────────────

const RESET_CSS_ID = 'spaarke-datagrid-pageshell-reset';
const RESET_CSS = `
*, *::before, *::after { box-sizing: border-box; }
html, body, #root {
  margin: 0;
  padding: 0;
  width: 100%;
  height: 100%;
  max-height: 100vh;
  overflow: hidden;
}
#root > div { height: 100%; }
`;

function injectResetCssOnce(): void {
  if (typeof document === 'undefined') return;
  if (document.getElementById(RESET_CSS_ID)) return;
  const styleEl = document.createElement('style');
  styleEl.id = RESET_CSS_ID;
  styleEl.textContent = RESET_CSS;
  document.head.appendChild(styleEl);
}

// ─────────────────────────────────────────────────────────────────────────────
// URL-parent-context parser — the common `data=` envelope pattern
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Declarative spec for parsing parent context from the URL's `data=` envelope.
 * Used by drill-through dialogs opened via `Xrm.Navigation.navigateTo({
 *   pageType: 'webresource', data: '…form-encoded…'
 * })`.
 */
export interface UrlParentContextSpec {
  /**
   * Key in the host's `parentContext` to set the parsed value into
   * (e.g. `'matterId'`). Matches the configjson `behavior.parentContextFilter.parentContextKey`.
   */
  key: string;
  /**
   * Entity type to record alongside the id. Defaults to `'sprk_matter'` since
   * matter drill-throughs are the dominant case.
   */
  entityType?: string;
  /**
   * Optional list of URL-param fallbacks to try in order. Defaults to
   * `['filterValue', 'matterId', 'id']` — matches the canonical
   * sprk_invoicespage / sprk_kpiassessmentspage parsing.
   */
  paramFallbacks?: ReadonlyArray<string>;
}

function parseUrlParentContext(spec: UrlParentContextSpec): DataGridParentContext | undefined {
  if (typeof window === 'undefined') return undefined;
  const fallbacks = spec.paramFallbacks ?? ['filterValue', 'matterId', 'id'];

  const tryGet = (params: URLSearchParams): string | null => {
    for (const k of fallbacks) {
      const v = params.get(k);
      if (v) return v;
    }
    return null;
  };

  try {
    const outer = new URLSearchParams(window.location.search);
    // 1) Outer URL param directly.
    let id = tryGet(outer);
    // 2) Inside the `data=` envelope (form-encoded).
    if (!id) {
      const data = outer.get('data');
      if (data) id = tryGet(new URLSearchParams(data));
    }
    if (!id) return undefined;
    const cleanId = id.replace(/[{}]/g, '');
    const ctx: DataGridParentContext = {
      entityType: spec.entityType ?? 'sprk_matter',
      id: cleanId,
      name: '',
    };
    // ALSO expose the id under the caller's chosen key so the configjson's
    // `behavior.parentContextFilter.parentContextKey` can read it. The framework
    // overlay does `parentContext[parentContextKey]` — without this line,
    // configs that use `parentContextKey: 'matterId'` would never find the value.
    ctx[spec.key] = cleanId;
    return ctx;
  } catch {
    return undefined;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Side-pane filter wire-up — generalizes the EventsPage calendar pane pattern.
 */
export interface DataGridPageShellSidePaneFilter<TPayload = unknown> {
  /** Side-pane id (matches what the pane web resource sends via `sendSidePaneFilter`). */
  paneId: string;
  /** Translator from pane payload → `HostFilterCondition[]`. */
  translator: SidePaneFilterTranslator<TPayload>;
}

/**
 * Props for {@link DataGridPageShell}. Subset + extension of `DataGridProps`.
 *
 * Most DataGrid props pass through unchanged. The shell adds opinionated
 * defaults for theme + dataverseClient + parentContext + hostFilters.
 */
export interface DataGridPageShellProps
  extends Omit<DataGridProps, 'parentContext' | 'hostFilters' | 'theme' | 'dataverseClient'> {
  /**
   * Optional explicit `parentContext`. Takes precedence over `useUrlParentContext`.
   * Pass when the host has its own parsing logic.
   */
  parentContext?: DataGridParentContext;

  /**
   * Optional declarative URL parser. Mutually exclusive with `parentContext`
   * (if both are provided, `parentContext` wins). Suitable for the common
   * drill-through case where a single matterId arrives via the `data=` envelope.
   */
  useUrlParentContext?: UrlParentContextSpec;

  /**
   * Optional side-pane wiring. When supplied, the shell subscribes to the
   * pane's filter channel and feeds the translated conditions into the
   * DataGrid's `hostFilters`. Lifecycle (open/close) is NOT managed — see
   * {@link DataGridSidePaneOrchestrator} for that.
   */
  sidePaneFilter?: DataGridPageShellSidePaneFilter;

  /**
   * Additional host-supplied filters that compose with (and follow) anything
   * coming from `sidePaneFilter`. Useful when a host has BOTH a side pane
   * AND its own UI driving filter state.
   */
  additionalHostFilters?: ReadonlyArray<HostFilterCondition>;

  /**
   * Explicit theme override. When omitted, the shell uses
   * `resolveCodePageTheme` + `setupCodePageThemeListener` for live light/dark
   * switching.
   */
  theme?: Theme;

  /**
   * Explicit `IDataverseClient`. When omitted, the shell instantiates one
   * `XrmDataverseClient` (cached via useRef). Non-MDA hosts (Storybook,
   * Code Pages outside MDA) MUST pass an explicit client.
   */
  dataverseClient?: DataGridProps['dataverseClient'];
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Canonical mount for a Custom Page hosting `<DataGrid>`. Renders FluentProvider
 * + DataGrid with sensible defaults; see {@link DataGridPageShellProps}.
 */
export const DataGridPageShell: React.FC<DataGridPageShellProps> = ({
  parentContext: explicitParentContext,
  useUrlParentContext,
  sidePaneFilter,
  additionalHostFilters,
  theme: explicitTheme,
  dataverseClient: explicitClient,
  ...dataGridProps
}) => {
  // Inject the canonical CSS reset once (safety net for shells that don't
  // include it in their index.html).
  React.useEffect(() => {
    injectResetCssOnce();
  }, []);

  // Theme: explicit override OR live-resolved Code Page theme.
  const [resolvedTheme, setResolvedTheme] = React.useState<Theme>(
    explicitTheme ?? resolveCodePageTheme() ?? webLightTheme
  );
  React.useEffect(() => {
    if (explicitTheme) {
      setResolvedTheme(explicitTheme);
      return undefined;
    }
    return setupCodePageThemeListener(() => {
      setResolvedTheme(resolveCodePageTheme() ?? webLightTheme);
    });
  }, [explicitTheme]);

  // Parent context: explicit takes precedence over URL parsing.
  const parentContext = React.useMemo<DataGridParentContext | undefined>(() => {
    if (explicitParentContext) return explicitParentContext;
    if (useUrlParentContext) return parseUrlParentContext(useUrlParentContext);
    return undefined;
    // useUrlParentContext is treated as a one-time parse on mount; if the
    // caller wants reactive parsing they should manage parentContext themselves.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [explicitParentContext]);

  // Dataverse client: explicit OR stable XrmDataverseClient default.
  const defaultClientRef = React.useRef<XrmDataverseClient | null>(null);
  if (!explicitClient && !defaultClientRef.current) {
    defaultClientRef.current = new XrmDataverseClient();
  }
  const dataverseClient = explicitClient ?? defaultClientRef.current ?? undefined;

  // Side-pane filter subscription (only when configured).
  const paneFilters = useSidePaneFilter(
    sidePaneFilter?.paneId ?? '__no-pane__',
    sidePaneFilter?.translator ?? (() => [])
  );

  // Compose final hostFilters: pane-driven + additional. Memoized to avoid
  // re-triggering the DataGrid's FetchXML composition when contents are stable.
  const hostFilters = React.useMemo<ReadonlyArray<HostFilterCondition> | undefined>(() => {
    const a = sidePaneFilter ? paneFilters : [];
    const b = additionalHostFilters ?? [];
    if (a.length === 0 && b.length === 0) return undefined;
    return [...a, ...b];
  }, [sidePaneFilter, paneFilters, additionalHostFilters]);

  return (
    <FluentProvider theme={resolvedTheme} applyStylesToPortals={true} style={{ height: '100%' }}>
      <DataGrid
        {...dataGridProps}
        parentContext={parentContext}
        hostFilters={hostFilters}
        theme={resolvedTheme}
        dataverseClient={dataverseClient}
      />
    </FluentProvider>
  );
};

export default DataGridPageShell;
