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
 * It does NOT implement the header band/body/attachments/connections/tracking
 * sub-views — those are supplied by the host as TWO render-slot props:
 *   - `renderHeader` — the HEADER BAND (subject + compact tracking trio +
 *     "Open full form"), rendered ABOVE the full-width `EmailToolbar`.
 *   - `renderBody` — the entire scrollable composed region BELOW the
 *     toolbar: recipients block, email body, attachments section, "Related
 *     to" (associations) section, in that order. The host (`EmailWorkspace`)
 *     composes all four pieces into one node it hands back from this single
 *     slot call — the shell does not know about (or render) each piece
 *     individually.
 *
 * (Reading-pane layout redesign, email-communication-solution-r5: previously
 * five slots — `renderHeader`/`renderBody`/`renderAttachments`/
 * `renderConnections`/`renderTracking` — rendered as five siblings below the
 * toolbar. Collapsed to two slots + a reordered header-above-toolbar layout
 * per the Outlook-style one-column redesign. No other consumer exists outside
 * this package's own `EmailWorkspace` composition root, so this is a safe
 * internal contract change.)
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
  /** Header BAND slot — subject + compact tracking trio + "Open full form"; rendered ABOVE the toolbar. Invoked with the selected id whenever a card is selected. */
  renderHeader?: EmailPaneSlotRenderer;
  /** Composed BODY-REGION slot — recipients block + email body + attachments section + "Related to" section, in that order (the host composes all four; the shell just renders the one returned node inside the scrollable region below the toolbar). Invoked with the selected id whenever a card is selected. */
  renderBody?: EmailPaneSlotRenderer;
  /** localStorage key the splitter width persists under (default: a stable shell-scoped key). */
  storageKey?: string;
  /** Initial/reset reading-pane width in pixels (default 480). */
  defaultReadingPaneWidth?: number;
  /** Minimum list-pane width in pixels (default 280). */
  minListWidth?: number;
  /** Minimum reading-pane width in pixels (default 360). */
  minReadingPaneWidth?: number;
}
