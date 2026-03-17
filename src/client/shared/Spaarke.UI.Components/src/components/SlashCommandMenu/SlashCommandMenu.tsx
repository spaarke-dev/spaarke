/**
 * SlashCommandMenu - Floating Fluent v9 Popover with filterable command list.
 *
 * Opens above the SprkChat input bar when the user types '/' to trigger the
 * slash command mode. Renders a filtered, keyboard-navigable list of SprkChat
 * commands (system + playbook capabilities).
 *
 * Positioning: The popover is anchored to `anchorRef` using an absolutely-
 * positioned container rendered in the same stacking context as the anchor.
 * The parent component is responsible for positioning the anchor correctly
 * (typically the chat input container).
 *
 * Keyboard navigation:
 * - ArrowDown / ArrowUp → move focused item
 * - Enter               → select focused item
 * - Escape              → dismiss menu (fires onDismiss)
 *
 * Accessibility:
 * - role="listbox" on the list container
 * - role="option" on each item
 * - aria-selected on the focused item
 * - aria-activedescendant on the listbox
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
import { type SlashCommandMenuProps, type SlashCommand } from './slashCommandMenu.types';

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
    padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalM}`,
    fontSize: tokens.fontSizeBase100,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

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
 * bar. Supports keyboard navigation, type-ahead filtering, and dark mode via
 * Fluent v9 design tokens.
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

  // Keyboard navigation handler — registered on the list container
  const handleKeyDown = React.useCallback(
    (event: React.KeyboardEvent<HTMLUListElement>) => {
      switch (event.key) {
        case 'ArrowDown': {
          event.preventDefault();
          setFocusedIndex(prev => Math.min(prev + 1, commands.length - 1));
          break;
        }
        case 'ArrowUp': {
          event.preventDefault();
          setFocusedIndex(prev => Math.max(prev - 1, 0));
          break;
        }
        case 'Enter': {
          event.preventDefault();
          if (commands.length > 0) {
            onSelect(commands[focusedIndex]);
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
    [commands, focusedIndex, onSelect, onDismiss],
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
    commands.length > 0 ? `slash-cmd-item-${commands[focusedIndex]?.id}` : undefined;

  // Group commands by category for section headers
  const systemCommands = commands.filter(c => c.category === 'system');
  const playbookCommands = commands.filter(c => c.category === 'playbook');

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
      </span>
    </li>
  );

  return (
    <div
      className={mergeClasses(styles.container, !visible && styles.hidden)}
      role="dialog"
      aria-label="Slash commands"
      aria-hidden={!visible}
      data-testid="slash-command-menu"
    >
      {commands.length === 0 ? (
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
          {systemCommands.length > 0 && playbookCommands.length > 0 && (
            <li role="presentation" className={styles.categoryHeader} aria-hidden="true">
              System
            </li>
          )}
          {systemCommands.map((cmd, idx) => renderItem(cmd, idx))}

          {playbookCommands.length > 0 && (
            <li role="presentation" className={styles.categoryHeader} aria-hidden="true">
              Playbook
            </li>
          )}
          {playbookCommands.map((cmd, idx) =>
            renderItem(cmd, systemCommands.length + idx),
          )}
        </ul>
      )}
    </div>
  );
};
