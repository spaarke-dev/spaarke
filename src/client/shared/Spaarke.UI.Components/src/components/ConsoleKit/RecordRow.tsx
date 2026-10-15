/**
 * RecordRow — one line of the Decision Record trail: state badge, headline, class / who / when, and an
 * "Open" link. The full entry sits behind a Fluent Accordion (closed by default) when `children` is given.
 *
 * Append-only (FR-21, R-13): the row has no edit or delete control and no overflow menu (D-46); the only
 * action is Open. Task: spaarke-ontology-platform-r1, task 057.
 */

import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Link,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { StatusBadge } from '../StatusBadge';
import { CONSOLE_STATE_BADGES } from './consoleStatus';
import type { ConsoleState, RecordClass } from './consoleStatus';

export interface RecordRowProps {
  recordId: string;
  /** Headline (`sprk_name`). */
  title: string;
  state: ConsoleState;
  recordClass?: RecordClass | null;
  decidedBy?: string | null;
  /** ISO 8601. */
  decidedOn?: string | null;
  /** Called by the Open link. The link is omitted when not supplied. */
  onOpen?: (recordId: string) => void;
  /** The full entry; rendered behind a closed-by-default disclosure. */
  children?: React.ReactNode;
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
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
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  line: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalS, minWidth: 0 },
  title: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  meta: { color: tokens.colorNeutralForeground3, flexShrink: 0 },
});

export const RecordRow: React.FC<RecordRowProps> = ({
  recordId,
  title,
  state,
  recordClass,
  decidedBy,
  decidedOn,
  onOpen,
  children,
  className,
}) => {
  const styles = useStyles();
  const badge = CONSOLE_STATE_BADGES[state];
  const when =
    decidedOn && !Number.isNaN(new Date(decidedOn).getTime()) ? new Date(decidedOn).toLocaleDateString() : null;
  const meta = [recordId, recordClass, decidedBy, when].filter((p): p is string => !!p).join(' · ');

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="record-row">
      <div className={styles.line}>
        <StatusBadge label={badge.label} tone={badge.tone} />
        <Text className={styles.title} title={title}>
          {title}
        </Text>
        <Text size={200} className={styles.meta}>
          {meta}
        </Text>
        {onOpen && <Link onClick={() => onOpen(recordId)}>Open ›</Link>}
      </div>
      {children !== undefined && children !== null && (
        <Accordion collapsible>
          <AccordionItem value="entry">
            <AccordionHeader size="small">Full entry</AccordionHeader>
            <AccordionPanel>{children}</AccordionPanel>
          </AccordionItem>
        </Accordion>
      )}
    </div>
  );
};

RecordRow.displayName = 'RecordRow';
