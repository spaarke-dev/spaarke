/**
 * useSlashCommands - State management hook for the SlashCommandMenu.
 *
 * Monitors changes to an input element's value to detect slash command mode
 * (value starts with '/'). When slash mode is active, filters the merged
 * command list (static system commands + dynamic playbook commands) by the
 * text typed after the '/'.
 *
 * Returns menu visibility state, the current filter text, the filtered command
 * list, and action handlers for the parent component.
 *
 * Usage:
 * ```tsx
 * const inputRef = useRef<HTMLTextAreaElement>(null);
 * const {
 *   menuVisible,
 *   filterText,
 *   filteredCommands,
 *   handleInputChange,
 *   handleCommandSelect,
 *   dismissMenu,
 * } = useSlashCommands({ inputRef, dynamicCommands: playbookCmds });
 *
 * return (
 *   <div style={{ position: 'relative' }}>
 *     <SlashCommandMenu
 *       visible={menuVisible}
 *       commands={filteredCommands}
 *       filterText={filterText}
 *       onSelect={handleCommandSelect}
 *       onDismiss={dismissMenu}
 *       anchorRef={anchorRef}
 *     />
 *     <textarea
 *       ref={inputRef}
 *       onChange={(e) => handleInputChange(e.target.value)}
 *     />
 *   </div>
 * );
 * ```
 *
 * Filter logic:
 * - Menu is shown when input value starts with '/' (first character)
 * - Filter text is the portion after '/' (e.g., '/sum' → filterText='sum')
 * - Commands are matched when their label OR trigger contains filterText
 *   (case-insensitive prefix or substring match)
 * - Menu hides when input is empty, value no longer starts with '/', or
 *   dismissMenu is explicitly called
 *
 * handleCommandSelect:
 * - Replaces the input value with the selected command's trigger followed by a space
 *   (e.g., selecting '/summarize' writes '/summarize ' to the input)
 * - Fires the optional onCommandSelected callback
 * - Dismisses the menu
 *
 * Constraints:
 * - MUST NOT import Xrm or ComponentFramework (ADR-012)
 * - MUST NOT make API calls — commands are provided via props (ADR-012)
 *
 * @see SlashCommandMenu component
 * @see slashCommandMenu.types.ts — SlashCommand type definitions
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 */

import { useState, useCallback, useMemo } from 'react';
import { DEFAULT_SLASH_COMMANDS, type SlashCommand } from '../components/SlashCommandMenu/slashCommandMenu.types';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseSlashCommandsOptions {
  /**
   * Ref to the text input (textarea or input) element being monitored.
   * The hook writes the selected command trigger back into this element when
   * handleCommandSelect is called.
   */
  inputRef: React.RefObject<HTMLTextAreaElement | HTMLInputElement>;

  /**
   * Static system commands to always include.
   * Defaults to DEFAULT_SLASH_COMMANDS if not provided.
   */
  staticCommands?: SlashCommand[];

  /**
   * Dynamic commands from playbook capabilities or other runtime sources.
   * Merged with staticCommands; appended after system commands in the list.
   */
  dynamicCommands?: SlashCommand[];

  /**
   * Optional callback fired after the user selects a command.
   * Receives the selected command and the final input value after the
   * trigger text has been written into the input.
   */
  onCommandSelected?: (command: SlashCommand) => void;
}

export interface UseSlashCommandsResult {
  /** Whether the slash command menu should be visible. */
  menuVisible: boolean;

  /**
   * The text after the '/' character, used for filtering and highlighting.
   * Empty string when no filter is applied (user just typed '/').
   */
  filterText: string;

  /**
   * The filtered list of commands matching the current filterText.
   * When filterText is empty, all merged commands are returned.
   */
  filteredCommands: SlashCommand[];

  /**
   * Call this with the full current input value on every input change.
   * The hook derives filterText and menuVisible from this value.
   */
  handleInputChange: (value: string) => void;

  /**
   * Call this when the user selects a command (click or Enter).
   * Writes the trigger text into the input ref and calls onCommandSelected.
   */
  handleCommandSelect: (command: SlashCommand) => void;

  /** Call this to hide the menu without selecting a command. */
  dismissMenu: () => void;

  /**
   * All merged commands (static + dynamic), unfiltered.
   * Useful for consumers that want to manage their own filtering.
   */
  allCommands: SlashCommand[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Character that activates slash command mode when it is the first character. */
const SLASH_TRIGGER = '/';

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Manages state for the SlashCommandMenu: detects slash trigger in an input,
 * filters commands by the typed text, and handles command selection.
 *
 * @param options - Configuration options
 * @returns Slash command menu state and action handlers
 */
export function useSlashCommands(options: UseSlashCommandsOptions): UseSlashCommandsResult {
  const {
    inputRef,
    staticCommands = DEFAULT_SLASH_COMMANDS,
    dynamicCommands = [],
    onCommandSelected,
  } = options;

  const [menuVisible, setMenuVisible] = useState(false);
  const [filterText, setFilterText] = useState('');

  // Merge static and dynamic commands; stable unless deps change
  const allCommands = useMemo<SlashCommand[]>(
    () => [...staticCommands, ...dynamicCommands],
    [staticCommands, dynamicCommands],
  );

  // Filter commands by filterText: match against label or trigger (case-insensitive)
  const filteredCommands = useMemo<SlashCommand[]>(() => {
    if (!filterText) return allCommands;

    const lower = filterText.toLowerCase();

    return allCommands.filter(cmd => {
      const labelMatch = cmd.label.toLowerCase().includes(lower);
      // Match against trigger without the leading '/'
      const triggerWithoutSlash = cmd.trigger.slice(1).toLowerCase();
      const triggerMatch = triggerWithoutSlash.includes(lower);
      return labelMatch || triggerMatch;
    });
  }, [allCommands, filterText]);

  /**
   * Processes the raw input value to detect slash mode and derive filterText.
   * Must be called on every input change event by the parent component.
   */
  const handleInputChange = useCallback((value: string) => {
    if (value.startsWith(SLASH_TRIGGER)) {
      // Extract text after the initial '/'
      const textAfterSlash = value.slice(1);

      // Only show the menu if the text after '/' has no spaces
      // (once a space is typed, the user is providing arguments, not typing a command name)
      if (!textAfterSlash.includes(' ')) {
        setFilterText(textAfterSlash);
        setMenuVisible(true);
        return;
      }
    }

    // Not in slash mode — hide menu and reset filter
    setMenuVisible(false);
    setFilterText('');
  }, []);

  /**
   * Selects a command: writes trigger + space into the input element,
   * fires the optional callback, and dismisses the menu.
   */
  const handleCommandSelect = useCallback((command: SlashCommand) => {
    const input = inputRef.current;
    if (input) {
      const newValue = `${command.trigger} `;

      // Write the value using the native input value setter so that React's
      // synthetic event system tracks the change correctly
      const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
        input instanceof HTMLTextAreaElement
          ? window.HTMLTextAreaElement.prototype
          : window.HTMLInputElement.prototype,
        'value',
      )?.set;

      if (nativeInputValueSetter) {
        nativeInputValueSetter.call(input, newValue);
        input.dispatchEvent(new Event('input', { bubbles: true }));
      } else {
        // Fallback: direct assignment (may not trigger React onChange in all cases)
        (input as HTMLTextAreaElement | HTMLInputElement).value = newValue;
      }

      // Place cursor at end
      const len = newValue.length;
      input.setSelectionRange(len, len);
      input.focus();
    }

    onCommandSelected?.(command);
    setMenuVisible(false);
    setFilterText('');
  }, [inputRef, onCommandSelected]);

  /** Hides the menu without selecting a command. */
  const dismissMenu = useCallback(() => {
    setMenuVisible(false);
    setFilterText('');
  }, []);

  return {
    menuVisible,
    filterText,
    filteredCommands,
    handleInputChange,
    handleCommandSelect,
    dismissMenu,
    allCommands,
  };
}
