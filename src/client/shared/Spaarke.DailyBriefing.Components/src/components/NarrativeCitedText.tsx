/**
 * NarrativeCitedText — renders a narrative sentence with inline entity-name
 * hyperlinks + trailing [N] citations.
 *
 * R7 W12 feedback items 2/3/4 (2026-07-01):
 *   Every fact-based statement in an AI-narrated bullet should be traceable to
 *   a source record. This component takes the plain narrative text plus a
 *   references[] array and produces mixed text + Link output:
 *
 *   - Mentioned refs (entity name appears in text): wrap first occurrence of
 *     `entityName` in a clickable Link with an "open" arrow icon (↗). Click →
 *     `onOpenRecord(entityType, entityId)`.
 *
 *   - Implicit refs (entity NOT mentioned in text): append trailing
 *     [1][2][3]... superscript citations. Click → same handler.
 *
 * Graceful degradation:
 *   - references.length === 0 → renders as plain <Text>{narrative}</Text>.
 *   - onOpenRecord omitted → links render but click is a no-op (still
 *     visually distinct so operator sees the citation).
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
    // wrap over multiple lines like the original <Text>.
    display: 'inline',
  },
  inlineLink: {
    // Preserve token-driven brand color on the inline link.
    color: tokens.colorBrandForeground1,
    textDecorationLine: 'none',
    cursor: 'pointer',
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
  citationList: {
    display: 'inline',
    marginLeft: tokens.spacingHorizontalXS,
  },
  citationLink: {
    color: tokens.colorBrandForeground1,
    textDecorationLine: 'none',
    cursor: 'pointer',
    fontSize: tokens.fontSizeBase200,
    verticalAlign: 'super',
    lineHeight: 1,
    marginLeft: tokens.spacingHorizontalXXS,
    marginRight: tokens.spacingHorizontalXXS,
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface NarrativeCitedTextProps {
  /** Plain narrative text emitted by the LLM. */
  narrative: string;
  /**
   * Per-bullet references. When empty (or omitted), the component renders
   * plain text — no interactive elements.
   */
  references?: NarrativeBulletReferenceResult[];
  /**
   * Called with (entityType, entityId) on any inline link or trailing citation
   * click. Wire this to the parent's Xrm.Navigation.navigateTo (target:2)
   * handler. When omitted, links render but click is a no-op.
   */
  onOpenRecord?: (entityType: string, entityId: string) => void;
  /** Text size passed through to the Fluent Text primitive. Default: 300. */
  textSize?: 200 | 300 | 400 | 500;
}

// ---------------------------------------------------------------------------
// Segment builder — splits narrative text around mentioned entity names.
// ---------------------------------------------------------------------------

type Segment =
  | { kind: 'text'; text: string }
  | {
      kind: 'link';
      display: string;
      entityType: string;
      entityId: string;
    };

function buildSegments(
  narrative: string,
  mentioned: readonly NarrativeBulletReferenceResult[]
): Segment[] {
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

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NarrativeCitedText: React.FC<NarrativeCitedTextProps> = ({
  narrative,
  references,
  onOpenRecord,
  textSize = 300,
}) => {
  const styles = useStyles();

  const refs = React.useMemo<NarrativeBulletReferenceResult[]>(
    () => (Array.isArray(references) ? references : []),
    [references]
  );

  const mentionedRefs = React.useMemo(() => refs.filter(r => r.mentioned), [refs]);
  const implicitRefs = React.useMemo(() => refs.filter(r => !r.mentioned && r.entityId), [refs]);

  const segments = React.useMemo(() => buildSegments(narrative, mentionedRefs), [narrative, mentionedRefs]);

  const handleClick = React.useCallback(
    (entityType: string, entityId: string): void => {
      if (!entityType || !entityId) return;
      onOpenRecord?.(entityType, entityId);
    },
    [onOpenRecord]
  );

  // Nothing to render when narrative is empty AND there are no implicit refs.
  if (!narrative && implicitRefs.length === 0) {
    return null;
  }

  return (
    <Text as="span" size={textSize} className={styles.wrapper}>
      {segments.map((seg, i) => {
        if (seg.kind === 'text') {
          return <React.Fragment key={i}>{seg.text}</React.Fragment>;
        }
        // Inline entity-name link — appended arrow (↗) matches the existing
        // regarding-name link on NarrativeBullet.
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
      {implicitRefs.length > 0 && (
        <span className={styles.citationList}>
          {implicitRefs.map(ref => (
            <Link
              key={ref.index}
              appearance="default"
              className={styles.citationLink}
              onClick={() => handleClick(ref.entityType, ref.entityId)}
              title={ref.entityName || `Reference ${ref.index}`}
              role="link"
              tabIndex={0}
              onKeyDown={(e: React.KeyboardEvent) => {
                if (e.key === 'Enter' || e.key === ' ') handleClick(ref.entityType, ref.entityId);
              }}
            >
              [{ref.index}]
            </Link>
          ))}
        </span>
      )}
    </Text>
  );
};
