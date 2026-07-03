/**
 * TextareaField — multiline label + value renderer for `FieldGrid` (FR-04).
 *
 * v1.0.2 (2026-07-03): added inline-editing scope expansion per user approval.
 * When `onSave` is provided, clicking the clamped value enters edit mode
 * (Fluent v9 `Textarea`); Ctrl+Enter saves (plain Enter inserts a newline
 * to preserve multiline typing), blur saves, Escape cancels. When `onSave`
 * is omitted, the renderer stays read-only with the "Show more" popover
 * (original R1 behavior — backward compatible).
 *
 * Read-mode contract:
 *  - Label above, multiline value clamped to `maxLines` (default 3)
 *  - "Show more" Fluent v9 `Link` appears when clamp truncates
 *  - Click "Show more" → Fluent v9 `Popover` with full scrollable value
 *  - `null` / `undefined` / empty-string render as em-dash "—"
 *  - `grid-column: span N` applied inline for `FieldGrid` integration
 *
 * Edit-mode contract (v1.0.2, when `onSave` supplied and not `disabled`):
 *  - Click clamped value → enters edit with Fluent v9 `Textarea`
 *  - Ctrl+Enter OR blur commits (only when value changed)
 *  - Plain Enter inserts a newline (preserves multiline typing UX)
 *  - Escape cancels; spinner during save; revert on error
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens exclusively. No hex /
 * rgb / hsl literals. Popover and Textarea chrome inherit Fluent theming so
 * the component adapts to light, dark, and high-contrast modes.
 *
 * Per ADR-022 (React 16/17 boundary): plain functional component using only
 * `useState`, `useRef`, `useEffect`, `useCallback` — no React-18 exclusive
 * APIs (safe to consume from PCFs running `react@16.14`).
 *
 * @see FR-04 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 */
import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Label,
  Link,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Textarea,
  Spinner,
} from '@fluentui/react-components';

const { useCallback, useEffect, useRef, useState } = React;

/**
 * The em-dash rendered for null / undefined / empty values (FR-04).
 */
const EMPTY_PLACEHOLDER = '—';

/**
 * Props for {@link TextareaField}.
 */
export interface ITextareaFieldProps {
  /** Field label rendered above the clamped value. */
  label: string;

  /**
   * Multiline value to render. `null` / `undefined` render as the em-dash "—".
   * Empty string is treated as empty (also renders em-dash).
   */
  value: string | null | undefined;

  /**
   * Grid column span. FieldGrid does not set `gridColumn` on children per
   * FR-03 — this component applies `gridColumn: span N` inline on its wrapper.
   */
  span: 1 | 2 | 3;

  /**
   * Maximum number of lines shown in the clamped view. Overflow triggers the
   * "Show more" link + popover.
   *
   * @defaultValue 3
   */
  maxLines?: number;

  /** Optional extra className applied to the outer wrapper. */
  className?: string;

  /**
   * When provided, the value becomes click-to-edit. Callback is invoked with
   * the new string on Ctrl+Enter or blur (only when the value changed).
   * Return a rejected Promise to signal a save error — the field will revert
   * to the previous value and stay in edit mode for retry. Omit to render
   * read-only (default).
   */
  onSave?: (newValue: string) => Promise<void>;

  /**
   * When `true`, disables editing (value shown but not clickable). Only
   * meaningful when `onSave` is also provided.
   */
  disabled?: boolean;
}

const useStyles = makeStyles({
  /** Outer wrapper — vertical stack of label + clamped value + optional link. */
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  /**
   * The label rendered above the value.
   * v1.0.4: match OOB Dataverse form-field label typography — 14px / #242424 /
   * 4px bottom padding. Original 12px / neutralForeground3 read too small.
   */
  label: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightRegular,
    paddingBottom: tokens.spacingVerticalXS,
  },
  /**
   * The clamped value container. Uses the `-webkit-box` + `-webkit-line-clamp`
   * combination — the only CSS mechanism widely supported today for a strict
   * line-count clamp with an ellipsis. `white-space: pre-line` preserves user
   * newlines while still allowing wrapping.
   *
   * `--sprk-textarea-max-lines` is a CSS custom property assigned inline via
   * the component's `style` prop so `maxLines` is a real prop (not baked into
   * static Griffel styles). `min-height` reserves space for `maxLines` even
   * when the value is empty — matches the OOB Dataverse multiline text input
   * so the header layout stays stable regardless of content.
   */
  clamped: {
    // v1.0.5: auto-grow instead of clamp+ellipsis. Users reported the "Show
    // more" popover collided visually with the OOB fields directly below the
    // header — clamped text was overlaid by the OOB Matter Description input
    // creating an unreadable stack. Removing the `-webkit-line-clamp` /
    // ellipsis lets the container grow to fit the content inline; the header
    // still reserves `maxLines` worth of height via `minHeight` so the layout
    // stays stable when the record's description is short or empty.
    whiteSpace: 'pre-line',
    wordBreak: 'break-word',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
    minHeight: 'calc(var(--sprk-textarea-max-lines, 3) * 1.4285em)',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  /**
   * Editable-value hover treatment — subtle dashed underline + text cursor to
   * hint clickability. Applied only when onSave is provided AND not disabled.
   */
  clampedEditable: {
    cursor: 'text',
    borderBottomStyle: 'dashed',
    borderBottomWidth: '1px',
    borderBottomColor: tokens.colorNeutralStroke2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3Hover,
      borderBottomColor: tokens.colorBrandStroke1,
    },
  },
  /**
   * Em-dash placeholder — same OOB-input surface as the clamped value so the
   * empty state occupies the same visual footprint (v1.0.3 polish).
   */
  placeholder: {
    display: 'flex',
    alignItems: 'flex-start',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
    minHeight: 'calc(var(--sprk-textarea-max-lines, 3) * 1.4285em)',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  /** "Show more" link — subtle, small font. */
  showMoreLink: {
    fontSize: tokens.fontSizeBase200,
    // `alignSelf` keeps the link left-aligned within the flex column.
    alignSelf: 'flex-start',
  },
  /**
   * Popover content surface for the full text. Scrollable when text is long;
   * width bounded per U-03 (spec unresolved question) at 320..480px.
   */
  popoverSurface: {
    minWidth: '320px',
    maxWidth: '480px',
    maxHeight: '480px',
    overflowY: 'auto',
    padding: tokens.spacingVerticalM,
  },
  popoverLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },
  popoverBody: {
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  editRow: {
    display: 'flex',
    alignItems: 'flex-start',
    columnGap: tokens.spacingHorizontalXS,
  },
  editTextarea: {
    flex: 1,
    minWidth: 0,
  },
});

/**
 * Multiline field renderer with a "Show more" popover in read mode and
 * optional inline editing when `onSave` is provided.
 *
 * See file-level JSDoc for the full contract, empty-state, and grid-span
 * behavior.
 */
export const TextareaField: React.FC<ITextareaFieldProps> = ({
  label,
  value,
  span,
  maxLines = 3,
  className,
  onSave,
  disabled,
}) => {
  const styles = useStyles();

  // Ref to the clamped element so we can measure overflow.
  const clampedRef = useRef<HTMLDivElement | null>(null);
  const [hasOverflow, setHasOverflow] = useState(false);

  // Normalize value: null / undefined / empty-string all render em-dash.
  const isEmpty = value === null || value === undefined || value === '';
  const displayValue = isEmpty ? EMPTY_PLACEHOLDER : (value as string);
  const editable = typeof onSave === 'function' && disabled !== true;

  // Edit-mode state
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<string>(isEmpty ? '' : (value as string));
  const [saving, setSaving] = useState(false);

  /**
   * Measure whether the clamped content overflows.
   *
   * `scrollHeight` reflects the full unclamped content height; `clientHeight`
   * reflects the visible clamped box. When `scrollHeight > clientHeight`, the
   * clamp truncated content and we need to show the "Show more" affordance.
   */
  const measureOverflow = useCallback(() => {
    if (isEmpty || editing) {
      setHasOverflow(false);
      return;
    }
    const el = clampedRef.current;
    if (!el) return;
    setHasOverflow(el.scrollHeight > el.clientHeight);
  }, [isEmpty, editing]);

  useEffect(() => {
    measureOverflow();
  }, [measureOverflow, value, maxLines]);

  // Sync draft when external value changes, but never while user is editing
  // (so an external refresh doesn't drop the user's in-progress text).
  useEffect(() => {
    if (!editing) {
      setDraft(isEmpty ? '' : (value as string));
    }
  }, [value, isEmpty, editing]);

  const enterEdit = useCallback(() => {
    if (!editable) return;
    setDraft(isEmpty ? '' : (value as string));
    setEditing(true);
  }, [editable, isEmpty, value]);

  const commit = useCallback(async () => {
    if (!onSave) return;
    const original = isEmpty ? '' : (value as string);
    if (draft === original) {
      setEditing(false);
      return;
    }
    setSaving(true);
    try {
      await onSave(draft);
      setEditing(false);
    } catch {
      // Revert draft to last-known value on error; stay in edit mode so the
      // user can retry or cancel.
      setDraft(original);
    } finally {
      setSaving(false);
    }
  }, [onSave, draft, isEmpty, value]);

  const cancel = useCallback(() => {
    const original = isEmpty ? '' : (value as string);
    setDraft(original);
    setEditing(false);
  }, [isEmpty, value]);

  const onKeyDown = useCallback(
    (ev: React.KeyboardEvent<HTMLTextAreaElement>) => {
      // Ctrl+Enter (or Cmd+Enter on mac) saves. Plain Enter inserts a newline
      // — critical for multiline UX so users can compose paragraphs freely.
      if (ev.key === 'Enter' && (ev.ctrlKey || ev.metaKey)) {
        ev.preventDefault();
        void commit();
      } else if (ev.key === 'Escape') {
        ev.preventDefault();
        cancel();
      }
    },
    [commit, cancel]
  );

  // Inline style for wrapper: applies gridColumn span + the CSS var driving
  // `-webkit-line-clamp` in the Griffel class.
  const wrapperInlineStyle: React.CSSProperties = {
    gridColumn: `span ${span}`,
    // Cast to `any` — the CSS custom property is not part of the standard
    // CSSProperties typing but is honored by all major browsers + jsdom.
    ['--sprk-textarea-max-lines' as any]: String(maxLines),
  };

  return (
    <div
      className={mergeClasses(styles.wrapper, className)}
      style={wrapperInlineStyle}
      data-field-type="textarea"
      data-span={span}
      data-editable={editable ? 'true' : 'false'}
      data-editing={editing ? 'true' : 'false'}
    >
      <Label className={styles.label}>{label}</Label>
      {editing ? (
        <div className={styles.editRow}>
          <Textarea
            appearance="filled-lighter"
            size="small"
            resize="vertical"
            rows={Math.max(3, maxLines)}
            value={draft}
            onChange={(_, data) => setDraft(data.value)}
            onBlur={() => void commit()}
            onKeyDown={onKeyDown}
            disabled={saving}
            autoFocus
            className={styles.editTextarea}
            data-testid="sprk-textarea-input"
          />
          {saving ? <Spinner size="tiny" aria-label="Saving" data-testid="sprk-textarea-spinner" /> : null}
        </div>
      ) : isEmpty ? (
        <Text
          className={mergeClasses(styles.placeholder, editable ? styles.clampedEditable : undefined)}
          aria-label={`${label}: no value`}
          onClick={editable ? enterEdit : undefined}
          role={editable ? 'button' : undefined}
          tabIndex={editable ? 0 : undefined}
          onKeyDown={
            editable
              ? ev => {
                  if (ev.key === 'Enter' || ev.key === ' ') {
                    ev.preventDefault();
                    enterEdit();
                  }
                }
              : undefined
          }
        >
          {EMPTY_PLACEHOLDER}
        </Text>
      ) : (
        <>
          <div
            ref={clampedRef}
            className={mergeClasses(styles.clamped, editable ? styles.clampedEditable : undefined)}
            data-testid="sprk-textarea-clamped"
            title={displayValue}
            onClick={editable ? enterEdit : undefined}
            role={editable ? 'button' : undefined}
            tabIndex={editable ? 0 : undefined}
            onKeyDown={
              editable
                ? ev => {
                    if (ev.key === 'Enter' || ev.key === ' ') {
                      ev.preventDefault();
                      enterEdit();
                    }
                  }
                : undefined
            }
          >
            {displayValue}
          </div>
          {hasOverflow && (
            <Popover positioning="below-start" withArrow>
              <PopoverTrigger disableButtonEnhancement>
                <Link
                  as="button"
                  type="button"
                  appearance="default"
                  className={styles.showMoreLink}
                  data-testid="sprk-textarea-show-more"
                >
                  Show more
                </Link>
              </PopoverTrigger>
              <PopoverSurface
                className={styles.popoverSurface}
                data-testid="sprk-textarea-popover"
                aria-label={`${label} full text`}
              >
                <div className={styles.popoverLabel}>{label}</div>
                <div className={styles.popoverBody}>{displayValue}</div>
              </PopoverSurface>
            </Popover>
          )}
        </>
      )}
    </div>
  );
};

TextareaField.displayName = 'TextareaField';
