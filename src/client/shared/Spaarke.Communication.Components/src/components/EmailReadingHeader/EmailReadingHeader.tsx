/**
 * EmailReadingHeader — the reading-pane envelope header sub-view
 * (email-communication-solution-r5 task 034, spec FR-11). Fills the
 * `EmailReadingPaneShell` (task 032) `renderHeader` slot.
 *
 * Renders the selected `sprk_communication` record's subject as the title and
 * a From / To / Cc / Bcc / Sent / Received meta line — mirroring the visual
 * pattern of `CommunicationHeader`
 * (`src/client/code-pages/CommunicationPage/src/components/CommunicationHeader.tsx`,
 * the task's cited canonical reference).
 *
 * DEVIATION NOTE (documented per root CLAUDE.md §6.5 spirit — not a formal ADR
 * conflict, a package-boundary correction): `CommunicationHeader` lives in the
 * private `communication-page` npm workspace package (component-local, not
 * exported), so `@spaarke/communication-components` — a package `communication-page`
 * itself DEPENDS ON — cannot import it without an inverted (leaf-app → shared-lib)
 * dependency, which ADR-012's shared-component boundary forbids. This component
 * is therefore a fresh implementation that matches `CommunicationHeader`'s exact
 * visual language (token-based header band, title row + meta rows) rather than a
 * literal import, and additionally surfaces Cc/Bcc and BOTH Sent/Received rows
 * independently (the source `CommunicationHeader` renders only From/To and a
 * single direction-branched date) — the fuller field set spec FR-11 requires. A
 * natural follow-up (out of this task's scope) is promoting `CommunicationHeader`
 * itself into this shared package, mirroring how `AttachmentList` was promoted in
 * task 021, so `communication-page` and the reading pane converge on one component.
 *
 * React-version note (ADR-022/NFR-05): `React.FC` + standard hooks only — no
 * React-18/19-only runtime API and no `as React.ComponentType` cast. Fluent v9
 * semantic tokens only (ADR-021) — themes correctly via the host `FluentProvider`.
 */
import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Skeleton,
  SkeletonItem,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import { Mail20Regular } from '@fluentui/react-icons';
import type { IEmailHeaderRecord, IEmailReadingHeaderProps } from './EmailReadingHeader.types';

const useStyles = makeStyles({
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalL,
    paddingInline: tokens.spacingHorizontalXL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  titleRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM },
  channelIcon: { color: tokens.colorBrandForeground1, fontSize: '20px', flexShrink: 0 },
  title: { flex: 1, minWidth: 0 },
  meta: { display: 'flex', gap: tokens.spacingHorizontalL, flexWrap: 'wrap', color: tokens.colorNeutralForeground3 },
  metaItem: { display: 'flex', gap: tokens.spacingHorizontalXS, alignItems: 'baseline' },
  metaLabel: { color: tokens.colorNeutralForeground4 },
  skeletonWrap: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  errorWrap: { padding: tokens.spacingHorizontalM },
});

const HEADER_SELECT = '$select=sprk_subject,sprk_from,sprk_to,sprk_cc,sprk_bcc,sprk_sentat,sprk_receiveddate';

function fmtDate(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (isNaN(d.getTime())) return null;
  return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

/** Structural cast helper — the retrieved record has whatever shape the host's `$select` returns. */
function toHeaderRecord(raw: Record<string, unknown>): IEmailHeaderRecord {
  return raw as IEmailHeaderRecord;
}

export const EmailReadingHeader: React.FC<IEmailReadingHeaderProps> = ({ selectedId, dataService }) => {
  const s = useStyles();
  const [record, setRecord] = React.useState<IEmailHeaderRecord | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!selectedId) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    void dataService
      .retrieveRecord('sprk_communication', selectedId, HEADER_SELECT)
      .then(raw => {
        if (!cancelled) setRecord(toHeaderRecord(raw));
      })
      .catch(err => {
        console.error('[EmailReadingHeader] retrieveRecord failed:', err);
        if (!cancelled) setError('Could not load this email header.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [dataService, selectedId]);

  if (error) {
    return (
      <div className={s.header}>
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      </div>
    );
  }

  if (loading || !record) {
    return (
      <div className={s.header} data-testid="email-header-skeleton">
        <Skeleton className={s.skeletonWrap}>
          <SkeletonItem shape="rectangle" style={{ width: '60%', height: '20px' }} />
          <SkeletonItem shape="rectangle" style={{ width: '80%', height: '16px' }} />
        </Skeleton>
      </div>
    );
  }

  const sent = fmtDate(record.sprk_sentat);
  const received = fmtDate(record.sprk_receiveddate);

  return (
    <div className={s.header} data-testid="email-reading-header">
      <div className={s.titleRow}>
        <span className={s.channelIcon}>
          <Mail20Regular />
        </span>
        <Text as="h1" size={500} weight="semibold" className={s.title} truncate wrap={false}>
          {record.sprk_subject || '(no subject)'}
        </Text>
      </div>
      <div className={s.meta}>
        {record.sprk_from && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              From
            </Text>
            <Text size={200}>{record.sprk_from}</Text>
          </span>
        )}
        {record.sprk_to && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              To
            </Text>
            <Text size={200}>{record.sprk_to}</Text>
          </span>
        )}
        {record.sprk_cc && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              Cc
            </Text>
            <Text size={200}>{record.sprk_cc}</Text>
          </span>
        )}
        {record.sprk_bcc && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              Bcc
            </Text>
            <Text size={200}>{record.sprk_bcc}</Text>
          </span>
        )}
        {sent && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              Sent
            </Text>
            <Text size={200}>{sent}</Text>
          </span>
        )}
        {received && (
          <span className={s.metaItem}>
            <Text size={200} className={s.metaLabel}>
              Received
            </Text>
            <Text size={200}>{received}</Text>
          </span>
        )}
      </div>
    </div>
  );
};

EmailReadingHeader.displayName = 'EmailReadingHeader';
