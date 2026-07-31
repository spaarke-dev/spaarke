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
import {
  Button,
  SearchBox,
  Skeleton,
  SkeletonItem,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
  type InputOnChangeData,
  type SearchBoxChangeEvent,
} from '@fluentui/react-components';
import { Search20Regular } from '@fluentui/react-icons';
import {
  EMAIL_COMMUNICATION_TYPE,
  type EmailCardItem,
  type EmailDateBucket,
  type EmailDateBucketKey,
  type EmailCardListProps,
} from './EmailCardList.types';

const DEFAULT_SKELETON_COUNT = 6;

/** Formats a card's date for display. Accepts an ISO string or a `Date`; falls back to the raw string on parse failure. */
function formatCardDate(date: string | Date): string {
  const parsed = date instanceof Date ? date : new Date(date);
  if (Number.isNaN(parsed.getTime())) {
    return typeof date === 'string' ? date : '';
  }
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' }).format(parsed);
}

const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * Ordered bucket definitions, newest → oldest. The render order (and the order
 * `bucketEmailsByDate` returns non-empty buckets) is driven by this table.
 */
const BUCKET_ORDER: ReadonlyArray<{ key: EmailDateBucketKey; label: string }> = [
  { key: 'today', label: 'Today' },
  { key: 'yesterday', label: 'Yesterday' },
  { key: 'thisWeek', label: 'This Week' },
  { key: 'thisMonth', label: 'This Month' },
  { key: 'older', label: 'Older' },
];

/** Local-calendar midnight (ms epoch) for the given date — bucketing is by calendar day, not by 24h offset. */
function startOfDay(d: Date): number {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
}

/**
 * Classify one card's received date into a date bucket, relative to `now`.
 * Boundaries (all by LOCAL calendar day):
 *  - `today`     — same calendar day as `now` (defensively also catches any
 *                  future-dated row so it never disappears below the fold).
 *  - `yesterday` — the calendar day immediately before today.
 *  - `thisWeek`  — within the last 7 calendar days (incl. today) but before
 *                  yesterday, i.e. 2–6 days ago.
 *  - `thisMonth` — older than `thisWeek` yet in the same calendar month + year
 *                  as `now` (same or prior weeks this month).
 *  - `older`     — everything else, plus any unparseable date (robustness).
 */
function classifyBucket(date: string | Date, now: Date): EmailDateBucketKey {
  const parsed = date instanceof Date ? date : new Date(date);
  if (Number.isNaN(parsed.getTime())) {
    return 'older';
  }
  const todayStart = startOfDay(now);
  const yesterdayStart = todayStart - DAY_MS;
  const weekStart = todayStart - 6 * DAY_MS; // last 7 calendar days, inclusive of today
  const dayStart = startOfDay(parsed);

  if (dayStart >= todayStart) {
    return 'today';
  }
  if (dayStart >= yesterdayStart) {
    return 'yesterday';
  }
  if (dayStart >= weekStart) {
    return 'thisWeek';
  }
  if (parsed.getFullYear() === now.getFullYear() && parsed.getMonth() === now.getMonth()) {
    return 'thisMonth';
  }
  return 'older';
}

/**
 * Pure, clock-injectable bucketing of email cards into Outlook-style date
 * groups. Takes an explicit `now` so it is deterministic + unit-testable
 * without mocking the clock (the component passes `new Date()`).
 *
 * Contract:
 *  - Returns only NON-EMPTY buckets, in newest→oldest order (`BUCKET_ORDER`).
 *  - Preserves the incoming order of `items` within each bucket (no re-sort).
 */
export function bucketEmailsByDate(items: ReadonlyArray<EmailCardItem>, now: Date): EmailDateBucket[] {
  const grouped = new Map<EmailDateBucketKey, EmailCardItem[]>();
  for (const item of items) {
    const key = classifyBucket(item.date, now);
    const existing = grouped.get(key);
    if (existing) {
      existing.push(item);
    } else {
      grouped.set(key, [item]);
    }
  }
  return BUCKET_ORDER.filter(b => grouped.has(b.key)).map(b => ({
    key: b.key,
    label: b.label,
    items: grouped.get(b.key) as EmailCardItem[],
  }));
}

const useStyles = makeStyles({
  // Root wrapper: a non-scrolling toolbar pinned above a scrollable card list.
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    width: '100%',
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Slim list toolbar. owner UAT 2026-07-30 R2 item 4 — collapsed by default to a
  // right-aligned search ICON; clicking it reveals the full-width search field.
  // `justifyContent: flex-end` keeps the collapsed icon on the right; when the
  // field is open it grows (`flex: 1`) and fills the row.
  toolbar: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  searchBox: {
    flex: '1 1 auto',
    minWidth: 0,
  },
  // Collapsed-state trigger — a right-aligned magnifier that expands the field.
  searchToggle: {
    flexShrink: 0,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minHeight: 0,
    width: '100%',
    minWidth: 0,
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Sticky, unobtrusive date-bucket divider (Today / Yesterday / …). Muted
  // foreground + a hairline separator, themed via semantic tokens (ADR-021).
  divider: {
    position: 'sticky',
    top: 0,
    zIndex: 1,
    display: 'flex',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  dividerLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    textTransform: 'uppercase',
    letterSpacing: tokens.spacingHorizontalXXS,
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
  // Toolbar search state is intentionally internal (no parent wiring needed).
  const [search, setSearch] = React.useState('');
  // owner UAT 2026-07-30 R2 item 4 — the search field is collapsed to an icon by
  // default and revealed on click; it collapses back on blur only when empty (a
  // non-empty query stays visible so the active filter — and its clear button —
  // remain reachable).
  const [searchOpen, setSearchOpen] = React.useState(false);
  const searchInputRef = React.useRef<HTMLInputElement>(null);
  React.useEffect(() => {
    if (searchOpen) searchInputRef.current?.focus();
  }, [searchOpen]);
  const handleSearchBlur = React.useCallback(() => {
    if (search.trim() === '') setSearchOpen(false);
  }, [search]);

  // FR-03 non-Email exclusion invariant: skip any row that is not Email, even
  // if the host passes an unfiltered array. Never trust the caller alone.
  const emailItems = React.useMemo<EmailCardItem[]>(
    () => items.filter(item => item.communicationType === EMAIL_COMMUNICATION_TYPE),
    [items]
  );

  // Client-side search: case-insensitive substring over sender + subject.
  // Applied BEFORE bucketing so dividers reflect only matching results.
  const filteredItems = React.useMemo<EmailCardItem[]>(() => {
    const q = search.trim().toLowerCase();
    if (q === '') {
      return emailItems;
    }
    return emailItems.filter(item => item.from.toLowerCase().includes(q) || item.subject.toLowerCase().includes(q));
  }, [emailItems, search]);

  // Bucket the (filtered) cards into Outlook-style date groups. `now` comes from
  // the wall clock in production; the pure helper takes it explicitly so it can
  // be unit-tested deterministically. Empty buckets are omitted by the helper.
  const buckets = React.useMemo<EmailDateBucket[]>(
    () => bucketEmailsByDate(filteredItems, new Date()),
    [filteredItems]
  );

  const handleSearchChange = React.useCallback((_e: SearchBoxChangeEvent, data: InputOnChangeData) => {
    setSearch(data.value);
  }, []);

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
              <SkeletonItem
                shape="rectangle"
                style={{ width: '70%', height: '12px', marginTop: tokens.spacingVerticalXS }}
              />
              <SkeletonItem
                shape="rectangle"
                style={{ width: '90%', height: '10px', marginTop: tokens.spacingVerticalXS }}
              />
            </Skeleton>
          </div>
        ))}
      </div>
    );
  }

  // No Email rows at all (before search) → the plain empty state, no toolbar.
  if (emailItems.length === 0) {
    return (
      <div className={styles.centeredState}>
        <Text>No emails in this view</Text>
      </div>
    );
  }

  const renderCard = (item: EmailCardItem) => {
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
  };

  return (
    <div className={styles.root}>
      {/*
       * List toolbar (owner UAT 2026-07-30 R2 item 4). Collapsed to a right-aligned
       * search ICON by default; clicking it reveals the field (auto-focused) and
       * blurring an empty field collapses it back. Filter behavior is unchanged —
       * sender OR subject, case-insensitive substring.
       */}
      <div className={styles.toolbar}>
        {searchOpen ? (
          <SearchBox
            ref={searchInputRef}
            className={styles.searchBox}
            value={search}
            onChange={handleSearchChange}
            onBlur={handleSearchBlur}
            placeholder="Search mail"
            aria-label="Search mail"
          />
        ) : (
          <Button
            className={styles.searchToggle}
            appearance="subtle"
            icon={<Search20Regular />}
            aria-label="Search mail"
            title="Search mail"
            onClick={() => setSearchOpen(true)}
          />
        )}
      </div>
      {buckets.length === 0 ? (
        <div className={styles.centeredState}>
          <Text>No emails match your search</Text>
        </div>
      ) : (
        <div className={styles.list} role="list" aria-label="Emails">
          {buckets.map(bucket => (
            <React.Fragment key={bucket.key}>
              <div className={styles.divider} role="presentation">
                <Text className={styles.dividerLabel}>{bucket.label}</Text>
              </div>
              {bucket.items.map(renderCard)}
            </React.Fragment>
          ))}
        </div>
      )}
    </div>
  );
};

EmailCardList.displayName = 'EmailCardList';
