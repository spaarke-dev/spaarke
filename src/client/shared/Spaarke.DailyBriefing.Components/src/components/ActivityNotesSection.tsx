/**
 * ActivityNotesSection -- renders all channel sections with headings and
 * AI-narrated bullets for the Daily Briefing.
 *
 * Each channel group gets a ChannelHeading followed by NarrativeBullet items.
 * Channel icons are resolved via CHANNEL_REGISTRY + getChannelIcon.
 *
 * R7 Wave 12 widget cutover (2026-06-30):
 *   Refactored to consume ONLY `channelNarratives` from the /render response.
 *   The legacy `channels: ChannelFetchResult[]` prop (appnotification-derived)
 *   is REMOVED. The FR-16 raw-notification-card fallback path that depended
 *   on it is REMOVED — when /render returns 0 narratives the parent component
 *   shows EmptyState directly. Per-bullet sub-list expansion (FR-11/12/13/14)
 *   is REMOVED — /render bullets carry their own primaryEntity* link so
 *   sub-rows are no longer needed.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Skeleton, SkeletonItem } from '@fluentui/react-components';
import { CHANNEL_REGISTRY } from '../types/notifications';
import type { NotificationCategory } from '../types/notifications';
import { getChannelIcon } from './channelIcons';
import { ChannelHeading } from './ChannelHeading';
import { NarrativeBullet } from './NarrativeBullet';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  heading: {
    marginTop: '0',
    marginBottom: tokens.spacingVerticalXXL,
  },
  channelSection: {
    marginBottom: tokens.spacingVerticalXXL,
  },
  skeletonSection: {
    marginBottom: tokens.spacingVerticalXXL,
  },
  skeletonHeading: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingBottom: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalM,
  },
  skeletonBullet: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalL,
  },
  skeletonBulletLines: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** Shape of a single narrative bullet within a channel. */
interface ChannelNarrativeBullet {
  narrative: string;
  itemIds: string[];
  primaryEntityType: string;
  primaryEntityId: string;
  primaryEntityName: string;
}

/** Shape of a channel narrative group. */
interface ChannelNarrative {
  category: string;
  bullets: ChannelNarrativeBullet[];
}

export interface ActivityNotesSectionProps {
  /** Channel narratives with AI-generated bullets grouped by category. */
  channelNarratives: ChannelNarrative[];
  /** Callback to add notification IDs to To Do. */
  onAddToTodo: (itemIds: string[]) => void;
  /** Callback to dismiss notification IDs. */
  onDismiss: (itemIds: string[]) => void;
  /** Check whether a To Do has been created for a notification ID. */
  isTodoCreated: (itemId: string) => boolean;
  /** Check whether a To Do creation is pending for a notification ID. */
  isTodoPending: (itemId: string) => boolean;
  /** Get any error message for a notification ID's To Do creation. */
  getTodoError: (itemId: string) => string | undefined;
  /** Whether channel narratives are still loading. */
  isLoading: boolean;
  /**
   * Optional per-item "Mark as read" callback (R3 FR-4). Not wired in the
   * post-/render cutover path — kept for forward-compat with future actions.
   */
  onCheck?: (itemId: string) => void;
  /** Optional per-item "Remove from briefing" callback (R3 FR-5). */
  onRemove?: (itemId: string) => void;
  /** Optional per-item "Keep +7 days" callback (R3 FR-6). */
  onKeep?: (itemId: string, currentTtlSeconds: number) => void;
  /**
   * "Open record" callback (FR-18 / FR-19) — handles both the regarding-name
   * link click in NarrativeBullet AND the overflow menu item. Wired from
   * DailyBriefingApp.handleOpenRecord which dispatches
   * Xrm.Navigation.navigateTo + surfaces a Toaster toast on 403.
   */
  onOpenRecord?: (entityType: string, entityId: string) => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Resolve a category string to an icon element via CHANNEL_REGISTRY.
 * Falls back to InfoRegular for unknown categories.
 */
function resolveChannelIcon(category: string): React.ReactElement {
  const meta = CHANNEL_REGISTRY[category as NotificationCategory];
  const IconComponent = getChannelIcon(meta?.iconName ?? 'Info');
  return React.createElement(IconComponent);
}

/**
 * Resolve a category string to its display label.
 */
function resolveChannelLabel(category: string): string {
  const meta = CHANNEL_REGISTRY[category as NotificationCategory];
  return meta?.label ?? category;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ActivityNotesSection: React.FC<ActivityNotesSectionProps> = ({
  channelNarratives,
  onAddToTodo,
  onDismiss,
  isTodoCreated,
  isTodoPending,
  getTodoError,
  isLoading,
  onCheck,
  onRemove,
  onKeep,
  onOpenRecord,
}) => {
  const styles = useStyles();

  // Loading state: 3 skeleton channel sections
  if (isLoading) {
    return (
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.heading}>
          Activity Notes
        </Text>
        <Skeleton aria-label="Loading activity notes">
          {[0, 1, 2].map(sectionIdx => (
            <div key={sectionIdx} className={styles.skeletonSection}>
              <div className={styles.skeletonHeading}>
                <SkeletonItem shape="circle" size={20} />
                <SkeletonItem size={16} style={{ width: '140px' }} />
              </div>
              {[0, 1].map(bulletIdx => (
                <div key={bulletIdx} className={styles.skeletonBullet}>
                  <SkeletonItem shape="circle" size={8} />
                  <div className={styles.skeletonBulletLines}>
                    <SkeletonItem size={16} style={{ width: '100%' }} />
                    <SkeletonItem size={12} style={{ width: '120px' }} />
                  </div>
                </div>
              ))}
            </div>
          ))}
        </Skeleton>
      </div>
    );
  }

  // Filter out "system" category — not actionable for users
  const filteredNarratives = channelNarratives.filter(cn => cn.category !== 'system');

  // No narratives at all → parent component renders EmptyState.
  // Return null here to preserve historical contract (parent owns the
  // "all caught up" footer / EmptyState outside this section).
  if (filteredNarratives.length === 0) {
    return null;
  }

  return (
    <div>
      <Text as="h2" size={500} weight="semibold" className={styles.heading}>
        Activity Notes
      </Text>
      {filteredNarratives.map(channel => (
        <div key={channel.category} className={styles.channelSection}>
          <ChannelHeading
            icon={resolveChannelIcon(channel.category)}
            label={resolveChannelLabel(channel.category)}
            itemCount={channel.bullets.length}
          />
          {channel.bullets.map((bullet, idx) => {
            // Determine todo state from the first item ID. In the /render path
            // itemIds are source-record GUIDs (sprk_event, sprk_document, etc.).
            const firstItemId = bullet.itemIds[0] ?? '';

            return (
              <NarrativeBullet
                key={`${channel.category}-${idx}`}
                narrative={bullet.narrative}
                primaryEntityName={bullet.primaryEntityName}
                primaryEntityType={bullet.primaryEntityType}
                primaryEntityId={bullet.primaryEntityId}
                itemIds={bullet.itemIds}
                onAddToTodo={onAddToTodo}
                onDismiss={onDismiss}
                isTodoCreated={isTodoCreated(firstItemId)}
                isTodoPending={isTodoPending(firstItemId)}
                todoError={getTodoError(firstItemId)}
                // R7 Wave 12: sub-list expansion is dropped (no appnotification
                // items to expand into per-row sub-list). `items` omitted →
                // NarrativeBullet renders just the narrative + primary link.
                // R3 per-item actions optional — not wired post-cutover.
                onCheck={onCheck}
                onRemove={onRemove}
                onKeep={onKeep}
                // R4 task 046+047 — single Open record path for both the
                // regarding-name link (FR-19) AND the overflow menu item
                // (FR-18 / AC-18a #6). DailyBriefingApp owns the 403-fallback
                // toast dispatch via the shared Toaster instance.
                onOpenRecord={onOpenRecord}
              />
            );
          })}
        </div>
      ))}
    </div>
  );
};
