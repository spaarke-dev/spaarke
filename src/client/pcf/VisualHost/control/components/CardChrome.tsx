/**
 * CardChrome - INTERNAL per-card wrapper for the Visual Host PCF.
 *
 * Renders a per-card "chrome": optional title (left) + corner-icon slots (right) +
 * the wrapped card content below. The chrome is additive — when no title and no
 * icon slot is configured, CardChrome renders its children with NO visible header
 * (preserves NFR-05 backward compatibility for every existing chart definition).
 *
 * FR-VH-05: this component is INTERNAL to Visual Host. It MUST NOT be exported
 * from `@spaarke/ui-components` (the contract is not yet stable; the AI sparkle
 * slot is forward-compat for r2 Insights Engine).
 *
 * Wiring rules:
 *  - `onExpand` MUST be wired to the existing `handleExpandClick` in
 *    `VisualHostRoot` so chart-def Drill Through Settings continue to apply
 *    (no new ClickActionHandler code path).
 *  - `showAiSparkle` defaults to `false`. v1 callers should pass `false` (or
 *    omit). The AI sparkle button is contract-only in v1; r2 Insights Engine
 *    will flip the flag per chart def.
 *
 * Standards:
 *  - ADR-021: Fluent v9 + semantic tokens only (no hex/rgb).
 *  - ADR-022: React 16/17 compatible — no React 18+ exclusive APIs.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Tooltip,
  Text,
  shorthands,
} from '@fluentui/react-components';
import { OpenRegular, Sparkle20Regular } from '@fluentui/react-icons';
import {
  AiSummaryPopover,
  type ISummaryData,
} from '../../../../shared/Spaarke.UI.Components/src/components/AiSummaryPopover';

/**
 * Props for the CardChrome wrapper.
 *
 * Contract aligned to spec.md §FR-VH-05.
 */
export interface ICardChromeProps {
  /** Optional title text rendered on the left of the chrome header. */
  title?: string;
  /**
   * Optional callback invoked when the expand corner-icon is clicked.
   * MUST be wired to `VisualHostRoot.handleExpandClick` so chart-def
   * Drill Through Settings apply (no new ClickActionHandler).
   * If omitted, the expand icon is not rendered.
   */
  onExpand?: () => void;
  /**
   * Optional async callback returning AI summary data. Wired to the
   * AI sparkle icon's popover when `showAiSparkle === true`.
   */
  onAiSummary?: () => Promise<ISummaryData>;
  /**
   * Whether the AI sparkle icon is rendered.
   * Default: false. v1 ships with the slot hidden (Insights Engine is r2 work).
   */
  showAiSparkle?: boolean;
  /** The actual card content (chart visual) rendered below the chrome header. */
  children: React.ReactNode;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    minWidth: 0,
    boxSizing: 'border-box',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    width: '100%',
    minHeight: '24px',
    // v1.4.10 — bumped bottom margin from spacingVerticalXS (4px) to
    // spacingVerticalM (12px) per UAT feedback ("add additional space
    // below the header"). All CardChrome-using cards (the 5 Matter charts)
    // get consistent breathing room between the title bar and the chart body.
    ...shorthands.margin(0, 0, tokens.spacingVerticalM, 0),
    ...shorthands.gap(tokens.spacingHorizontalS),
    flexShrink: 0,
  },
  title: {
    // Truncate long titles instead of pushing icons off-screen.
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    flexGrow: 1,
    minWidth: 0,
  },
  iconSlots: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    flexShrink: 0,
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    minWidth: 0,
    flexGrow: 1,
  },
});

/**
 * Per-card chrome wrapper. See file-level comment for the binding rules.
 */
export const CardChrome: React.FC<ICardChromeProps> = ({
  title,
  onExpand,
  onAiSummary,
  showAiSparkle = false,
  children,
}) => {
  const styles = useStyles();

  const hasTitle = !!title && title.trim().length > 0;
  const hasExpand = !!onExpand;
  // v1: AI sparkle slot is contract-only. The button renders ONLY when the
  // caller explicitly opts in via `showAiSparkle === true` AND supplies an
  // `onAiSummary` callback. v1 callers in VisualHostRoot pass false.
  const hasAiSparkle = showAiSparkle === true && !!onAiSummary;
  const hasAnyIcon = hasExpand || hasAiSparkle;
  const renderHeader = hasTitle || hasAnyIcon;

  return (
    <div className={styles.root}>
      {renderHeader && (
        <div className={styles.header}>
          {hasTitle ? (
            <Text
              size={300}
              className={styles.title}
              title={title}
              aria-label={title}
            >
              {title}
            </Text>
          ) : (
            // Spacer so icons stay right-aligned when no title is provided.
            <span aria-hidden={true} style={{ flexGrow: 1 }} />
          )}
          {hasAnyIcon && (
            <div className={styles.iconSlots}>
              {hasAiSparkle && onAiSummary && (
                <AiSummaryPopover
                  trigger={
                    <Tooltip content="AI Summary" relationship="label">
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<Sparkle20Regular />}
                        aria-label="View AI summary"
                      />
                    </Tooltip>
                  }
                  onFetchSummary={onAiSummary}
                  positioning="below"
                />
              )}
              {hasExpand && (
                <Tooltip content="View details" relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<OpenRegular />}
                    onClick={onExpand}
                    aria-label="View details in expanded workspace"
                  />
                </Tooltip>
              )}
            </div>
          )}
        </div>
      )}
      <div className={styles.body}>{children}</div>
    </div>
  );
};
