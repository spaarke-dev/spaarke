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
 * Task 037 (FR-D6/D7/D8) — what changed:
 *   - **Row body = open, secondary actions in a per-row overflow (⋮) menu.**
 *     Each row is still a `<MenuItem>` whose click opens the session
 *     (`handleSelect`), but the trailing-edge "Promote…" up-arrow button
 *     (`ArrowUpRegular`, the old FR-07 explicit-promotion trigger) is GONE —
 *     removed per FR-D6 ("remove the ambiguous up-arrow"). In its place, each
 *     row carries a compact overflow trigger (`MoreVerticalRegular`) opening
 *     a `<Popover inline>` with four actions: **Open** (redundant with the
 *     row click, but explicit per FR-D6's acceptance list), **Rename** (opens
 *     a naming Dialog, wired to the existing `PATCH /api/ai/chat/sessions/{id}`
 *     endpoint shipped by task 032), **Set related record** (a NO-OP
 *     placeholder — see the note below), and **Delete** (opens a confirm
 *     Dialog, wired to the existing `DELETE /api/ai/chat/sessions/{id}`
 *     endpoint). **Why `<Popover>`, not a nested `<Menu>`:** a `<Menu>`
 *     rendered anywhere inside a `<MenuList>` is unconditionally treated by
 *     Fluent as a SUBMENU (`useIsSubmenu` returns true whenever
 *     `useHasParentContext(MenuListContext)` is true — trigger type is
 *     irrelevant), which wires an automatic close-chain: selecting (or even
 *     just opening) the nested Menu bubbles a close request to the OUTER
 *     Menu via `parentSetOpen`, closing the whole history dropdown out from
 *     under the user. `<Popover inline>` participates in none of Menu's
 *     context machinery and renders its surface IN DOM ORDER (not portaled
 *     to `document.body`), so it stays inside the outer Menu's own popover
 *     subtree and never trips its `useOnClickOutside` detection either. Each
 *     action button still calls `stopPropagation()` (mirrors the
 *     `stopPropagation`-on-trigger-click technique the file already used for
 *     the now-removed Promote button) so a row's overflow click never also
 *     "opens" the row. The Rename/Delete confirm Dialogs are NOT inline
 *     (Dialog has no such escape hatch — it's a true modal, portaled by
 *     design) so the outer Menu's `onOpenChange` explicitly ignores close
 *     requests while `renameTarget`/`deleteTarget` is non-null (see that
 *     handler's own comment) — the one remaining, well-understood guard.
 *   - **"Set related record" is a SLOT for task 034 (FR-D9), not this task.**
 *     The as-built record (`notes/as-built-evidence-2026-08-04.md` §3c) frames
 *     "Set related record" as the RENAMED, broadened successor to the FR-07
 *     "Promote to Analysis…" affordance (task 023) — task 034 owns turning it
 *     into a real association action. Task 037's directive was explicit:
 *     render the menu item, leave its handler a documented no-op, and do NOT
 *     implement its logic. Because the old up-arrow trigger is gone (the ONLY
 *     path into the FR-07 promote Dialog), that Dialog + its state/handlers +
 *     the `applyAgreementTypeToAnalysis` (task 023 classifier-write) call have
 *     no remaining caller — `noUnusedLocals`/`noUnusedParameters` (see
 *     `tsconfig.base.json`) would fail the build on the dead code, so this
 *     task REMOVES that dialog/state/handlers rather than leaving them
 *     orphaned. The `resolveClassifiedSubDomain` / `dataService` PROPS stay in
 *     `HistoryMenuProps` unchanged (ConversationPane.tsx still passes both —
 *     out of this task's file scope) — they are simply unused inside this
 *     file until task 034 re-wires "Set related record" (destructured-but-
 *     unused parameters are exempt from `noUnusedParameters`, so this is
 *     typecheck-clean). Task 034 is expected to reuse
 *     `agreementTypeLookupWrite.ts` (`applyAgreementTypeToAnalysis`) the same
 *     way FR-07 did.
 *   - **Preview + message count + tab summary (FR-D7) — client is ready, BFF
 *     is not yet.** `HistorySessionRow` grew three OPTIONAL fields (`preview`,
 *     `messageCount`, `tabsSummary`) and `mapSession` reads them defensively
 *     from the list response (`conversationSummary`/`preview`,
 *     `messageCount`, `tabSummary`/`tabs[]`). As of this task, the BFF's
 *     `GET /api/ai/chat/sessions` projection (`ChatEndpoints.cs` `RecentSessionDto`
 *     / `SessionPersistenceService.ListRecentSessionsAsync` → `RecentSessionInfo`)
 *     does **not** return any of the three — `conversationSummary` is fetched
 *     from Cosmos only as a title-fallback seed and dropped before the DTO
 *     boundary; message count and tabs are never projected at all. This is a
 *     genuine BFF gap (no in-flight task in this project extends that
 *     projection) flagged in the task 037 completion report for a follow-up.
 *     The rendering here is forward-compatible: once the BFF adds these
 *     fields, rows light up with no further client change; until then the
 *     meta line simply omits the missing pieces (never renders "undefined").
 *   - **Grouping + search (FR-D8).** Rows are grouped into Today / Yesterday /
 *     This week / Older (`groupSessionsByRecency`, calendar-day boundaries,
 *     local time) with a `<MenuGroupHeader>` per non-empty bucket — the same
 *     primitive `AssistantToolMenu.tsx` already uses inside a `<MenuList>`
 *     (no parallel grouping idiom introduced). A `<SearchBox>` above the list
 *     filters by title + preview (case-insensitive substring); its wrapping
 *     row stops non-Escape keydown propagation so Fluent's Menu type-ahead
 *     doesn't steal keystrokes meant for the input.
 *
 * Data flow:
 *   Menu opens (user clicks the History trigger)
 *     →  fetch GET /api/ai/chat/sessions?limit=50 via authenticatedFetch
 *     →  render up to 50 most-recent sessions, grouped + searchable
 *     →  row click (or the overflow "Open" item) ➜ onSelectSession(sessionId) + Menu auto-closes
 *     →  overflow "Rename" ➜ Dialog ➜ PATCH .../{sessionId} ➜ local title update
 *     →  overflow "Delete" ➜ confirm Dialog ➜ DELETE .../{sessionId} ➜ row removed locally
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
 *   - ARIA labels on the trigger ("Open chat history menu"), on each
 *     MenuItem ("Open conversation: {title}, {meta}"), and on the per-row
 *     overflow trigger ("More actions for conversation: {title}").
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
 * @see AssistantToolMenu.tsx — sibling that already uses `MenuGroupHeader` inside a `MenuList`
 * @see errorTelemetry.ts — TELEMETRY_HISTORY_LOAD_FAILURE constant
 * @see agreementTypeLookupWrite.ts — the task 023 write helper task 034's "Set related record" is
 *      expected to call again once it replaces this task's no-op placeholder.
 */

import * as React from "react";
import {
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuGroupHeader,
  MenuItem,
  Button,
  SearchBox,
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
  Popover,
  PopoverSurface,
  PopoverTrigger,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  HistoryRegular,
  MoreVerticalRegular,
  OpenRegular,
  EditRegular,
  LinkRegular,
  DeleteRegular,
} from "@fluentui/react-icons";
import { buildBffApiUrl, type AuthenticatedFetchFn } from "@spaarke/auth";
import type { IDataService } from "@spaarke/ui-components";
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
 *
 * `preview` / `messageCount` / `tabsSummary` are FR-D7 fields — see the file
 * header "Task 037" note. They are optional because the BFF does not project
 * them yet; `mapSession` fills them in defensively when present so the UI is
 * forward-compatible with zero further client change once the BFF catches up.
 */
interface HistorySessionRow {
  sessionId: string;
  title: string;
  lastMessageAt: string;
  /** Last-message / conversationSummary preview text (FR-D7). Undefined when the BFF omits it. */
  preview?: string;
  /** Message count (FR-D7). Undefined when the BFF omits it. */
  messageCount?: number;
  /** Pre-joined tab-type summary, e.g. "Email · Compose" (FR-D7). Undefined when the BFF omits it. */
  tabsSummary?: string;
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
   * given `sessionId`, if known. ConversationPane still supplies this prop; task 037 (FR-D6/D7/D8)
   * does not consume it — it is dormant here pending task 034's "Set related record" rewiring (see
   * file header). Kept in the prop contract unchanged so ConversationPane.tsx needs no edit.
   */
  resolveClassifiedSubDomain?: (sessionId: string) => string | null | undefined;
  /** Xrm.WebApi data service, paired with `resolveClassifiedSubDomain` above. Also currently dormant. */
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
    minWidth: "300px",
    overflowY: "auto",
  },
  /** Search row above the grouped list (task 037, FR-D8). Not a MenuItem — mirrors statusRow. */
  searchRow: {
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  searchBox: {
    width: "100%",
  },
  /**
   * MenuItem inner layout — lines stacked vertically: title (semibold-ish)
   * on top, a meta line (relative time · message count · tab summary) below,
   * and an optional preview line beneath that (task 037, FR-D7).
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
  /** Preview line (FR-D7) — smaller + more muted than the meta line, truncated. */
  itemPreview: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "240px",
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
  /** Row layout: title/meta stack grows; the overflow (⋮) trigger sits at the trailing edge. */
  itemRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
    gap: tokens.spacingHorizontalS,
  },
  /** Per-row overflow (⋮) trigger button (task 037, FR-D6 — replaces the removed up-arrow). */
  overflowButton: {
    minWidth: "auto",
    flexShrink: 0,
  },
  /** `<Popover inline>` surface for the per-row overflow actions (task 037, FR-D6). */
  overflowSurface: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: "180px",
    padding: tokens.spacingVerticalXS,
  },
  /** Individual action button inside the overflow surface — full width, left-aligned. */
  overflowAction: {
    justifyContent: "flex-start",
    width: "100%",
  },
  /** Shared error-text style for the Rename / Delete confirm dialogs. */
  dialogError: {
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
 *
 * FR-D7 (task 037): reads `preview`/`conversationSummary`, `messageCount`, and
 * `tabSummary`/`tabs[]` defensively — none of these are returned by the BFF's
 * list projection today (see file header). This mapping is forward-compatible:
 * once the BFF starts sending them, they render with no further client change.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function mapSession(item: any): HistorySessionRow {
  const previewRaw =
    typeof item.preview === "string" && item.preview.trim().length > 0
      ? item.preview
      : typeof item.conversationSummary === "string" && item.conversationSummary.trim().length > 0
        ? item.conversationSummary
        : undefined;

  const messageCount =
    typeof item.messageCount === "number" && Number.isFinite(item.messageCount)
      ? item.messageCount
      : undefined;

  const tabsSummaryRaw: unknown = item.tabSummary;
  const tabsArray: unknown = item.tabs;
  const tabsSummary =
    typeof tabsSummaryRaw === "string" && tabsSummaryRaw.trim().length > 0
      ? tabsSummaryRaw.trim()
      : Array.isArray(tabsArray)
        ? tabsArray
            .filter((t: unknown): t is string => typeof t === "string" && t.trim().length > 0)
            .join(" · ")
        : "";

  return {
    sessionId: String(item.sessionId ?? item.id ?? ""),
    title: String(
      item.title ?? item.playbookName ?? "Untitled Conversation"
    ),
    lastMessageAt: String(
      item.lastMessageAt ?? item.updatedAt ?? new Date().toISOString()
    ),
    preview: previewRaw ? previewRaw.replace(/\s+/g, " ").trim() : undefined,
    messageCount,
    tabsSummary: tabsSummary.length > 0 ? tabsSummary : undefined,
  };
}

/** FR-D8 time-grouping buckets, in display order. */
const GROUP_ORDER = ["Today", "Yesterday", "This week", "Older"] as const;
type GroupLabel = (typeof GROUP_ORDER)[number];

/**
 * Resolve which FR-D8 bucket a row's `lastMessageAt` falls into, using LOCAL
 * calendar-day boundaries (not a rolling 24h/168h window) so "Today" and
 * "Yesterday" match what a user visually expects regardless of what hour it
 * currently is.
 */
function resolveGroupLabel(timestamp: string, now: Date = new Date()): GroupLabel {
  const ts = Date.parse(timestamp);
  if (Number.isNaN(ts)) {
    return "Older";
  }
  const date = new Date(ts);
  const startOfDay = (d: Date): number =>
    new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const diffDays = Math.round((startOfDay(now) - startOfDay(date)) / 86_400_000);
  if (diffDays <= 0) return "Today";
  if (diffDays === 1) return "Yesterday";
  if (diffDays <= 7) return "This week";
  return "Older";
}

/** Group rows (already in server order — most-recent-first) into non-empty FR-D8 buckets. */
function groupSessionsByRecency(
  rows: readonly HistorySessionRow[]
): Array<{ label: GroupLabel; rows: HistorySessionRow[] }> {
  const buckets = new Map<GroupLabel, HistorySessionRow[]>();
  for (const label of GROUP_ORDER) {
    buckets.set(label, []);
  }
  for (const row of rows) {
    buckets.get(resolveGroupLabel(row.lastMessageAt))!.push(row);
  }
  return GROUP_ORDER.map((label) => ({ label, rows: buckets.get(label) ?? [] })).filter(
    (group) => group.rows.length > 0
  );
}

// Exported for unit testing (pure functions — no component render needed).
export { resolveGroupLabel, groupSessionsByRecency };

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * HistoryMenu — Fluent v9 dropdown menu listing recent chat sessions.
 *
 * Renders a `<Button>` ("History ▾") inside a `<MenuTrigger>`. On open, the
 * MenuPopover fetches up to 50 sessions from the BFF and renders them,
 * grouped (Today/Yesterday/This week/Older) and searchable, as MenuItems.
 * Selecting a row (or its overflow "Open" action) calls
 * `onSelectSession(sessionId)` and the Menu auto-closes.
 *
 * This component replaces the prior `OverlayDrawer`-based HistoryOverlay
 * (task 022) — see file header for the design rationale (task 097, task 037).
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

  // ── Search (task 037, FR-D8) ─────────────────────────────────────────────
  const [searchQuery, setSearchQuery] = React.useState<string>("");

  // ── Per-row overflow (⋮) action popover state (task 037, FR-D6) ─────────
  // Only one row's overflow popover can be open at a time. Rendered via
  // `<Popover inline>` (see file header "Why <Popover>, not a nested <Menu>")
  // so it never trips the outer Menu's own dismiss/outside-click detection.
  const [overflowOpenId, setOverflowOpenId] = React.useState<string | null>(null);

  // ── Fetch effect ─────────────────────────────────────────────────────────
  //
  // Fires on the closed → open transition (and on retry). Cancels via a
  // captured boolean when unmounted or when `open` flips back to false.
  React.useEffect(() => {
    if (!open) {
      // Clear transient UI state on close so the next open starts fresh.
      setErrorState(null);
      setSearchQuery("");
      setOverflowOpenId(null);
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

  // ── Filtering + grouping (task 037, FR-D8) ──────────────────────────────
  const filteredSessions = React.useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) {
      return sessions;
    }
    return sessions.filter(
      (s) =>
        s.title.toLowerCase().includes(q) ||
        (s.preview ?? "").toLowerCase().includes(q)
    );
  }, [sessions, searchQuery]);

  const groupedSessions = React.useMemo(
    () => groupSessionsByRecency(filteredSessions),
    [filteredSessions]
  );

  // ── Selection handler (row click + overflow "Open") ─────────────────────
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

  // ── "Set related record" placeholder (task 037, FR-D6 slot for task 034) ─
  //
  // Deliberately a no-op — see the file header "Task 037" note. Task 034
  // (FR-D9) replaces this handler with the real association action.
  const handleSetRelatedRecordPlaceholder = React.useCallback(
    (event: React.MouseEvent): void => {
      event.preventDefault();
      event.stopPropagation();
      setOverflowOpenId(null);
    },
    []
  );

  // ── Rename (task 037, FR-D6 — PATCH /api/ai/chat/sessions/{id}, task 032) ─
  const [renameTarget, setRenameTarget] = React.useState<{
    sessionId: string;
    title: string;
  } | null>(null);
  const [renameValue, setRenameValue] = React.useState<string>("");
  const [renaming, setRenaming] = React.useState<boolean>(false);
  const [renameError, setRenameError] = React.useState<string | null>(null);

  const handleOpenRename = React.useCallback(
    (event: React.MouseEvent, sessionId: string, title: string): void => {
      // Stop propagation so this doesn't also select/open the session.
      event.preventDefault();
      event.stopPropagation();
      setOverflowOpenId(null);
      setRenameError(null);
      setRenameValue(title);
      setRenameTarget({ sessionId, title });
    },
    []
  );

  const handleCloseRename = React.useCallback((): void => {
    setRenameTarget(null);
    setRenameError(null);
    setRenaming(false);
  }, []);

  const handleConfirmRename = React.useCallback(async (): Promise<void> => {
    if (!renameTarget || !renameValue.trim() || !bffBaseUrl) {
      return;
    }
    const nextTitle = renameValue.trim();
    setRenaming(true);
    setRenameError(null);
    try {
      const url = buildBffApiUrl(
        bffBaseUrl,
        `/api/ai/chat/sessions/${encodeURIComponent(renameTarget.sessionId)}`
      );
      const response = await authenticatedFetch(url, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title: nextTitle }),
      });
      if (!response.ok) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        let detail: any = null;
        try {
          detail = await response.json();
        } catch {
          // non-JSON error body — fall through to the generic message below
        }
        setRenameError(
          (detail && typeof detail.detail === "string" && detail.detail) ||
            "Couldn't rename this conversation. Try again."
        );
        setRenaming(false);
        return;
      }

      setSessions((prev) =>
        prev.map((s) =>
          s.sessionId === renameTarget.sessionId ? { ...s, title: nextTitle } : s
        )
      );
      setRenaming(false);
      setRenameTarget(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setRenameError(message || "Couldn't rename this conversation. Try again.");
      setRenaming(false);
    }
  }, [renameTarget, renameValue, bffBaseUrl, authenticatedFetch]);

  // ── Delete (task 037, FR-D6 — existing DELETE /api/ai/chat/sessions/{id}) ─
  const [deleteTarget, setDeleteTarget] = React.useState<{
    sessionId: string;
    title: string;
  } | null>(null);
  const [deleting, setDeleting] = React.useState<boolean>(false);
  const [deleteError, setDeleteError] = React.useState<string | null>(null);

  const handleRequestDelete = React.useCallback(
    (event: React.MouseEvent, sessionId: string, title: string): void => {
      event.preventDefault();
      event.stopPropagation();
      setOverflowOpenId(null);
      setDeleteError(null);
      setDeleteTarget({ sessionId, title });
    },
    []
  );

  const handleCancelDelete = React.useCallback((): void => {
    setDeleteTarget(null);
    setDeleteError(null);
    setDeleting(false);
  }, []);

  const handleConfirmDelete = React.useCallback(async (): Promise<void> => {
    if (!deleteTarget || !bffBaseUrl) {
      return;
    }
    setDeleting(true);
    setDeleteError(null);
    try {
      const url = buildBffApiUrl(
        bffBaseUrl,
        `/api/ai/chat/sessions/${encodeURIComponent(deleteTarget.sessionId)}`
      );
      const response = await authenticatedFetch(url, { method: "DELETE" });
      // 204 = deleted; 404 = already gone (e.g., raced with another tab) — both
      // mean the row should disappear from THIS list. Any other non-ok status
      // is a genuine failure.
      if (!response.ok && response.status !== 404) {
        setDeleteError("Couldn't delete this conversation. Try again.");
        setDeleting(false);
        return;
      }

      setSessions((prev) => prev.filter((s) => s.sessionId !== deleteTarget.sessionId));
      setDeleting(false);
      setDeleteTarget(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setDeleteError(message || "Couldn't delete this conversation. Try again.");
      setDeleting(false);
    }
  }, [deleteTarget, bffBaseUrl, authenticatedFetch]);

  // ── Search box keydown guard ─────────────────────────────────────────────
  //
  // Fluent's <Menu> attaches type-ahead / roving-tabindex keyboard handling
  // at the MenuList level. Without this guard, typing into the SearchBox
  // would also drive MenuItem type-ahead navigation. Escape is allowed to
  // bubble so the outer Menu can still close on Escape from the search box.
  const handleSearchRowKeyDown = React.useCallback((event: React.KeyboardEvent): void => {
    if (event.key !== "Escape") {
      event.stopPropagation();
    }
  }, []);

  // ── Row renderer ─────────────────────────────────────────────────────────
  const renderRow = (s: HistorySessionRow): React.ReactElement => {
    const relative = formatRelative(s.lastMessageAt);
    const metaParts: string[] = [];
    if (relative) metaParts.push(relative);
    if (typeof s.messageCount === "number") {
      metaParts.push(`${s.messageCount} ${s.messageCount === 1 ? "message" : "messages"}`);
    }
    if (s.tabsSummary) metaParts.push(s.tabsSummary);
    const metaLine = metaParts.join(" · ");

    const ariaLabel = metaLine
      ? `Open conversation: ${s.title}, ${metaLine}`
      : `Open conversation: ${s.title}`;

    const overflowOpen = overflowOpenId === s.sessionId;

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
            {metaLine && <span className={styles.itemMeta}>{metaLine}</span>}
            {s.preview && (
              <span className={styles.itemPreview} title={s.preview}>
                {s.preview}
              </span>
            )}
          </span>

          {/* task 037 (FR-D6) — per-row overflow (⋮) actions: Open / Rename / Set related
              record / Delete. `<Popover inline>` (NOT a nested `<Menu>` — see file header "Why
              <Popover>, not a nested <Menu>") so it renders as a compact trailing-edge
              affordance, stays inside the outer Menu's own popover DOM (no outside-click/
              dismiss-chain interference), and each action still stops propagation so a row's
              overflow click never also "opens" the row (mirrors the technique the removed
              Promote button used). */}
          <Popover
            inline
            open={overflowOpen}
            onOpenChange={(_, data) => setOverflowOpenId(data.open ? s.sessionId : null)}
            positioning="below-end"
          >
            <PopoverTrigger disableButtonEnhancement>
              <Tooltip content="More actions" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<MoreVerticalRegular />}
                  aria-label={`More actions for conversation: ${s.title}`}
                  className={styles.overflowButton}
                  data-testid={`history-menu-overflow-${s.sessionId}`}
                  onClick={(e) => e.stopPropagation()}
                />
              </Tooltip>
            </PopoverTrigger>
            <PopoverSurface
              className={styles.overflowSurface}
              aria-label={`Actions for conversation: ${s.title}`}
              data-testid={`history-menu-overflow-popover-${s.sessionId}`}
            >
              <Button
                appearance="subtle"
                className={styles.overflowAction}
                icon={<OpenRegular />}
                onClick={(e) => {
                  e.stopPropagation();
                  setOverflowOpenId(null);
                  handleSelect(s.sessionId);
                }}
                data-testid={`history-menu-overflow-open-${s.sessionId}`}
              >
                Open
              </Button>
              <Button
                appearance="subtle"
                className={styles.overflowAction}
                icon={<EditRegular />}
                onClick={(e) => handleOpenRename(e, s.sessionId, s.title)}
                data-testid={`history-menu-overflow-rename-${s.sessionId}`}
              >
                Rename
              </Button>
              <Button
                appearance="subtle"
                className={styles.overflowAction}
                icon={<LinkRegular />}
                onClick={handleSetRelatedRecordPlaceholder}
                data-testid={`history-menu-overflow-set-related-${s.sessionId}`}
              >
                Set related record
              </Button>
              <Button
                appearance="subtle"
                className={styles.overflowAction}
                icon={<DeleteRegular />}
                onClick={(e) => handleRequestDelete(e, s.sessionId, s.title)}
                data-testid={`history-menu-overflow-delete-${s.sessionId}`}
              >
                Delete
              </Button>
            </PopoverSurface>
          </Popover>
        </span>
      </MenuItem>
    );
  };

  // ── Render ───────────────────────────────────────────────────────────────
  // Fragment wraps the Menu + the Rename/Delete dialogs as SIBLINGS — Fluent v9 `<Menu>` only
  // expects a `MenuTrigger` + `MenuPopover` pair as children, so the dialogs are rendered
  // alongside it, not nested inside it (mirrors the pre-037 Promote dialog placement).
  return (
    <>
      <Menu
        open={open}
        onOpenChange={(_, data) => {
          // Rename/Delete render as a true modal `<Dialog>` (portaled, not `inline` —
          // see file header "Why <Popover>, not a nested <Menu>"), so a click on its
          // Save/Cancel/Delete button lands outside the outer Menu's own popover DOM
          // and would otherwise be treated as an outside-click dismissal. Ignore close
          // requests for as long as one of these dialogs is open so the row list
          // survives the round trip.
          if (!data.open && (renameTarget !== null || deleteTarget !== null)) {
            return;
          }
          setOpen(data.open);
        }}
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
              <>
                {/* task 037 (FR-D8) — search over title/preview. */}
                <div className={styles.searchRow} onKeyDown={handleSearchRowKeyDown}>
                  <SearchBox
                    className={styles.searchBox}
                    size="small"
                    value={searchQuery}
                    onChange={(_, data) => setSearchQuery(data.value)}
                    placeholder="Search conversations"
                    aria-label="Search chat history"
                    data-testid="history-menu-search"
                  />
                </div>
                {groupedSessions.length === 0 ? (
                  <div className={styles.statusRow}>
                    <Text size={200}>No conversations match your search.</Text>
                  </div>
                ) : (
                  groupedSessions.map((group) => (
                    <React.Fragment key={group.label}>
                      <MenuGroupHeader>{group.label}</MenuGroupHeader>
                      {group.rows.map((s) => renderRow(s))}
                    </React.Fragment>
                  ))
                )}
              </>
            )}
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* task 037 (FR-D6) — Rename dialog, wired to PATCH /api/ai/chat/sessions/{id} (task 032). */}
      <Dialog
        open={renameTarget !== null}
        onOpenChange={(_, data) => {
          if (!data.open) handleCloseRename();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Rename conversation</DialogTitle>
            <DialogContent>
              <Field label="Conversation title" required>
                <Input
                  value={renameValue}
                  onChange={(_, data) => setRenameValue(data.value)}
                  placeholder={renameTarget?.title ?? "Conversation title"}
                  disabled={renaming}
                  data-testid="rename-session-title-input"
                />
              </Field>
              {renameError && (
                <Text size={200} className={styles.dialogError} role="alert">
                  {renameError}
                </Text>
              )}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={handleCloseRename} disabled={renaming}>
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => void handleConfirmRename()}
                disabled={renaming || !renameValue.trim()}
                data-testid="rename-session-confirm"
              >
                {renaming ? <Spinner size="tiny" /> : "Save"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {/* task 037 (FR-D6) — Delete confirm dialog, wired to the existing DELETE endpoint. */}
      <Dialog
        open={deleteTarget !== null}
        onOpenChange={(_, data) => {
          if (!data.open) handleCancelDelete();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete conversation</DialogTitle>
            <DialogContent>
              <Text size={200}>
                Delete "{deleteTarget?.title}"? It will be removed from your history.
              </Text>
              {deleteError && (
                <Text size={200} className={styles.dialogError} role="alert">
                  {deleteError}
                </Text>
              )}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={handleCancelDelete} disabled={deleting}>
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => void handleConfirmDelete()}
                disabled={deleting}
                data-testid="delete-session-confirm"
              >
                {deleting ? <Spinner size="tiny" /> : "Delete"}
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
