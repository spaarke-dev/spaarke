/**
 * SprkSidePaneHost — the reusable, multi-contributor docked side-pane host
 * (spaarke-side-pane-navigation-history-r1 task 011, spec FR-01).
 *
 * WRAPS `SidePaneShell` for chrome (header/scroll/footer layout) rather than
 * extending it — `SidePaneShell` stays presentational-only (OQ-9 resolved).
 * Adds:
 *  - A right-rail icon strip driven by {@link listSidePaneRegistryEntries}
 *    (`sidePaneRegistry.ts`) — one entry = one icon = one contributor. The
 *    active contributor's component renders inside the `SidePaneShell`.
 *  - `Xrm.App.sidePanes` pane lifecycle (idempotent create, close-on-navigate,
 *    unload cleanup) via `DataGridSidePaneOrchestrator` (GENERALIZED, not
 *    forked — see that file's header comment for the task-011 additive
 *    changes: optional `webResourceName`, new `alwaysRender`). Root
 *    CLAUDE.md §11 / NFR-08: reuse, don't reinvent createPane/close-on-
 *    navigate/unload plumbing.
 *  - Theme resolution (`resolveCodePageTheme` + `setupCodePageThemeListener`,
 *    the same mechanism `DataGridPageShell` uses) combined with the
 *    `--sprk-ui-scale` scaled theme (`useUiScale` + `scaleTheme`, NFR-07),
 *    fed into this host's OWN root `FluentProvider` (this host runs inside
 *    its own pane webresource iframe — a separate document from the MDA
 *    shell — so it is a root provider, not a nested one, mirroring
 *    `DataGridPageShell`).
 *  - ADR-021 portal re-wrap: the rail icons' `Tooltip` content is portalled;
 *    per the ADR-021 portal-gotcha pattern, iframe/host-bridge scenarios
 *    require the EXPLICIT re-wrap (Option A), not just the root provider's
 *    `applyStylesToPortals` default — so each Tooltip's content is
 *    explicitly re-wrapped in its own `FluentProvider` (matching the
 *    `ColumnHeaderMenu` / `ViewSelector` / `CommandBar` precedent for
 *    portal-bearing surfaces).
 *
 * Unknown/unregistered contributor ids degrade to a benign empty state and
 * never throw (negative acceptance criterion).
 *
 * @see SidePaneShell (chrome, wrapped not extended)
 * @see DataGridSidePaneOrchestrator (pane lifecycle, generalized not forked)
 * @see sidePaneRegistry (contributor registration table)
 * @see ADR-021 (Fluent v9 tokens, portal re-wrap, light/dark, --sprk-ui-scale)
 * @see ADR-022 (React-16/17-safe shared-lib code)
 */

import * as React from 'react';
import { Button, FluentProvider, Tooltip, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import { SidePaneShell } from './SidePaneShell';
import { PaneHeader } from '../PaneHeader/PaneHeader';
import {
  listSidePaneRegistryEntries,
  resolveSidePaneContributor,
  type SidePaneContributorComponent,
} from './sidePaneRegistry';
import { DataGridSidePaneOrchestrator } from '../DataGrid/sidePane/DataGridSidePaneOrchestrator';
import { resolveCodePageTheme, setupCodePageThemeListener } from '../../utils/themeStorage';
import { useUiScale } from '../../hooks/useUiScale';
import { scaleTheme } from '../SprkModal/scaledTheme';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface SprkSidePaneHostProps {
  /** The `Xrm.App.sidePanes` pane id this host manages. */
  paneId?: string;
  /** Pane title shown in the Xrm pane launcher chrome. */
  paneTitle?: string;
  /** Pane width in pixels, forwarded to `createPane`. Default 340. */
  width?: number;
  /**
   * Initially active contributor id. Defaults to the lowest-`order`
   * registered entry. An id that is not registered degrades to the benign
   * empty state (never throws) — useful for exercising the negative path.
   */
  defaultActiveId?: string;
  /**
   * Whether this host owns the `Xrm.App.sidePanes` pane LIFECYCLE (create +
   * close-on-navigate + visibility). Default `true` (self-hosted: the host
   * creates and manages its own pane).
   *
   * Set `false` when the host is rendered as the CONTENT of a pane that some
   * OTHER agent already created — e.g. the Path B app-startup bootstrap
   * (`sprk_SidePaneManager`) that `createPane`s the docked Navigator pane and
   * `navigate`s it to this bundle's webresource. In that EMBEDDED case the
   * host must NOT create a second pane: doing so spawns a rival empty pane and
   * the two contend for selection, producing a load/unload loop
   * (spaarke-side-pane-navigation-history-r1 task 086 UAT). Embedded mode still
   * renders the full host UI (FluentProvider root, rail, contributor) — it only
   * skips the pane-creation side effect.
   */
  managePane?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    height: '100%',
    width: '100%',
  },
  layout: {
    display: 'flex',
    flexDirection: 'row',
    height: '100%',
    width: '100%',
    minHeight: 0,
  },
  content: {
    flexGrow: 1,
    minWidth: 0,
    height: '100%',
  },
  emptyState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalL),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    textAlign: 'center',
  },
  rail: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    flexShrink: 0,
    width: '40px',
    height: '100%',
    ...shorthands.gap(tokens.spacingVerticalXS),
    ...shorthands.padding(tokens.spacingVerticalS, '0'),
    ...shorthands.borderLeft('1px', 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_PANE_ID = 'sprk-sidepane-host';
const DEFAULT_PANE_TITLE = 'Spaarke';
const DEFAULT_WIDTH = 340;

export const SprkSidePaneHost: React.FC<SprkSidePaneHostProps> = ({
  paneId = DEFAULT_PANE_ID,
  paneTitle = DEFAULT_PANE_TITLE,
  width = DEFAULT_WIDTH,
  defaultActiveId,
  managePane = true,
}) => {
  const styles = useStyles();
  const layoutRef = React.useRef<HTMLDivElement>(null);

  // Read the CURRENT registry snapshot every render (cheap Map iteration +
  // sort) rather than memoizing with an empty dep array — contributors may
  // register after this host's module evaluates (registration order is not
  // guaranteed), and tests exercise add/remove-then-rerender.
  const entries = listSidePaneRegistryEntries();

  const [activeId, setActiveId] = React.useState<string | undefined>(() => defaultActiveId ?? entries[0]?.id);

  // If the active id was never set (no entries at mount) but entries have
  // since appeared, or the active id fell out of the registry, fall back to
  // the first available entry — still never throws when entries is empty.
  React.useEffect(() => {
    if (activeId && entries.some(e => e.id === activeId)) return;
    if (!activeId && entries[0]) {
      setActiveId(entries[0].id);
    }
    // Intentionally NOT depending on `entries` (a fresh array every render) —
    // only re-evaluate when the active id itself changes to/from an
    // unregistered value. Tests exercise add/remove via remount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId]);

  // ── Theme + --sprk-ui-scale (ADR-021 / NFR-07) ──────────────────────────
  const [hostTheme, setHostTheme] = React.useState(() => resolveCodePageTheme());
  const { uiScale } = useUiScale();
  const resolvedTheme = React.useMemo(() => scaleTheme(hostTheme, uiScale), [hostTheme, uiScale]);

  React.useEffect(() => setupCodePageThemeListener(setHostTheme), []);

  // The SAME uiScale value also sets the `--sprk-ui-scale` CSS variable
  // (design §6.9 / NFR-07) on THIS document's root — matching the
  // SpaarkeAi App.tsx / LegalWorkspaceApp.tsx app-shell precedent. This
  // host runs inside its own pane webresource iframe (a separate document
  // from the MDA shell), so `document` here is the pane's own document —
  // the correct portal-safe root for any `SprkModal` a contributor renders.
  React.useEffect(() => {
    document.documentElement.style.setProperty('--sprk-ui-scale', String(uiScale));
  }, [uiScale]);

  // ── Pane lifecycle (reused, not reinvented — see file header) ──────────
  const orchestrator = React.useMemo(() => new DataGridSidePaneOrchestrator(), []);

  React.useEffect(() => {
    // EMBEDDED mode (managePane === false): this host is the CONTENT of a pane
    // some other agent already created (the Path B bootstrap). It must NOT
    // create a second pane — doing so spawns a rival empty pane that contends
    // for selection and produces a load/unload loop (task 086 UAT). Skip the
    // entire pane-lifecycle side effect; the host still renders its UI below.
    if (!managePane) return undefined;

    // Self-hosted pane: no webResourceName (this component IS the content).
    // MUST rule / NFR-05: canClose:false + alwaysRender:true.
    void orchestrator.registerPane({
      paneId,
      title: paneTitle,
      width,
      canClose: false,
      alwaysRender: true,
      isSelected: true,
    });

    const root = layoutRef.current;
    const detach = root ? orchestrator.attachVisibilityLifecycle(root) : undefined;
    return () => {
      detach?.();
    };
  }, [orchestrator, paneId, paneTitle, width, managePane]);

  // ── Active contributor resolution (lazy; unknown id -> undefined, never throws) ──
  const [ActiveComponent, setActiveComponent] = React.useState<SidePaneContributorComponent | null>(null);

  React.useEffect(() => {
    let cancelled = false;
    if (!activeId) {
      setActiveComponent(null);
      return undefined;
    }
    resolveSidePaneContributor(activeId)
      .then(component => {
        if (!cancelled) setActiveComponent(() => component ?? null);
      })
      .catch(() => {
        if (!cancelled) setActiveComponent(null);
      });
    return () => {
      cancelled = true;
    };
  }, [activeId]);

  const activeEntry = entries.find(e => e.id === activeId);

  return (
    <FluentProvider theme={resolvedTheme} applyStylesToPortals={true} className={styles.root}>
      <div ref={layoutRef} className={styles.layout} data-testid="sprk-sidepane-host">
        <div className={styles.content}>
          <SidePaneShell header={<PaneHeader title={activeEntry?.title ?? paneTitle} icon={activeEntry?.icon} />} footer={<></>}>
            {ActiveComponent ? (
              <ActiveComponent paneId={paneId} />
            ) : (
              <div className={styles.emptyState} data-testid="sprk-sidepane-host-empty-state">
                {entries.length === 0
                  ? 'No side-pane sections are registered.'
                  : 'This section is unavailable.'}
              </div>
            )}
          </SidePaneShell>
        </div>

        {entries.length > 0 && (
          <div className={styles.rail} role="tablist" aria-label="Side pane sections" data-testid="sprk-sidepane-host-rail">
            {entries.map(entry => (
              <Tooltip
                key={entry.id}
                relationship="label"
                withArrow={false}
                content={
                  // ADR-021 portal re-wrap (Option A): this Tooltip renders
                  // through a Portal; the iframe/host-bridge note in the
                  // fluent-v9-portal-gotcha pattern requires an EXPLICIT
                  // re-wrap here rather than relying on the root provider's
                  // `applyStylesToPortals` default alone.
                  <FluentProvider theme={resolvedTheme} applyStylesToPortals={true}>
                    <span>{entry.title}</span>
                  </FluentProvider>
                }
              >
                <Button
                  appearance={entry.id === activeId ? 'primary' : 'subtle'}
                  icon={entry.icon}
                  aria-label={entry.title}
                  aria-selected={entry.id === activeId}
                  role="tab"
                  onClick={() => setActiveId(entry.id)}
                  data-testid={`sprk-sidepane-rail-icon-${entry.id}`}
                />
              </Tooltip>
            ))}
          </div>
        )}
      </div>
    </FluentProvider>
  );
};

export default SprkSidePaneHost;
