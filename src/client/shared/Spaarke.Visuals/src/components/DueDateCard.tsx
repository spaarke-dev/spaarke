/**
 * DueDateCard (presentational — @spaarke/visuals)
 * Renders a single EventDueDateCard for an event, with loading / error / empty
 * states. Data-in only: the host container fetches the event, maps it to
 * `IEventDueDateCardProps`, and passes it here. No webApi / Xrm / FetchXML.
 *
 * VHVU-050 — extracted from the VisualHost PCF's self-fetching DueDateCardVisual.
 */

import * as React from 'react';
import { Spinner, makeStyles, tokens, Text, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { EventDueDateCard, type IEventDueDateCardProps } from './EventDueDateCard';

export interface IDueDateCardProps {
  /** Already-fetched + mapped card data (null while unresolved / not found) */
  cardProps: IEventDueDateCardProps | null;
  /** Loading state driven by the host container's fetch */
  loading?: boolean;
  /** Error message from the host container's fetch */
  error?: string | null;
  /** Click handler — receives the event id */
  onCardClick?: (eventId: string) => void;
  /** Whether a navigation triggered by a card click is in progress */
  isNavigating?: boolean;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
    minHeight: '80px',
    // v1.4.12 — visual <body> top padding so the chart content sits below
    // CardChrome's header with consistent breathing room (per UAT).
    paddingTop: '20px',
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    padding: tokens.spacingVerticalM,
  },
});

export const DueDateCard: React.FC<IDueDateCardProps> = ({
  cardProps,
  loading = false,
  error = null,
  onCardClick,
  isNavigating = false,
}) => {
  const styles = useStyles();

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner size="small" label="Loading event..." />
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

  if (!cardProps) {
    return (
      <div className={styles.empty}>
        <Text size={200}>No event data available</Text>
      </div>
    );
  }

  return <EventDueDateCard {...cardProps} onClick={onCardClick} isNavigating={isNavigating} />;
};
