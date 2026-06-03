/**
 * HistoryOverlay.tsx — Assistant pane "History" dropdown menu (R3 task 097).
 *
 * Historical context:
 *   Task 022 introduced this surface as a Fluent v9 `<OverlayDrawer>` (a
 *   right-side overlay, "Claude-Code-style") triggered by an icon-only
 *   `HistoryRegular` button in the Assistant PaneHeader's rightSlot. The 2026-
 *   05-22 operator smoke flagged the inconsistency: Workspace and Context panes
 *   use a Fluent v9 `<Menu>` dropdown ("Workspace ▾" / "Tools ▾") in their
 *   PaneHeader rightSlots, whereas Assistant had an icon-only button that
 *   opened a slide-in overlay. The dropdown pattern is the canonical Spaarke
 *   pane-trigger UX (per task 097 design).
 *
 * Task 097 — what changed:
 *   - The `OverlayDrawer` surface is GONE. The session list now renders inside
 *     a `<MenuPopover>` (Path A from task 097 design notes).
 *   - The trigger is now a subtle `<Button>` with the text label "History" +
 *     `<ChevronDownRegular>` icon (iconPosition="after") — visually identical
 *     to `WorkspacePaneMenu` ("Workspace ▾") and `ContextPaneMenu` ("Tools ▾").
 *   - The MenuPopover has a max-height + overflow-y: auto so the 50-item list
 *     scrolls inside the popover without forcing a separate overlay surface.
 *   - The previous `HistoryOverlayProps { open, onClose, onSelectSession, ... }`
 *     was replaced by `HistoryMenuProps { onSelectSession, bffBaseUrl,
 *     authenticatedFetch }` because Fluent v9 `<Menu>` manages its own
 *     open/close state — ConversationPane no longer needs the `historyOpen`
 *     boolean.
 *   - The legacy `HistoryOverlay` named export is preserved as a thin alias
 *     to `HistoryMenu` to keep imports stable for any future renamer. The
 *     ConversationPane is the only consumer and is updated to import
 *     `HistoryMenu` directly.
 *
 * Why Path A (inline Menu+MenuPopover) was chosen over Path B (kept overlay,
 * new trigger only): Path A unifies BOTH the trigger AND the surface with
 * Workspace/Context. The 50-item ceiling fits comfortably inside a scrollable
 * MenuPopover (max-height 360px, ~10 items visible, scroll for the rest) and
 * eliminates the separate slide-in surface that no other pane uses. Path B
 * would leave the surface mismatched with the rest of the shell.
 *
 * Data flow:
 *   Menu opens (user clicks "History ▾")
 *     →  fetch GET /api/ai/chat/sessions?limit=50 via authenticatedFetch
 *     →  render a list of up to 50 most-recent sessions in MenuItems
 *     →  click a MenuItem ➜ onSelectSession(sessionId) + Menu auto-closes
 *
 * Performance (NFR-03):
 *   - List populated in <300 ms p95 for 50 items — measured at the request
 *     boundary with `performance.now()` and surfaced through DevTools timing.
 *
 * Auth (ADR-028 §H-4):
 *   - NO accessToken prop. All BFF calls use the per-request authenticatedFetch
 *     returned by useAiSession().
 *
 * Telemetry (FR-24 / OC-09):
 *   - Error-only. On fetch failure (network error OR non-2xx response) emit
 *     `logTelemetryError(TELEMETRY_HISTORY_LOAD_FAILURE, ...)`.
 *
 * Accessibility (NFR-05):
 *   - Fluent v9 `<Menu>` is keyboard-navigable out of the box (Tab to enter,
 *     ArrowDown / ArrowUp between items, Enter to select, Escape to close).
 *   - ARIA labels on the trigger ("Open chat history menu") and on each
 *     MenuItem ("Resume conversation: {title}, last activity {relative}").
 *
 * Constraints:
 *   - ADR-012 — solution-local. Mirrors WorkspacePaneMenu / ContextPaneMenu
 *     pattern (also solution-local).
 *   - ADR-021 — Fluent v9 tokens only. No hex, no rgba.
 *   - ADR-022 — React 19 functional component, hooks-based.
 *   - ADR-025 — ChevronDownRegular icon from @fluentui/react-icons.
 *   - ADR-028 — function-based auth contract; no token snapshots.
 *
 * @see ConversationPane.tsx — wires this into the PaneHeader rightSlot
 * @see WorkspacePaneMenu.tsx — sibling pattern this mirrors (task 089)
 * @see ContextPaneMenu.tsx — sibling pattern this mirrors (task 095)
 * @see errorTelemetry.ts — TELEMETRY_HISTORY_LOAD_FAILURE constant
 */

import * as React from "react";
import {
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Button,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { ChevronDownRegular } from "@fluentui/react-icons";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";
import {
  logTelemetryError,
  TELEMETRY_HISTORY_LOAD_FAILURE,
} from "../../telemetry/errorTelemetry";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * One row of the session list. Shape adapts to the BFF response — the BFF
 * `/api/ai/chat/sessions?limit=50` endpoint returns objects with at least
 * `sessionId` (or `id`) + a title-like field and a last-activity timestamp.
 */
interface HistorySessionRow {
  sessionId: string;
  title: string;
  lastMessageAt: string;
}

/**
 * Props accepted by HistoryMenu.
 *
 * No `open` / `onClose` — Fluent v9 `<Menu>` manages its own open state.
 * No `accessToken` — auth flows through `authenticatedFetch` (ADR-028 §H-4).
 */
export interface HistoryMenuProps {
  /** Called when the user picks a session. The Menu auto-closes afterward. */
  onSelectSession: (sessionId: string) => void;
  /** BFF host URL (e.g. https://spe-api-dev.example.com). Pass-through from useAiSession(). */
  bffBaseUrl: string;
  /** Per-request authenticated fetch from useAiSession(). No token snapshot. */
  authenticatedFetch: AuthenticatedFetchFn;
}

/**
 * @deprecated Renamed to {@link HistoryMenuProps} in task 097.
 * Kept as an alias to ease grep across older comments / docs.
 */
export type HistoryOverlayProps = HistoryMenuProps;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Trigger button — matches WorkspacePaneMenu / ContextPaneMenu trigger:
   * subtle appearance, small size, ChevronDownRegular icon AFTER the label.
   * `minWidth: auto` keeps the button width tight against the text.
   */
  trigger: {
    minWidth: "auto",
  },
  /**
   * MenuPopover content — capped at ~10 visible items (360px) with internal
   * scroll for longer lists. Matches the typical dropdown ergonomics; deeper
   * lists are reachable via scroll, not a separate surface.
   */
  popover: {
    maxHeight: "360px",
    minWidth: "280px",
    overflowY: "auto",
  },
  /**
   * MenuItem inner layout — two lines stacked vertically: title (semibold)
   * on top, relative timestamp meta below.
   */
  itemInner: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-start",
    gap: tokens.spacingVerticalXXS,
    width: "100%",
    minWidth: 0,
  },
  itemTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "240px",
  },
  itemMeta: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  /**
   * Inline status rows — loading / empty / error sit inside the MenuPopover
   * but are NOT MenuItems (they're not selectable). Padding mirrors Fluent v9
   * MenuItem padding so they feel visually consistent.
   */
  statusRow: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  errorRow: {
    color: tokens.colorPaletteRedForeground1,
  },
  retryButton: {
    marginTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format a timestamp into a short relative-time string for the MenuItem meta
 * line. Returns "Just now", "5m ago", "2h ago", "3d ago", or the localized
 * date for older entries.
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
  return new Date(ts).toLocaleDateString();
}

/**
 * Map an arbitrary BFF session payload to the strongly-typed HistorySessionRow.
 * Mirrors the legacy mapping so the BFF response contract is unchanged.
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
 * HistoryMenu — Fluent v9 dropdown menu listing recent chat sessions.
 *
 * Renders a `<Button>` ("History ▾") inside a `<MenuTrigger>`. On open, the
 * MenuPopover fetches up to 50 sessions from the BFF and renders them as
 * MenuItems. Selecting an item calls `onSelectSession(sessionId)` and the
 * Menu auto-closes.
 *
 * This component replaces the prior `OverlayDrawer`-based HistoryOverlay
 * (task 022) — see file header for the design rationale (task 097).
 */
export const HistoryMenu: React.FC<HistoryMenuProps> = ({
  onSelectSession,
  bffBaseUrl,
  authenticatedFetch,
}) => {
  const styles = useStyles();

  // ── Menu open state ──────────────────────────────────────────────────────
  //
  // We track open state explicitly so we can trigger the fetch only on the
  // closed → open transition (avoids refetching on every render while the
  // popover is open). Fluent v9 `<Menu>` also accepts `open` + `onOpenChange`
  // for fully-controlled behaviour, which we use here.
  const [open, setOpen] = React.useState<boolean>(false);

  // ── Fetch state ──────────────────────────────────────────────────────────
  const [sessions, setSessions] = React.useState<HistorySessionRow[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [errorState, setErrorState] = React.useState<{ message: string } | null>(
    null
  );
  // Reload key — bumped by the Retry MenuItem to re-fire the fetch effect.
  const [reloadKey, setReloadKey] = React.useState<number>(0);

  // ── Fetch effect ─────────────────────────────────────────────────────────
  //
  // Fires on the closed → open transition (and on retry). Cancels via a
  // captured boolean when unmounted or when `open` flips back to false.
  React.useEffect(() => {
    if (!open) {
      // Clear error on close so the next open starts fresh.
      setErrorState(null);
      return;
    }
    if (!bffBaseUrl) {
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
          logTelemetryError(TELEMETRY_HISTORY_LOAD_FAILURE, {
            status: response.status,
            message: `HTTP ${response.status}`,
          });
          if (!cancelled) {
            setSessions([]);
            setErrorState({ message: "Couldn't load history. Try again." });
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
          if (typeof performance !== "undefined" && performance.mark) {
            performance.mark("HistoryMenu.populated");
          }
          const elapsed = performance.now() - startedAt;
          if (elapsed > 0) {
            // eslint-disable-next-line no-console
            console.debug(
              `[HistoryMenu] sessions populated in ${Math.round(elapsed)} ms (${mapped.length} items)`
            );
          }
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        logTelemetryError(TELEMETRY_HISTORY_LOAD_FAILURE, {
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
      setOpen(false);
    },
    [onSelectSession]
  );

  // ── Retry handler ────────────────────────────────────────────────────────
  const handleRetry = React.useCallback((event: React.MouseEvent): void => {
    // Stop click propagation so the MenuItem inside the popover doesn't
    // close the Menu — we want to stay open and refetch.
    event.preventDefault();
    event.stopPropagation();
    setReloadKey((k) => k + 1);
  }, []);

  // ── Render ───────────────────────────────────────────────────────────────
  return (
    <Menu
      open={open}
      onOpenChange={(_, data) => setOpen(data.open)}
      positioning="below-end"
    >
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Show chat history" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronDownRegular />}
            iconPosition="after"
            aria-label="Open chat history menu"
            className={styles.trigger}
            data-testid="history-menu-trigger"
            onClick={(e) => {
              // Prevent the PaneHeader's collapse handler from firing when
              // clicking the History trigger (parity with the legacy icon-
              // button click handler in ConversationPane).
              e.stopPropagation();
            }}
          >
            History
          </Button>
        </Tooltip>
      </MenuTrigger>

      <MenuPopover
        className={styles.popover}
        data-testid="history-menu-popover"
      >
        <MenuList aria-label="Recent chat sessions">
          {isLoading ? (
            <div className={styles.statusRow} role="status">
              <Spinner size="tiny" label="Loading history…" labelPosition="below" />
            </div>
          ) : errorState ? (
            <div
              className={`${styles.statusRow} ${styles.errorRow}`}
              role="alert"
            >
              <Text size={200}>{errorState.message}</Text>
              <Button
                className={styles.retryButton}
                appearance="secondary"
                size="small"
                onClick={handleRetry}
                aria-label="Retry loading chat history"
              >
                Retry
              </Button>
            </div>
          ) : sessions.length === 0 ? (
            <div className={styles.statusRow}>
              <Text size={200}>No recent conversations.</Text>
              <Text size={100}>
                Start a chat from the Assistant and it will appear here.
              </Text>
            </div>
          ) : (
            sessions.map((s) => {
              const relative = formatRelative(s.lastMessageAt);
              const ariaLabel = relative
                ? `Resume conversation: ${s.title}, last activity ${relative}`
                : `Resume conversation: ${s.title}`;
              return (
                <MenuItem
                  key={s.sessionId}
                  onClick={() => handleSelect(s.sessionId)}
                  aria-label={ariaLabel}
                  data-testid={`history-menu-item-${s.sessionId}`}
                >
                  <span className={styles.itemInner}>
                    <span className={styles.itemTitle} title={s.title}>
                      {s.title}
                    </span>
                    {relative && (
                      <span className={styles.itemMeta}>{relative}</span>
                    )}
                  </span>
                </MenuItem>
              );
            })
          )}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

/**
 * @deprecated Renamed to {@link HistoryMenu} in task 097 (operator UX feedback —
 * dropdown replaces OverlayDrawer to match Workspace/Context pane menus).
 * Kept as a named alias so stale imports keep compiling during transition.
 */
export const HistoryOverlay = HistoryMenu;

export default HistoryMenu;
