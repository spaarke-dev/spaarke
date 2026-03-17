/**
 * SprkChatInput - Chat input area with send button and slash command support
 *
 * Features:
 * - Textarea with send button
 * - Ctrl+Enter to send (Enter for newline)
 * - Disabled during streaming
 * - Character count indicator
 * - [/] button in toolbar opens SlashCommandMenu
 * - Typing '/' as the first character also opens SlashCommandMenu with type-ahead filtering
 * - Esc closes the menu; selecting a command writes the trigger into the input
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see spec-FR-09 - Slash command menu with type-ahead filtering and keyboard nav
 */

import * as React from 'react';
import { makeStyles, shorthands, tokens, Textarea, Button, Text } from '@fluentui/react-components';
import { SendRegular, PromptRegular } from '@fluentui/react-icons';
import { ISprkChatInputProps } from './types';
import { SlashCommandMenu } from '../SlashCommandMenu/SlashCommandMenu';
import { useSlashCommands } from '../../hooks/useSlashCommands';
import { DEFAULT_SLASH_COMMANDS, type SlashCommand } from '../SlashCommandMenu/slashCommandMenu.types';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Wrapper around the input row that provides the positioning context for
  // the absolutely-positioned SlashCommandMenu overlay
  inputWrapper: {
    position: 'relative',
  },
  inputRow: {
    display: 'flex',
    alignItems: 'flex-end',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  textarea: {
    flexGrow: 1,
  },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  charCount: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  charCountWarning: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorPaletteRedForeground1,
  },
  hint: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_MAX_CHAR_COUNT = 2000;

/**
 * SprkChatInput - Textarea with send button, Ctrl+Enter shortcut, character counter,
 * and integrated slash command menu (opened by typing '/' or clicking the [/] button).
 *
 * @example
 * ```tsx
 * <SprkChatInput
 *   onSend={(msg) => handleSend(msg)}
 *   disabled={isStreaming}
 *   maxCharCount={2000}
 * />
 * ```
 */
export const SprkChatInput: React.FC<ISprkChatInputProps> = ({
  onSend,
  disabled = false,
  maxCharCount = DEFAULT_MAX_CHAR_COUNT,
  placeholder = 'Type a message...',
  dynamicSlashCommands,
}) => {
  const styles = useStyles();
  const [value, setValue] = React.useState<string>('');

  // Ref to the underlying <textarea> element — required by useSlashCommands to
  // write the selected command trigger back into the input after selection.
  const inputRef = React.useRef<HTMLTextAreaElement>(null);

  // Ref for the input wrapper div; passed to SlashCommandMenu as the anchor so
  // the popover knows where to position itself (above the input row).
  const anchorRef = React.useRef<HTMLDivElement>(null);

  // ── Slash command hook ────────────────────────────────────────────────────

  const {
    menuVisible,
    filterText,
    filteredCommands,
    handleInputChange: hookHandleInputChange,
    handleCommandSelect,
    dismissMenu,
  } = useSlashCommands({
    inputRef,
    staticCommands: DEFAULT_SLASH_COMMANDS,
    dynamicCommands: dynamicSlashCommands ?? [],
    onCommandSelected: (command: SlashCommand) => {
      // After the hook writes the trigger to the DOM element, sync React state
      // so that the controlled Textarea reflects the value.
      setValue(`${command.trigger} `);
    },
  });

  // ── Derived state ─────────────────────────────────────────────────────────

  const charCount = value.length;
  const isOverLimit = charCount > maxCharCount;
  const canSend = value.trim().length > 0 && !disabled && !isOverLimit;

  // ── Handlers ─────────────────────────────────────────────────────────────

  const handleSend = React.useCallback(() => {
    const trimmed = value.trim();
    if (trimmed && !disabled && !isOverLimit) {
      onSend(trimmed);
      setValue('');
    }
  }, [value, disabled, isOverLimit, onSend]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Escape') {
        // Dismiss the slash command menu on Esc; also clear input when menu was open
        if (menuVisible) {
          e.preventDefault();
          dismissMenu();
          setValue('');
        }
        return;
      }

      if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend, menuVisible, dismissMenu],
  );

  const handleChange = React.useCallback(
    (_event: React.ChangeEvent<HTMLTextAreaElement>, data: { value: string }) => {
      setValue(data.value);
      // Notify the slash command hook so it can update filter state and menu visibility
      hookHandleInputChange(data.value);
    },
    [hookHandleInputChange],
  );

  // ── [/] button handler ────────────────────────────────────────────────────

  const handleSlashButtonClick = React.useCallback(() => {
    // Open the menu by writing '/' into the input — this triggers slash mode in the hook
    setValue('/');
    hookHandleInputChange('/');
    // Focus the textarea so the user can continue typing to filter commands
    inputRef.current?.focus();
  }, [hookHandleInputChange]);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root} role="form" aria-label="Chat input">
      {/* inputWrapper provides `position: relative` as stacking context for SlashCommandMenu */}
      <div className={styles.inputWrapper} ref={anchorRef}>
        {/* SlashCommandMenu renders as `position: absolute; bottom: 100%` inside this wrapper */}
        <SlashCommandMenu
          visible={menuVisible}
          commands={filteredCommands}
          filterText={filterText}
          onSelect={handleCommandSelect}
          onDismiss={dismissMenu}
          anchorRef={anchorRef}
        />

        <div className={styles.inputRow}>
          {/* [/] button — opens slash command menu; appearance="subtle" per spec-FR-09 */}
          <Button
            appearance="subtle"
            icon={<PromptRegular />}
            onClick={handleSlashButtonClick}
            disabled={disabled}
            aria-label="Open slash commands"
            title="Open slash commands (/)"
            data-testid="slash-command-button"
          />

          <Textarea
            className={styles.textarea}
            value={value}
            onChange={handleChange}
            onKeyDown={handleKeyDown}
            placeholder={placeholder}
            disabled={disabled}
            resize="vertical"
            aria-label="Message input"
            aria-expanded={menuVisible}
            aria-haspopup="listbox"
            data-testid="chat-input-textarea"
            // Fluent Textarea forwards the ref to the underlying <textarea> element
            // (primary slot — confirmed by TextareaSlots.textarea JSDoc)
            ref={inputRef}
          />

          <Button
            appearance="primary"
            icon={<SendRegular />}
            onClick={handleSend}
            disabled={!canSend}
            aria-label="Send message"
            data-testid="chat-send-button"
          />
        </div>
      </div>

      <div className={styles.footer}>
        <Text className={styles.hint}>Ctrl+Enter to send · / for commands</Text>
        <Text className={isOverLimit ? styles.charCountWarning : styles.charCount}>
          {charCount}/{maxCharCount}
        </Text>
      </div>
    </div>
  );
};

export default SprkChatInput;
