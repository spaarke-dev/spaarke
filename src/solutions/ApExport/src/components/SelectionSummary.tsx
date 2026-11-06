/** "N invoices, total X" for a selection (one line per currency). */

import * as React from 'react';
import { Text } from '@fluentui/react-components';
import { formatAmount, type SelectionSummary as Summary } from '../services/invoices';

export const SelectionSummary: React.FC<{ summary: Summary; label?: string }> = ({ summary, label = 'Selected' }) => {
  const totals =
    summary.totals.length === 0 ? '—' : summary.totals.map(t => formatAmount(t.amount, t.currency)).join(' + ');
  return (
    <Text>
      {label}: <strong>{summary.count}</strong> invoice{summary.count === 1 ? '' : 's'}, approved total <strong>{totals}</strong>
      {summary.withoutTotal > 0 && ` (${summary.withoutTotal} without a total shown)`}
    </Text>
  );
};
