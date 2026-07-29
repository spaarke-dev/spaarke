/**
 * EmailReadingPaneShell.types.ts
 *
 * Type contract for `<EmailReadingPaneShell />` — the Outlook-style two-pane
 * composition root for the Email surface (email-communication-solution-r5 task
 * 032, spec FR-05/FR-08/FR-19; design Lens 2/4). The shell owns:
 *   - the resizable list/reading-pane split (reused `PanelSplitter` +
 *     `useTwoPanelLayout`, both from `@spaarke/ui-components` — ADR-012 reuse),
 *   - the `selectedId` selection state driving the right pane,
 *   - the full-width toolbar dispatch seam (Reply / Reply All / Forward / New /
 *     Archive / Create).
 *
 * It does NOT implement the body/header/attachments/connections/tracking
 * sub-views — those are supplied by the host as render-slot props so tasks
 * 033 (.eml body), 034 (header + attachments), 035 (connections + tracking),
 * and 036 (compose wiring + "Open full form") can compose in WITHOUT editing
 * this file (the slot contract this task establishes).
 */

import type * as React from 'react';
import type { EmailCardItem } from '../EmailCardList/EmailCardList.types';

/**
 * A render-slot invoked with the currently selected `sprk_communication` id.
 * Returns `undefined`/`null` (render nothing) when the host has no content for
 * that slot yet — the shell always renders the slot call, never conditions on
 * whether a given renderer prop was supplied vs. what it returns.
 */
export type EmailPaneSlotRenderer = (selectedId: string) => React.ReactNode;

/**
 * Toolbar dispatch seam (FR-08). The shell/`<EmailToolbar/>` NEVER implements
 * compose/prefill/archive logic itself — it only calls the handler the host
 * supplies, keyed by selected id (New is not record-scoped: it's a blank
 * compose, mirroring `deriveComposerFields('compose', …)` in the extracted
 * task-022 `logic/actions` — see `@spaarke/communication-components/logic/actions`).
 * Reply/Reply All/Forward/Archive/Create the button is disabled until a card is
 * selected. Any handler left unwired falls back to a console-warned no-op
 * (task 036 supplies the real compose/archive/create-from-email dispatch).
 */
export interface EmailToolbarActionHandlers {
  /** Opens the reply composer for `selectedId` (task 036). */
  onReply?: (selectedId: string) => void;
  /** Opens the reply-all composer for `selectedId` (task 036). */
  onReplyAll?: (selectedId: string) => void;
  /** Opens the forward composer for `selectedId` (task 036). */
  onForward?: (selectedId: string) => void;
  /** Opens a blank "+ New" compose — NOT tied to the current selection. */
  onNew?: () => void;
  /** Archives (saves to SharePoint) `selectedId`. */
  onArchive?: (selectedId: string) => void;
  /** Launches the "create from this email" flow for `selectedId`. */
  onCreate?: (selectedId: string) => void;
}

export interface EmailReadingPaneShellProps {
  /** Host-supplied rows — forwarded verbatim to the left `<EmailCardList/>`. */
  items: ReadonlyArray<EmailCardItem>;
  /** Forwarded to `<EmailCardList/>` — renders skeleton cards while true. */
  isLoading?: boolean;
  /** Optional id to select on first mount (uncontrolled thereafter — the shell owns `selectedId`). */
  initialSelectedId?: string;
  /** Fired whenever the shell's `selectedId` changes (selection observability for the host — does not control it). */
  onSelectedIdChange?: (id: string | undefined) => void;
  /** Toolbar dispatch handlers (FR-08). Omitted handlers no-op with a console warning. */
  actions?: EmailToolbarActionHandlers;
  /** Header slot (task 034) — invoked with the selected id whenever a card is selected. */
  renderHeader?: EmailPaneSlotRenderer;
  /** Body slot (task 033, `.eml` render) — invoked with the selected id whenever a card is selected. */
  renderBody?: EmailPaneSlotRenderer;
  /** Attachments slot (task 034) — invoked with the selected id whenever a card is selected. */
  renderAttachments?: EmailPaneSlotRenderer;
  /** Connections slot (task 035) — invoked with the selected id whenever a card is selected. */
  renderConnections?: EmailPaneSlotRenderer;
  /** Tracking slot (task 035) — invoked with the selected id whenever a card is selected. */
  renderTracking?: EmailPaneSlotRenderer;
  /** localStorage key the splitter width persists under (default: a stable shell-scoped key). */
  storageKey?: string;
  /** Initial/reset reading-pane width in pixels (default 480). */
  defaultReadingPaneWidth?: number;
  /** Minimum list-pane width in pixels (default 280). */
  minListWidth?: number;
  /** Minimum reading-pane width in pixels (default 360). */
  minReadingPaneWidth?: number;
}
