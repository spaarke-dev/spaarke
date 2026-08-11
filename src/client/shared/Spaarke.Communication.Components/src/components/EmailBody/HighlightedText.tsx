/**
 * HighlightedText.tsx — renders a normalized text string with a resolved
 * `[start, end)` span wrapped in an ephemeral highlight `<mark>` and, when
 * `active`, scrolled into view (email-communication-intelligence-r2 task 054,
 * NFR-11). The span offsets come from `resolveQuotedCitation` (over the SAME
 * normalized text passed here), so the highlight is exact — no re-search, no
 * fuzzy/nearest guess. Fluent v9 semantic tokens (ADR-021) — visible in light +
 * dark. React-version-agnostic (ADR-022): `React.FC` + standard hooks.
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    whiteSpace: 'pre-wrap',
    overflowWrap: 'anywhere',
    wordBreak: 'break-word',
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  mark: {
    // Theme-aware highlighter — the yellow palette tokens adapt light/dark, so the
    // marked span keeps legible contrast in both (ADR-021, no hardcoded colors).
    backgroundColor: tokens.colorPaletteYellowBackground2,
    color: tokens.colorNeutralForeground1,
    borderRadius: tokens.borderRadiusSmall,
    paddingInline: tokens.spacingHorizontalXXS,
  },
});

export interface HighlightedTextProps {
  /** The normalized segment text the span offsets index into. */
  text: string;
  /** Start offset (inclusive) of the highlighted span. */
  start: number;
  /** End offset (exclusive) of the highlighted span. */
  end: number;
  /** When true, the highlighted span is scrolled into view on mount / span change. */
  active?: boolean;
  className?: string;
}

/** Splits `text` into before / marked / after and scrolls the mark into view when `active`. */
export const HighlightedText: React.FC<HighlightedTextProps> = ({ text, start, end, active = true, className }) => {
  const s = useStyles();
  const markRef = React.useRef<HTMLElement | null>(null);

  // Clamp defensively so bad offsets can never throw or hide text.
  const from = Math.max(0, Math.min(start, text.length));
  const to = Math.max(from, Math.min(end, text.length));

  React.useEffect(() => {
    if (active && markRef.current) {
      markRef.current.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }, [active, from, to, text]);

  return (
    <Text as="span" className={className ? `${s.root} ${className}` : s.root} data-testid="citation-highlighted-text">
      {text.slice(0, from)}
      <mark ref={markRef} className={s.mark} data-testid="citation-highlight-mark">
        {text.slice(from, to)}
      </mark>
      {text.slice(to)}
    </Text>
  );
};
HighlightedText.displayName = 'HighlightedText';
