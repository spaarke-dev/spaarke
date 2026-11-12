/**
 * Manage Access — the notes bell (unified-access-control-r2 task 196, owner UX decision 2026-11-12).
 *
 * The modal's INFORMATIONAL messages (the record's Secure / Restricted / Limited explanation, the parent's minimum access,
 * the secure owner) used to stack as banners at the top of the dialog, where they took space and could not be put
 * away. They now sit behind a bell in the modal header (`SprkModal` `headerActions`, beside maximize and close): the
 * badge counts the current notes, and the popover lists each one in full, links included. Messages that block Save,
 * explain a disabled action where it is, or report a failure stay inline in the modal body — they are never passed here.
 *
 * No persistence: the badge is the number of notes the modal holds now, so it changes as the record does.
 */

import * as React from 'react';
import {
  Button,
  CounterBadge,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Alert20Regular } from '@fluentui/react-icons';

/** One informational note shown behind the bell. */
export interface IAccessNote {
  /** Stable key. */
  id: string;
  intent: 'info' | 'warning' | 'error';
  title: string;
  /** The note's full text; may hold links (e.g. the parent record). */
  content: React.ReactNode;
}

export interface IAccessNotesBellProps {
  notes: readonly IAccessNote[];
}

const useStyles = makeStyles({
  trigger: { position: 'relative', display: 'inline-flex' },
  badge: {
    position: 'absolute',
    top: `calc(-1 * ${tokens.spacingVerticalXXS})`,
    right: `calc(-1 * ${tokens.spacingHorizontalXXS})`,
    pointerEvents: 'none',
  },
  surface: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    maxWidth: 'min(420px, 80vw)',
    maxHeight: 'min(480px, 70vh)',
    overflowY: 'auto',
  },
  heading: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
});

/** The accessible name of the bell, with the count (e.g. "Access notes (2)"). */
export function accessNotesLabel(count: number): string {
  return `Access notes (${count})`;
}

export const AccessNotesBell: React.FC<IAccessNotesBellProps> = ({ notes }) => {
  const styles = useStyles();
  const count = notes.length;
  const label = accessNotesLabel(count);
  return (
    <span className={styles.trigger}>
      <Popover withArrow positioning="below-end" trapFocus>
        <PopoverTrigger disableButtonEnhancement>
          <Button
            appearance="subtle"
            size="small"
            icon={<Alert20Regular />}
            aria-label={label}
            title={label}
            data-testid="access-notes-bell"
          />
        </PopoverTrigger>
        <PopoverSurface className={styles.surface} aria-label="Access notes" data-testid="access-notes-popover">
          <Text className={styles.heading}>Access notes</Text>
          {count === 0 ? (
            <Text>There are no notes about this record&apos;s access.</Text>
          ) : (
            notes.map(note => (
              <MessageBar key={note.id} intent={note.intent} layout="multiline">
                <MessageBarBody>
                  <MessageBarTitle>{note.title}</MessageBarTitle>
                  {note.content}
                </MessageBarBody>
              </MessageBar>
            ))
          )}
        </PopoverSurface>
      </Popover>
      {count > 0 && (
        <CounterBadge
          className={styles.badge}
          count={count}
          size="small"
          color="informative"
          aria-hidden
          data-testid="access-notes-count"
        />
      )}
    </span>
  );
};
