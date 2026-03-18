/**
 * SlashCommandMenu - Floating Fluent v9 Popover with filterable command list.
 *
 * Opens above the SprkChat input bar when the user types '/' to trigger the
 * slash command mode. Renders a filtered, keyboard-navigable list of SprkChat
 * commands grouped by source category: System, Playbook, and Scope.
 *
 * Category grouping:
 * - System Commands   → Built-in commands always available (neutral icon)
 * - Playbook Commands → Dynamic commands from the active playbook (brand/purple accent)
 * - Scope Commands    → Dynamic commands from analysis scopes (teal/secondary accent)
 *
 * Each non-system command item optionally shows a source subtitle indicating
 * which playbook or scope contributed the command (e.g., "From: Legal Research").
 *
 * Positioning: The popover is anchored to `anchorRef` using an absolutely-
 * positioned container rendered in the same stacking context as the anchor.
 * The parent component is responsible for positioning the anchor correctly
 * (typically the chat input container).
 *
 * Keyboard navigation:
 * - ArrowDown / ArrowUp → move focused item (across category boundaries)
 * - Enter               → select focused item
 * - Escape              → dismiss menu (fires onDismiss)
 *
 * Category headers are visual-only (not selectable, skipped by keyboard nav).
 *
 * Accessibility:
 * - role="listbox" on the list container
 * - role="option" on each item
 * - aria-selected on the focused item
 * - aria-activedescendant on the listbox
 * - Category headers use role="presentation" and aria-hidden
 *
 * @see slashCommandMenu.types.ts - Type definitions and DEFAULT_SLASH_COMMANDS
 * @see useSlashCommands.ts       - Hook for managing state and filtering
 * @see ADR-021                   - Fluent v9 tokens; no hard-coded colors; dark mode
 * @see ADR-012                   - Shared Component Library (no Xrm imports)
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
} from '@fluentui/react-components';
import {
  SettingsRegular,
  BookTemplateRegular,
  GlobeRegular,
} from '@fluentui/react-icons';
import { type SlashCommandMenuProps, type SlashCommand, type SlashCommandSource } from './slashCommandMenu.types';

// ─────────────────────────────────────────────────────────────────────────────
// Category configuration
// ─────────────────────────────────────────────────────────────────────────────

interface CategoryConfig {
  key: SlashCommandSource;
  label: string;
  icon: React.ReactElement;
  /** Semantic token for the category header icon color accent. */
  accentColor: string;
}

/**
 * Ordered category definitions. Only categories with matching commands are rendered.
 * - System: neutral icon (SettingsRegular), neutral foreground
 * - Playbook: BookTemplateRegular with brand/purple accent
 * - Scope: GlobeRegular with compound brand (teal/secondary) accent
 */
const CATEGORY_CONFIG: CategoryConfig[] = [
  {
    key: 'system',
    label: 'System Commands',
    icon: React.createElement(SettingsRegular),
    accentColor: tokens.colorNeutralForeground2,
  },
  {
    key: 'playbook',
    label: 'Playbook Commands',
    icon: React.createElement(BookTemplateRegular),
    accentColor: tokens.colorBrandForeground1,
  },
  {
    key: 'scope',
    label: 'Scope Commands',
    icon: React.createElement(GlobeRegular),
    accentColor: tokens.colorCompoundBrandForeground1,
  },
];

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    position: 'absolute',
    bottom: '100%',
    left: 0,
    right: 0,
    marginBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow16,
    maxHeight: '280px',
    overflowY: 'auto',
    zIndex: 1200,
    outline: 'none',
  },
  hidden: {
    display: 'none',
  },
  list: {
    listStyle: 'none',
    margin: 0,
    padding: `${tokens.spacingVerticalXXS} 0`,
  },
  item: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    cursor: 'pointer',
    transition: 'background-color 100ms ease',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  itemFocused: {
    backgroundColor: tokens.colorNeutralBackground1Hover,
    outline: `2px solid ${tokens.colorBrandStroke1}`,
    outlineOffset: '-2px',
  },
  itemIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '20px',
    height: '20px',
    flexShrink: 0,
    color: tokens.colorBrandForeground1,
  },
  itemContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  itemLabel: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },
  itemTrigger: {
    color: tokens.colorBrandForeground1,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
  },
  itemDescription: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  itemSourceSubtitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
    fontStyle: 'italic',
  },
  highlightMatch: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightBold,
    backgroundColor: tokens.colorBrandBackground2,
    borderRadius: tokens.borderRadiusSmall,
    paddingLeft: '1px',
    paddingRight: '1px',
  },
  emptyState: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    textAlign: 'center',
  },
  categoryHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalM}`,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  categoryIcon: {
    display: 'flex',
    alignItems: 'center',
    fontSize: '12px',
  },
  /** Brand/purple accent for playbook category header icon. */
  categoryIconPlaybook: {
    color: tokens.colorBrandForeground1,
  },
  /** Compound brand (teal) accent for scope category header icon. */
  categoryIconScope: {
    color: tokens.colorCompoundBrandForeground1,
  },
  /** Neutral accent for system category header icon. */
  categoryIconSystem: {
    color: tokens.colorNeutralForeground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Groups commands by their `source` field, preserving the category order
 * defined in CATEGORY_CONFIG. Only returns non-empty groups.
 */
function groupCommandsBySource(commands: SlashCommand[]): Array<{
  config: CategoryConfig;
  commands: SlashCommand[];
}> {
  const groups: Array<{ config: CategoryConfig; commands: SlashCommand[] }> = [];

  for (const config of CATEGORY_CONFIG) {
    const matching = commands.filter(c => (c.source ?? 'system') === config.key);
    if (matching.length > 0) {
      groups.push({ config, commands: matching });
    }
  }

  return groups;
}

/**
 * Builds a flat ordered list of commands from the grouped categories.
 * This ensures keyboard navigation indices stay consistent with visual order.
 */
function buildFlatCommandList(
  groups: Array<{ config: CategoryConfig; commands: SlashCommand[] }>,
): SlashCommand[] {
  const flat: SlashCommand[] = [];
  for (const group of groups) {
    flat.push(...group.commands);
  }
  return flat;
}

/**
 * Renders a label with the matched portion highlighted.
 * If filterText is empty or no match is found, renders the label as-is.
 */
function HighlightedLabel({
  label,
  filterText,
  className,
  highlightClass,
}: {
  label: string;
  filterText: string;
  className?: string;
  highlightClass: string;
}): React.ReactElement {
  if (!filterText) {
    return <span className={className}>{label}</span>;
  }

  const lowerLabel = label.toLowerCase();
  const lowerFilter = filterText.toLowerCase();
  const matchIndex = lowerLabel.indexOf(lowerFilter);

  if (matchIndex === -1) {
    return <span className={className}>{label}</span>;
  }

  const before = label.slice(0, matchIndex);
  const match = label.slice(matchIndex, matchIndex + filterText.length);
  const after = label.slice(matchIndex + filterText.length);

  return (
    <span className={className}>
      {before}
      <span className={highlightClass}>{match}</span>
      {after}
    </span>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SlashCommandMenu renders a floating command palette above the SprkChat input
 * bar. Commands are grouped by source category (System, Playbook, Scope) with
 * distinct visual headers and optional source subtitles. Supports keyboard
 * navigation across category boundaries and dark mode via Fluent v9 design tokens.
 *
 * @example
 * ```tsx
 * const inputRef = useRef<HTMLTextAreaElement>(null);
 * const anchorRef = useRef<HTMLDivElement>(null);
 * const { menuVisible, filterText, filteredCommands, handleCommandSelect, dismissMenu } =
 *   useSlashCommands({ inputRef });
 *
 * return (
 *   <div ref={anchorRef} style={{ position: 'relative' }}>
 *     <SlashCommandMenu
 *       visible={menuVisible}
 *       commands={filteredCommands}
 *       filterText={filterText}
 *       onSelect={handleCommandSelect}
 *       onDismiss={dismissMenu}
 *       anchorRef={anchorRef}
 *     />
 *     <textarea ref={inputRef} ... />
 *   </div>
 * );
 * ```
 */
export const SlashCommandMenu: React.FC<SlashCommandMenuProps> = ({
  visible,
  commands,
  filterText,
  onSelect,
  onDismiss,
}) => {
  const styles = useStyles();

  const [focusedIndex, setFocusedIndex] = React.useState(0);
  const listRef = React.useRef<HTMLUListElement>(null);

  // Group commands by source category and build a flat list for keyboard nav
  const groups = React.useMemo(() => groupCommandsBySource(commands), [commands]);
  const flatCommands = React.useMemo(() => buildFlatCommandList(groups), [groups]);

  // Whether to show category headers (only when there are 2+ categories)
  const showCategoryHeaders = groups.length > 1;

  // Reset focused index when the command list or visibility changes
  React.useEffect(() => {
    setFocusedIndex(0);
  }, [commands, visible]);

  // Scroll focused item into view
  React.useEffect(() => {
    if (!visible || !listRef.current) return;
    const items = listRef.current.querySelectorAll('[role="option"]');
    const focused = items[focusedIndex] as HTMLElement | undefined;
    if (focused) {
      focused.scrollIntoView({ block: 'nearest' });
    }
  }, [focusedIndex, visible]);

  // Keyboard navigation handler — uses flatCommands for cross-category navigation
  const handleKeyDown = React.useCallback(
    (event: React.KeyboardEvent<HTMLUListElement>) => {
      switch (event.key) {
        case 'ArrowDown': {
          event.preventDefault();
          setFocusedIndex(prev => Math.min(prev + 1, flatCommands.length - 1));
          break;
        }
        case 'ArrowUp': {
          event.preventDefault();
          setFocusedIndex(prev => Math.max(prev - 1, 0));
          break;
        }
        case 'Enter': {
          event.preventDefault();
          if (flatCommands.length > 0) {
            onSelect(flatCommands[focusedIndex]);
          }
          break;
        }
        case 'Escape': {
          event.preventDefault();
          onDismiss();
          break;
        }
        default:
          break;
      }
    },
    [flatCommands, focusedIndex, onSelect, onDismiss],
  );

  const handleItemClick = React.useCallback(
    (command: SlashCommand) => {
      onSelect(command);
    },
    [onSelect],
  );

  const handleItemMouseEnter = React.useCallback(
    (index: number) => {
      setFocusedIndex(index);
    },
    [],
  );

  // Derive the active descendant id for aria
  const activeDescendantId =
    flatCommands.length > 0 ? `slash-cmd-item-${flatCommands[focusedIndex]?.id}` : undefined;

  /**
   * Returns the style class for a category header icon based on source type.
   */
  const getCategoryIconClass = (source: SlashCommandSource): string => {
    switch (source) {
      case 'playbook':
        return styles.categoryIconPlaybook;
      case 'scope':
        return styles.categoryIconScope;
      default:
        return styles.categoryIconSystem;
    }
  };

  const renderItem = (command: SlashCommand, absoluteIndex: number): React.ReactElement => (
    <li
      key={command.id}
      id={`slash-cmd-item-${command.id}`}
      role="option"
      aria-selected={absoluteIndex === focusedIndex}
      className={mergeClasses(
        styles.item,
        absoluteIndex === focusedIndex && styles.itemFocused,
      )}
      onMouseEnter={() => handleItemMouseEnter(absoluteIndex)}
      onMouseDown={(e) => {
        // Use mousedown to avoid losing input focus
        e.preventDefault();
        handleItemClick(command);
      }}
      data-testid={`slash-cmd-item-${command.id}`}
    >
      {command.icon && (
        <span className={styles.itemIcon} aria-hidden="true">
          {command.icon}
        </span>
      )}
      <span className={styles.itemContent}>
        <HighlightedLabel
          label={command.label}
          filterText={filterText}
          className={styles.itemLabel}
          highlightClass={styles.highlightMatch}
        />
        <Text className={styles.itemTrigger} as="span">
          {command.trigger}
        </Text>
        <Text className={styles.itemDescription} as="span" title={command.description}>
          {command.description}
        </Text>
        {command.sourceName && (
          <Text className={styles.itemSourceSubtitle} as="span">
            From: {command.sourceName}
          </Text>
        )}
      </span>
    </li>
  );

  // Track the running absolute index across groups for keyboard navigation
  let absoluteIndex = 0;

  return (
    <div
      className={mergeClasses(styles.container, !visible && styles.hidden)}
      role="dialog"
      aria-label="Slash commands"
      aria-hidden={!visible}
      data-testid="slash-command-menu"
    >
      {flatCommands.length === 0 ? (
        <div className={styles.emptyState} role="status">
          No commands match &ldquo;/{filterText}&rdquo;
        </div>
      ) : (
        <ul
          ref={listRef}
          role="listbox"
          aria-label="Available commands"
          aria-activedescendant={activeDescendantId}
          className={styles.list}
          onKeyDown={handleKeyDown}
          // Make the list focusable so keyboard events register when
          // the parent wires up focus management
          tabIndex={-1}
        >
          {groups.map(group => {
            const startIndex = absoluteIndex;
            absoluteIndex += group.commands.length;

            return (
              <React.Fragment key={group.config.key}>
                {showCategoryHeaders && (
                  <li
                    role="presentation"
                    className={styles.categoryHeader}
                    aria-hidden="true"
                    data-testid={`slash-cmd-category-${group.config.key}`}
                  >
                    <span
                      className={mergeClasses(
                        styles.categoryIcon,
                        getCategoryIconClass(group.config.key),
                      )}
                      aria-hidden="true"
                    >
                      {group.config.icon}
                    </span>
                    {group.config.label}
                  </li>
                )}
                {group.commands.map((cmd, idx) =>
                  renderItem(cmd, startIndex + idx),
                )}
              </React.Fragment>
            );
          })}
        </ul>
      )}
    </div>
  );
};
