/**
 * LookupField — record-header field renderer for Xrm-style lookup values (FR-04).
 *
 * Renders a Fluent v9 field cell with a label above and a clickable value row
 * consisting of an optional 16x16 entity icon prefix + a `Link` displaying the
 * lookup's display name. On click, the field opens the lookup target via
 * `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId })`
 * — the exact contract in FR-04 / spec §3.3.
 *
 * Xrm access uses `getXrm()` from `../../../utils/xrmContext` — the same
 * cross-frame walker used by `useRecordFieldValues` (task 009), so this
 * renderer works from both PCF surfaces (window.Xrm) and Custom Pages
 * (window.parent.Xrm). If Xrm is unavailable (test environment, non-Xrm host),
 * the click is a silent no-op — the render still succeeds so consumers can
 * unit-test compositions without stubbing the full SDK.
 *
 * Value shape (`ILookupFieldValue`) intentionally mirrors the Xrm.LookupValue
 * projection used by `useRecordFieldValues` for lookup attributes:
 *   { id, name, entityType, iconUrl? }.
 *
 * Null / undefined / empty values render a hyphen "—" (matching TextField's
 * empty-state convention) so consumers get a consistent empty-cell look
 * across the FieldGrid regardless of which renderer occupies the cell.
 *
 * Layout:
 *  - `gridColumn: span N` applied by THIS component per FR-03 contract
 *    (FieldGrid is renderer-agnostic; the child owns its span).
 *  - Label typography matches TextField (small, secondary-foreground caption).
 *  - Value row uses currentColor icon + Fluent Link text (semantic link color).
 *
 * Standards:
 *  - ADR-021 Fluent v9 semantic tokens only — zero hex / rgb / hsl literals
 *  - ADR-022 React 16/17 safe — no `use()`, no `useSyncExternalStore`,
 *            no `createRoot`, no React 18-exclusive concurrent APIs
 *  - NFR-05  No `@spaarke/auth` imports (host-context surface only)
 *  - NFR-07  No BFF calls
 *
 * @see FR-04 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 *
 * @example
 * ```tsx
 * <FieldGrid columns={3}>
 *   <LookupField
 *     span={1}
 *     label="Matter Type"
 *     value={{ id: "…guid…", name: "Litigation", entityType: "sprk_mattertype" }}
 *   />
 * </FieldGrid>
 * ```
 */

import * as React from 'react';
import {
  Link,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
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
    // Match sibling TextField typography for a consistent label across the
    // FieldGrid — small caption, semibold, secondary neutral color (ADR-021).
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase200,
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
 * See file-level JSDoc for full contract, layout, and empty-state behavior.
 */
export const LookupField: React.FC<ILookupFieldProps> = ({ label, value, span }) => {
  const styles = useStyles();

  // Spanning is applied inline per FR-03 contract (FieldGrid does not touch
  // gridColumn on its children — the field renderer owns it).
  const gridColumnStyle: React.CSSProperties = { gridColumn: `span ${span}` };

  const empty = isEmpty(value);

  const handleClick = React.useCallback(
    (event: React.MouseEvent<HTMLAnchorElement>) => {
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
    [empty, value],
  );

  return (
    <div className={styles.root} style={gridColumnStyle} data-field-type="lookup">
      <Text as="span" className={styles.label} title={label}>
        {label}
      </Text>

      {empty ? (
        <span className={mergeClasses(styles.valueRow, styles.empty)} aria-label={`${label}: empty`}>
          —
        </span>
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
          <Link
            as="a"
            href="#"
            onClick={handleClick}
            className={styles.linkText}
            title={(value as ILookupFieldValue).name}
          >
            {(value as ILookupFieldValue).name}
          </Link>
        </span>
      )}
    </div>
  );
};

LookupField.displayName = 'LookupField';
