/**
 * TextField — single-line label + value renderer for `FieldGrid` (FR-04).
 *
 * v1.0.2 (2026-07-03): added inline-editing scope expansion per user approval.
 * When `onSave` is provided, clicking the value enters edit mode (Fluent v9 `Input`);
 * blur or Enter saves via the callback; Escape cancels. When `onSave` is omitted, the
 * renderer stays read-only (original R1 behavior — backward compatible).
 *
 * Contract (FR-04, spec §3.3):
 *  - Label above (small caption; secondary neutral color)
 *  - Value below (primary neutral color)
 *  - Value is single-line with ellipsis on overflow (read mode)
 *  - `null` / `undefined` value renders as an em-dash "—"
 *  - `required===true` renders a "*" marker beside the label
 *  - CSS `grid-column: span N` on root for FieldGrid integration
 *  - When `onSave` supplied: click-to-edit; save on blur or Enter; cancel on Escape;
 *    spinner during save; revert to previous value on error
 *
 * Per ADR-021: Fluent v9 semantic tokens only. Per ADR-022 (React 16/17 boundary):
 * plain functional component, no React-18-exclusive APIs.
 */
import * as React from 'react';
import { Input, Spinner, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * Props for {@link TextField}.
 */
export interface ITextFieldProps {
  label: string;
  value: string | number | null | undefined;
  span: 1 | 2 | 3;
  required?: boolean;
  /**
   * When provided, the value becomes click-to-edit. Callback is invoked with
   * the new string on Enter or blur (only when the value changed). Return a
   * rejected Promise to signal a save error — the field will revert to the
   * previous value. Omit to render read-only (default).
   */
  onSave?: (newValue: string) => Promise<void>;
  /**
   * When `true`, disables editing (value shown but not clickable). Only meaningful
   * when `onSave` is also provided.
   */
  disabled?: boolean;
}

export const EMPTY_VALUE_PLACEHOLDER = '—';

const useTextFieldStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },
  label: {
    // v1.0.4: match OOB Dataverse form-field label typography verified via
    // Elements inspector (2026-07-03) — 14px Segoe UI, #242424 foreground,
    // 4px bottom padding. Our previous 12px / neutralForeground2 stack read
    // too small and too gray next to the OOB fields below the header.
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    lineHeight: tokens.lineHeightBase300,
    paddingBottom: tokens.spacingVerticalXS,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  requiredMarker: {
    color: tokens.colorPaletteRedForeground1,
    marginLeft: tokens.spacingHorizontalXXS,
  },
  value: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    // v1.0.3: OOB Dataverse-style input surface — light neutral cell with
    // rounded corners so the read state visually matches the form's other
    // inputs (see user's screenshot comparing our header to the OOB fields
    // in the section below).
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    // OOB inputs are ~32px tall; reserve a single line's worth so empty
    // values keep the field cell height consistent.
    minHeight: '2em',
    display: 'flex',
    alignItems: 'center',
  },
  /**
   * Editable value hover treatment — hint clickability. Applied only when
   * onSave is provided AND not disabled.
   */
  valueEditable: {
    cursor: 'text',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  editRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
});

export const TextField: React.FC<ITextFieldProps> = ({ label, value, span, required, onSave, disabled }) => {
  const styles = useTextFieldStyles();

  const displayValue = value === null || value === undefined ? EMPTY_VALUE_PLACEHOLDER : String(value);
  const editable = typeof onSave === 'function' && disabled !== true;

  // Edit-mode state
  const [editing, setEditing] = React.useState(false);
  const [draft, setDraft] = React.useState<string>(displayValue === EMPTY_VALUE_PLACEHOLDER ? '' : displayValue);
  const [saving, setSaving] = React.useState(false);

  // Reset draft when the external value changes (so an external refresh
  // doesn't drop the user's typed value mid-edit — only sync on entry).
  React.useEffect(() => {
    if (!editing) {
      setDraft(displayValue === EMPTY_VALUE_PLACEHOLDER ? '' : displayValue);
    }
  }, [displayValue, editing]);

  const enterEdit = React.useCallback(() => {
    if (!editable) return;
    setDraft(displayValue === EMPTY_VALUE_PLACEHOLDER ? '' : displayValue);
    setEditing(true);
  }, [editable, displayValue]);

  const commit = React.useCallback(async () => {
    if (!onSave) return;
    const original = displayValue === EMPTY_VALUE_PLACEHOLDER ? '' : displayValue;
    if (draft === original) {
      setEditing(false);
      return;
    }
    setSaving(true);
    try {
      await onSave(draft);
      setEditing(false);
    } catch {
      // Revert draft to last-known value on error; stay in edit mode so
      // user can retry or cancel.
      setDraft(original);
    } finally {
      setSaving(false);
    }
  }, [onSave, draft, displayValue]);

  const cancel = React.useCallback(() => {
    const original = displayValue === EMPTY_VALUE_PLACEHOLDER ? '' : displayValue;
    setDraft(original);
    setEditing(false);
  }, [displayValue]);

  const onKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === 'Enter') {
        ev.preventDefault();
        void commit();
      } else if (ev.key === 'Escape') {
        ev.preventDefault();
        cancel();
      }
    },
    [commit, cancel]
  );

  return (
    <div
      className={mergeClasses(styles.root)}
      style={{ gridColumn: `span ${span}` }}
      data-testid="record-header-text-field"
      data-span={span}
      data-editable={editable ? 'true' : 'false'}
      data-editing={editing ? 'true' : 'false'}
    >
      <div className={styles.label} data-testid="record-header-text-field-label">
        {label}
        {required === true ? (
          <span
            className={styles.requiredMarker}
            aria-hidden="true"
            data-testid="record-header-text-field-required-marker"
          >
            {'*'}
          </span>
        ) : null}
      </div>
      {editing ? (
        <div className={styles.editRow}>
          <Input
            appearance="filled-lighter"
            size="small"
            value={draft}
            onChange={(_, data) => setDraft(data.value)}
            onBlur={() => void commit()}
            onKeyDown={onKeyDown}
            disabled={saving}
            autoFocus
            data-testid="record-header-text-field-input"
          />
          {saving ? <Spinner size="tiny" aria-label="Saving" data-testid="record-header-text-field-spinner" /> : null}
        </div>
      ) : (
        <div
          className={mergeClasses(styles.value, editable ? styles.valueEditable : undefined)}
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
          data-testid="record-header-text-field-value"
        >
          {displayValue}
        </div>
      )}
    </div>
  );
};

TextField.displayName = 'TextField';
