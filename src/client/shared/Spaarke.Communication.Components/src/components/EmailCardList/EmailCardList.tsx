/**
 * EmailCardList.tsx
 *
 * The left-pane FLAT CARD list of Email-type `sprk_communication` rows
 * (email-communication-solution-r5 task 030, FR-03/FR-19; design Lens 2/4 —
 * BUILD verdict, an Outlook-style mail column, no conversation grouping).
 * Each row is a card: from / subject / single-line preview / date / unread
 * visual (bold text + a brand dot), mirroring the row-list pattern in
 * `ConversationWorkspace/subcomponents/ThreadList.tsx` but WITHOUT thread
 * grouping/pin — this is a flat mail list, not a thread list.
 *
 * Presentational-only (ADR-012): all data arrives via props; the host (task
 * 031 view wiring → task 032 reading-pane shell) owns the Dataverse/BFF fetch,
 * the Email filter at query time, and selection state. This component ONLY
 * emits `onSelect(id)`.
 *
 * Non-Email exclusion invariant (FR-03): `items` is NOT assumed to be
 * pre-filtered — any row whose `communicationType !== EMAIL_COMMUNICATION_TYPE`
 * is skipped here too, so a Teams/SMS/ACS row can never render as a card even
 * if an unfiltered array reaches this component (defense-in-depth alongside
 * the host's FetchXML filter).
 *
 * React-version note (ADR-022/NFR-05): uses only `React.FC` + standard event
 * types — no React-18/19-only runtime API and no `as React.ComponentType`
 * cast. This is a Layer-2 (React 19 code-page) view; it is not shared across
 * the PCF boundary.
 *
 * Fluent v9 semantic tokens only (ADR-021) — no hardcoded colors — so the
 * card list themes correctly via the host `FluentProvider` in both light and
 * dark mode.
 */
import * as React from 'react';
import { Skeleton, SkeletonItem, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { EMAIL_COMMUNICATION_TYPE, type EmailCardItem, type EmailCardListProps } from './EmailCardList.types';

const DEFAULT_SKELETON_COUNT = 6;

/** Formats a card's date for display. Accepts an ISO string or a `Date`; falls back to the raw string on parse failure. */
function formatCardDate(date: string | Date): string {
  const parsed = date instanceof Date ? date : new Date(date);
  if (Number.isNaN(parsed.getTime())) {
    return typeof date === 'string' ? date : '';
  }
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' }).format(parsed);
}

const useStyles = makeStyles({
  list: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    width: '100%',
    minWidth: 0,
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: 'pointer',
    outlineStyle: 'none',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  cardSelected: {
    backgroundColor: tokens.colorBrandBackground2,
  },
  cardFocused: {
    boxShadow: `inset 0 0 0 2px ${tokens.colorStrokeFocus2}`,
  },
  cardHeaderRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  from: {
    flex: '1 1 auto',
    minWidth: 0,
    color: tokens.colorNeutralForeground1,
    // Sender address is always bold (owner UAT) — the primary identifier per card.
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  date: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  subject: {
    // Subject in the brand (blue) color (owner UAT) so it reads as the actionable line.
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightRegular,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  subjectUnread: {
    fontWeight: tokens.fontWeightSemibold,
  },
  // Association review status dot, left of the sender (🔴 requires review ·
  // 🟡 needs confirmation · 🟢 confirmed). Semantic tokens → dark-mode correct.
  reviewDot: { flexShrink: 0, width: '8px', height: '8px', borderRadius: tokens.borderRadiusCircular },
  reviewDotRed: { backgroundColor: tokens.colorPaletteRedForeground1 },
  reviewDotYellow: { backgroundColor: tokens.colorPaletteMarigoldForeground1 },
  reviewDotGreen: { backgroundColor: tokens.colorPaletteGreenForeground1 },
  preview: {
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  // Compact unread signal (mirrors ThreadList's brand dot) — a semantic token
  // so it adapts in dark mode (ADR-021).
  unreadDot: {
    flexShrink: 0,
    width: '8px',
    height: '8px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground,
  },
  skeletonCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  centeredState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flexGrow: 1,
    height: '100%',
    padding: tokens.spacingVerticalXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
});

export const EmailCardList: React.FC<EmailCardListProps> = ({
  items,
  selectedId,
  isLoading = false,
  skeletonCount = DEFAULT_SKELETON_COUNT,
  onSelect,
}) => {
  const styles = useStyles();
  const [focusedId, setFocusedId] = React.useState<string | undefined>(undefined);

  // FR-03 non-Email exclusion invariant: skip any row that is not Email, even
  // if the host passes an unfiltered array. Never trust the caller alone.
  const emailItems = React.useMemo<EmailCardItem[]>(
    () => items.filter(item => item.communicationType === EMAIL_COMMUNICATION_TYPE),
    [items]
  );

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>, id: string) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onSelect(id);
      }
    },
    [onSelect]
  );

  if (isLoading) {
    return (
      <div className={styles.list} role="list" aria-label="Loading emails" aria-busy="true">
        {Array.from({ length: Math.max(0, skeletonCount) }).map((_, index) => (
          <div key={`email-skeleton-${index}`} className={styles.skeletonCard}>
            <Skeleton aria-label="Loading email">
              <SkeletonItem shape="rectangle" style={{ width: '40%', height: '12px' }} />
              <SkeletonItem shape="rectangle" style={{ width: '70%', height: '12px', marginTop: tokens.spacingVerticalXS }} />
              <SkeletonItem shape="rectangle" style={{ width: '90%', height: '10px', marginTop: tokens.spacingVerticalXS }} />
            </Skeleton>
          </div>
        ))}
      </div>
    );
  }

  if (emailItems.length === 0) {
    return (
      <div className={styles.centeredState}>
        <Text>No emails in this view</Text>
      </div>
    );
  }

  return (
    <div className={styles.list} role="list" aria-label="Emails">
      {emailItems.map(item => {
        const isSelected = item.id === selectedId;
        const isFocused = item.id === focusedId;
        const unread = item.isUnread === true;
        return (
          <div
            key={item.id}
            role="listitem"
            aria-selected={isSelected}
            tabIndex={0}
            className={mergeClasses(
              styles.card,
              isSelected ? styles.cardSelected : undefined,
              isFocused ? styles.cardFocused : undefined
            )}
            onClick={() => onSelect(item.id)}
            onKeyDown={e => handleKeyDown(e, item.id)}
            onFocus={() => setFocusedId(item.id)}
            onBlur={() => setFocusedId(prev => (prev === item.id ? undefined : prev))}
          >
            <div className={styles.cardHeaderRow}>
              {item.reviewTone ? (
                <span
                  className={mergeClasses(
                    styles.reviewDot,
                    item.reviewTone === 'green'
                      ? styles.reviewDotGreen
                      : item.reviewTone === 'yellow'
                        ? styles.reviewDotYellow
                        : styles.reviewDotRed
                  )}
                  role="img"
                  aria-label={
                    item.reviewTone === 'green'
                      ? 'Confirmed'
                      : item.reviewTone === 'yellow'
                        ? 'Needs confirmation'
                        : 'Requires review'
                  }
                  title={
                    item.reviewTone === 'green'
                      ? 'Confirmed'
                      : item.reviewTone === 'yellow'
                        ? 'Needs confirmation'
                        : 'Requires review'
                  }
                />
              ) : unread ? (
                <span className={styles.unreadDot} role="img" aria-label="Unread" title="Unread" />
              ) : null}
              <Text className={styles.from} title={item.from}>
                {item.from}
              </Text>
              <Text className={styles.date}>{formatCardDate(item.date)}</Text>
            </div>
            <Text className={mergeClasses(styles.subject, unread ? styles.subjectUnread : undefined)} title={item.subject}>
              {item.subject}
            </Text>
            <Text className={styles.preview} title={item.preview}>
              {item.preview}
            </Text>
          </div>
        );
      })}
    </div>
  );
};

EmailCardList.displayName = 'EmailCardList';
