/**
 * NarrativeCitedText — renders an item row's title text, wrapping the SAME
 * item's own regarding-record name as an inline hyperlink whenever that name
 * is textually present in the title.
 *
 * R5 task 011 (FR-A1, 2026-07-08) — REWRITE from the R7 W12 "AI narrative +
 * multi-mention citations" design to a deterministic, single-item design:
 *
 *   BEFORE (R7 W12): accepted a free-floating `references[]` array — modeled
 *   after an LLM narrative that could mention an arbitrary number of DIFFERENT
 *   entities anywhere in free text, with unmatched refs appended as numbered
 *   `[1][2][3]` footnote citations. That shape only made sense when the
 *   narrative text itself was LLM-authored prose.
 *
 *   AFTER (R5 task 010 removed the per-channel LLM call — see
 *   DailyBriefingNarrator.BuildDeterministicBullet): `narrative` is now built
 *   server-side directly from a single source item's own `Title` field, and
 *   every bullet has AT MOST ONE associated entity (its own regarding
 *   record). Modeling this as a `references[]` array sourced independently
 *   of `narrative` no longer reflects reality and re-opens a (currently
 *   theoretical, but structurally possible) door for cross-item mismatch: a
 *   consumer could supply a references array that didn't actually originate
 *   from the same item as `narrative`.
 *
 *   This component now takes the item's own `regardingName` /
 *   `regardingEntityType` / `regardingId` scalars directly — the SAME shape
 *   of fields `NarrativeBullet` already carries for its own primary-entity
 *   link — and derives the (at most one) candidate reference internally.
 *   There is no way to pass more than one entity, and no way for the linked
 *   entity to come from anywhere other than the caller's own props: cross-
 *   item pairing is structurally impossible, not merely discouraged.
 *
 * Rendering behavior:
 *   - `regardingName` found verbatim (case-insensitive) inside `narrative`
 *     -> wrapped in place as a clickable inline Link with a trailing "open"
 *     arrow (matches the existing entity-link convention used across the
 *     Daily Briefing surface, e.g. `HighPrioritySection` / `SubRowLink`).
 *   - Not found (the common case — item titles rarely restate the regarding
 *     record's own display name), or no regarding target supplied -> plain
 *     text only, no link. This component deliberately does NOT render a
 *     fallback/trailing link for the "not found" case — that responsibility
 *     belongs to the caller (see `NarrativeBullet`, which renders its own
 *     separate regarding-name link line via the exported
 *     `hasInlineRegardingMention` helper below). Keeping the "not found"
 *     fallback in exactly one place avoids ever rendering the same entity's
 *     link twice for one row.
 *
 * `buildSegments` (the regex-free inline-match splitter) is UNCHANGED from
 * its pre-R5 shape/behavior — it's a pure function over
 * `(narrative, mentioned)`, still covered verbatim by
 * `test/NarrativeCitedText.buildSegments.test.ts` (task 035). Only ITS
 * CALLER changed: `mentioned` is now always derived from this component's
 * own scalar props instead of accepted as an external array.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark-mode via semantic tokens.
 *   - Xrm-free: navigation happens in the parent via onOpenRecord (matches
 *     the existing DailyBriefingApp.handleOpenRecord pattern).
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Link } from '@fluentui/react-components';

import type { NarrativeBulletReferenceResult } from '../services/briefingService';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    // wrap over multiple lines like a plain <Text>.
    display: 'inline',
  },
  inlineLink: {
    // Preserve token-driven brand color on the inline/trailing link.
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

export interface NarrativeCitedTextProps {
  /**
   * The item's own title text. R5 task 010: the server composes this
   * directly from the source record's `Title` field — deterministic, never
   * LLM-authored.
   */
  narrative: string;
  /** The SAME item's regarding-record display name (or source-record name for orphan items). */
  regardingName?: string;
  /** The SAME item's regarding-record Dataverse logical name. */
  regardingEntityType?: string;
  /** The SAME item's regarding-record GUID. */
  regardingId?: string;
  /**
   * Called with (entityType, entityId) on the inline or trailing link click.
   * Wire this to the parent's Xrm.Navigation.navigateTo (target:2) handler.
   * When omitted, the link renders but click is a no-op.
   */
  onOpenRecord?: (entityType: string, entityId: string) => void;
  /** Text size passed through to the Fluent Text primitive. Default: 300. */
  textSize?: 200 | 300 | 400 | 500;
}

// ---------------------------------------------------------------------------
// Segment builder — splits narrative text around a mentioned entity name.
//
// UNCHANGED from the pre-R5 shape (task 035 tests cover this exact function
// verbatim). `mentioned` is a list purely so the function signature didn't
// need to change; callers in this file now only ever pass 0 or 1 entries,
// always derived from a single item's own fields (see `NarrativeCitedText`
// below and `hasInlineRegardingMention`).
// ---------------------------------------------------------------------------

type Segment =
  | { kind: 'text'; text: string }
  | {
      kind: 'link';
      display: string;
      entityType: string;
      entityId: string;
    };

export function buildSegments(narrative: string, mentioned: readonly NarrativeBulletReferenceResult[]): Segment[] {
  if (!narrative) return [];
  if (mentioned.length === 0) return [{ kind: 'text', text: narrative }];

  // Find the first case-insensitive occurrence of each mentioned entity name.
  // Skip refs whose name is empty or too short (avoid false matches on tokens
  // like "of", "a"). Skip refs whose target ids are empty (no click target).
  const lowerText = narrative.toLowerCase();
  interface Match {
    ref: NarrativeBulletReferenceResult;
    start: number;
    end: number;
  }
  const matches: Match[] = [];
  const usedRanges: Array<[number, number]> = [];

  for (const ref of mentioned) {
    if (!ref.entityName || ref.entityName.length < 3) continue;
    if (!ref.entityId) continue;
    const idx = lowerText.indexOf(ref.entityName.toLowerCase());
    if (idx < 0) continue;
    const end = idx + ref.entityName.length;
    // Skip if it overlaps with a previously matched range (defensive — dedupe
    // narrative text with overlapping name matches).
    const overlaps = usedRanges.some(([s, e]) => idx < e && end > s);
    if (overlaps) continue;
    matches.push({ ref, start: idx, end });
    usedRanges.push([idx, end]);
  }

  matches.sort((a, b) => a.start - b.start);

  const segments: Segment[] = [];
  let cursor = 0;
  for (const m of matches) {
    if (m.start > cursor) {
      segments.push({ kind: 'text', text: narrative.substring(cursor, m.start) });
    }
    segments.push({
      kind: 'link',
      display: narrative.substring(m.start, m.end),
      entityType: m.ref.entityType,
      entityId: m.ref.entityId,
    });
    cursor = m.end;
  }
  if (cursor < narrative.length) {
    segments.push({ kind: 'text', text: narrative.substring(cursor) });
  }
  return segments;
}

/**
 * Build the (at most one) candidate reference for this component's own
 * `regardingName` / `regardingEntityType` / `regardingId` props. Returns an
 * empty array when there's no usable target — `buildSegments` already
 * treats an empty `mentioned` list as "plain text, no link".
 */
function toCandidateRefs(
  regardingName: string | undefined,
  regardingEntityType: string | undefined,
  regardingId: string | undefined
): NarrativeBulletReferenceResult[] {
  if (!regardingEntityType || !regardingId) return [];
  return [
    {
      index: 1,
      entityType: regardingEntityType,
      entityId: regardingId,
      entityName: regardingName ?? '',
      mentioned: true,
    },
  ];
}

/**
 * Deterministically test whether `regardingName` occurs verbatim (case-
 * insensitive, same matching rule `buildSegments` applies) within
 * `narrative`. Exported so callers (e.g. `NarrativeBullet`) can decide
 * whether to render their OWN separate entity-link line, without
 * re-implementing the match rule — single source of truth stays
 * `buildSegments`.
 */
export function hasInlineRegardingMention(
  narrative: string,
  regardingName?: string,
  regardingEntityType?: string,
  regardingId?: string
): boolean {
  const refs = toCandidateRefs(regardingName, regardingEntityType, regardingId);
  if (refs.length === 0) return false;
  return buildSegments(narrative, refs).some(seg => seg.kind === 'link');
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NarrativeCitedText: React.FC<NarrativeCitedTextProps> = ({
  narrative,
  regardingName,
  regardingEntityType,
  regardingId,
  onOpenRecord,
  textSize = 300,
}) => {
  const styles = useStyles();

  // R5 task 011 (FR-A1): the ONLY candidate reference for this row is THIS
  // component's own `regardingName`/`regardingEntityType`/`regardingId`
  // props — never an externally supplied list. Cross-item pairing is
  // structurally impossible: there is exactly one entity (or none) this
  // component can ever link to, and it always originates from the same
  // props call as `narrative` (see `NarrativeBullet`, which sources both
  // from the same source item).
  const candidateRefs = React.useMemo(
    () => toCandidateRefs(regardingName, regardingEntityType, regardingId),
    [regardingName, regardingEntityType, regardingId]
  );

  const segments = React.useMemo(() => buildSegments(narrative, candidateRefs), [narrative, candidateRefs]);

  const handleClick = React.useCallback(
    (entityType: string, entityId: string): void => {
      if (!entityType || !entityId) return;
      onOpenRecord?.(entityType, entityId);
    },
    [onOpenRecord]
  );

  if (!narrative) {
    return null;
  }

  // NOTE: when a candidate ref exists but is NOT found verbatim inside
  // `narrative` (the common case — item titles rarely restate the regarding
  // record's own display name), this component renders PLAIN TEXT only. It
  // deliberately does NOT append its own fallback link — that's the
  // caller's responsibility (see `NarrativeBullet`, which renders its own
  // separate regarding-name link line when `hasInlineRegardingMention`
  // returns false). Keeping the "not found" fallback link in exactly ONE
  // place avoids ever rendering the same entity's link twice for one row.
  return (
    <Text as="span" size={textSize} className={styles.wrapper}>
      {segments.map((seg, i) => {
        if (seg.kind === 'text') {
          return <React.Fragment key={i}>{seg.text}</React.Fragment>;
        }
        // Inline entity-name link — trailing arrow (↗) matches the existing
        // entity-link convention used across the Daily Briefing surface.
        return (
          <Link
            key={i}
            appearance="default"
            className={styles.inlineLink}
            onClick={() => handleClick(seg.entityType, seg.entityId)}
            role="link"
            tabIndex={0}
            onKeyDown={(e: React.KeyboardEvent) => {
              if (e.key === 'Enter' || e.key === ' ') handleClick(seg.entityType, seg.entityId);
            }}
          >
            {seg.display}&nbsp;&#8599;
          </Link>
        );
      })}
    </Text>
  );
};
