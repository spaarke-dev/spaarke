/**
 * SprkChatCitationPopover - Citation superscript marker + popover
 *
 * Two sub-components:
 *
 * 1. **CitationMarker** - Inline clickable superscript [N] rendered in
 *    brand color. Triggers the popover on click.
 *
 * 2. **SprkChatCitationPopover** - Fluent UI v9 Popover showing source
 *    name, page, excerpt (truncated to 200 chars), and an "Open Source"
 *    link. Dismisses on click-outside or Escape.
 *
 * Supports two citation source types:
 * - **document** (default) — internal SPE file reference with page + excerpt
 * - **web** — external web search result with title, clickable URL, snippet,
 *   and an "[External Source]" badge (ADR-015)
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - Data governance: mark external sources
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */
import * as React from 'react';
import { ICitationMarkerProps, ISprkChatCitationPopoverProps } from './types';
/**
 * CitationMarker - Inline clickable superscript [N] for citations.
 *
 * Designed to be embedded inside message text. Renders as a brand-colored
 * superscript that opens the citation popover on click.
 *
 * Keyboard accessible: Tab to focus, Enter or Space to activate.
 *
 * @example
 * ```tsx
 * <CitationMarker citation={citation} />
 * ```
 */
export declare const CitationMarker: React.FC<ICitationMarkerProps>;
/**
 * SprkChatCitationPopover - Controlled popover for citation details.
 *
 * Use this when you need explicit open/close control (e.g., the parent
 * manages popover state). For the simpler self-contained version, use
 * `CitationMarker` which wraps its own Popover.
 *
 * Supports both document and web citation types — the popover content
 * adapts automatically based on `citation.sourceType`.
 *
 * @example
 * ```tsx
 * const [open, setOpen] = React.useState(false);
 *
 * <SprkChatCitationPopover
 *   citation={citation}
 *   open={open}
 *   onOpenChange={setOpen}
 * >
 *   <span onClick={() => setOpen(true)}>[1]</span>
 * </SprkChatCitationPopover>
 * ```
 */
export declare const SprkChatCitationPopover: React.FC<ISprkChatCitationPopoverProps>;
export default SprkChatCitationPopover;
//# sourceMappingURL=SprkChatCitationPopover.d.ts.map