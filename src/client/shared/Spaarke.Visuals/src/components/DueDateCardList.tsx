/**
 * DueDateCardList (presentational — @spaarke/visuals)
 * Renders a list of EventDueDateCards with loading / error / empty states and
 * an optional "View All" link. Data-in only: the host container fetches +
 * maps the events and owns record navigation. No webApi / Xrm / FetchXML.
 *
 * VHVU-050 — extracted from the VisualHost PCF's self-fetching
 * DueDateCardListVisual (which reached window.Xrm directly).
 */

import * as React from 'react';
import { Spinner, makeStyles, tokens, Text, Link, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { ChevronRight20Regular } from '@fluentui/react-icons';
import { EventDueDateCard, type IEventDueDateCardProps } from './EventDueDateCard';

export interface IDueDateCardListProps {
  /** Already-fetched + mapped cards */
  cards: IEventDueDateCardProps[];
  /** Loading state driven by the host container's fetch */
  loading?: boolean;
  /** Error message from the host container's fetch */
  error?: string | null;
  /** Event id currently navigating (renders that card's spinner) */
  navigatingId?: string | null;
  /** Click handler — receives the event id; host owns the actual navigation */
  onCardClick?: (eventId: string) => void;
  /** Whether to show the "View All" link */
  showViewListLink?: boolean;
  /** "View All" link handler */
  onViewListClick?: () => void;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    gap: tokens.spacingVerticalS,
    // v1.4.12 — visual <body> top padding so the list of cards sits below
    // CardChrome's header with consistent breathing room (per UAT).
    paddingTop: '20px',
  },
  cardList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  viewListLink: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalXS,
    padding: tokens.spacingVerticalXS,
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100px',
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    padding: tokens.spacingVerticalL,
  },
});

export const DueDateCardList: React.FC<IDueDateCardListProps> = ({
  cards,
  loading = false,
  error = null,
  navigatingId = null,
  onCardClick,
  showViewListLink = false,
  onViewListClick,
}) => {
  const styles = useStyles();

  if (loading) {
    return (
      <div className={styles.loading}>
        <Spinner size="small" label="Loading events..." />
      </div>
    );
  }

  if (error) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }

  if (cards.length === 0) {
    return (
      <div className={styles.empty}>
        <Text size={200}>No upcoming events</Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.cardList}>
        {cards.map(card => (
          <EventDueDateCard
            key={card.eventId}
            {...card}
            onClick={onCardClick}
            isNavigating={navigatingId === card.eventId}
          />
        ))}
      </div>

      {showViewListLink && (
        <div className={styles.viewListLink}>
          <Link onClick={onViewListClick}>
            View All <ChevronRight20Regular />
          </Link>
        </div>
      )}
    </div>
  );
};
