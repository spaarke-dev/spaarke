/**
 * NotificationBell - the portal header's bell: an unread badge, and a popover listing the in-portal notification feed
 * (spaarke-SPA-external-access-platform-r3 task 055, FR-08; server: task 037).
 *
 * BEHAVIOUR
 *   - The badge is the feed's `unreadCount` for the page the server returned. When the feed is known to be incomplete
 *     (`truncated`) or the page is full, the count is a floor and shows as "N+" ("100+" for a full page).
 *   - Opening the bell re-reads the feed and, only if that read succeeded and had unread items, posts `/seen` once; the badge
 *     clears. The items that were new stay highlighted while the list is open. A failed read is shown as a failure, never as
 *     an empty list, and never reaches `/seen` (see `useNotificationFeed`).
 *   - Items are listed newest first. A click opens that record's detail view through the shared opener
 *     (`RecordOpener`): the same `RecordDetailModal` a grid row opens, so a record the caller can no longer see shows its
 *     existing denied state. An item whose record type or id is not one the detail view can open is shown but is not a link.
 *   - The feed carries no message body, so none is shown: a message item reads "{sender} sent a message on {record}".
 *
 * ACCESSIBILITY. The bell button's name carries the unread count ("Notifications, 3 unread"); the badge itself is decorative.
 * The popover traps focus, closes on Escape and returns focus to the bell; the list is a vertical arrow-key group and every
 * openable item is a native button. After a record's detail view closes, focus goes back to the bell.
 *
 * THEMING (ADR-021). Fluent v9 components and semantic tokens only: no hard-coded colour, so light, dark and the Teams
 * themes all follow the surrounding FluentProvider.
 */
import * as React from 'react';
import {
  Badge,
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
  useArrowNavigationGroup,
} from '@fluentui/react-components';
import { Alert20Regular, AlertOn20Regular, Chat20Regular, PersonAdd20Regular } from '@fluentui/react-icons';
import { unreadBadgeText, type ExternalNotification, type NotificationFailure } from '../../api/notifications-client';
import { useNotificationFeed } from '../../hooks/useNotificationFeed';
import { useOpenRecord } from '../detail/RecordOpener';
import { getRecordDetailEntity } from '../detail/RecordDetailFields';
import { recordTargetOfFeedItem, type RecordTarget } from '../detail/record-targets';

const useStyles = makeStyles({
  anchor: { position: 'relative', display: 'inline-flex', alignItems: 'center' },
  badge: {
    position: 'absolute',
    top: tokens.spacingVerticalXXS,
    right: tokens.spacingHorizontalXXS,
    pointerEvents: 'none',
  },
  surface: {
    padding: 0,
    width: '380px',
    maxWidth: 'calc(100vw - 32px)',
    maxHeight: '70vh',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  head: {
    flexShrink: 0,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  headTitle: { margin: 0, color: tokens.colorNeutralForeground1 },
  body: { overflowY: 'auto', minHeight: 0 },
  notice: { padding: tokens.spacingHorizontalM },
  center: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  list: { listStyleType: 'none', margin: 0, padding: 0 },
  row: {
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    ':last-child': { borderBottomStyle: 'none' },
  },
  item: {
    display: 'flex',
    alignItems: 'flex-start',
    columnGap: tokens.spacingHorizontalS,
    width: '100%',
    boxSizing: 'border-box',
    textAlign: 'left',
    font: 'inherit',
    color: tokens.colorNeutralForeground1,
    backgroundColor: 'transparent',
    borderTopStyle: 'none',
    borderRightStyle: 'none',
    borderBottomStyle: 'none',
    borderLeftStyle: 'none',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },
  itemButton: {
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':active': { backgroundColor: tokens.colorNeutralBackground1Pressed },
    ':focus-visible': {
      outlineWidth: tokens.strokeWidthThick,
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: `calc(-1 * ${tokens.strokeWidthThick})`,
    },
  },
  itemFresh: { backgroundColor: tokens.colorBrandBackground2 },
  dot: {
    flexShrink: 0,
    width: '8px',
    height: '8px',
    marginTop: tokens.spacingVerticalXS,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: 'transparent',
  },
  dotFresh: { backgroundColor: tokens.colorBrandBackground },
  icon: { flexShrink: 0, color: tokens.colorNeutralForeground3, marginTop: tokens.spacingVerticalXXS },
  text: { display: 'flex', flexDirection: 'column', minWidth: 0, rowGap: tokens.spacingVerticalXXS },
  primary: { overflowWrap: 'anywhere' },
  meta: { color: tokens.colorNeutralForeground3 },
  foot: {
    padding: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
});

// ---------------------------------------------------------------------------
// Wording
// ---------------------------------------------------------------------------

/** "a matter", "a work assignment": what a record is called when the server sent no title. */
function recordNoun(item: ExternalNotification): string {
  const entityName = recordTargetOfFeedItem(item)?.entityName;
  const label = getRecordDetailEntity(entityName)?.label ?? 'record';
  return `a ${label.toLowerCase()}`;
}

/** The headline of an item. A null title falls back to the record's kind, never to a blank or an id. */
export function notificationText(item: ExternalNotification): string {
  const what = item.recordTitle ?? recordNoun(item);
  return item.kind === 'grant'
    ? `You were given access to ${what}`
    : `${item.senderName ?? 'Someone'} sent a message on ${what}`;
}

/** "5 minutes ago", "yesterday", or a date for anything older than a week. */
export function relativeTime(iso: string, now: number = Date.now()): string {
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return '';
  const seconds = Math.round((then - now) / 1000);
  const abs = Math.abs(seconds);
  if (abs < 60) return 'just now';
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  if (abs < 3600) return rtf.format(Math.round(seconds / 60), 'minute');
  if (abs < 86_400) return rtf.format(Math.round(seconds / 3600), 'hour');
  if (abs < 7 * 86_400) return rtf.format(Math.round(seconds / 86_400), 'day');
  return new Date(then).toLocaleDateString();
}

function failureText(failure: NotificationFailure): string {
  switch (failure.kind) {
    case 'signin':
      return 'Sign in again to see notifications.';
    case 'unauthorized':
      return "Notifications aren't available for your account.";
    case 'rate-limited':
      return "Notifications are being requested too often. They'll refresh again shortly.";
    case 'contact-unreadable':
      return "We couldn't check your account just now, so your notifications weren't read or changed. Try again shortly.";
    default:
      return "Notifications couldn't be loaded just now.";
  }
}

/** The bell's accessible name: includes the unread count. */
export function bellLabel(unreadText: string | null): string {
  if (!unreadText) return 'Notifications';
  return unreadText.endsWith('+')
    ? `Notifications, ${unreadText.slice(0, -1)} or more unread`
    : `Notifications, ${unreadText} unread`;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NotificationBell: React.FC = () => {
  const styles = useStyles();
  const openRecord = useOpenRecord();
  const [open, setOpen] = React.useState(false);
  const bellRef = React.useRef<HTMLButtonElement>(null);
  const arrowNav = useArrowNavigationGroup({ axis: 'vertical', circular: true });
  const { feed, failure, loading, seenFailed, freshIds, refreshAndMarkSeen } = useNotificationFeed({ paused: open });

  const unreadText = unreadBadgeText(feed);
  const label = bellLabel(unreadText);

  const handleOpenChange = React.useCallback(
    (next: boolean, key?: string) => {
      setOpen(next);
      if (next) void refreshAndMarkSeen();
      // Escape returns focus to the bell. (A click elsewhere must not take focus back, so only Escape does this.)
      else if (key === 'Escape') window.setTimeout(() => bellRef.current?.focus(), 0);
    },
    [refreshAndMarkSeen]
  );

  const handleItemClick = React.useCallback(
    (target: RecordTarget) => {
      setOpen(false);
      openRecord(target, { returnFocusTo: bellRef.current });
    },
    [openRecord]
  );

  const renderItem = (item: ExternalNotification): React.ReactNode => {
    const target = recordTargetOfFeedItem(item);
    const fresh = item.isUnread || freshIds.has(item.id);
    const text = notificationText(item);
    const when = relativeTime(item.occurredOn);
    const kindLabel = getRecordDetailEntity(target?.entityName)?.label;
    const KindIcon = item.kind === 'grant' ? PersonAdd20Regular : Chat20Regular;
    const content = (
      <>
        <span className={mergeClasses(styles.dot, fresh && styles.dotFresh)} aria-hidden="true" />
        <KindIcon className={styles.icon} aria-hidden="true" />
        <span className={styles.text}>
          <Text className={styles.primary} weight={fresh ? 'semibold' : 'regular'}>
            {text}
          </Text>
          <Text size={200} className={styles.meta}>
            {[kindLabel, when].filter(Boolean).join(' · ')}
          </Text>
        </span>
      </>
    );
    return (
      <li key={`${item.kind}:${item.id}`} className={styles.row}>
        {target ? (
          <button
            type="button"
            className={mergeClasses(styles.item, styles.itemButton, fresh && styles.itemFresh)}
            aria-label={`${fresh ? 'Unread. ' : ''}${text}. ${when}`.trim()}
            onClick={() => handleItemClick(target)}
          >
            {content}
          </button>
        ) : (
          <div className={mergeClasses(styles.item, fresh && styles.itemFresh)}>{content}</div>
        )}
      </li>
    );
  };

  const retry = (
    <MessageBarActions>
      <Button size="small" onClick={() => void refreshAndMarkSeen()}>
        Try again
      </Button>
    </MessageBarActions>
  );

  let body: React.ReactNode;
  if (failure) {
    // A failed read is a failure, not an empty feed.
    body = (
      <MessageBar intent="error" className={styles.notice} data-testid="notifications-error">
        <MessageBarBody>{failureText(failure)}</MessageBarBody>
        {retry}
      </MessageBar>
    );
  } else if (!feed) {
    body = (
      <div className={styles.center}>
        <Spinner size="small" label="Loading notifications..." />
      </div>
    );
  } else if (feed.value.length === 0) {
    body = (
      <div className={styles.center} role="status" data-testid="notifications-empty">
        <Text>You're all caught up. Nothing new in the last 30 days.</Text>
      </div>
    );
  } else {
    body = (
      <>
        {seenFailed && (
          <MessageBar intent="warning" className={styles.notice} data-testid="notifications-seen-failed">
            <MessageBarBody>
              We couldn't mark these as read. They'll stay unread until you open notifications again.
            </MessageBarBody>
          </MessageBar>
        )}
        <ul className={styles.list} aria-label="Notifications" {...arrowNav}>
          {feed.value.map(renderItem)}
        </ul>
        {feed.truncated && (
          <Text as="p" size={200} className={styles.foot} data-testid="notifications-truncated">
            Some notifications may not be shown.
          </Text>
        )}
      </>
    );
  }

  return (
    <Popover
      open={open}
      onOpenChange={(e, data) => handleOpenChange(data.open, 'key' in e ? e.key : undefined)}
      positioning="below-end"
      trapFocus
    >
      <span className={styles.anchor}>
        <PopoverTrigger disableButtonEnhancement>
          <Tooltip content={label} relationship="label">
            <Button
              ref={bellRef}
              appearance="subtle"
              icon={unreadText ? <AlertOn20Regular /> : <Alert20Regular />}
              aria-label={label}
              data-testid="notification-bell"
            />
          </Tooltip>
        </PopoverTrigger>
        {unreadText && (
          <Badge
            className={styles.badge}
            appearance="filled"
            color="danger"
            size="small"
            shape="rounded"
            aria-hidden="true"
            data-testid="notification-badge"
          >
            {unreadText}
          </Badge>
        )}
      </span>
      <PopoverSurface className={styles.surface} aria-label="Notifications">
        <div className={styles.head}>
          <Text as="h2" size={400} weight="semibold" className={styles.headTitle}>
            Notifications
          </Text>
        </div>
        <div className={styles.body} aria-busy={loading}>
          {body}
        </div>
      </PopoverSurface>
    </Popover>
  );
};

export default NotificationBell;
