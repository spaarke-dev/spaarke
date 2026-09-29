/**
 * AiSummaryPopover - Reusable AI Summary popover component.
 *
 * Displays a popover with AI-generated summary content (TLDR + full summary).
 * Fetches summary lazily on first open via callback prop. Includes copy-to-clipboard.
 *
 * Consumer provides a trigger element and an async fetch callback.
 * Zero service dependencies — fully callback-based.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from 'react';
import { useCallback, useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Tooltip,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Spinner,
  shorthands,
} from '@fluentui/react-components';
import { Sparkle20Filled, CopyRegular } from '@fluentui/react-icons';

/**
 * Summary data returned by the fetch callback.
 */
export interface ISummaryData {
  summary: string | null;
  tldr: string | null;
}

/**
 * Props for the AiSummaryPopover component.
 */
export interface IAiSummaryPopoverProps {
  /** The trigger element that opens the popover (typically a Button). */
  trigger: React.ReactElement;
  /** Async callback to fetch summary data. Called once on first open. */
  onFetchSummary: () => Promise<ISummaryData>;
  /** Popover positioning relative to trigger. Default: "after". */
  positioning?: 'above' | 'below' | 'before' | 'after';
  /** Whether to show the arrow. Default: true. */
  withArrow?: boolean;
  /**
   * Body text when the fetch resolves with neither `summary` nor `tldr`.
   *
   * Defaults to the DOCUMENT-oriented copy this component has always shipped,
   * so the nine existing callers (VisualHost, SemanticSearchControl,
   * DocumentCard, RichFilePreview, InsightSummaryCard, DocumentLibrary,
   * MessageQuickView, CalendarVisual, MatterHeader) render exactly what they
   * rendered before this prop existed.
   *
   * Record headers override it: a Matter or Project is not a document, and at
   * R2 ship time an unpopulated summary is the EXPECTED steady state rather
   * than a failure — a separate project populates the column (r2 FR-17).
   */
  emptyText?: string;
}

/** The shipped default for {@link IAiSummaryPopoverProps.emptyText}. */
export const AI_SUMMARY_DEFAULT_EMPTY_TEXT = 'No summary available for this document.';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    width: '480px',
    maxHeight: '400px',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingBottom: tokens.spacingVerticalXS,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  headerLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  centered: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.padding('16px'),
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AiSummaryPopover: React.FC<IAiSummaryPopoverProps> = ({
  trigger,
  onFetchSummary,
  positioning = 'after',
  withArrow = true,
  emptyText = AI_SUMMARY_DEFAULT_EMPTY_TEXT,
}) => {
  const styles = useStyles();

  const [data, setData] = useState<ISummaryData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(() => {
    if (!data) return;
    const text = [data.tldr, data.summary].filter(Boolean).join('\n\n');
    void navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [data]);

  const handleOpenChange = useCallback(
    (_ev: unknown, openData: { open: boolean }) => {
      if (openData.open && !data && !loading) {
        setLoading(true);
        setError(false);
        void onFetchSummary()
          .then(sd => {
            setData(sd);
            setLoading(false);
            return sd;
          })
          .catch(() => {
            setError(true);
            setLoading(false);
          });
      }
    },
    [data, loading, onFetchSummary]
  );

  return (
    <Popover positioning={positioning} withArrow={withArrow} onOpenChange={handleOpenChange}>
      <PopoverTrigger disableButtonEnhancement>{trigger}</PopoverTrigger>
      <PopoverSurface className={styles.surface}>
        <div className={styles.headerRow}>
          <Text className={styles.headerLabel}>
            <Sparkle20Filled aria-hidden="true" />
            AI Summary
          </Text>
          {data && !loading && (
            <Tooltip content={copied ? 'Copied!' : 'Copy'} relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<CopyRegular />}
                aria-label="Copy summary"
                onClick={handleCopy}
              />
            </Tooltip>
          )}
        </div>
        {loading && (
          <div className={styles.centered}>
            <Spinner size="small" label="Loading summary..." />
          </div>
        )}
        {error && <Text data-testid="sparkle-popover-error">Summary not available for this document.</Text>}
        {data && !loading && (
          // The three bodies below are mutually exclusive by construction, and
          // each carries a stable `data-testid` so a consumer suite can assert
          // WHICH state rendered rather than pattern-matching on copy. An empty
          // STRING is falsy here, so `''` lands on the empty state alongside
          // `null`/`undefined` — that is deliberate (r2 FR-17).
          <React.Fragment>
            {data.tldr && (
              <Text weight="semibold" data-testid="sparkle-popover-tldr">
                {data.tldr}
              </Text>
            )}
            {data.summary && (
              <Text style={{ whiteSpace: 'pre-wrap' }} data-testid="sparkle-popover-summary">
                {data.summary}
              </Text>
            )}
            {!data.summary && !data.tldr && <Text data-testid="sparkle-popover-empty">{emptyText}</Text>}
          </React.Fragment>
        )}
      </PopoverSurface>
    </Popover>
  );
};

export default AiSummaryPopover;
