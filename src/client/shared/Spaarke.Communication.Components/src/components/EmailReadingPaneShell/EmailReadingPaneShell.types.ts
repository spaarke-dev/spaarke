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
 * It does NOT implement the title-bar/body/attachments/related-to/association
 * sub-views — those are supplied by the host as TWO render-slot props:
 *   - `renderHeader` — the TITLE BAR (the email subject on its own row with a
 *     light-gray background), rendered ABOVE the full-width `EmailToolbar`.
 *   - `renderBody` — the entire scrollable composed region BELOW the toolbar:
 *     recipients block, then the collapsible Attachments / Related-to /
 *     Association sections, then the email body, in that order. The host
 *     (`EmailWorkspace`) composes all pieces into one node it hands back from
 *     this single slot call — the shell does not know about (or render) each
 *     piece individually.
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
 * compose/prefill/archive/create logic itself — it only calls the handler the
 * host supplies, keyed by selected id (New is not record-scoped: it's a blank
 * compose, mirroring `deriveComposerFields('compose', …)` in the extracted
 * task-022 `logic/actions`). The record-scoped buttons disable until a card is
 * selected. Any handler left unwired falls back to a console-warned no-op.
 *
 * (Reading-pane MAIN-AREA redesign, email-communication-solution-r5: the toolbar
 * is now Reply / Reply All / Forward / New as icon+text on the LEFT, and a
 * right-aligned group of ICON-ONLY buttons with tooltips — Save to SharePoint,
 * Create Event, Create To Do, Link Invoice, and the demoted Open full form. The
 * four create/save actions act on the email's RESOLVED association; the host
 * builds those handlers from the same `launchCreate` / archive seams the
 * `CommunicationActions` PCF uses. The old generic `onArchive`/`onCreate` pair is
 * superseded by these explicit handlers.)
 */
export interface EmailToolbarActionHandlers {
  /** Opens the reply composer for `selectedId`. */
  onReply?: (selectedId: string) => void;
  /** Opens the reply-all composer for `selectedId`. */
  onReplyAll?: (selectedId: string) => void;
  /** Opens the forward composer for `selectedId`. */
  onForward?: (selectedId: string) => void;
  /** Opens a blank "+ New" compose — NOT tied to the current selection. */
  onNew?: () => void;
  /** Saves the selected email to SharePoint (archive). */
  onSaveToSharePoint?: (selectedId: string) => void;
  /** Launches the "Create Event" flow against the email's resolved association. */
  onCreateEvent?: (selectedId: string) => void;
  /** Launches the "Create To Do" flow against the email's resolved association. */
  onCreateTodo?: (selectedId: string) => void;
  /** Launches the "Link Invoice" flow against the email's resolved association. */
  onLinkInvoice?: (selectedId: string) => void;
  /** Opens the OOB Email main form for `selectedId` as an 85% `navigateTo` modal (demoted "Open full form"). */
  onOpenFullForm?: (selectedId: string) => void;
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
  /** TOP slot — rendered at the VERY TOP of the reading pane, ABOVE the title bar (the Association section lives here so its status dot is the first thing the user sees). Invoked with the selected id whenever a card is selected. */
  renderTop?: EmailPaneSlotRenderer;
  /** TITLE-BAR slot — the email subject on its own light-gray row; rendered ABOVE the toolbar (and below the top slot). Invoked with the selected id whenever a card is selected. */
  renderHeader?: EmailPaneSlotRenderer;
  /** Composed BODY-REGION slot — recipients block + collapsible Attachments / Related-to / Association sections + email body, in that order (the host composes them; the shell just renders the one returned node inside the scrollable region below the toolbar). Invoked with the selected id whenever a card is selected. */
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
