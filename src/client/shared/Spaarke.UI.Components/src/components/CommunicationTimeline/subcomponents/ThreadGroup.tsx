/**
 * ThreadGroup.tsx
 *
 * One collapsible thread group in regarding mode (R2 task 020, FR-03): a
 * header showing the thread's `sprk_name` + message count, with an
 * expand/collapse chevron; when expanded, renders that thread's interleaved
 * email+chat timeline by REUSING `buildTimeline` + `MessageRow` +
 * `ChannelBadge` — the exact R1 rendering engine, not a fork of it (ADR-012,
 * §11). Mirrors the interaction pattern of `WorkspaceShell/SectionPanel.tsx`
 * (Button + `ChevronDownRegular`/`ChevronUpRegular` + `aria-expanded`) so the
 * collapse affordance is consistent across the shared library.
 *
 * Read-only: no compose box here (see `CommunicationTimeline.types.ts`
 * `CommunicationTimelineRegardingModeProps` doc — composing stays inside a
 * specific thread, not the grouped view).
 *
 * NO client-side access/privacy filtering (NFR-03) — this component renders
 * exactly the messages it is given.
 */
import * as React from 'react';
import { Badge, Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { ChevronDownRegular, ChevronUpRegular } from '@fluentui/react-icons';
import { buildTimeline } from '../CommunicationTimeline.buildTimeline';
import { MessageRow } from './MessageRow';
import type { RegardingThreadGroup } from '../CommunicationTimeline.types';

export interface IThreadGroupProps {
  group: RegardingThreadGroup;
  expanded: boolean;
  onToggle: (threadId: string) => void;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  header: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  name: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    flexGrow: 1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
  },
  emptyState: {
    padding: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
});

export const ThreadGroup: React.FC<IThreadGroupProps> = ({ group, expanded, onToggle }) => {
  const styles = useStyles();

  // Only pay the interleave/nesting cost for a group's messages once it is expanded and visible.
  const timeline = React.useMemo(() => (expanded ? buildTimeline(group.messages) : []), [expanded, group.messages]);

  const handleToggle = React.useCallback(() => onToggle(group.threadId), [onToggle, group.threadId]);

  return (
    <div className={styles.root}>
      <div
        className={styles.header}
        role="button"
        tabIndex={0}
        aria-expanded={expanded}
        aria-label={`${expanded ? 'Collapse' : 'Expand'} thread ${group.name}`}
        onClick={handleToggle}
        onKeyDown={e => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            handleToggle();
          }
        }}
      >
        <Text className={styles.name}>{group.name}</Text>
        <Badge appearance="tint" color="informative" size="small">
          {group.messageCount}
        </Badge>
        <Button
          appearance="transparent"
          size="small"
          icon={expanded ? <ChevronUpRegular /> : <ChevronDownRegular />}
          aria-hidden="true"
          tabIndex={-1}
        />
      </div>

      {expanded && (
        <div className={styles.body} role="log" aria-label={`Messages in ${group.name}`}>
          {timeline.map(entry => (
            <MessageRow key={entry.message.id} message={entry.message} depth={entry.depth} />
          ))}
          {timeline.length === 0 && <Text className={styles.emptyState}>No messages in this thread.</Text>}
        </div>
      )}
    </div>
  );
};

ThreadGroup.displayName = 'ThreadGroup';
