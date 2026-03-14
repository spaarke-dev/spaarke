/**
 * PlaybookCardGrid Component
 *
 * Responsive card grid selector for AI playbooks (Code Page / React 18).
 * Ported from PCF PlaybookSelector with a full grid layout replacing the
 * horizontal scroll strip used in the narrow PCF context.
 *
 * Features:
 * - Responsive CSS Grid: 3 columns (wide) → 2 (medium) → 1 (narrow)
 * - Selected card highlight using Fluent v9 brand tokens
 * - Info icon Popover for full description on hover/click
 * - Loading state (Fluent Spinner) and empty state message
 * - Zero hard-coded colors — all Fluent v9 semantic tokens
 */

import React from 'react';
import {
  Card,
  CardHeader,
  Text,
  Spinner,
  makeStyles,
  tokens,
  mergeClasses,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Button,
} from '@fluentui/react-components';
import {
  Lightbulb24Regular,
  Document24Regular,
  Certificate24Regular,
  Shield24Regular,
  Settings24Regular,
  Notebook24Regular,
  Info16Regular,
} from '@fluentui/react-icons';
import { IPlaybook } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IPlaybookCardGridProps {
  playbooks: IPlaybook[];
  selectedId?: string;
  onSelect: (playbook: IPlaybook) => void;
  isLoading: boolean;
  /** Compact mode: smaller cards with icon + name only, description in popover. */
  compact?: boolean;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    width: '100%',
  },

  // Responsive CSS Grid — 3 / 2 / 1 columns
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, 1fr)',
    gap: tokens.spacingHorizontalM,
    // Fallback narrow breakpoints via container queries aren't universally
    // available yet — use minmax so columns naturally wrap below ~200px each.
    // Explicit responsive overrides are provided via media queries below.
    '@media (max-width: 680px)': {
      gridTemplateColumns: 'repeat(2, 1fr)',
    },
    '@media (max-width: 420px)': {
      gridTemplateColumns: '1fr',
    },
  },

  // Card base
  card: {
    cursor: 'pointer',
    position: 'relative',
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    transition: 'background-color 0.1s ease, border-color 0.1s ease',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineColor: tokens.colorBrandStroke1,
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineOffset: '2px',
    },
  },

  // Selected state
  cardSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    borderTopColor: tokens.colorBrandStroke1,
    borderRightColor: tokens.colorBrandStroke1,
    borderBottomColor: tokens.colorBrandStroke1,
    borderLeftColor: tokens.colorBrandStroke1,
  },

  // Card body layout
  cardContent: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: 'center',
    gap: tokens.spacingVerticalXS,
  },

  // Icon wrapper
  iconWrapper: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
    color: tokens.colorBrandForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },

  // Playbook name
  name: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },

  // Truncated description (2-line clamp)
  description: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    display: '-webkit-box',
    '-webkit-line-clamp': '2',
    '-webkit-box-orient': 'vertical',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },

  // Info button (top-right corner of card)
  infoButtonWrapper: {
    position: 'absolute',
    top: tokens.spacingVerticalXS,
    right: tokens.spacingHorizontalXS,
    zIndex: 1,
  },

  infoButton: {
    minWidth: '24px',
    width: '24px',
    height: '24px',
    padding: '0',
  },

  // Popover content
  popoverContent: {
    maxWidth: '280px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
  },

  popoverTitle: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  popoverDescription: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
  },

  // Loading state
  loading: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },

  // Empty state
  empty: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
  },

  // ── Compact overrides ────────────────────────────────────────────────
  gridCompact: {
    gridTemplateColumns: 'repeat(4, 1fr)',
    gap: tokens.spacingHorizontalS,
    '@media (max-width: 680px)': {
      gridTemplateColumns: 'repeat(3, 1fr)',
    },
    '@media (max-width: 420px)': {
      gridTemplateColumns: 'repeat(2, 1fr)',
    },
  },
  cardContentCompact: {
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  iconWrapperCompact: {
    width: '24px',
    height: '24px',
    marginBottom: '0px',
  },
  nameCompact: {
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Icon registry
// ---------------------------------------------------------------------------

const ICON_MAP: Record<string, React.ReactElement> = {
  Lightbulb: <Lightbulb24Regular />,
  DocumentText: <Document24Regular />,
  Certificate: <Certificate24Regular />,
  Shield: <Shield24Regular />,
  Settings: <Settings24Regular />,
  default: <Notebook24Regular />,
};

function resolveIcon(iconName?: string): React.ReactElement {
  if (iconName && ICON_MAP[iconName]) {
    return ICON_MAP[iconName];
  }
  return ICON_MAP['default'];
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

interface PlaybookCardProps {
  playbook: IPlaybook;
  isSelected: boolean;
  onSelect: (playbook: IPlaybook) => void;
  styles: ReturnType<typeof useStyles>;
  compact?: boolean;
}

const PlaybookCard: React.FC<PlaybookCardProps> = ({ playbook, isSelected, onSelect, styles, compact }) => {
  const handleClick = (): void => {
    onSelect(playbook);
  };

  const handleKeyDown = (event: React.KeyboardEvent): void => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onSelect(playbook);
    }
  };

  const stopPropagation = (e: React.MouseEvent): void => {
    e.stopPropagation();
  };

  return (
    <Card
      className={mergeClasses(styles.card, isSelected && styles.cardSelected)}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      tabIndex={0}
      role="button"
      aria-pressed={isSelected}
      aria-label={playbook.name}
    >
      {/* Info popover — only shown when description is present */}
      {playbook.description && (
        <div className={styles.infoButtonWrapper}>
          <Popover withArrow positioning="above-end">
            <PopoverTrigger disableButtonEnhancement>
              <Button
                className={styles.infoButton}
                appearance="subtle"
                icon={<Info16Regular />}
                size="small"
                onClick={stopPropagation}
                aria-label={`More info about ${playbook.name}`}
              />
            </PopoverTrigger>
            <PopoverSurface>
              <div className={styles.popoverContent}>
                <Text className={styles.popoverTitle}>{playbook.name}</Text>
                <Text className={styles.popoverDescription}>{playbook.description}</Text>
              </div>
            </PopoverSurface>
          </Popover>
        </div>
      )}

      <CardHeader
        header={
          <div className={mergeClasses(styles.cardContent, compact && styles.cardContentCompact)}>
            <div className={mergeClasses(styles.iconWrapper, compact && styles.iconWrapperCompact)}>
              {resolveIcon(playbook.icon)}
            </div>
            <Text className={mergeClasses(styles.name, compact && styles.nameCompact)}>{playbook.name}</Text>
            {!compact && playbook.description && <Text className={styles.description}>{playbook.description}</Text>}
          </div>
        }
      />
    </Card>
  );
};

// ---------------------------------------------------------------------------
// PlaybookCardGrid
// ---------------------------------------------------------------------------

export const PlaybookCardGrid: React.FC<IPlaybookCardGridProps> = ({
  playbooks,
  selectedId,
  onSelect,
  isLoading,
  compact,
}) => {
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={styles.container}>
        <div className={styles.loading}>
          <Spinner size="medium" label="Loading playbooks..." />
        </div>
      </div>
    );
  }

  if (playbooks.length === 0) {
    return (
      <div className={styles.container}>
        <Text className={styles.empty}>No playbooks available</Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={mergeClasses(styles.grid, compact && styles.gridCompact)}>
        {playbooks.map(playbook => (
          <PlaybookCard
            key={playbook.id}
            playbook={playbook}
            isSelected={selectedId === playbook.id}
            onSelect={onSelect}
            styles={styles}
            compact={compact}
          />
        ))}
      </div>
    </div>
  );
};

export default PlaybookCardGrid;
