/**
 * WelcomeStartCards.tsx — the three "get started" quick-action cards shown on the
 * Assistant's cold-open welcome stage (P1-1, UAT 2026-07-18), directly beneath the
 * WelcomePanel heading and above the (always-mounted) SprkChat input.
 *
 * The three actions the owner specified for first-open:
 *   1. Summarize a document → the Summarize Files wizard (its own file-browse popup).
 *   2. Create a matter       → the Create Matter wizard, via the shipped surface-launch
 *      hand-off (same entry point as the Quick Start "Create Matter" card).
 *   3. Compose a document    → open a blank Compose tab in the workspace pane.
 *
 * Justification (CLAUDE.md §11): the shared `GetStartedCardsWidget` is a fixed 7-card
 * grid (create-matter/-project, assign-work, summarize, find-similar, email, meeting)
 * — it has NO "Compose a document" card and shows four actions the owner did not want on
 * first open. This is a focused 3-card welcome surface, not a second general card grid;
 * each card reuses an EXISTING launch mechanism (no new launcher, no new wizard).
 *
 * ADR-021: Fluent v9 semantic tokens only; dark mode adapts automatically. No I/O here —
 * the callbacks are wired by ConversationPane (which owns bffBaseUrl + the pane bus).
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import {
  DocumentTextRegular,
  BriefcaseRegular,
  DocumentAddRegular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  grid: {
    display: 'flex',
    flexWrap: 'wrap',
    justifyContent: 'center',
    gap: tokens.spacingHorizontalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    paddingBottom: tokens.spacingVerticalL,
    flexShrink: 0,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalXS,
    width: '190px',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: 'left',
    cursor: 'pointer',
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    fontFamily: tokens.fontFamilyBase,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      borderTopColor: tokens.colorNeutralStroke1,
      borderRightColor: tokens.colorNeutralStroke1,
      borderBottomColor: tokens.colorNeutralStroke1,
      borderLeftColor: tokens.colorNeutralStroke1,
    },
    ':active': {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  icon: {
    fontSize: '24px',
    color: tokens.colorBrandForeground1,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
  },
  subtitle: {
    color: tokens.colorNeutralForeground3,
  },
});

export interface WelcomeStartCardsProps {
  /** Summarize a document — opens the Summarize Files wizard (file-browse). */
  onSummarize: () => void;
  /** Create a matter — opens the Create Matter wizard (surface-launch hand-off). */
  onCreateMatter: () => void;
  /** Compose a document — opens a blank Compose tab. */
  onCompose: () => void;
}

/** The three cold-open quick-start cards. */
export const WelcomeStartCards: React.FC<WelcomeStartCardsProps> = ({
  onSummarize,
  onCreateMatter,
  onCompose,
}) => {
  const styles = useStyles();

  const cards: Array<{
    key: string;
    icon: React.ReactElement;
    title: string;
    subtitle: string;
    onClick: () => void;
  }> = [
    {
      key: 'summarize',
      icon: <DocumentTextRegular className={styles.icon} />,
      title: 'Summarize a document',
      subtitle: 'Pick a file and get a structured summary.',
      onClick: onSummarize,
    },
    {
      key: 'create-matter',
      icon: <BriefcaseRegular className={styles.icon} />,
      title: 'Create a matter',
      subtitle: 'Open the guided Create Matter wizard.',
      onClick: onCreateMatter,
    },
    {
      key: 'compose',
      icon: <DocumentAddRegular className={styles.icon} />,
      title: 'Compose a document',
      subtitle: 'Start a new document in the editor.',
      onClick: onCompose,
    },
  ];

  return (
    <div className={styles.grid} role="group" aria-label="Get started" data-testid="welcome-start-cards">
      {cards.map(card => (
        <button
          key={card.key}
          type="button"
          className={styles.card}
          onClick={card.onClick}
          data-testid={`welcome-start-card-${card.key}`}
        >
          {card.icon}
          <Text className={styles.title}>{card.title}</Text>
          <Text size={200} className={styles.subtitle}>
            {card.subtitle}
          </Text>
        </button>
      ))}
    </div>
  );
};

WelcomeStartCards.displayName = 'WelcomeStartCards';

export default WelcomeStartCards;
