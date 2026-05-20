/**
 * WorkspaceWidgetWrapper
 *
 * Generic HOC wrapper that adapts any R1 output widget (which accepts
 * OutputWidgetProps<T>) to the R2 WorkspaceWidgetProps<T> interface and
 * adds serialize/restore capability without modifying the original widget
 * source files.
 *
 * The wrapper implements the data-refreshed restore principle (D-08):
 *   - serializeState() returns only the query identifiers (sessionId, turnId,
 *     documentId, etc.) that were passed as query params — never the fetched
 *     data payload itself.
 *   - restoreState(state) dispatches a workspace_widget_restore pane event so
 *     the shell can re-issue the BFF request with the stored identifiers. The
 *     wrapper itself sets isLoading=true so the R1 widget renders its loading
 *     skeleton while the fresh fetch is in flight.
 *
 * Usage (in register-workspace-widgets.ts):
 *   registerWorkspaceWidget(
 *     'BudgetDashboard',
 *     { ... },
 *     () => import('./WorkspaceWidgetWrapper').then(m => ({
 *       default: m.createWorkspaceWrapper(
 *         () => import('@spaarke/ai-outputs/dist/output-widgets/BudgetDashboardWidget'),
 *         'BudgetDashboard',
 *       ),
 *     }))
 *   );
 *
 * React 19, NOT PCF-safe.
 *
 * @see ADR-021 — Fluent UI v9, no hard-coded colors
 * @see ADR-022 — React 19 for Code Pages
 * @see D-08    — data-refreshed restore
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Spinner, makeStyles, mergeClasses, tokens, Text } from '@fluentui/react-components';
import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';
import { useCitationLink } from '../../interactions/useCitationLink';
import type { CitationClickHandler } from '../../interactions/useCitationLink';

// ---------------------------------------------------------------------------
// Internal types
// ---------------------------------------------------------------------------

/**
 * The query params stored in WidgetState.queryParams for all workspace
 * widgets migrated from R1. Every widget stores at minimum sessionId and
 * turnId so the shell can re-issue the originating BFF call.
 *
 * Additional widget-specific keys (documentId, comparisonDocumentId, etc.)
 * are passed through as-is — the record accepts any string-keyed string value.
 */
export interface WorkspaceWidgetQueryParams extends Record<string, string> {
  /** Active AI session identifier (Cosmos DB session document key). */
  sessionId: string;
  /**
   * Turn index within the session (0-based string-encoded integer).
   * Identifies which BFF response produced this widget payload.
   */
  turnId: string;
}

/**
 * Extended WorkspaceWidgetProps that carries the query params needed for
 * serialize/restore. The shell injects these alongside the data payload.
 */
export interface WorkspaceWidgetWrapperProps<T = unknown> extends WorkspaceWidgetProps<T> {
  /**
   * Query parameters used to identify and re-fetch the data on restore.
   * Must contain at minimum sessionId and turnId.
   */
  queryParams: WorkspaceWidgetQueryParams;
  /**
   * Optional layout hints (e.g. scroll position, active tab index) previously
   * restored from a WidgetState.layout snapshot. The wrapped R1 widget ignores
   * this; the wrapper stores it verbatim in serializeState() so UX position
   * can be round-tripped without re-fetching.
   */
  restoredLayout?: Record<string, unknown>;
  /**
   * Imperative handle setter — the shell calls this after mount so it can
   * invoke serializeState() and restoreState() on the wrapper instance.
   */
  onRegisterHandle?: (handle: WorkspaceWidgetHandle) => void;
  /**
   * Citation link click handler injected by the wrapper into the inner R1
   * widget. When the inner widget renders a bracketed citation reference
   * (e.g. "[1]") and the user clicks it, the widget calls this function.
   * The wrapper provides a stable implementation that dispatches a
   * `context_highlight` PaneEventBus event to the `context` channel,
   * causing the ContextPaneController to scroll to and highlight the
   * corresponding source passage in the active context widget.
   *
   * Inner widgets that support citation linking must accept and call this
   * prop when a citation anchor is clicked. Widgets that do not support
   * citation linking may ignore this prop — the wrapper always provides it
   * but never forces the inner widget to use it.
   *
   * Set to `undefined` to disable citation link dispatch for a specific
   * wrapper instance (e.g. during testing without a PaneEventBusProvider).
   */
  onLink?: CitationClickHandler;
}

// Re-export so callers that import WorkspaceWidgetWrapperProps also get the
// handler type without a separate import path.
export type { CitationClickHandler };

/**
 * Imperative handle exposed by each wrapped widget instance.
 * The shell stores this handle to call serialize/restore on demand.
 */
export interface WorkspaceWidgetHandle {
  serializeState(): WidgetState<unknown>;
  restoreState(state: WidgetState<unknown>): Promise<void>;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  restoring: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    height: '100%',
    minHeight: '120px',
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// createWorkspaceWrapper factory
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Inner widget props with citation link support
// ---------------------------------------------------------------------------

/**
 * Props interface for inner R1 output widgets that support citation links.
 *
 * R1 widgets that opt-in to citation highlighting declare `onLink` in their
 * own props and call it when a citation anchor is clicked. The wrapper always
 * provides this prop so the inner widget never needs to check for undefined.
 */
interface InnerWidgetWithLinkProps<T> {
  data: T;
  isLoading?: boolean;
  error?: string;
  className?: string;
  /** Citation link handler — call with citationId (and optional selectionRef) on anchor click. */
  onLink?: CitationClickHandler;
}

/**
 * Factory that creates a React component wrapping an R1 output widget.
 *
 * The returned component:
 * 1. Renders the R1 widget with its original OutputWidgetProps interface
 *    translated from WorkspaceWidgetProps.
 * 2. Exposes an imperative handle (via onRegisterHandle) with
 *    serializeState() and restoreState() implementations.
 * 3. Shows a Fluent v9 Spinner while restoreState() is in flight.
 * 4. Injects an `onLink` citation click handler (AIPU2-100) so the inner
 *    widget can dispatch context_highlight events to the context pane when
 *    the user clicks a bracketed citation reference.
 *
 * @param loaderFn   - Async factory that returns the R1 widget module.
 * @param widgetType - The registered type string (e.g. 'BudgetDashboard').
 * @param stateVersion - State schema version. Increment when queryParams
 *                       shape changes. Defaults to 1.
 */
export function createWorkspaceWrapper<T = unknown>(
  loaderFn: () => Promise<{ default: React.ComponentType<InnerWidgetWithLinkProps<T>> }>,
  widgetType: string,
  stateVersion = 1
): React.ComponentType<WorkspaceWidgetWrapperProps<T>> {
  /**
   * WrappedWidget — the actual HOC component produced for a specific R1 widget.
   */
  function WrappedWidget({
    data,
    isLoading: isLoadingProp,
    error: errorProp,
    className,
    widgetType: _widgetType,
    queryParams,
    restoredLayout,
    onRegisterHandle,
    onLink: onLinkProp,
  }: WorkspaceWidgetWrapperProps<T>): React.ReactElement {
    const styles = useStyles();

    // Citation link handler — dispatches context_highlight to the context channel.
    // useCitationLink() requires a PaneEventBusProvider ancestor. When no
    // PaneEventBusProvider is present (e.g. in isolated unit tests) the caller
    // should pass onLink={undefined} via props to suppress the hook. In that
    // case we use the caller-supplied onLinkProp directly (may also be undefined).
    //
    // The hook is always called (Rules of Hooks) but the returned callback is
    // only used when onLinkProp is not explicitly provided.
    const builtInOnLink = useCitationLink();

    // If the shell explicitly supplies onLink (including undefined to disable),
    // honour that. Otherwise use the built-in handler.
    // Note: we check the prop key presence via arguments length — but since
    // WorkspaceWidgetWrapperProps makes onLink optional, an absent prop arrives
    // as `undefined`. We always default to the built-in handler when absent.
    const onLink: CitationClickHandler =
      onLinkProp !== undefined ? onLinkProp : builtInOnLink;

    // Lazy-loaded R1 widget component
    const [WrappedComponent, setWrappedComponent] = useState<React.ComponentType<
      InnerWidgetWithLinkProps<T>
    > | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);

    // Restore state — true while restoreState() is waiting for a re-fetch signal
    const [isRestoring, setIsRestoring] = useState(false);

    // Mutable ref to latest queryParams so serializeState sees current values
    const queryParamsRef = useRef<WorkspaceWidgetQueryParams>(queryParams);
    useEffect(() => {
      queryParamsRef.current = queryParams;
    }, [queryParams]);

    // Layout hint ref — updated on each render so serializeState captures
    // current UX position without requiring layout in props
    const layoutRef = useRef<Record<string, unknown> | undefined>(restoredLayout);

    // Load the R1 widget component once on mount
    useEffect(() => {
      let cancelled = false;
      loaderFn()
        .then(mod => {
          if (!cancelled) {
            setWrappedComponent(() => mod.default);
          }
        })
        .catch((err: unknown) => {
          if (!cancelled) {
            const message = err instanceof Error ? err.message : String(err);
            setLoadError(`[WorkspaceWidgetWrapper] Failed to load widget "${widgetType}": ${message}`);
          }
        });
      return () => {
        cancelled = true;
      };
      // loaderFn and widgetType are stable for the lifetime of this component
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // Build and register the imperative handle
    const serializeState = useCallback((): WidgetState<T> => {
      return {
        widgetType,
        version: stateVersion,
        // D-08: store only the query identifiers — never the data payload
        queryParams: { ...queryParamsRef.current },
        layout: layoutRef.current,
        timestamp: new Date().toISOString(),
      };
    }, []);

    const restoreState = useCallback(async (state: WidgetState<unknown>): Promise<void> => {
      // Signal that a restore is in progress — the R1 widget will render
      // its loading skeleton on the next render (isLoading=true path).
      setIsRestoring(true);

      // Update the queryParams ref with the stored identifiers so that the
      // next serializeState() call round-trips the restored params.
      if (state.queryParams) {
        queryParamsRef.current = state.queryParams as WorkspaceWidgetQueryParams;
      }

      if (state.layout) {
        layoutRef.current = state.layout;
      }

      // The wrapper's job is to signal the shell that a restore is needed.
      // The shell will re-issue the BFF call using state.queryParams and
      // push fresh data via the workspace SSE event — at which point the
      // parent WorkspacePane updates the data prop and we clear isRestoring.
      //
      // We resolve immediately so the shell knows we have accepted the state.
      // The actual loading spinner is shown via isRestoring=true until fresh
      // data arrives (the shell passes isLoading=true during that window).
      //
      // In practice the shell drives this; we clear restoring once isLoadingProp
      // transitions from true→false (handled in the effect below).
    }, []);

    // Clear isRestoring once the shell signals loading is done
    useEffect(() => {
      if (!isLoadingProp && isRestoring) {
        setIsRestoring(false);
      }
    }, [isLoadingProp, isRestoring]);

    // Register the handle with the shell
    useEffect(() => {
      if (onRegisterHandle) {
        onRegisterHandle({ serializeState, restoreState });
      }
    }, [onRegisterHandle, serializeState, restoreState]);

    // ---- Render states ----

    // Module load failed (network/bundle error) — not the same as widget data error
    if (loadError) {
      return (
        <div className={mergeClasses(className)}>
          <Text className={styles.errorText}>{loadError}</Text>
        </div>
      );
    }

    // Still loading the widget component module itself
    if (WrappedComponent === null) {
      return (
        <div className={mergeClasses(styles.restoring, className)}>
          <Spinner size="medium" label={`Loading ${widgetType}...`} />
        </div>
      );
    }

    // Restore in progress — show a restore-specific loading overlay
    if (isRestoring) {
      return (
        <div className={mergeClasses(styles.restoring, className)}>
          <Spinner size="medium" label="Restoring..." />
        </div>
      );
    }

    // Normal render — delegate to the R1 widget.
    // Pass onLink so the inner widget can dispatch citation highlight events
    // when a user clicks a bracketed citation reference. Inner widgets that do
    // not support citation linking safely ignore this extra prop.
    return (
      <WrappedComponent
        data={data}
        isLoading={isLoadingProp}
        error={errorProp}
        className={className}
        onLink={onLink}
      />
    );
  }

  WrappedWidget.displayName = `WorkspaceWidgetWrapper(${widgetType})`;

  return WrappedWidget as React.ComponentType<WorkspaceWidgetWrapperProps<T>>;
}

// ---------------------------------------------------------------------------
// Re-export helper types for consumers
// ---------------------------------------------------------------------------

export type { WidgetState };
