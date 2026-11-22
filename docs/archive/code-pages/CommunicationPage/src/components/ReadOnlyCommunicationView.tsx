/**
 * READ-ONLY CHANNEL RENDERER — Teams / SMS / Notification (and any future
 * channel that has no interactive composer yet).
 *
 * This is the NFR-04 extensibility payoff: a new channel needs only a
 * read-only renderer (or reuses this generic one) — the shell contract and the
 * layout switch key do not change. The renderer is channel-agnostic; it
 * displays the record fields common to every `sprk_communication` row.
 */

import * as React from 'react';
import { makeStyles, tokens, Title2, Caption1, Body1, Field, Divider, Button } from '@fluentui/react-components';
import type { ICommunicationNavCallbacks, ICommunicationRecord } from '../types/communication';

export interface IReadOnlyCommunicationViewProps {
  /** Human-readable channel label (from the option-set formatted value). */
  channelLabel: string;
  record: ICommunicationRecord;
  nav: ICommunicationNavCallbacks;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    height: '100%',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalM,
  },
  body: {
    whiteSpace: 'pre-wrap',
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

function displayOrDash(value: string | null | undefined): string {
  return value && value.trim() ? value : '—';
}

export function ReadOnlyCommunicationView(props: IReadOnlyCommunicationViewProps): React.JSX.Element {
  const styles = useStyles();
  const { record } = props;

  return (
    <div className={styles.root}>
      <div className={styles.header}>
        <div>
          <Title2>{displayOrDash(record.sprk_subject)}</Title2>
          <Caption1>
            {props.channelLabel} · read-only{record.sprk_directionname ? ` · ${record.sprk_directionname}` : ''}
          </Caption1>
        </div>
        <Button appearance="secondary" onClick={props.nav.onClose}>
          Close
        </Button>
      </div>

      <Divider />

      <Field label="From">
        <Body1>{displayOrDash(record.sprk_from)}</Body1>
      </Field>
      <Field label="To">
        <Body1>{displayOrDash(record.sprk_to)}</Body1>
      </Field>
      {record.sprk_cc && (
        <Field label="Cc">
          <Body1>{record.sprk_cc}</Body1>
        </Field>
      )}
      <Field label="Message">
        <div className={styles.body}>
          <Body1>{displayOrDash(record.sprk_body)}</Body1>
        </div>
      </Field>
    </div>
  );
}
