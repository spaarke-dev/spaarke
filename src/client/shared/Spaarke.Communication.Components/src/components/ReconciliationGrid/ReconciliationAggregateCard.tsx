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
import { Spinner } from '@fluentui/react-components';
import { MailInboxRegular } from '@fluentui/react-icons';
import { AggregateCard, type IDataverseClient } from '@spaarke/ui-components';
import { useNeedsReviewCount } from './needsReviewCount';

export interface ReconciliationAggregateCardProps {
  /** MUST be referentially stable (memoised) - see useNeedsReviewCount. */
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
  const { count, loading } = useNeedsReviewCount(dataverseClient, configId);
  // While loading, show a neutral placeholder - "Missing" is for a failed/unavailable count only.
  if (loading) {
    return (
      <div className={className} data-testid="aggregate-card-loading" role="status" aria-busy="true">
        <Spinner size="tiny" label="Counting emails awaiting a match confirmation" labelPosition="after" />
      </div>
    );
  }
  return (
    <AggregateCard
      count={count}
      label={count === 1 ? 'email awaits a match confirmation' : 'emails await a match confirmation'}
      linkLabel="Open Email Review"
      onOpen={onOpen}
      icon={<MailInboxRegular />}
      className={className}
    />
  );
};
ReconciliationAggregateCard.displayName = 'ReconciliationAggregateCard';
