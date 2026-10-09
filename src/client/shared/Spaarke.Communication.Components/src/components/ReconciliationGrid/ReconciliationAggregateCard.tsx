/**
 * ReconciliationAggregateCard - the worklist's single aggregate reconciliation item: "N emails await a
 * match confirmation" linking to the reconciliation tab. A computed count, NOT a Signal and not a lane.
 * The count comes from {@link useNeedsReviewCount} (the Needs Review grid configuration's FetchXML), so it
 * equals the tab's. Placement (top of the Do lane, outside lane counts, hidden under a Do filter) is the
 * worklist host's (task 059). Reuses the console kit's AggregateCard; tokens only.
 *
 * Task: spaarke-ontology-platform-r1, task 054 (FR-29).
 */
import * as React from 'react';
import { MailInboxRegular } from '@fluentui/react-icons';
import { AggregateCard, type IDataverseClient } from '@spaarke/ui-components';
import { useNeedsReviewCount } from './needsReviewCount';

export interface ReconciliationAggregateCardProps {
  dataverseClient: IDataverseClient | undefined;
  /** Opens the reconciliation tab. */
  onOpen: () => void;
  /** Override the Needs Review grid configuration id (defaults to the seeded one). */
  configId?: string;
  className?: string;
}

export const ReconciliationAggregateCard: React.FC<ReconciliationAggregateCardProps> = ({
  dataverseClient,
  onOpen,
  configId,
  className,
}) => {
  const { count } = useNeedsReviewCount(dataverseClient, configId);
  return (
    <AggregateCard
      count={count}
      label="emails await a match confirmation"
      linkLabel="Open Email Review"
      onOpen={onOpen}
      icon={<MailInboxRegular />}
      className={className}
    />
  );
};
ReconciliationAggregateCard.displayName = 'ReconciliationAggregateCard';
