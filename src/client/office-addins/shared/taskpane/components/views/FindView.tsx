import React from 'react';
import { makeStyles, tokens, Text, Body1 } from '@fluentui/react-components';
import { DocumentSearchRegular } from '@fluentui/react-icons';

/**
 * FindView — the Find tab's r1 FRAME ONLY (task 015 / FR-03).
 *
 * This is a placeholder mount point. It issues NO network request and renders NO
 * results list — the real Find view (three-state gating on `sprk_searchindexed`, Run
 * Index, similarity results) is tasks 033-034. Their prerequisite, task 032's per-row
 * authorization on the similarity surface (plan.md finding F-b), is already in place.
 * Do not add a list, a fetch call, or any pager control here (ADR-051) — when tasks
 * 033-034 build the real view, it takes over this mount point.
 *
 * Fluent UI v9 + Griffel `makeStyles` + semantic tokens only (ADR-021).
 */

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  icon: {
    fontSize: '32px',
    color: tokens.colorNeutralForeground3,
  },
});

export const FindView: React.FC = () => {
  const styles = useStyles();

  return (
    <div className={styles.container}>
      <DocumentSearchRegular className={styles.icon} />
      <Text weight="semibold">Find is coming soon</Text>
      <Body1>Finding similar documents isn&apos;t available yet.</Body1>
    </div>
  );
};

export default FindView;
