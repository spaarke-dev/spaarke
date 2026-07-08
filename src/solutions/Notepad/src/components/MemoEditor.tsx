/**
 * MemoEditor — Fluent v9 Textarea wired to the current `sprk_memo` body.
 *
 * Save behavior (spec FR-17 per owner clarification O5, VSCode-style):
 *   - Ctrl+Enter (or Cmd+Enter on macOS) → immediate save; preventDefault so
 *     no newline is inserted. Signals the caller via `onChange(value, { immediate: true })`.
 *   - Blur → immediate save with the current value.
 *   - Plain keystroke → non-immediate `onChange(value)`; the caller
 *     (useSprkMemoRepository.updateBody) owns the 1s idle debounce.
 *   - Plain Enter → default textarea behavior (newline). No save is triggered
 *     here; the newline surfaces as a keystroke and is coalesced into the
 *     caller's debounced write.
 *
 * IME composition:
 *   Ctrl+Enter fired during an active IME composition is suppressed so the
 *   composition can complete. We track composition state with
 *   `onCompositionStart` / `onCompositionEnd` and gate the keydown handler.
 *
 * Styling: fills the parent's height; `resize="vertical"` lets users grow the
 * editor if they want. Parent controls overall height and width.
 *
 * ADR-021: Fluent v9 only, semantic tokens only, no hex/rgb literals.
 * React 18 compat: Notepad is a React 18 Vite SPA — this component is NOT
 * consumed by PCFs. `React.forwardRef` and functional components are fine.
 *
 * @see projects/record-header-and-notepad-r1/spec.md FR-17
 * @see projects/record-header-and-notepad-r1/design.md §3.6
 * @see .claude/adr/ADR-021-fluent-design-system.md
 */
import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Textarea,
  type TextareaOnChangeData,
} from '@fluentui/react-components';

/**
 * Props for {@link MemoEditor}.
 */
export interface IMemoEditorProps {
  /** Current body text bound to the textarea. */
  value: string;
  /**
   * Called on every keystroke and on save events (Ctrl+Enter, blur).
   * `options.immediate=true` signals the caller to bypass its debounce and
   * write NOW; without it, the caller may debounce.
   */
  onChange: (body: string, options?: { immediate?: boolean }) => void;
  /** Disables input; keydown handlers still attach but Fluent gates events. */
  disabled?: boolean;
  /** Placeholder shown when `value` is empty. */
  placeholder?: string;
  /** Optional extra className applied to the Fluent Textarea root. */
  className?: string;
}

const useStyles = makeStyles({
  root: {
    // Fill parent container in both axes; parent owns absolute height.
    width: '100%',
    height: '100%',
    minHeight: '100%',
  },
  textarea: {
    // The inner <textarea> — override its min-height so it grows to fill.
    minHeight: '100%',
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

/**
 * Fluent v9 Textarea for the current memo body with FR-17 save keybindings.
 *
 * v1.0.8: wrapped in `React.memo` so the Textarea does not re-render on every
 * NotepadShell state bump. Live QA reported typing lag; the root cause was
 * un-memoized child re-renders every ~1s when the repository's debounce
 * commit updated the memo timestamp on the shell. Fluent v9 Textarea + Menu
 * both have non-trivial re-render costs on IME-active keystroke streams.
 *
 * @see IMemoEditorProps
 */
export const MemoEditor = React.memo(React.forwardRef<HTMLTextAreaElement, IMemoEditorProps>(
  ({ value, onChange, disabled, placeholder, className }, ref) => {
    const styles = useStyles();
    // IME composition state — Ctrl+Enter during composition is a no-op.
    const isComposingRef = React.useRef(false);

    const handleKeyDown = React.useCallback(
      (event: React.KeyboardEvent<HTMLTextAreaElement>): void => {
        // Ctrl+Enter (Windows/Linux) or Cmd+Enter (macOS parity) → immediate save.
        // Skip when an IME composition is active so the composition can finish.
        const isCtrlOrMetaEnter =
          (event.ctrlKey || event.metaKey) && event.key === 'Enter';
        if (isCtrlOrMetaEnter && !isComposingRef.current) {
          event.preventDefault();
          onChange((event.currentTarget as HTMLTextAreaElement).value, {
            immediate: true,
          });
        }
        // Every other key (including plain Enter) — no interception; the
        // textarea handles it natively. Newline is inserted; the change event
        // fires and we propagate via handleChange.
      },
      [onChange]
    );

    const handleChange = React.useCallback(
      (
        _ev: React.ChangeEvent<HTMLTextAreaElement>,
        data: TextareaOnChangeData
      ): void => {
        onChange(data.value);
      },
      [onChange]
    );

    const handleBlur = React.useCallback(
      (event: React.FocusEvent<HTMLTextAreaElement>): void => {
        onChange(event.currentTarget.value, { immediate: true });
      },
      [onChange]
    );

    const handleCompositionStart = React.useCallback((): void => {
      isComposingRef.current = true;
    }, []);

    const handleCompositionEnd = React.useCallback((): void => {
      isComposingRef.current = false;
    }, []);

    return (
      <Textarea
        ref={ref}
        className={mergeClasses(styles.root, className)}
        textarea={{ className: styles.textarea }}
        value={value}
        onChange={handleChange}
        onKeyDown={handleKeyDown}
        onBlur={handleBlur}
        onCompositionStart={handleCompositionStart}
        onCompositionEnd={handleCompositionEnd}
        disabled={disabled}
        placeholder={placeholder ?? 'Start typing your memo...'}
        resize="vertical"
        data-testid="sprk-memo-editor"
      />
    );
  }
));

MemoEditor.displayName = 'MemoEditor';
