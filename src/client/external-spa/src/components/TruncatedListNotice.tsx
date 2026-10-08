import * as React from 'react';
import { MessageBar, MessageBarBody } from '@fluentui/react-components';

/**
 * Says plainly that a list is not complete (unified-access-control-r2 task 105, NFR-03 — a cap is never silent).
 *
 * The BFF returns `truncated: true` when it stopped at its row cap or lost a later page, so the rows shown are only
 * part of the list. Every list view that can receive such a list renders this notice above it; one component keeps
 * the wording identical everywhere.
 *
 * Renders nothing when `truncated` is false.
 */
export const TruncatedListNotice: React.FC<{
  /** Whether the list is known to be incomplete. */
  truncated: boolean;
  /** How many rows are shown. */
  shown: number;
  /** Plural noun for the rows, e.g. "documents". */
  noun: string;
}> = ({ truncated, shown, noun }) => {
  if (!truncated) return null;

  return (
    <MessageBar intent="warning" data-testid="truncated-list-notice">
      <MessageBarBody>
        {`This list is incomplete: only ${shown} ${noun} could be shown. Some ${noun} are not listed here.`}
      </MessageBarBody>
    </MessageBar>
  );
};

export default TruncatedListNotice;
