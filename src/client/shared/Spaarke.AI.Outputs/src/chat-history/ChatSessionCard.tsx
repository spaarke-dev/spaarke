/**
 * ChatSessionCard
 *
 * A single card in the chat history panel showing:
 *  - Session title (1-line truncation, semibold)
 *  - Last message preview (2-line truncation, muted token)
 *  - Relative timestamp (e.g. "2 hours ago") via Intl.RelativeTimeFormat
 *  - Entity context badge: "{entityType} — {entityName}" (Fluent v9 Badge,
 *    informative appearance), shown only when entityType is present
 *  - Resume button (primary appearance), shown only when onResume provided
 *  - Delete button (subtle appearance), shown only when onDelete provided
 *
 * All colors are from Fluent v9 design tokens only — no hard-coded hex values.
 * Dark mode is automatic via FluentProvider theme switching (ADR-021).
 *
 * NOT PCF-safe — requires React 19.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Card, CardHeader, Text, Badge, Button } from '@fluentui/react-components';
import { DeleteRegular } from '@fluentui/react-icons';
import type { ChatSessionCardProps } from './ChatHistoryPanel.types';

// ---------------------------------------------------------------------------
// Relative time formatting
// ---------------------------------------------------------------------------

/**
 * Formats an ISO 8601 timestamp as a human-readable relative time string.
 * Examples: "just now", "5 minutes ago", "2 hours ago", "3 days ago".
 *
 * Uses Intl.RelativeTimeFormat for locale-aware output.
 */
function formatRelativeTime(isoTimestamp: string): string {
  const now = Date.now();
  const then = new Date(isoTimestamp).getTime();

  if (Number.isNaN(then)) {
    return isoTimestamp;
  }

  const diffMs = then - now; // negative = in the past
  const diffSec = Math.round(diffMs / 1000);
  const diffMin = Math.round(diffMs / (1000 * 60));
  const diffHr = Math.round(diffMs / (1000 * 60 * 60));
  const diffDay = Math.round(diffMs / (1000 * 60 * 60 * 24));
  const diffWeek = Math.round(diffMs / (1000 * 60 * 60 * 24 * 7));
  const diffMonth = Math.round(diffMs / (1000 * 60 * 60 * 24 * 30));

  const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

  if (Math.abs(diffSec) < 60) return rtf.format(diffSec, 'second');
  if (Math.abs(diffMin) < 60) return rtf.format(diffMin, 'minute');
  if (Math.abs(diffHr) < 24) return rtf.format(diffHr, 'hour');
  if (Math.abs(diffDay) < 7) return rtf.format(diffDay, 'day');
  if (Math.abs(diffWeek) < 4) return rtf.format(diffWeek, 'week');
  return rtf.format(diffMonth, 'month');
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  card: {
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
    cursor: 'pointer',
    transition: 'box-shadow 150ms ease',
    ':hover': {
      boxShadow: tokens.shadow8,
    },
  },
  cardActive: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    borderLeftWidth: '3px',
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorBrandBackground,
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    display: 'block',
    color: tokens.colorNeutralForeground1,
  },
  preview: {
    overflow: 'hidden',
    // 2-line clamp via WebkitLineClamp (widely supported in modern browsers)
    display: '-webkit-box',
    WebkitBoxOrient: 'vertical',
    WebkitLineClamp: '2',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  meta: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  timestamp: {
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders a single chat session entry in the history panel.
 *
 * @example
 * ```tsx
 * <ChatSessionCard
 *   session={session}
 *   onResume={(id) => navigate(`/chat/${id}`)}
 *   onDelete={(id) => deleteSession(id)}
 * />
 * ```
 */
export function ChatSessionCard({
  session,
  onResume,
  onDelete,
  isActive = false,
}: ChatSessionCardProps): React.ReactElement {
  const styles = useStyles();

  const entityBadgeLabel =
    session.entityType && session.entityName ? `${session.entityType} \u2014 ${session.entityName}` : null;

  const relativeTime = formatRelativeTime(session.updatedAt);

  const handleResumeClick = (e: React.MouseEvent<HTMLButtonElement>): void => {
    e.stopPropagation();
    onResume?.(session.id);
  };

  const handleDeleteClick = (e: React.MouseEvent<HTMLButtonElement>): void => {
    e.stopPropagation();
    onDelete?.(session.id);
  };

  return (
    <Card className={mergeClasses(styles.card, isActive && styles.cardActive)} aria-selected={isActive}>
      <CardHeader
        header={
          <div className={styles.content}>
            {/* Title — 1-line truncation */}
            <Text className={styles.title} title={session.title}>
              {session.title}
            </Text>

            {/* Last message preview — 2-line truncation */}
            {session.lastMessagePreview !== undefined && (
              <Text className={styles.preview}>{session.lastMessagePreview}</Text>
            )}

            {/* Metadata row: entity badge + relative timestamp */}
            <div className={styles.meta}>
              {entityBadgeLabel !== null && (
                <Badge appearance="filled" color="informative" size="small">
                  {entityBadgeLabel}
                </Badge>
              )}
              <Text className={styles.timestamp}>{relativeTime}</Text>
            </div>

            {/* Action buttons — only rendered when callbacks are provided */}
            {(onResume !== undefined || onDelete !== undefined) && (
              <div className={styles.actions}>
                {onResume !== undefined && (
                  <Button
                    appearance="primary"
                    size="small"
                    onClick={handleResumeClick}
                    aria-label={`Resume session: ${session.title}`}
                  >
                    Resume
                  </Button>
                )}
                {onDelete !== undefined && (
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<DeleteRegular />}
                    onClick={handleDeleteClick}
                    aria-label={`Delete session: ${session.title}`}
                  >
                    Delete
                  </Button>
                )}
              </div>
            )}
          </div>
        }
      />
    </Card>
  );
}

export default ChatSessionCard;
