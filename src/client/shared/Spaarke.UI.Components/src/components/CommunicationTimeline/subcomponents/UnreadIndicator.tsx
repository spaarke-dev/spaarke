/**
 * UnreadIndicator.tsx
 *
 * Surfaces the BFF unread-count (task 050) as a "new messages" affordance
 * (task 060, FR-10). Clicking "Mark as read" advances the last-seen
 * watermark (`ADVANCE_LAST_SEEN`), which the next poll cycle uses as
 * `?since=` for `getUnreadCount` — see `CommunicationTimeline.tsx`.
 */
import * as React from 'react';
import { Button, Text, makeStyles, tokens } from '@fluentui/react-components';

export interface IUnreadIndicatorProps {
  unreadCount: number;
  onMarkAsRead: () => void;
}

const useStyles = makeStyles({
  bar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorBrandBackground2,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  label: {
    color: tokens.colorBrandForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
});

export const UnreadIndicator: React.FC<IUnreadIndicatorProps> = ({ unreadCount, onMarkAsRead }) => {
  const styles = useStyles();

  if (unreadCount <= 0) return null;

  return (
    <div className={styles.bar} role="status" aria-live="polite">
      <Text className={styles.label}>
        {unreadCount} new message{unreadCount === 1 ? '' : 's'}
      </Text>
      <Button appearance="transparent" size="small" onClick={onMarkAsRead}>
        Mark as read
      </Button>
    </div>
  );
};

UnreadIndicator.displayName = 'UnreadIndicator';
