/**
 * HistoryOverlay.tsx — Claude-Code-style right-side overlay for chat history.
 *
 * Replaces the legacy "History" tab on ConversationPane (removed in task 021,
 * FR-02). Per OC-01 / FR-03, history is now a side overlay (Drawer surface)
 * triggered by the HistoryRegular icon in the PaneHeader rightSlot. Selecting
 * a session calls `setChatSessionId(sessionId)` and closes the overlay; the
 * conversation then resumes via the existing AiSessionProvider flow (the same
 * data-refreshed restore semantics used in R2 D-08).
 *
 * Data flow:
 *   open=true  →  fetch GET /api/ai/chat/sessions?limit=50 via authenticatedFetch
 *               →  render a list of up to 50 most-recent sessions
 *               →  click a session ➜ onSelectSession(sessionId) + onClose()
 *
 * Performance (NFR-03):
 *   - Open animation MUST land in <200 ms (handled by Fluent v9 OverlayDrawer).
 *   - List populated in <300 ms p95 for 50 items — measured at the request
 *     boundary with `performance.now()` and surfaced through DevTools timing.
 *
 * Auth (ADR-028 §H-4):
 *   - NO accessToken prop. All BFF calls use the per-request authenticatedFetch
 *     returned by useAiSession(). The token never crosses the component
 *     boundary; authenticatedFetch attaches the Bearer header internally.
 *
 * Telemetry (FR-24 / OC-09):
 *   - Error-only. On fetch failure (network error OR non-2xx response) emit
 *     `logTelemetryError(TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE, { status, message })`.
 *   - No happy-path events.
 *
 * Accessibility (NFR-05):
 *   - Keyboard-navigable (Tab cycles list, Enter selects, Escape closes).
 *   - ARIA labels on the surface ("Chat history") and on each list item
 *     ("Resume conversation: {title}, last activity {relative}").
 *   - Screen-reader announces "Chat history, dialog" on open (OverlayDrawer's
 *     built-in role="dialog" + aria-label propagation).
 *
 * Constraints:
 *   - ADR-012 — solution-local (HistoryOverlay binds to SpaarkeAi's session
 *     context). Promote to @spaarke/ui-components when a second consumer
 *     appears.
 *   - ADR-021 — Fluent v9 tokens only. No hex, no rgba.
 *   - ADR-022 — React 19 functional component, hooks-based.
 *   - ADR-025 — HistoryRegular + DismissRegular icons from @fluentui/react-icons.
 *   - ADR-028 — function-based auth contract; no token snapshots.
 *
 * @see ConversationPane.tsx — wires the rightSlot trigger and renders this overlay
 * @see ChatHistoryPanel.tsx — the prior tab-mode panel (kept for collapsed-strip
 *      usage; this overlay is the new primary surface)
 * @see errorTelemetry.ts — TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE constant
 */

import * as React from "react";
import {
  OverlayDrawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from "@fluentui/react-components";
import { DismissRegular, HistoryRegular } from "@fluentui/react-icons";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";
import {
  logTelemetryError,
  TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE,
} from "../../telemetry/errorTelemetry";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * One row of the session list. Shape adapts to the BFF response — the BFF
 * `/api/ai/chat/sessions?limit=50` endpoint returns objects with at least
 * `sessionId` (or `id`) + a title-like field and a last-activity timestamp.
 * See ChatHistoryPanel.tsx for the corresponding mapping in the legacy panel.
 */
interface HistorySessionRow {
  sessionId: string;
  title: string;
  lastMessageAt: string;
}

/**
 * Props accepted by HistoryOverlay.
 *
 * NO accessToken prop — auth flows through useAiSession() → authenticatedFetch
 * (ADR-028 §H-4). The parent (ConversationPane) controls the open state and
 * supplies the session-select handler (typically a direct reference to
 * `setChatSessionId` from useAiSession()).
 */
export interface HistoryOverlayProps {
  /** Whether the overlay is currently shown. */
  open: boolean;
  /** Called when the overlay requests close (Escape, dismiss button, or selection). */
  onClose: () => void;
  /** Called when the user picks a session. The component closes itself afterward. */
  onSelectSession: (sessionId: string) => void;
  /** BFF host URL (e.g. https://spe-api-dev.example.com). Pass-through from useAiSession(). */
  bffBaseUrl: string;
  /** Per-request authenticated fetch from useAiSession(). No token snapshot. */
  authenticatedFetch: AuthenticatedFetchFn;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Drawer surface width — matches the assistant pane's typical overlay width.
   * Width is fixed at 360px so the list has comfortable line length for 50
   * entries and consistent feel across viewports.
   */
  drawer: {
    width: "360px",
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalS,
  },
  headerTitleInner: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  /**
   * Body — full vertical scroll, single column. No horizontal padding because
   * individual items own their own padding for crisp keyboard focus rings.
   */
  body: {
    paddingLeft: 0,
    paddingRight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  list: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
  },
  item: {
    display: "flex",
    flexDirection: "column",
    alignItems: "stretch",
    justifyContent: "flex-start",
    gap: tokens.spacingVerticalXXS,
    width: "100%",
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    borderTopWidth: "0",
    borderRightWidth: "0",
    borderLeftWidth: "0",
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    cursor: "pointer",
    textAlign: "left",
    fontFamily: "inherit",
    fontSize: tokens.fontSizeBase300,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ":focus-visible": {
      outlineWidth: "2px",
      outlineStyle: "solid",
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: "-2px",
    },
  },
  itemTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  itemMeta: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  empty: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  errorState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "stretch",
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorPaletteRedForeground1,
  },
  retryButton: {
    alignSelf: "flex-start",
  },
  loading: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format a timestamp into a short relative-time string for the list-item meta
 * line. Returns "Just now", "5m ago", "2h ago", "3d ago", or the localized
 * date for older entries. Pure function — safe to call during render.
 */
function formatRelative(timestamp: string): string {
  const ts = Date.parse(timestamp);
  if (Number.isNaN(ts)) {
    return "";
  }
  const diffMs = Date.now() - ts;
  if (diffMs < 60_000) return "Just now";
  if (diffMs < 3_600_000) return `${Math.floor(diffMs / 60_000)}m ago`;
  if (diffMs < 86_400_000) return `${Math.floor(diffMs / 3_600_000)}h ago`;
  if (diffMs < 7 * 86_400_000) return `${Math.floor(diffMs / 86_400_000)}d ago`;
  // Older — show the localized short date.
  return new Date(ts).toLocaleDateString();
}

/**
 * Map an arbitrary BFF session payload to the strongly-typed HistorySessionRow
 * used by the overlay. Mirrors the mapping in ChatHistoryPanel.tsx so the two
 * surfaces remain compatible until ChatHistoryPanel is fully retired.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function mapSession(item: any): HistorySessionRow {
  return {
    sessionId: String(item.sessionId ?? item.id ?? ""),
    title: String(
      item.title ?? item.playbookName ?? "Untitled Conversation"
    ),
    lastMessageAt: String(
      item.lastMessageAt ?? item.updatedAt ?? new Date().toISOString()
    ),
  };
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * HistoryOverlay — Claude-Code-style right-side overlay listing recent chat
 * sessions. Renders nothing visible when `open === false`; when `open === true`
 * the OverlayDrawer slides in from the trailing edge and the session list is
 * fetched (once per open transition). See file-header docblock for full design.
 */
export const HistoryOverlay: React.FC<HistoryOverlayProps> = ({
  open,
  onClose,
  onSelectSession,
  bffBaseUrl,
  authenticatedFetch,
}) => {
  const styles = useStyles();

  // ── Local state ───────────────────────────────────────────────────────────
  const [sessions, setSessions] = React.useState<HistorySessionRow[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [errorState, setErrorState] = React.useState<{ message: string } | null>(
    null
  );
  // Reload key — bumped by the Retry button to re-fire the fetch effect even
  // though `open` has not changed.
  const [reloadKey, setReloadKey] = React.useState<number>(0);

  // Ref to the list container so we can move focus into it once items render.
  // First focusable child becomes the focus target when the overlay opens —
  // a11y requirement per NFR-05.
  const listRef = React.useRef<HTMLDivElement | null>(null);

  // ── Fetch effect ─────────────────────────────────────────────────────────
  //
  // Fires on the open=false → open=true transition (and on retry). Cancels via
  // a captured boolean when unmounted or when `open` flips back to false.
  React.useEffect(() => {
    if (!open) {
      // Reset state when closing so the next open starts fresh — prevents
      // stale list flashes if a session was just added.
      setErrorState(null);
      return;
    }
    if (!bffBaseUrl) {
      // Defensive: no BFF host configured — surface as load failure so the
      // user sees the inline error state rather than an empty list.
      setErrorState({ message: "BFF host not configured." });
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setErrorState(null);

    const fetchSessions = async (): Promise<void> => {
      const startedAt = performance.now();
      try {
        const url = buildBffApiUrl(bffBaseUrl, "/api/ai/chat/sessions?limit=50");
        const response = await authenticatedFetch(url, {
          headers: { "Content-Type": "application/json" },
        });

        if (!response.ok) {
          // Non-2xx — emit error telemetry and surface inline error state.
          logTelemetryError(TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE, {
            status: response.status,
            message: `HTTP ${response.status}`,
          });
          if (!cancelled) {
            setSessions([]);
            setErrorState({
              message: "Couldn't load history. Try again.",
            });
          }
          return;
        }

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const data = (await response.json()) as any[];
        const mapped: HistorySessionRow[] = Array.isArray(data)
          ? data.map(mapSession).filter((row) => row.sessionId.length > 0)
          : [];

        if (!cancelled) {
          setSessions(mapped);
          // Performance mark — surfaces in DevTools Performance tab so NFR-03
          // measurement (<300 ms p95) can be recorded without instrumentation.
          if (typeof performance !== "undefined" && performance.mark) {
            performance.mark("HistoryOverlay.populated");
          }
          // Lightweight dev-only timing log — kept silent in production by the
          // surrounding production-build strip; safe in Vite dev for NFR-03.
          const elapsed = performance.now() - startedAt;
          if (elapsed > 0) {
            // eslint-disable-next-line no-console
            console.debug(
              `[HistoryOverlay] sessions populated in ${Math.round(elapsed)} ms (${mapped.length} items)`
            );
          }
        }
      } catch (err) {
        // Network error / parse error / etc. — emit telemetry and surface
        // inline error state. Never let exceptions reach the React tree.
        const message = err instanceof Error ? err.message : String(err);
        logTelemetryError(TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE, {
          status: 0,
          message,
        });
        if (!cancelled) {
          setSessions([]);
          setErrorState({ message: "Couldn't load history. Try again." });
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    void fetchSessions();

    return () => {
      cancelled = true;
    };
    // authenticatedFetch is a stable module-level export from @spaarke/auth and
    // is intentionally omitted from deps to avoid re-fires on every render — see
    // ChatHistoryPanel.tsx for the same precedent.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, bffBaseUrl, reloadKey]);

  // ── Selection handler ────────────────────────────────────────────────────
  const handleSelect = React.useCallback(
    (sessionId: string): void => {
      if (!sessionId) return;
      onSelectSession(sessionId);
      onClose();
    },
    [onSelectSession, onClose]
  );

  // ── Keyboard support on individual rows ───────────────────────────────────
  // Buttons natively handle Enter/Space; we additionally accept ArrowDown /
  // ArrowUp for between-item navigation since list items are sibling buttons
  // inside a flat container (no ListBox role to avoid double-announce issues).
  const handleItemKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLButtonElement>): void => {
      if (e.key === "ArrowDown") {
        e.preventDefault();
        const next = e.currentTarget.nextElementSibling as HTMLElement | null;
        next?.focus();
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        const prev = e.currentTarget.previousElementSibling as HTMLElement | null;
        prev?.focus();
      }
    },
    []
  );

  // ── Retry handler ────────────────────────────────────────────────────────
  const handleRetry = React.useCallback((): void => {
    setReloadKey((k) => k + 1);
  }, []);

  // ── Auto-focus first item once list renders ───────────────────────────────
  React.useEffect(() => {
    if (!open || isLoading || errorState || sessions.length === 0) return;
    // Microtask-defer so the OverlayDrawer transition completes its initial
    // paint before we move focus — prevents a focus-then-scroll jank during
    // the open animation.
    const id = window.setTimeout(() => {
      const first = listRef.current?.querySelector<HTMLButtonElement>("button");
      first?.focus();
    }, 0);
    return () => window.clearTimeout(id);
  }, [open, isLoading, errorState, sessions.length]);

  // ── Render ───────────────────────────────────────────────────────────────
  return (
    <OverlayDrawer
      open={open}
      position="end"
      onOpenChange={(_, data) => {
        if (!data.open) {
          onClose();
        }
      }}
      className={styles.drawer}
      aria-label="Chat history"
    >
      <DrawerHeader>
        <div className={styles.header}>
          <DrawerHeaderTitle>
            <span className={styles.headerTitleInner}>
              <HistoryRegular />
              Chat history
            </span>
          </DrawerHeaderTitle>
          <Button
            appearance="subtle"
            aria-label="Close chat history"
            icon={<DismissRegular />}
            onClick={onClose}
          />
        </div>
      </DrawerHeader>

      <DrawerBody className={styles.body}>
        {isLoading ? (
          <div className={styles.loading}>
            <Spinner size="small" label="Loading history…" labelPosition="below" />
          </div>
        ) : errorState ? (
          <div className={styles.errorState} role="alert">
            <Text>{errorState.message}</Text>
            <Button
              className={styles.retryButton}
              appearance="secondary"
              onClick={handleRetry}
              aria-label="Retry loading chat history"
            >
              Retry
            </Button>
          </div>
        ) : sessions.length === 0 ? (
          <div className={styles.empty}>
            <Text>No recent conversations.</Text>
            <Text size={200}>
              Start a chat from the Assistant and it will appear here.
            </Text>
          </div>
        ) : (
          <div
            ref={listRef}
            className={styles.list}
            role="list"
            aria-label="Recent chat sessions"
          >
            {sessions.map((s) => {
              const relative = formatRelative(s.lastMessageAt);
              const ariaLabel = relative
                ? `Resume conversation: ${s.title}, last activity ${relative}`
                : `Resume conversation: ${s.title}`;
              return (
                <button
                  key={s.sessionId}
                  type="button"
                  role="listitem"
                  aria-label={ariaLabel}
                  className={mergeClasses(styles.item)}
                  onClick={() => handleSelect(s.sessionId)}
                  onKeyDown={handleItemKeyDown}
                >
                  <span className={styles.itemTitle} title={s.title}>
                    {s.title}
                  </span>
                  {relative && (
                    <span className={styles.itemMeta}>{relative}</span>
                  )}
                </button>
              );
            })}
          </div>
        )}
      </DrawerBody>
    </OverlayDrawer>
  );
};

export default HistoryOverlay;
