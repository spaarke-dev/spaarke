/**
 * @spaarke/ai-widgets — PinnedMemoryProvenanceBadge
 *
 * Small "source attribution" badge rendered next to each pinned memory item in
 * the {@link PinnedMemoryListWidget} list. Shows the user where a pin came from
 * so they can distinguish "I told the assistant to remember this" (chat-side
 * `ManagePinnedContextHandler` from R6 task 069) from "I created this manually
 * in the UI" (this widget surface from R6 task 070).
 *
 * ── R6 task 070 PART B STUB CAVEAT ─────────────────────────────────────────
 *
 * The PART A `PinnedContextItem` data model (and the BFF DTO it serialises to)
 * does NOT carry a `source` discriminator today. Both chat-created and
 * UI-created pins land in the same Cosmos document with the same fields. The
 * PART A evidence note documents this explicitly:
 *
 *   > "the BFF does NOT currently surface a `source` field distinguishing
 *   > 'created via chat' vs 'created via UI'. The chat-side
 *   > ManagePinnedContextHandler (task 069) and the UI endpoint both populate
 *   > the same PinnedContextItem model — there is no discriminator at the data
 *   > layer."
 *
 * As a result, this badge is delivered as a STUB: it always renders the
 * "Created via UI" label until the data-layer extension lands. The component
 * is in place + tested + consumes the same prop shape the wired-up version
 * will consume — only the source-of-truth lookup is stubbed.
 *
 * TODO(R7): wire actual provenance once `source` field lands on
 * `PinnedContextItem` (per PART A evidence note follow-up). At that point the
 * `source` prop will be passed in from the list widget reading
 * `PinDto.source`, the badge will return either "Created via chat" or
 * "Created via UI" based on the discriminator, and the stub default below
 * will be removed.
 *
 * Standards:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; Fluent v9 components.
 *   - ADR-021: zero hardcoded colors; Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component + hooks.
 *
 * Task: R6-070 (D-C-24 / D-C-25, Pillar 7, Q7 scope expansion) — PART B.
 */

import React from 'react';
import { Badge, makeStyles, mergeClasses, tokens, Tooltip } from '@fluentui/react-components';
import { ChatRegular, PersonRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Pin source discriminator. Mirrors the (future) `PinDto.source` field shape.
 * "ui" is the stub default until the data-layer extension lands.
 */
export type PinSource = 'ui' | 'chat';

export interface PinnedMemoryProvenanceBadgeProps {
  /**
   * Where the pin originated. Optional — when absent the component defaults
   * to "ui" (the stub assumption, per the file-header caveat). Once the
   * `PinDto.source` field is wired, callers will always pass this prop.
   */
  source?: PinSource;
  /** Optional class name forwarded to the badge root. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    // Subtle, non-distracting — meant to live alongside the item title without
    // competing for attention.
    fontSize: tokens.fontSizeBase200,
  },
  // Icon inside the badge — coloured via brand tokens so dark mode flips
  // automatically.
  icon: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Map the source discriminator to its display label. */
function labelFor(source: PinSource): string {
  switch (source) {
    case 'chat':
      return 'Created via chat';
    case 'ui':
    default:
      return 'Created via UI';
  }
}

/** Map the source discriminator to its tooltip explainer. */
function tooltipFor(source: PinSource): string {
  switch (source) {
    case 'chat':
      return 'You asked the assistant to remember this (e.g. "remember X").';
    case 'ui':
    default:
      return 'You created this pin manually in the Pinned Memory pane.';
  }
}

// ---------------------------------------------------------------------------
// PinnedMemoryProvenanceBadge
// ---------------------------------------------------------------------------

/**
 * Compact provenance badge for a pinned memory item. Shows an icon + short
 * label inside a Fluent v9 `Badge` with a tooltip explainer. Display-only —
 * no click handlers, no mutation.
 *
 * @example
 * <PinnedMemoryProvenanceBadge source="chat" />
 */
export const PinnedMemoryProvenanceBadge: React.FC<PinnedMemoryProvenanceBadgeProps> = ({
  source,
  className,
}) => {
  const styles = useStyles();
  // STUB: until `PinDto.source` lands, callers won't pass `source`, and we
  // default to "ui". TODO(R7) — see file-header caveat.
  const resolvedSource: PinSource = source ?? 'ui';

  const label = labelFor(resolvedSource);
  const tooltip = tooltipFor(resolvedSource);
  const Icon = resolvedSource === 'chat' ? ChatRegular : PersonRegular;

  return (
    <Tooltip content={tooltip} relationship="description" positioning="above">
      <Badge
        appearance="ghost"
        size="small"
        color="informative"
        className={mergeClasses(styles.root, className)}
        data-testid="pinned-memory-provenance-badge"
        data-source={resolvedSource}
        aria-label={label}
      >
        <Icon className={styles.icon} aria-hidden="true" />
        <span>{label}</span>
      </Badge>
    </Tooltip>
  );
};

PinnedMemoryProvenanceBadge.displayName = 'PinnedMemoryProvenanceBadge';

export default PinnedMemoryProvenanceBadge;
