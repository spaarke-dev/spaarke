/**
 * TldrSection -- renders the AI-generated TL;DR summary at the top of the
 * Daily Briefing redesign.
 *
 * States:
 *   - Loading: 5 shimmer skeleton lines
 *   - Success: briefing narrative, optional top action, footer metadata
 *   - Unavailable: info icon + reason text
 *   - Error: inline error message
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 *   - AI-generated content labelled with "AI Insight" badge
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A). Source of truth; the original-location file at
 * `src/solutions/DailyBriefing/src/components/TldrSection.tsx` is now a
 * re-export shim pending full cleanup in R2 task 017.
 *
 * R5 task 014 (FR-A5, 2026-07-08) — BINARY anchor resolution. `tldr.itemRefs[]`
 * pairs a verbatim text span the TL;DR named (`anchorText`) with the source
 * item it claims to reference (`itemId`). This component is the WIDGET half
 * of the binary contract: for each itemRef, resolve `itemId` against the
 * `resolvableItems` map the caller supplies (built from the same narrate
 * request's items — see `DailyBriefingApp`'s `tldrResolvableItems`). A
 * resolving itemId gets its `anchorText` wrapped as a clickable link inline
 * in whichever of summary/keyTakeaways/topAction contains it (reuses
 * `NarrativeCitedText.buildSegments`, the same matching rule the item-row
 * links already use). A NON-resolving itemId is DROPPED — the anchor text
 * renders as plain prose, exactly as if no itemRefs entry had ever named it.
 * There is deliberately NO warn badge, confidence indicator, or withheld-
 * content placeholder anywhere in this path (FR-A6 locks the no-threshold
 * posture) — resolution is exists-or-doesn't, never scored.
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Badge, Skeleton, SkeletonItem, Link } from '@fluentui/react-components';
import { InfoRegular } from '@fluentui/react-icons';
import { buildSegments } from './NarrativeCitedText';
import type { NarrativeBulletReferenceResult, TldrItemRefResult } from '../services/briefingService';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    position: 'relative',
  },
  heading: {
    marginTop: '0',
    marginBottom: tokens.spacingVerticalM,
  },
  aiBadge: {
    position: 'absolute',
    top: tokens.spacingVerticalL,
    right: tokens.spacingHorizontalXL,
  },
  briefingText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    display: 'block',
    marginBottom: tokens.spacingVerticalM,
  },
  takeawaysList: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
    marginTop: '0',
    marginBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
  },
  takeawayItem: {
    marginBottom: tokens.spacingVerticalXS,
  },
  topAction: {
    display: 'block',
    marginBottom: tokens.spacingVerticalS,
  },
  footer: {
    color: tokens.colorNeutralForeground3,
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
  },
  fallbackContainer: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  fallbackIcon: {
    fontSize: '16px',
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  skeletonContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  inlineLink: {
    // R5 task 014 — matches NarrativeCitedText's inline-link token usage (ADR-021: no
    // hard-coded colors, brand-forward token so the link reads distinctly from body text).
    color: tokens.colorBrandForeground1,
    textDecorationLine: 'none',
    cursor: 'pointer',
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * The resolved link target for a TL;DR item ref (R5 task 014, FR-A5) — the shape
 * `DailyBriefingApp` builds its `resolvableItems` map from (mirrors
 * `NarrativeBulletResult`'s primary-entity fields, the same target every other
 * item-row link in the widget already points at).
 */
export interface TldrResolvableItem {
  entityType: string;
  entityId: string;
}

export interface TldrSectionProps {
  /**
   * Structured TL;DR — R2.2 replaced the prior single `briefing` blob with a
   * 2-3 sentence summary + 3-5 key-takeaway bullets + a top-action sentence so
   * the user can scan the briefing at a glance instead of reading a paragraph.
   */
  tldr: {
    summary: string;
    keyTakeaways: string[];
    topAction: string;
    categoryCount: number;
    priorityItemCount: number;
    /** R5 task 014 (FR-A5) — anchor-to-item grounding. See module JSDoc. */
    itemRefs?: TldrItemRefResult[];
  } | null;
  isLoading: boolean;
  isUnavailable: boolean;
  unavailableReason: string | null;
  error: string | null;
  /** ISO timestamp of when the TL;DR was generated. */
  generatedAt: string | null;
  /**
   * R5 task 014 (FR-A5) — map from item id (`ChannelItemDto.Id` / `TldrItemRefResult.itemId`)
   * to its click-through target. An itemRef whose `itemId` is NOT a key in this map is DROPPED
   * — no entry, no warning, no residue (binary resolution; FR-A6 forbids a threshold/warn
   * band). Omitted/empty map = every anchor renders as plain unlinked text (safe default).
   */
  resolvableItems?: Record<string, TldrResolvableItem>;
  /** Called with (entityType, entityId) when a resolved anchor link is clicked. */
  onOpenRecord?: (entityType: string, entityId: string) => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * R7 W12 (2026-07-01) — fun rotating emoji for the TL;DR header. Deterministic
 * per-briefing pick via a simple hash of the `generatedAt` timestamp so the
 * emoji stays consistent across re-renders of the same briefing but changes on
 * every fresh /render call (i.e., every refresh). Kept small + lightweight per
 * operator request ("add some fun, not if too much effort").
 */
const FUN_EMOJI_POOL = ['🚀', '👍', '⛰️', '😊', '☀️', '🎯', '💡', '🌟', '🍀', '🌈', '⚡', '🎉', '🌱', '🔥', '🎨', '🏆'];

function pickTldrEmoji(seed: string | null): string {
  if (!seed) return FUN_EMOJI_POOL[0];
  let hash = 0;
  for (let i = 0; i < seed.length; i++) {
    hash = (hash * 31 + seed.charCodeAt(i)) | 0;
  }
  return FUN_EMOJI_POOL[Math.abs(hash) % FUN_EMOJI_POOL.length];
}

function formatRelativeTime(isoTimestamp: string): string {
  const now = Date.now();
  const generated = new Date(isoTimestamp).getTime();
  const diffMs = now - generated;
  const diffMin = Math.floor(diffMs / 60_000);

  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHrs = Math.floor(diffMin / 60);
  if (diffHrs < 24) return `${diffHrs}h ago`;
  const diffDays = Math.floor(diffHrs / 24);
  return `${diffDays}d ago`;
}

/**
 * R5 task 014 (FR-A5) — BINARY anchor resolution. Filters `itemRefs` down to the subset
 * whose `itemId` resolves against `resolvableItems`, mapping each survivor to the
 * `NarrativeBulletReferenceResult` shape `buildSegments` (NarrativeCitedText.tsx) already
 * knows how to text-match and link. A non-resolving itemId (missing key, or an empty
 * `resolvableItems` map) is simply excluded — there is no partial/low-confidence entry to
 * emit; resolution is exists-or-doesn't, never scored (FR-A6).
 */
function resolveTldrRefs(
  itemRefs: TldrItemRefResult[] | undefined,
  resolvableItems: Record<string, TldrResolvableItem> | undefined
): NarrativeBulletReferenceResult[] {
  if (!itemRefs || itemRefs.length === 0 || !resolvableItems) return [];
  const resolved: NarrativeBulletReferenceResult[] = [];
  for (const ref of itemRefs) {
    const target = resolvableItems[ref.itemId];
    if (!target) continue; // DROP — no resolving item for this anchor's itemId.
    resolved.push({
      index: resolved.length + 1,
      entityType: target.entityType,
      entityId: target.entityId,
      entityName: ref.anchorText,
      mentioned: true,
    });
  }
  return resolved;
}

/**
 * Renders `text` with each resolved anchor (that's textually present in `text`) wrapped as a
 * clickable inline link — via the same `buildSegments` splitter `NarrativeCitedText` uses for
 * item-row links. Anchors not present in `text` (or dropped by `resolveTldrRefs` before this
 * point) simply don't produce a link segment; the surrounding prose renders unchanged, with
 * zero residue — no placeholder, no asterisk, no "citation unavailable" note.
 */
const TldrAnchoredText: React.FC<{
  text: string;
  refs: NarrativeBulletReferenceResult[];
  onOpenRecord?: (entityType: string, entityId: string) => void;
  linkClassName: string;
}> = ({ text, refs, onOpenRecord, linkClassName }) => {
  const segments = React.useMemo(() => buildSegments(text, refs), [text, refs]);
  return (
    <>
      {segments.map((seg, i) =>
        seg.kind === 'text' ? (
          <React.Fragment key={i}>{seg.text}</React.Fragment>
        ) : (
          <Link
            key={i}
            appearance="default"
            className={linkClassName}
            onClick={() => onOpenRecord?.(seg.entityType, seg.entityId)}
            role="link"
            tabIndex={0}
            onKeyDown={(e: React.KeyboardEvent) => {
              if (e.key === 'Enter' || e.key === ' ') onOpenRecord?.(seg.entityType, seg.entityId);
            }}
          >
            {seg.display}
          </Link>
        )
      )}
    </>
  );
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TldrSection: React.FC<TldrSectionProps> = ({
  tldr,
  isLoading,
  isUnavailable,
  unavailableReason,
  error,
  generatedAt,
  resolvableItems,
  onOpenRecord,
}) => {
  const styles = useStyles();

  // Loading state: skeleton with 5 shimmer lines
  if (isLoading) {
    return (
      <div className={styles.card}>
        <Text as="h2" size={500} weight="semibold" className={styles.heading}>
          {pickTldrEmoji(generatedAt)} TL;DR
        </Text>
        <Skeleton aria-label="Loading TL;DR summary">
          <div className={styles.skeletonContainer}>
            <SkeletonItem size={16} style={{ width: '100%' }} />
            <SkeletonItem size={16} style={{ width: '95%' }} />
            <SkeletonItem size={16} style={{ width: '90%' }} />
            <SkeletonItem size={16} style={{ width: '85%' }} />
            <SkeletonItem size={16} style={{ width: '60%' }} />
          </div>
        </Skeleton>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className={styles.card}>
        <Text as="h2" size={500} weight="semibold" className={styles.heading}>
          {pickTldrEmoji(generatedAt)} TL;DR
        </Text>
        <div className={styles.fallbackContainer}>
          <InfoRegular className={styles.fallbackIcon} />
          <Text size={200} className={styles.errorText}>
            {error}
          </Text>
        </div>
      </div>
    );
  }

  // Unavailable state
  if (isUnavailable) {
    return (
      <div className={styles.card}>
        <Text as="h2" size={500} weight="semibold" className={styles.heading}>
          {pickTldrEmoji(generatedAt)} TL;DR
        </Text>
        <div className={styles.fallbackContainer}>
          <InfoRegular className={styles.fallbackIcon} />
          <Text size={200}>
            {unavailableReason ?? 'AI summary is temporarily unavailable.'} Your notifications are shown below.
          </Text>
        </div>
      </div>
    );
  }

  // No data guard
  if (!tldr) {
    return null;
  }

  // R5 task 014 (FR-A5): binary anchor resolution — non-resolving itemRefs are excluded
  // BEFORE any text rendering happens, so there is no code path downstream that could ever
  // show a warning/withhold affordance for a dropped anchor (FR-A6). resolveTldrRefs is a
  // plain function (no hooks), safe to call after this component's earlier conditional
  // returns.
  const resolvedRefs = resolveTldrRefs(tldr.itemRefs, resolvableItems);

  // Success state
  return (
    <div className={styles.card}>
      <Text as="h2" size={500} weight="semibold" className={styles.heading}>
        TL;DR
      </Text>
      <Badge className={styles.aiBadge} appearance="tint" color="brand" size="small">
        AI Insight
      </Badge>
      {tldr.summary && (
        <Text size={300} className={styles.briefingText}>
          <TldrAnchoredText
            text={tldr.summary}
            refs={resolvedRefs}
            onOpenRecord={onOpenRecord}
            linkClassName={styles.inlineLink}
          />
        </Text>
      )}
      {Array.isArray(tldr.keyTakeaways) && tldr.keyTakeaways.length > 0 && (
        <ul className={styles.takeawaysList} aria-label="Key takeaways">
          {tldr.keyTakeaways.map((takeaway, idx) => (
            <li key={idx} className={styles.takeawayItem}>
              <Text size={300}>
                <TldrAnchoredText
                  text={takeaway}
                  refs={resolvedRefs}
                  onOpenRecord={onOpenRecord}
                  linkClassName={styles.inlineLink}
                />
              </Text>
            </li>
          ))}
        </ul>
      )}
      {tldr.topAction && (
        <Text size={300} weight="semibold" className={styles.topAction}>
          <TldrAnchoredText
            text={tldr.topAction}
            refs={resolvedRefs}
            onOpenRecord={onOpenRecord}
            linkClassName={styles.inlineLink}
          />
        </Text>
      )}
      <div className={styles.footer}>
        {generatedAt && <Text size={200}>Generated {formatRelativeTime(generatedAt)}</Text>}
        <Text size={200}>
          {tldr.categoryCount} categories, {tldr.priorityItemCount} priority items
        </Text>
      </div>
    </div>
  );
};
