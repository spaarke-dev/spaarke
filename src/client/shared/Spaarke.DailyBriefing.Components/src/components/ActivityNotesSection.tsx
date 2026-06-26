/**
 * ActivityNotesSection -- renders all channel sections with headings and
 * AI-narrated bullets for the Daily Briefing redesign.
 *
 * Each channel group gets a ChannelHeading followed by NarrativeBullet items.
 * Channel icons are resolved via CHANNEL_REGISTRY + getChannelIcon.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A). Source of truth; the original-location file at
 * `src/solutions/DailyBriefing/src/components/ActivityNotesSection.tsx` is now
 * a re-export shim pending full cleanup in R2 task 017.
 *
 * INTERIM IMPORT NOTE (R2 task 011 only): `types/notifications` and
 * `CHANNEL_REGISTRY` still live in the standalone DailyBriefing solution.
 * They will be hoisted in R2 task 014/015. Until then, this component reaches
 * back across the package boundary via a relative path — intentional,
 * temporary debt documented in the task POML step 3.
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Skeleton, SkeletonItem } from '@fluentui/react-components';
import { CHANNEL_REGISTRY } from '../types/notifications';
import type { ChannelFetchResult, NotificationCategory, NotificationItem } from '../types/notifications';
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
  /** Raw channel fetch results for icon resolution. */
  channels: ChannelFetchResult[];
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
   * R3 task 031 / FR-4 — per-item "Mark as read" callback.
   * Wired from `DailyBriefingApp.handleCheck` which composes the `markChecked`
   * hook handler with optimistic-update + toast callbacks.
   *
   * Optional for backward compatibility with consumers that have not yet
   * adopted the R3 action layer; absent value hides the button per
   * NarrativeBullet's defensive default.
   */
  onCheck?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-5 — per-item "Remove from briefing" callback.
   * Wired from `DailyBriefingApp.handleRemove` which composes `markRemoved`.
   */
  onRemove?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-6 — per-item "Keep +7 days" callback.
   * Wired from `DailyBriefingApp.handleKeep` which composes `extendTtl`.
   * `currentTtlSeconds` is supplied by the bullet (currently 0 until the
   * service layer surfaces `ttlinseconds` on `NotificationItem`).
   */
  onKeep?: (itemId: string, currentTtlSeconds: number) => void;
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
  channels,
  onAddToTodo,
  onDismiss,
  isTodoCreated,
  isTodoPending,
  getTodoError,
  isLoading,
  onCheck,
  onRemove,
  onKeep,
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

  // No narratives
  if (filteredNarratives.length === 0) {
    return null;
  }

  return (
    <div>
      <Text as="h2" size={500} weight="semibold" className={styles.heading}>
        Activity Notes
      </Text>
      {filteredNarratives.map(channel => {
        // R2.1 hotfix (2026-06-19) — Fix B: resolve the underlying NotificationItem
        // array for this channel so each bullet can expand into per-item sub-rows
        // (FR-11/12/13/14). Wave 9 (tasks 020-023) built the SubRow* slot components
        // and added optional `items` + `onAddToTodoItem` + `onDismissItem` props to
        // NarrativeBullet, but the wiring from ActivityNotesSection was deferred.
        // This block closes that gap so aggregated bullets ("Multiple X") expand
        // into an indented per-item sub-list with link + To-Do + Dismiss per row.
        const matchingChannel = channels.find(
          ch => ch.status === 'success' && ch.group.meta.category === channel.category
        );
        const channelItems: NotificationItem[] =
          matchingChannel?.status === 'success' ? matchingChannel.group.items : [];

        return (
          <div key={channel.category} className={styles.channelSection}>
            <ChannelHeading
              icon={resolveChannelIcon(channel.category)}
              label={resolveChannelLabel(channel.category)}
              itemCount={channel.bullets.length}
            />
            {channel.bullets.map((bullet, idx) => {
              // Determine todo/dismiss state from the first item ID
              const firstItemId = bullet.itemIds[0] ?? '';

              // Resolve the underlying items for this bullet, preserving the
              // order of `itemIds`. Missing items are filtered out (defensive
              // — should not normally happen if the BFF and frontend agree).
              const bulletItems: NotificationItem[] = bullet.itemIds
                .map(id => channelItems.find(item => item.id === id))
                .filter((item): item is NotificationItem => Boolean(item));

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
                  // FR-11/12/13/14 wiring: when itemIds.length > 1 AND items is
                  // supplied, NarrativeBullet renders an indented per-item sub-list.
                  // Per-item callbacks reuse the aggregated handlers with a
                  // single-item array (DailyBriefingApp's handleAddToTodo /
                  // handleDismiss already accept arbitrary itemId arrays).
                  items={bulletItems}
                  onAddToTodoItem={itemId => onAddToTodo([itemId])}
                  onDismissItem={itemId => onDismiss([itemId])}
                  // R3 task 031 — per-item action wiring (FR-4 / FR-5 / FR-6).
                  // Passed straight through from DailyBriefingApp's hook-
                  // composed handlers (handleCheck / handleRemove / handleKeep).
                  onCheck={onCheck}
                  onRemove={onRemove}
                  onKeep={onKeep}
                />
              );
            })}
          </div>
        );
      })}
    </div>
  );
};
