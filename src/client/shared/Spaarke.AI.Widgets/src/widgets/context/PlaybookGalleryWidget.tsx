/**
 * @spaarke/ai-widgets — PlaybookGalleryWidget
 *
 * Context pane widget rendered during the Welcome / playbook-selection stage
 * of the SpaarkeAi shell. Displays available AI playbooks as Fluent v9 Cards
 * so the user can browse and select one before starting a conversation.
 *
 * On card selection the widget dispatches a `playbook_change` event to the
 * 'conversation' PaneEventBus channel. ConversationPane and AiSessionProvider
 * listen for this event to initialise the session with the chosen playbook.
 *
 * Data contract:
 *   PlaybookGalleryData — { playbooks: PlaybookSummary[] }
 *   PlaybookSummary     — { id, name, description, capabilityBadges, iconName? }
 *
 * Design constraints (ADR-021):
 *   - All colours via Fluent v9 tokens — zero hard-coded hex values.
 *   - Dark-mode compatible by construction (tokens adapt automatically).
 *   - Skeleton loading state using Fluent v9 Skeleton components.
 *   - EmptyState for an empty playbook list (never a blank pane).
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-086
 */

import React, { useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Card,
  CardHeader,
  Skeleton,
  SkeletonItem,
  mergeClasses,
} from '@fluentui/react-components';
import { AppsRegular, BookOpenRegular } from '@fluentui/react-icons';
import type { ContextWidgetProps } from '../../types/widget-types';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// Public data types
// ---------------------------------------------------------------------------

/**
 * A single playbook entry as returned by the BFF playbook catalog endpoint.
 */
export interface PlaybookSummary {
  /** Stable playbook identifier — matches the catalog key in JPS. */
  id: string;
  /** Human-readable playbook name displayed as the card title. */
  name: string;
  /** Short description shown as the card body text. */
  description: string;
  /**
   * Capability badge labels rendered as small Fluent v9 Badges beneath the
   * description. Examples: ["Document Analysis", "Compare", "Redline"].
   */
  capabilityBadges: string[];
  /**
   * Optional Fluent UI icon component name. When absent the widget renders
   * a generic BookOpenRegular icon.
   */
  iconName?: string;
  /**
   * Default workspace widgets to pre-load when this playbook is selected.
   * An empty array means the workspace keeps its current tabs on selection.
   */
  defaultWidgets?: Array<{ widgetType: string; widgetData?: unknown; displayName?: string }>;
  /**
   * When true, selecting this playbook clears all existing workspace tabs
   * before seeding defaultWidgets (guardrail / exclusive-focus playbooks).
   * Defaults to false.
   */
  isExclusive?: boolean;
}

/**
 * Data payload for PlaybookGalleryWidget.
 * Delivered via the AI streaming response or an initial BFF fetch.
 */
export interface PlaybookGalleryData {
  playbooks: PlaybookSummary[];
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    overflowY: 'auto',
    boxSizing: 'border-box',
  },

  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalS,
  },

  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
  },

  headerSubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // Responsive grid: 2 columns when there is room, collapses to 1 column via
  // a min-width constraint. Works without media queries since the context pane
  // width varies per layout and is not a fixed breakpoint.
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },

  // Playbook card — base state
  playbookCard: {
    cursor: 'pointer',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    transition: 'box-shadow 0.12s ease',
    ':hover': {
      borderTopColor: tokens.colorBrandStroke1,
      borderRightColor: tokens.colorBrandStroke1,
      borderBottomColor: tokens.colorBrandStroke1,
      borderLeftColor: tokens.colorBrandStroke1,
      boxShadow: tokens.shadow4,
    },
    ':focus-within': {
      borderTopColor: tokens.colorBrandStroke1,
      borderRightColor: tokens.colorBrandStroke1,
      borderBottomColor: tokens.colorBrandStroke1,
      borderLeftColor: tokens.colorBrandStroke1,
      outlineStyle: 'none',
    },
  },

  // Playbook card — selected state (highlighted with brand border)
  playbookCardSelected: {
    borderTopColor: tokens.colorBrandStroke1,
    borderRightColor: tokens.colorBrandStroke1,
    borderBottomColor: tokens.colorBrandStroke1,
    borderLeftColor: tokens.colorBrandStroke1,
    borderTopWidth: tokens.strokeWidthThick,
    borderRightWidth: tokens.strokeWidthThick,
    borderBottomWidth: tokens.strokeWidthThick,
    borderLeftWidth: tokens.strokeWidthThick,
    boxShadow: tokens.shadow8,
    backgroundColor: tokens.colorBrandBackground2,
  },

  cardIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase500,
    flexShrink: 0,
  },

  cardIconSelected: {
    color: tokens.colorBrandForeground2,
  },

  cardTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },

  cardDescription: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    marginTop: tokens.spacingVerticalXS,
  },

  badgeContainer: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },

  badge: {
    fontSize: tokens.fontSizeBase100,
  },

  // Skeleton grid (mirrors playbookCard layout)
  skeletonGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },

  skeletonCard: {
    padding: tokens.spacingHorizontalM,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },

  // Empty state
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXL,
    flex: 1,
    textAlign: 'center',
  },

  emptyStateIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: '48px',
  },

  emptyStateTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground2,
  },

  emptyStateBody: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    maxWidth: '240px',
  },

  // Error state
  errorText: {
    padding: tokens.spacingHorizontalM,
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

/** Skeleton placeholder rendered while playbooks are loading. */
const PlaybookGallerySkeletons: React.FC<{ styles: ReturnType<typeof useStyles> }> = ({
  styles,
}) => (
  <div className={styles.skeletonGrid} aria-busy="true" aria-label="Loading playbooks">
    {Array.from({ length: 4 }, (_, i) => (
      <div key={i} className={styles.skeletonCard}>
        <Skeleton>
          <SkeletonItem shape="circle" size={32} />
          <SkeletonItem size={12} style={{ marginTop: tokens.spacingVerticalS, width: '70%' }} />
          <SkeletonItem size={8} style={{ width: '100%' }} />
          <SkeletonItem size={8} style={{ width: '85%' }} />
          <div style={{ display: 'flex', gap: tokens.spacingHorizontalXS, marginTop: tokens.spacingVerticalXS }}>
            <SkeletonItem size={8} style={{ width: '40%' }} />
            <SkeletonItem size={8} style={{ width: '30%' }} />
          </div>
        </Skeleton>
      </div>
    ))}
  </div>
);

/** Empty state shown when the playbook list is empty (never a blank pane). */
const PlaybookGalleryEmptyState: React.FC<{ styles: ReturnType<typeof useStyles> }> = ({
  styles,
}) => (
  <div className={styles.emptyState} role="status" aria-label="No playbooks available">
    <AppsRegular className={styles.emptyStateIcon} />
    <Text className={styles.emptyStateTitle}>No playbooks available</Text>
    <Text className={styles.emptyStateBody}>
      No AI playbooks have been configured for your workspace. Contact your administrator to enable
      playbooks.
    </Text>
  </div>
);

// ---------------------------------------------------------------------------
// PlaybookCard
// ---------------------------------------------------------------------------

interface PlaybookCardProps {
  playbook: PlaybookSummary;
  isSelected: boolean;
  onSelect: (id: string, name: string) => void;
  styles: ReturnType<typeof useStyles>;
}

const PlaybookCard: React.FC<PlaybookCardProps> = ({ playbook, isSelected, onSelect, styles }) => {
  const handleClick = () => {
    onSelect(playbook.id, playbook.name);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onSelect(playbook.id, playbook.name);
    }
  };

  return (
    <Card
      className={mergeClasses(
        styles.playbookCard,
        isSelected && styles.playbookCardSelected
      )}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
      aria-pressed={isSelected}
      aria-label={`${playbook.name}${isSelected ? ', selected' : ''}`}
    >
      <CardHeader
        image={
          <BookOpenRegular
            className={mergeClasses(
              styles.cardIcon,
              isSelected && styles.cardIconSelected
            )}
          />
        }
        header={
          <Text className={styles.cardTitle}>{playbook.name}</Text>
        }
      />

      <Text className={styles.cardDescription}>{playbook.description}</Text>

      {playbook.capabilityBadges.length > 0 && (
        <div className={styles.badgeContainer}>
          {playbook.capabilityBadges.map((badge) => (
            <Badge
              key={badge}
              className={styles.badge}
              appearance={isSelected ? 'filled' : 'tint'}
              color={isSelected ? 'brand' : 'neutral'}
              size="small"
            >
              {badge}
            </Badge>
          ))}
        </div>
      )}
    </Card>
  );
};

// ---------------------------------------------------------------------------
// PlaybookGalleryWidget
// ---------------------------------------------------------------------------

/**
 * PlaybookGalleryWidget — context pane widget for the Welcome / playbook-gallery stage.
 *
 * Renders available playbooks as Fluent v9 Cards. On selection, dispatches
 * `playbook_change` to the 'conversation' PaneEventBus channel.
 *
 * Props satisfy ContextWidgetProps<PlaybookGalleryData> so this component is
 * directly storable in ContextWidgetRegistry as a ContextWidgetComponent.
 */
const PlaybookGalleryWidget: React.FC<ContextWidgetProps<PlaybookGalleryData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // Track the currently selected playbook id locally.
  // The authoritative selection state lives in AiSessionProvider (via the
  // playbook_change event), but we need local state for immediate visual feedback.
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const handlePlaybookSelect = (id: string, name: string) => {
    setSelectedId(id);
    // Find the full playbook summary to include defaultWidgets and isExclusive.
    const playbook = (data?.playbooks ?? []).find((p) => p.id === id);
    dispatch('conversation', {
      type: 'playbook-selected',
      playbookId: id,
      playbookName: name,
      defaultWidgets: playbook?.defaultWidgets ?? [],
      isExclusive: playbook?.isExclusive ?? false,
    });
  };

  const playbooks = data?.playbooks ?? [];

  return (
    <div className={mergeClasses(styles.root, className)} role="region" aria-label="Playbook gallery">
      {/* Header */}
      <div className={styles.header}>
        <Text className={styles.headerTitle}>Choose a Playbook</Text>
        <Text className={styles.headerSubtitle}>
          Select an AI playbook to guide your conversation.
        </Text>
      </div>

      {/* Error state */}
      {error && (
        <Text className={styles.errorText} role="alert">
          {error}
        </Text>
      )}

      {/* Loading state */}
      {isLoading && !error && (
        <PlaybookGallerySkeletons styles={styles} />
      )}

      {/* Empty state */}
      {!isLoading && !error && playbooks.length === 0 && (
        <PlaybookGalleryEmptyState styles={styles} />
      )}

      {/* Playbook card grid */}
      {!isLoading && !error && playbooks.length > 0 && (
        <div className={styles.grid} role="list" aria-label="Available playbooks">
          {playbooks.map((playbook) => (
            <PlaybookCard
              key={playbook.id}
              playbook={playbook}
              isSelected={selectedId === playbook.id}
              onSelect={handlePlaybookSelect}
              styles={styles}
            />
          ))}
        </div>
      )}
    </div>
  );
};

export default PlaybookGalleryWidget;
