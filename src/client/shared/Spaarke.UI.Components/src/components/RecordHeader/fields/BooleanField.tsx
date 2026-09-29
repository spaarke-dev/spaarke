/**
 * BooleanField — Yes/No label + Fluent `Switch` renderer for `FieldGrid` (FR-08).
 *
 * Covers Dataverse `Boolean` / `TwoOptions` attributes (e.g. `sprk_highpriority`,
 * `sprk_monitor`). Read mode shows the resolved Yes/No label (never raw
 * `true`/`false`); when `onSave` is provided, clicking the value enters edit
 * mode with a Fluent v9 `Switch`. The contract is copied verbatim from
 * {@link TextField} (FR-10) — draft/commit/cancel semantics are identical;
 * for a Switch, "typing" becomes "toggling": each toggle updates only the
 * DRAFT, and Enter/blur commits it via `onSave`.
 *
 * Contract (FR-08, FR-10, FR-11, spec §11 gate row "BooleanField"):
 *  - Label above (matches sibling renderers' typography)
 *  - Value below: `true` renders `trueLabel` (default "Yes"), `false` renders
 *    `falseLabel` (default "No") — `false` is a REAL value and never renders
 *    the empty placeholder (strict `=== ''`/`null`/`undefined` check only)
 *  - `null` / `undefined` / `''` render the em-dash "—"
 *  - CSS `grid-column: span N` self-applied on root for `FieldGrid` integration
 *  - `required===true` renders NOTHING (D-10) — the `*` marker is
 *    deliberately TextField-only; the prop exists for contract-shape parity
 *  - When `onSave` supplied: click-to-edit; toggling the Switch changes only
 *    the draft; Enter/blur commits; Escape cancels; tiny `Spinner` + disabled
 *    Switch while saving; on save rejection the draft reverts and the
 *    component STAYS in edit mode
 *
 * The display labels default to Yes/No and can be overridden by the consumer
 * with the TwoOptions metadata option labels (e.g. resolved from Dataverse
 * `GlobalOptionSetDefinitions`) — this renderer stays context-agnostic
 * (ADR-012) and never resolves metadata itself.
 *
 * Per ADR-021: Fluent v9 semantic tokens only. Per ADR-022 (React 16/17
 * boundary): plain functional component, no React-18-exclusive APIs.
 *
 * @see FR-08, FR-10, FR-11 record-header-and-notepad-r2 spec
 * @see TextField.tsx — canonical draft/commit reference implementation
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 */
import * as React from 'react';
import { Switch, Spinner, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * Props for {@link BooleanField}.
 */
export interface IBooleanFieldProps {
  label: string;
  /**
   * `true` / `false` render their resolved label. `null`, `undefined`, and
   * `''` render the em-dash empty state (the `''` member exists so callers
   * that pass every renderer the same FR-11 empty-value fixture type-check
   * without a cast).
   */
  value: boolean | '' | null | undefined;
  span: 1 | 2 | 3;
  /**
   * Label shown for `value === true`. The consumer may pass the TwoOptions
   * metadata option label instead of the default.
   *
   * @defaultValue 'Yes'
   */
  trueLabel?: string;
  /**
   * Label shown for `value === false`. The consumer may pass the TwoOptions
   * metadata option label instead of the default.
   *
   * @defaultValue 'No'
   */
  falseLabel?: string;
  /**
   * Accepted for prop-shape parity with sibling renderers (FR-10 contract)
   * but intentionally renders NOTHING — the `*` marker is TextField-only
   * (D-10).
   */
  required?: boolean;
  /**
   * When provided, the value becomes click-to-edit. Callback is invoked with
   * the new boolean when the Switch's draft differs from the committed value
   * on Enter or blur. Return a rejected Promise to signal a save error — the
   * field will revert to the previous value and stay in edit mode. Omit to
   * render read-only (default).
   */
  onSave?: (newValue: boolean) => Promise<void>;
  /**
   * When `true`, disables editing (value shown but not clickable). Only
   * meaningful when `onSave` is also provided.
   */
  disabled?: boolean;
}

export const EMPTY_VALUE_PLACEHOLDER = '—';

const useBooleanFieldStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },
  label: {
    // Matches TextField's OOB-parity typography (v1.0.4): 14px Segoe UI,
    // colorNeutralForeground1, 4px bottom padding.
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    lineHeight: tokens.lineHeightBase300,
    paddingBottom: tokens.spacingVerticalXS,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  value: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    // OOB-input-parity surface (matches TextField's v1.0.3 treatment) — light
    // neutral cell with rounded corners, ~2em min-height for a stable row.
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    minHeight: '2em',
    display: 'flex',
    alignItems: 'center',
  },
  /**
   * Editable value hover treatment — hint clickability. Applied only when
   * onSave is provided AND not disabled. Pointer cursor (not text) since the
   * click target enters a Switch, not a text input.
   */
  valueEditable: {
    cursor: 'pointer',
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

export const BooleanField: React.FC<IBooleanFieldProps> = ({
  label,
  value,
  span,
  trueLabel = 'Yes',
  falseLabel = 'No',
  onSave,
  disabled,
}) => {
  const styles = useBooleanFieldStyles();

  // Strict empty check (FR-11) — `false` is a real value, never empty.
  const isEmpty = value === null || value === undefined || value === '';
  const committedValue = isEmpty ? false : (value as boolean);
  const displayLabel = isEmpty ? EMPTY_VALUE_PLACEHOLDER : committedValue ? trueLabel : falseLabel;
  const editable = typeof onSave === 'function' && disabled !== true;

  // ══════════════════════════════════════════════════════════════════════════
  // The Switch is ALWAYS VISIBLE when editable — no click-to-edit step.
  // ══════════════════════════════════════════════════════════════════════════
  // Its five siblings hide their editor until the value is clicked, which is
  // right for text: a grid of always-live inputs reads as a form, not a header.
  // A boolean is the exception, and UAT said so — an unset flag rendered as a
  // grey cell containing an em-dash, which looks like a broken or read-only
  // field rather than something you can set.
  //
  // A Switch has no display/edit distinction to make: its position IS its
  // value, and it is self-evidently interactive in a way an em-dash is not.
  // Requiring a click to reveal it also means two clicks to flip one flag.
  // This mirrors OOB Dataverse, where a two-option field is a live toggle.
  //
  // Draft/commit semantics are UNCHANGED (FR-10): toggling stages the draft,
  // Enter or blur commits via `onSave`, Escape reverts. Only the reveal step is
  // gone — so the renderer is "permanently in edit mode" while editable, and
  // reports `data-editing="true"` to say so. The contract suite holds this
  // renderer out of the three click-to-edit assertions and applies the other
  // eighty-eight unchanged; see `rendererContract.test.tsx` `alwaysEditing`.
  const showSwitch = editable;

  const [draft, setDraft] = React.useState<boolean>(committedValue);
  const [saving, setSaving] = React.useState(false);

  // Re-sync the draft whenever the committed value changes underneath us —
  // a record navigation, a form reload, or our own successful commit. Skipped
  // mid-save so an in-flight toggle is not clobbered by a re-render. Cannot key
  // off "not editing" any more, because editable cells never leave edit mode.
  React.useEffect(() => {
    if (!saving) {
      setDraft(committedValue);
    }
  }, [committedValue, saving]);

  const commit = React.useCallback(async () => {
    if (!onSave) return;
    if (draft === committedValue) return;
    setSaving(true);
    try {
      await onSave(draft);
    } catch {
      // Revert to the last-known value on error. The Switch stays live, so the
      // user can simply toggle again to retry.
      setDraft(committedValue);
    } finally {
      setSaving(false);
    }
  }, [onSave, draft, committedValue]);

  const cancel = React.useCallback(() => {
    setDraft(committedValue);
  }, [committedValue]);

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
      data-testid="record-header-boolean-field"
      data-span={span}
      data-editable={editable ? 'true' : 'false'}
      data-editing={showSwitch ? 'true' : 'false'}
    >
      <div className={styles.label} data-testid="record-header-boolean-field-label">
        {label}
      </div>
      {showSwitch ? (
        <div className={styles.editRow}>
          <Switch
            checked={draft}
            onChange={(_, data) => setDraft(data.checked)}
            onBlur={() => void commit()}
            onKeyDown={onKeyDown}
            disabled={saving}
            labelPosition="after"
            label={draft ? trueLabel : falseLabel}
            data-testid="record-header-boolean-field-switch"
          />
          {saving ? (
            <Spinner size="tiny" aria-label="Saving" data-testid="record-header-boolean-field-spinner" />
          ) : null}
        </div>
      ) : (
        // Read-only cell — no `onSave`, or explicitly `disabled`. Keeps the
        // em-dash empty state (FR-11) that the contract suite asserts, and
        // deliberately carries no `role="button"`: there is nothing to click.
        <div className={styles.value} title={displayLabel} data-testid="record-header-boolean-field-value">
          {displayLabel}
        </div>
      )}
    </div>
  );
};

BooleanField.displayName = 'BooleanField';
