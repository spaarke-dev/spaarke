/**
 * DataGridSidePaneOrchestrator — generalized side-pane lifecycle for any
 * Custom Page hosting `<DataGrid>` OR any other Fluent v9 host that needs
 * `Xrm.App.sidePanes` lifecycle management.
 *
 * Lifted from `src/solutions/EventsPage/src/calendarPaneOrchestrator.ts` in
 * task 035 UAT iteration 5 and made pane-agnostic. The same orchestrator now
 * powers ANY side-pane type — calendar, advanced filter, saved filters, AI
 * assistant, geographic, etc. — driven by `SidePaneSpec` at registration time.
 *
 * spaarke-side-pane-navigation-history-r1 task 011 reuses this class (rather
 * than forking a parallel implementation, per root CLAUDE.md §11 + the task's
 * NFR-08 reuse mandate) as the pane-lifecycle core for `SprkSidePaneHost` —
 * the app-wide, multi-contributor docked side-pane framework. That consumer
 * is SELF-HOSTED (it IS the pane's content, not a separate webresource being
 * opened INTO a pane the way EventsPage opens a detail pane), so two fields
 * were added additively:
 *  - `webResourceName` is now OPTIONAL — when omitted, `registerPane` skips
 *    the `pane.navigate(...)` call (there is nothing to navigate to; the
 *    caller's own React tree is already the pane's content).
 *  - `alwaysRender` is a new optional passthrough to `createPane` (spec MUST
 *    rule / NFR-05 for `SprkSidePaneHost`'s pane). Existing callers (e.g.
 *    EventsPage) that never set it see unchanged `createPane` behavior.
 * Both changes are additive/backward-compatible — no existing consumer
 * behavior changes.
 *
 * Responsibilities:
 *  - `Xrm.App.sidePanes.createPane` registration (idempotent — repeat registration
 *    of the same paneId is a no-op).
 *  - Mutual exclusivity: opening a pane can close other panes the spec declares
 *    as exclusive (preserves the EventsPage calendar/detail-pane UX).
 *  - Visibility lifecycle via `IntersectionObserver`: detects when the host
 *    iframe is hidden by an MDA form-tab switch and closes panes; re-registers
 *    them when the iframe becomes visible again.
 *  - `beforeunload` / `pagehide` cleanup on browser navigation.
 *
 * Designed to coexist with `useSidePaneFilter` — the orchestrator owns the
 * pane LIFECYCLE; the hook owns the filter MESSAGE flow. A typical host wires
 * both together; some hosts (where MDA's sitemap opens the pane on its own)
 * use only the hook.
 *
 * @see useSidePaneFilter (filter message subscription)
 * @see SidePaneFilterChannel (the underlying transport)
 * @see docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §5
 * @see SprkSidePaneHost (../../SidePane/SprkSidePaneHost.tsx) — the
 *      self-hosted, multi-contributor consumer added by task 011.
 */

// Task 011 wires the orchestrator to the WIDENED `xrmContext.ts` `getXrm()`
// (task 010: typed 3-frame window→parent→top walk, `SidePanesApi.getPane`,
// `SidePane.select()`, `CreatePaneOptions.alwaysRender`) instead of the
// untyped `services/xrmGlobal.ts` walker it previously used. Runtime
// semantics are equivalent (both walk window→parent→top and require
// `WebApi` to consider a frame's `Xrm` usable); this only gains type
// coverage for the `xrm.App.sidePanes.*` calls below.
import { getXrm } from '../../../utils/xrmContext';

/**
 * Declaration of a side pane that the orchestrator should manage.
 */
export interface SidePaneSpec {
  /** Unique pane identifier (also used as the SidePaneFilterChannel key). */
  paneId: string;
  /** Title shown in the pane's chrome. */
  title: string;
  /**
   * Name of the web resource to navigate to (e.g. `'sprk_calendarsidepane.html'`).
   * OPTIONAL (task 011) — omit when the caller's own React tree IS the pane's
   * content (a self-hosted pane, e.g. `SprkSidePaneHost`) and there is nothing
   * to navigate to. When omitted, `registerPane` skips the `pane.navigate(...)`
   * call entirely.
   */
  webResourceName?: string;
  /** Pane width in pixels. Default 340. */
  width?: number;
  /** Optional MDA icon reference (e.g. `'WebResources/sprk_calendarline_24'`). */
  iconName?: string;
  /** Whether the user can close the pane with the X button. Default `true`. */
  canClose?: boolean;
  /** Whether the pane is initially selected (focused). Default `true`. */
  isSelected?: boolean;
  /**
   * Passthrough to `Xrm.App.sidePanes.createPane`'s `alwaysRender` option
   * (task 011 — added for `SprkSidePaneHost`'s spec MUST rule / NFR-05).
   * Default `undefined` (platform default) — existing callers are unaffected.
   */
  alwaysRender?: boolean;
  /**
   * Pane IDs that should be CLOSED when this pane opens. Implements mutual
   * exclusivity (e.g. opening an Event Detail pane closes the Calendar pane).
   */
  mutuallyExclusiveWith?: ReadonlyArray<string>;
}

/**
 * Side-pane lifecycle manager. Instantiate once per Custom Page (typically
 * via `React.useMemo`) and call `registerPane` + `attachVisibilityLifecycle`
 * from a `useEffect`.
 */
export class DataGridSidePaneOrchestrator {
  private registeredPanes = new Map<string, SidePaneSpec>();
  private detachLifecycle: (() => void) | null = null;
  private detachBrowserCleanup: (() => void) | null = null;

  /**
   * Register a side pane with Xrm. Idempotent — calling twice with the same
   * paneId no-ops (the existing pane is just re-selected if already open).
   *
   * Honors `mutuallyExclusiveWith` — other listed panes are closed before
   * this one opens.
   *
   * Silently no-ops when `Xrm.App.sidePanes` is unavailable (Storybook,
   * non-MDA hosts).
   */
  async registerPane(spec: SidePaneSpec): Promise<void> {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const xrm: any = getXrm();
    if (!xrm?.App?.sidePanes) return;

    this.registeredPanes.set(spec.paneId, spec);

    if (spec.mutuallyExclusiveWith) {
      for (const otherId of spec.mutuallyExclusiveWith) {
        this.closePane(otherId);
      }
    }

    try {
      const existing = xrm.App.sidePanes.getPane(spec.paneId);
      if (existing) {
        existing.select?.();
        return;
      }
      const pane = await xrm.App.sidePanes.createPane({
        title: spec.title,
        paneId: spec.paneId,
        canClose: spec.canClose ?? true,
        width: spec.width ?? 340,
        isSelected: spec.isSelected ?? true,
        imageSrc: spec.iconName,
        alwaysRender: spec.alwaysRender,
      });
      // Self-hosted panes (task 011, e.g. SprkSidePaneHost) omit webResourceName —
      // the caller's own React tree is already the pane's content, so there is
      // nothing to navigate to.
      if (spec.webResourceName) {
        await pane.navigate({ pageType: 'webresource', webresourceName: spec.webResourceName });
      }
    } catch (error) {
      // eslint-disable-next-line no-console
      console.error(`[DataGridSidePaneOrchestrator] register '${spec.paneId}' failed:`, error);
    }
    /* eslint-enable @typescript-eslint/no-explicit-any */
  }

  /**
   * Re-open / re-select a previously registered pane (useful after a
   * visibility lifecycle restore).
   */
  async openPane(paneId: string): Promise<void> {
    const spec = this.registeredPanes.get(paneId);
    if (!spec) return;
    await this.registerPane(spec);
  }

  /**
   * Close a pane by id. Best-effort — silent when the pane doesn't exist
   * or Xrm is unavailable.
   */
  closePane(paneId: string): void {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const xrm: any = getXrm();
    try {
      xrm?.App?.sidePanes?.getPane?.(paneId)?.close?.();
    } catch {
      /* best-effort */
    }
    /* eslint-enable @typescript-eslint/no-explicit-any */
  }

  /**
   * Close every pane this orchestrator has registered. Use on host unmount
   * + on `beforeunload` (the lifecycle wireup below does this automatically).
   */
  closeAll(): void {
    for (const paneId of this.registeredPanes.keys()) {
      this.closePane(paneId);
    }
  }

  /**
   * Attach visibility + browser-navigation lifecycle:
   *  - IntersectionObserver on `rootElement` detects when the host iframe
   *    becomes hidden (MDA form-tab switch) and calls `closeAll`. When the
   *    iframe returns visible, all previously registered panes are re-registered.
   *  - `beforeunload` + `pagehide` listeners close all panes on browser navigation.
   *
   * Returns a single detach function that tears down both observer + listeners.
   * Idempotent — calling attach twice replaces the prior attachment.
   *
   * Typical usage:
   * ```tsx
   * React.useEffect(() => {
   *   const root = document.getElementById('root');
   *   if (root) return orchestrator.attachVisibilityLifecycle(root);
   * }, []);
   * ```
   */
  attachVisibilityLifecycle(rootElement: HTMLElement): () => void {
    // Tear down any prior attachment.
    this.detachLifecycle?.();
    this.detachBrowserCleanup?.();

    let visibilityObserver: IntersectionObserver | null = null;
    if (typeof IntersectionObserver !== 'undefined') {
      let wasVisible = true;
      visibilityObserver = new IntersectionObserver(
        entries => {
          for (const entry of entries) {
            const visible = entry.isIntersecting && entry.intersectionRatio > 0;
            if (wasVisible && !visible) {
              this.closeAll();
            } else if (!wasVisible && visible) {
              for (const spec of this.registeredPanes.values()) {
                void this.registerPane(spec);
              }
            }
            wasVisible = visible;
          }
        },
        { threshold: 0 }
      );
      visibilityObserver.observe(rootElement);
    }

    const closeOnNav = () => this.closeAll();
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', closeOnNav);
      window.addEventListener('pagehide', closeOnNav);
    }

    this.detachLifecycle = () => {
      visibilityObserver?.disconnect();
    };
    this.detachBrowserCleanup = () => {
      if (typeof window !== 'undefined') {
        window.removeEventListener('beforeunload', closeOnNav);
        window.removeEventListener('pagehide', closeOnNav);
      }
    };

    return () => {
      this.detachLifecycle?.();
      this.detachBrowserCleanup?.();
      this.detachLifecycle = null;
      this.detachBrowserCleanup = null;
      this.closeAll();
    };
  }
}
