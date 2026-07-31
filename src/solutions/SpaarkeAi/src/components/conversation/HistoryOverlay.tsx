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
 *
 * Two-tier session model — explicit promotion (ai-advanced-capabilities-analysis-hub-r1 task 023,
 * spec FR-07): a loose (casual/ad-hoc) chat session persists and appears in this list like any
 * other, but is NEVER auto-associated with an `sprk_analysis` record. Each row carries a small
 * "Promote…" affordance that opens an inline Fluent v9 Dialog to name + bind the session to a NEW
 * Analysis via `POST /api/ai/analysis/promote` (a bind on the session's EXISTING Dataverse row —
 * no new session is minted, contrast with the fork endpoint, task 021). Originally kept entirely
 * local to this file (zero `HistoryMenuProps`/`ConversationPane.tsx` changes) — that held until
 * `ai-advanced-capabilities-agreements-r1` task 023 (spec FR-09 classifier door) needed to write the
 * resolved `sprk_agreementtype` lookup onto a JUST-PROMOTED Analysis when the promoted session went
 * through the classifier gate. Two ADDITIVE, OPTIONAL props were introduced for exactly that:
 * `resolveClassifiedSubDomain` + `dataService` (see their own doc comments) — omitting both preserves
 * promote's byte-identical pre-023 behavior.
 *
 * @see agreementTypeLookupWrite.ts — the task 023 write helper this component's promote success
 *      handler invokes (fire-and-forget; never blocks/fails the promote UX that already succeeded).
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
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Field,
  Input,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { HistoryRegular, ArrowUpRegular } from "@fluentui/react-icons";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";
import type { IDataService } from "@spaarke/ui-components";
import {
  logTelemetryError,
  TELEMETRY_HISTORY_LOAD_FAILURE,
} from "../../telemetry/errorTelemetry";
import { applyAgreementTypeToAnalysis } from "./agreementTypeLookupWrite";

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
  /**
   * task 023 (agreements-r1, classifier-path lookup write) — optional resolver returning the
   * agreement sub-domain key (`sprk_agreementtype.sprk_key`) the classifier gate resolved for the
   * given `sessionId`, if known. The caller (ConversationPane) only knows this for its OWN current
   * session — a promote of a different/older listed session returns `null`/`undefined`. When a
   * promote succeeds AND this resolves a key AND `dataService` is supplied, the newly-bound
   * Analysis's `sprk_agreementtype` lookup is written so persistence matches routing (see
   * `agreementTypeLookupWrite.ts`). Omitted (undefined) preserves promote's EXACT pre-023 behavior —
   * additive, optional, no fabricated default.
   */
  resolveClassifiedSubDomain?: (sessionId: string) => string | null | undefined;
  /** Xrm.WebApi data service for the task 023 lookup write. Required only alongside `resolveClassifiedSubDomain`. */
  dataService?: IDataService;
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
    // R5-5 (UAT 2026-07-20): smaller + not bold — the list read too heavy.
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
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
  /** Row layout: title/meta stack grows; the "Promote…" affordance sits at the trailing edge. */
  itemRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
    gap: tokens.spacingHorizontalS,
  },
  promoteButton: {
    minWidth: "auto",
    flexShrink: 0,
  },
  promoteError: {
    color: tokens.colorPaletteRedForeground1,
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
  resolveClassifiedSubDomain,
  dataService,
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
    // is intentionally omitted from deps to avoid re-fires on every render.
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

  // ── Explicit promotion (task 023, spec FR-07) ───────────────────────────
  //
  // `promoteTarget` non-null opens the naming Dialog for that session. Kept as component-local
  // state — no lift to ConversationPane (see file-header rationale).
  const [promoteTarget, setPromoteTarget] = React.useState<{
    sessionId: string;
    title: string;
  } | null>(null);
  const [promoteName, setPromoteName] = React.useState<string>("");
  const [promoting, setPromoting] = React.useState<boolean>(false);
  const [promoteError, setPromoteError] = React.useState<string | null>(null);

  const handleOpenPromote = React.useCallback(
    (event: React.MouseEvent, sessionId: string, title: string): void => {
      // Stop propagation so this doesn't also select/resume the session or close the Menu.
      event.preventDefault();
      event.stopPropagation();
      setPromoteError(null);
      setPromoteName("");
      setPromoteTarget({ sessionId, title });
    },
    []
  );

  const handleClosePromote = React.useCallback((): void => {
    setPromoteTarget(null);
    setPromoteError(null);
    setPromoting(false);
  }, []);

  const handleConfirmPromote = React.useCallback(async (): Promise<void> => {
    if (!promoteTarget || !promoteName.trim() || !bffBaseUrl) {
      return;
    }
    setPromoting(true);
    setPromoteError(null);
    try {
      const url = buildBffApiUrl(bffBaseUrl, "/api/ai/analysis/promote");
      const response = await authenticatedFetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ sessionId: promoteTarget.sessionId, name: promoteName.trim() }),
      });
      if (!response.ok) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        let detail: any = null;
        try {
          detail = await response.json();
        } catch {
          // non-JSON error body — fall through to the generic message below
        }
        setPromoteError(
          (detail && typeof detail.detail === "string" && detail.detail) ||
            "Couldn't promote this conversation. Try again."
        );
        setPromoting(false);
        return;
      }

      // task 023 (classifier-path lookup write, spec FR-09): if the promoted session's classifier
      // resolution is known (only true for the CURRENT session — see the prop doc comment) AND a
      // dataService is supplied, write the resolved sprk_agreementtype lookup onto the newly-bound
      // Analysis so persistence matches routing. Fire-and-forget: NEVER blocks or fails the promote
      // UX that already succeeded — a write failure only logs (mirrors
      // agreementTypeLookupWrite.ts's own graceful-degrade contract).
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      let promoteBody: any = null;
      try {
        promoteBody = await response.json();
      } catch {
        // Non-JSON success body (unexpected, but non-fatal — the promote itself already succeeded).
      }
      const analysisId = typeof promoteBody?.analysisId === "string" ? promoteBody.analysisId : null;
      const subDomainKey = resolveClassifiedSubDomain?.(promoteTarget.sessionId);
      if (analysisId && subDomainKey && dataService) {
        void applyAgreementTypeToAnalysis(dataService, analysisId, subDomainKey).then((result) => {
          if (!result.success) {
            console.warn("[HistoryMenu] task 023 lookup write did not complete:", result.warning);
          }
        });
      }

      setPromoting(false);
      setPromoteTarget(null);
      // Refresh the list so the promoted session's row reflects its (now Analysis-owned) state
      // on next open.
      setReloadKey((k) => k + 1);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setPromoteError(message || "Couldn't promote this conversation. Try again.");
      setPromoting(false);
    }
  }, [promoteTarget, promoteName, bffBaseUrl, authenticatedFetch, resolveClassifiedSubDomain, dataService]);

  // ── Render ───────────────────────────────────────────────────────────────
  // Fragment wraps the Menu + the promote Dialog as SIBLINGS — Fluent v9 `<Menu>` only expects a
  // `MenuTrigger` + `MenuPopover` pair as children, so the Dialog is rendered alongside it, not
  // nested inside it.
  return (
    <>
    <Menu
      open={open}
      onOpenChange={(_, data) => setOpen(data.open)}
      positioning="below-end"
    >
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Chat history" relationship="label">
          {/* P2-2 (UAT 2026-07-18): icon-only History trigger (Claude-Code style) —
              the "History" word + chevron were replaced by a single HistoryRegular
              icon so the three header controls read as icons (History / New session /
              Tools). aria-label + tooltip preserve accessibility. */}
          <Button
            appearance="subtle"
            size="small"
            icon={<HistoryRegular />}
            aria-label="Chat history"
            className={styles.trigger}
            data-testid="history-menu-trigger"
            onClick={(e) => {
              // Prevent the PaneHeader's collapse handler from firing when
              // clicking the History trigger (parity with the legacy icon-
              // button click handler in ConversationPane).
              e.stopPropagation();
            }}
          />
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
                  <span className={styles.itemRow}>
                    <span className={styles.itemInner}>
                      <span className={styles.itemTitle} title={s.title}>
                        {s.title}
                      </span>
                      {relative && (
                        <span className={styles.itemMeta}>{relative}</span>
                      )}
                    </span>
                    {/* task 023 (FR-07) — explicit promotion into a new, named Analysis. A loose
                        session is never auto-promoted; this is the only opt-in affordance. */}
                    <Tooltip content="Promote to Analysis…" relationship="label">
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<ArrowUpRegular />}
                        aria-label={`Promote conversation to Analysis: ${s.title}`}
                        className={styles.promoteButton}
                        data-testid={`history-menu-promote-${s.sessionId}`}
                        onClick={(e) => handleOpenPromote(e, s.sessionId, s.title)}
                      />
                    </Tooltip>
                  </span>
                </MenuItem>
              );
            })
          )}
        </MenuList>
      </MenuPopover>
    </Menu>
      <Dialog
        open={promoteTarget !== null}
        onOpenChange={(_, data) => {
          if (!data.open) handleClosePromote();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Promote to Analysis</DialogTitle>
            <DialogContent>
              <Text size={200}>
                Bind this conversation to a new, named Analysis. The conversation itself is
                unchanged — it just becomes discoverable from the Analysis hub.
              </Text>
              <Field label="Analysis name" required style={{ marginTop: tokens.spacingVerticalM }}>
                <Input
                  value={promoteName}
                  onChange={(_, data) => setPromoteName(data.value)}
                  placeholder={promoteTarget?.title ?? "Analysis name"}
                  disabled={promoting}
                  data-testid="promote-analysis-name-input"
                />
              </Field>
              {promoteError && (
                <Text size={200} className={styles.promoteError} role="alert">
                  {promoteError}
                </Text>
              )}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={handleClosePromote} disabled={promoting}>
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => void handleConfirmPromote()}
                disabled={promoting || !promoteName.trim()}
                data-testid="promote-analysis-confirm"
              >
                {promoting ? <Spinner size="tiny" /> : "Promote"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
};

/**
 * @deprecated Renamed to {@link HistoryMenu} in task 097 (operator UX feedback —
 * dropdown replaces OverlayDrawer to match Workspace/Context pane menus).
 * Kept as a named alias so stale imports keep compiling during transition.
 */
export const HistoryOverlay = HistoryMenu;

export default HistoryMenu;
