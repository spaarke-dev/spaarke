/**
 * LookupField — record-header field renderer for Xrm-style lookup values
 * (FR-04; editable mode FR-15/FR-15a, record-header-and-notepad-r2 task 023).
 *
 * READ-ONLY MODE (default, unchanged from R1): renders a Fluent v9 field cell
 * with a label above and a clickable value row consisting of an optional
 * 16x16 entity icon prefix + a `Link` displaying the lookup's display name.
 * On click, the field opens the lookup target via
 * `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId })`
 * — the exact contract in FR-04 / spec §3.3.
 *
 * EDITABLE MODE (FR-15/FR-15a — opt in via `targets` + `onSave`): clicking the
 * value (populated OR empty) opens the OOB `Xrm.Utility.lookupObjects` picker
 * — the same native Records/Recent/Advanced/"+ New" dialog every Dataverse
 * lookup uses — instead of navigating. This deliberately replaces R1's
 * hand-rolled OData `contains()` search-as-you-type builder rather than
 * hoisting it: `lookupObjects` already returns `{ id, name, entityType }`,
 * which IS the exact `Xrm.Page` form-buffer `setValue([{ id, name, entityType }])`
 * payload `useRecordHeaderFields.saveLookup` needs — no translation layer.
 * Only `targets[0]` is used (multi-target lookups are out of scope; see the
 * task's `<escalation>` trigger). Cancelling the picker or resolving with zero
 * results is a no-op: `onSave` is NEVER invoked with `null` from this
 * component — clearing a lookup is not part of this contract.
 *
 * Xrm access uses `getXrm()` from `../../../utils/xrmContext` — the same
 * cross-frame walker used by `useRecordFieldValues` (task 009), so this
 * renderer works from both PCF surfaces (window.Xrm) and Custom Pages
 * (window.parent.Xrm). If Xrm (or `Xrm.Utility.lookupObjects`) is unavailable
 * (test environment, non-Xrm host, older API surface), the click is a silent
 * no-op — the render still succeeds so consumers can unit-test compositions
 * without stubbing the full SDK.
 *
 * Value shape (`ILookupFieldValue`) intentionally mirrors the Xrm.LookupValue
 * projection used by `useRecordFieldValues` for lookup attributes:
 *   { id, name, entityType, iconUrl? }.
 *
 * Null / undefined / empty values render a hyphen "—" (matching TextField's
 * empty-state convention) so consumers get a consistent empty-cell look
 * across the FieldGrid regardless of which renderer occupies the cell. In
 * editable mode the empty hyphen is ALSO clickable, so a field with no value
 * yet can be populated for the first time.
 *
 * Layout:
 *  - `gridColumn: span N` applied by THIS component per FR-03 contract
 *    (FieldGrid is renderer-agnostic; the child owns its span) — composing it
 *    editable inside `FieldGrid` needs no wrapper `div` from the consumer.
 *  - Label typography matches TextField (small, secondary-foreground caption).
 *  - Read-only value row uses currentColor icon + Fluent Link text (semantic
 *    link color). Editable value row uses the same icon + plain text (it is
 *    an action trigger, not a navigation link) with a hover affordance.
 *
 * Standards:
 *  - ADR-021 Fluent v9 semantic tokens only — zero hex / rgb / hsl literals
 *  - ADR-022 React 16/17 safe — no `use()`, no `useSyncExternalStore`,
 *            no `createRoot`, no React 18-exclusive concurrent APIs
 *  - NFR-05  No `@spaarke/auth` imports (host-context surface only)
 *  - NFR-07  No BFF calls
 *
 * @see FR-04 record-header-and-notepad-r1 spec
 * @see FR-15, FR-15a record-header-and-notepad-r2 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 *
 * @example Read-only (unchanged)
 * ```tsx
 * <FieldGrid columns={3}>
 *   <LookupField
 *     span={1}
 *     label="Matter Type"
 *     value={{ id: "…guid…", name: "Litigation", entityType: "sprk_mattertype" }}
 *   />
 * </FieldGrid>
 * ```
 *
 * @example Editable — wired to task 022's `useRecordHeaderFields.saveLookup`
 * ```tsx
 * const h = useRecordHeaderFields({ entity, recordId, fields });
 * // `targets` resolved from Dataverse metadata (task 020's
 * // `EntityAttributeMetadata.targets`) — NEVER hard-coded.
 * <LookupField
 *   span={1}
 *   label="Matter Type"
 *   value={h.displayLookup('sprk_mattertype')}
 *   targets={targets}
 *   onSave={item => item && h.saveLookup('sprk_mattertype', item, item.entityType)}
 * />
 * ```
 */

import * as React from 'react';
import { Link, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { Link24Regular } from '@fluentui/react-icons';

import { getXrm } from '../../../utils/xrmContext';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Shape of a lookup field value — mirrors the Xrm.LookupValue projection
 * used by `useRecordFieldValues` for lookup attributes.
 */
export interface ILookupFieldValue {
  /** GUID of the target record (no braces). */
  id: string;
  /** Display name (primary attribute) of the target record. */
  name: string;
  /** Logical name of the target entity (e.g. `sprk_matter`). */
  entityType: string;
  /** Optional pre-resolved 16x16 entity icon URL; falls back to a generic icon. */
  iconUrl?: string;
}

/**
 * Props for {@link LookupField}.
 */
export interface ILookupFieldProps {
  /** Field caption shown above the value row. */
  label: string;
  /** Lookup value; null / undefined renders a hyphen "—". */
  value: ILookupFieldValue | null | undefined;
  /**
   * Number of grid columns this field cell should span (1..3).
   * Applied via inline `gridColumn: span N` per FieldGrid FR-03 contract.
   */
  span: 1 | 2 | 3;
  /**
   * Target table logical name(s) for this lookup, resolved by the caller
   * from Dataverse metadata (task 020's `EntityAttributeMetadata.targets`) —
   * NEVER hard-code a target entity name here (the naming convention across
   * taxonomy tables is non-uniform). Only `targets[0]` is used — see the
   * task's escalation trigger for the multi-target case. Required
   * (non-empty) together with `onSave` for the field to become editable;
   * omit to keep the field permanently read-only (default, backward
   * compatible).
   */
  targets?: string[];
  /**
   * When provided together with a non-empty `targets` array, the value
   * (populated OR empty) becomes click-to-open the OOB
   * `Xrm.Utility.lookupObjects` picker. Invoked with the selected record's
   * `{ id, name, entityType }` — the id is brace-stripped and lowercased —
   * which IS the exact `Xrm.Page` form-buffer `setValue([{ id, name,
   * entityType }])` payload shape (see task 022's
   * `useRecordHeaderFields.saveLookup`). Never called with `null` from THIS
   * component: cancelling the picker or an empty result is a no-op — no
   * pending state changes, `onSave` is simply not invoked. The `| null` arm
   * exists only for prop-shape symmetry with a future explicit clear
   * affordance (out of this task's scope). Omit to render read-only
   * (default, backward compatible).
   */
  onSave?: (item: ILookupFieldValue | null) => void | Promise<void>;
  /**
   * When `true`, disables editing (value shown but not clickable, and no
   * picker opens). Only meaningful when `onSave` and `targets` are also
   * provided.
   */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Styles — semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },

  label: {
    // v1.0.4: match OOB Dataverse form-field label typography — 14px / #242424
    // regular weight / 4px bottom padding. Consistent with sibling TextField
    // and TextareaField after the same treatment.
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    lineHeight: tokens.lineHeightBase300,
    paddingBottom: tokens.spacingVerticalXS,
    // Truncate long labels instead of pushing the value row down.
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },

  valueRow: {
    display: 'flex',
    alignItems: 'center',
    minWidth: 0,
    columnGap: tokens.spacingHorizontalXS,
    // v1.0.3: OOB Dataverse-style input surface so lookup cells match the
    // TextField / TextareaField visual footprint. Light neutral background,
    // rounded corners, single-line height.
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    minHeight: '2em',
  },

  icon: {
    display: 'inline-flex',
    alignItems: 'center',
    flexShrink: 0,
    width: '16px',
    height: '16px',
    // currentColor so SVG icons follow the surrounding text color and adapt
    // to light / dark / high-contrast themes (ADR-021).
    color: tokens.colorNeutralForeground2,
  },

  iconImg: {
    display: 'block',
    width: '16px',
    height: '16px',
  },

  linkText: {
    // Allow the link to shrink and ellipsize within its cell rather than
    // overflowing the FieldGrid column.
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  empty: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    // Reserve the same footprint as the populated valueRow so the FieldGrid
    // stays aligned between empty and populated states.
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

  // FR-15/FR-15a editable affordance — hint clickability on the picker
  // trigger (populated or empty). Mirrors TextField's / OptionSetField's
  // `valueEditable` hover treatment (ADR-021 semantic tokens only).
  valueEditable: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },

  // Plain (non-Link) text used for the editable populated value — clicking
  // opens the picker rather than navigating, so this is an action trigger,
  // not a hyperlink.
  editableValueText: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Type guard — treats null / undefined / missing id / missing name as "empty".
 * Empty values render the hyphen "—" placeholder per FR-04 conventions.
 */
function isEmpty(value: ILookupFieldValue | null | undefined): boolean {
  if (value === null || value === undefined) {
    return true;
  }
  if (!value.id || !value.name) {
    return true;
  }
  return false;
}

/**
 * Clickable field renderer for Xrm-style lookup values.
 *
 * See file-level JSDoc for full contract, layout, empty-state, and editable
 * (FR-15/FR-15a) behavior.
 */
export const LookupField: React.FC<ILookupFieldProps> = ({ label, value, span, targets, onSave, disabled }) => {
  const styles = useStyles();

  // Spanning is applied inline per FR-03 contract (FieldGrid does not touch
  // gridColumn on its children — the field renderer owns it).
  const gridColumnStyle: React.CSSProperties = { gridColumn: `span ${span}` };

  const empty = isEmpty(value);

  const hasTargets = Array.isArray(targets) && targets.length > 0;
  const editable = typeof onSave === 'function' && disabled !== true && hasTargets;

  // Guards against a double-invocation of `lookupObjects` (e.g. a rapid
  // double-click) spawning two native picker dialogs while the first is
  // still awaiting the user. A ref (not state) — this is a re-entrancy
  // guard, not something the render output ever needs to reflect.
  const openingRef = React.useRef(false);

  // ── FR-15/FR-15a: open the OOB Xrm.Utility.lookupObjects picker ──────────
  const openPicker = React.useCallback(async (): Promise<void> => {
    if (!editable || openingRef.current) {
      return;
    }
    const target = targets![0];

    openingRef.current = true;
    try {
      const xrm = getXrm();
      const lookupObjects = xrm?.Utility?.lookupObjects;
      if (typeof lookupObjects !== 'function') {
        // Host doesn't expose the picker (test env, unsupported surface) —
        // graceful no-op per the component's "never throws on click" contract.
        return;
      }

      const results = await lookupObjects({
        entityTypes: [target],
        defaultEntityType: target,
        allowMultiSelect: false,
      });

      if (!results || results.length === 0) {
        // Cancelled or resolved empty — no-op. `onSave` is intentionally NOT
        // called with `null` here; clearing is not staged by cancel.
        return;
      }

      const picked = results[0];
      // Normalize the same way CommunicationActionsApp.tsx:420 does, so
      // pending values compare consistently with useRecordFieldValues
      // projections (brace-stripped, lowercased GUID).
      const id = String(picked.id).replace(/[{}]/g, '').toLowerCase();

      await onSave!({ id, name: picked.name, entityType: picked.entityType });
    } catch {
      // Xrm surfaces its own error UX for picker/save failures; swallow here
      // to preserve the "no throw" contract (mirrors the read-only path).
    } finally {
      openingRef.current = false;
    }
  }, [editable, targets, onSave]);

  // ── Read-only mode (unchanged): click navigates via Xrm.Navigation ───────
  const handleNavigateClick = React.useCallback(
    (event: React.MouseEvent<HTMLElement>) => {
      // Prevent any default anchor navigation — we control routing via Xrm.
      event.preventDefault();

      if (empty) {
        return;
      }

      // `value` is narrowed by `empty` check above.
      const lookup = value as ILookupFieldValue;

      // Xrm may be unavailable in test / non-Xrm hosts — swallow gracefully
      // rather than throwing (FR-04 acceptance criterion + task constraint).
      try {
        const xrm = getXrm();
        if (xrm && xrm.Navigation && typeof xrm.Navigation.navigateTo === 'function') {
          // Fire-and-forget: navigateTo returns a Promise; consumers of
          // LookupField don't observe its resolution here. Swallow rejections
          // to preserve the "no throw" contract when navigation fails at the
          // Xrm layer (e.g., missing privileges surface via Xrm's own dialog).
          void xrm.Navigation.navigateTo({
            pageType: 'entityrecord',
            entityName: lookup.entityType,
            entityId: lookup.id,
          }).catch(() => {
            /* Xrm surfaces its own error UX; do not re-throw. */
          });
        }
      } catch {
        // getXrm / navigateTo threw synchronously — treat as no-op.
      }
    },
    [empty, value]
  );

  // ── Unified click handler: editable opens the picker, read-only navigates ─
  const handleClick = React.useCallback(
    (event: React.MouseEvent<HTMLElement>) => {
      event.preventDefault();
      if (editable) {
        void openPicker();
        return;
      }
      handleNavigateClick(event);
    },
    [editable, openPicker, handleNavigateClick]
  );

  const handleKeyDown = React.useCallback(
    (event: React.KeyboardEvent<HTMLElement>) => {
      if (!editable) {
        return;
      }
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        void openPicker();
      }
    },
    [editable, openPicker]
  );

  return (
    <div
      className={styles.root}
      style={gridColumnStyle}
      data-field-type="lookup"
      data-testid="record-header-lookup-field"
      data-editable={editable ? 'true' : 'false'}
    >
      <Text as="span" className={styles.label} title={label}>
        {label}
      </Text>

      {empty ? (
        editable ? (
          <span
            className={mergeClasses(styles.valueRow, styles.empty, styles.valueEditable)}
            aria-label={`${label}: empty`}
            role="button"
            tabIndex={0}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
            data-testid="record-header-lookup-field-value"
          >
            —
          </span>
        ) : (
          <span
            className={mergeClasses(styles.valueRow, styles.empty)}
            aria-label={`${label}: empty`}
            data-testid="record-header-lookup-field-value"
          >
            —
          </span>
        )
      ) : (
        <span className={styles.valueRow}>
          <span className={styles.icon} aria-hidden="true">
            {value && value.iconUrl ? (
              <img
                src={value.iconUrl}
                alt=""
                className={styles.iconImg}
                // Presentation-only image — decorative alongside the link text.
                aria-hidden="true"
              />
            ) : (
              // Generic fallback per spec §Assumptions U-02 — currentColor SVG.
              <Link24Regular width={16} height={16} />
            )}
          </span>
          {editable ? (
            // Editable: an action trigger (opens the picker), not a
            // navigation link — plain text, not a Fluent `Link`.
            <span
              className={mergeClasses(styles.editableValueText, styles.valueEditable)}
              title={(value as ILookupFieldValue).name}
              role="button"
              tabIndex={0}
              onClick={handleClick}
              onKeyDown={handleKeyDown}
              data-testid="record-header-lookup-field-value"
            >
              {(value as ILookupFieldValue).name}
            </span>
          ) : (
            <Link
              as="a"
              href="#"
              onClick={handleClick}
              className={styles.linkText}
              title={(value as ILookupFieldValue).name}
              data-testid="record-header-lookup-field-value"
            >
              {(value as ILookupFieldValue).name}
            </Link>
          )}
        </span>
      )}
    </div>
  );
};

LookupField.displayName = 'LookupField';
