/**
 * HeaderToolbar — canonical shared toolbar for record-header surfaces.
 *
 * Renders an optional left-aligned title (ellipsis on overflow) plus a
 * right-aligned strip of icon-only Fluent v9 Buttons. Each button is
 * wrapped in a Tooltip (relationship="label") and may overlay a
 * CounterBadge when its `badge` value is a positive integer.
 *
 * FR-01 (record-header-and-notepad-r1) — this is the "one shared toolbar"
 * consumed by `RecordHeaderShell` (this project) and future record-header
 * PCFs. Do NOT extend this component with VisualHost-internal concerns
 * (drill-through, chart-def wiring) — those live in `CardChrome.tsx`
 * (VisualHost-internal per FR-VH-05).
 *
 * Standards:
 *  - ADR-012  Shared component library (context-agnostic; no PCF imports)
 *  - ADR-021  Fluent v9 semantic tokens only — zero hex / rgb / hsl literals
 *  - ADR-022  React 16/17 safe — no `use()`, no `useSyncExternalStore`,
 *             no `createRoot`, no React 18-exclusive concurrent APIs
 *
 * @see ../../../../projects/record-header-and-notepad-r1/spec.md#fr-01
 */

import * as React from 'react';
import { Button, CounterBadge, Text, Tooltip, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import type { IHeaderToolbarProps, IHeaderToolbarSlot } from './types';

// ---------------------------------------------------------------------------
// Styles — module scope per fluent-v9-component-authoring.md
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    width: '100%',
    minWidth: 0,
    minHeight: '24px',
    boxSizing: 'border-box',
    ...shorthands.gap(tokens.spacingHorizontalS),
    // Neutral surface + text so the toolbar inherits page theming and
    // adapts to light / dark / high-contrast via semantic tokens.
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },

  title: {
    // Truncate long titles instead of pushing icons off-screen.
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    flexGrow: 1,
    minWidth: 0,
  },

  // Invisible spacer used when no title is provided, so the icon strip
  // still hugs the trailing edge of the toolbar (matches CardChrome behavior).
  titleSpacer: {
    flexGrow: 1,
    minWidth: 0,
  },

  iconSlots: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },

  // Positioned wrapper so CounterBadge can overlay the top-right corner
  // of its icon button without shifting layout. Fluent v9 badges use a
  // small filled treatment (color="brand"), so overlay honours theming.
  slotAnchor: {
    position: 'relative',
    display: 'inline-flex',
  },

  badge: {
    position: 'absolute',
    top: 0,
    // rtl-safe: inset-inline-end keeps the badge on the visual trailing
    // edge under both LTR and RTL layouts.
    insetInlineEnd: 0,
    // Nudge the badge outward by ~half its diameter so it visually sits on
    // the corner rather than inside the button.
    transform: 'translate(35%, -35%)',
    // Ensure the badge sits above the button surface.
    zIndex: 1,
    // Non-interactive — clicks pass through to the button behind it.
    pointerEvents: 'none',
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * FR-01: badge is rendered ONLY when the value is a positive integer.
 * `undefined`, `null`, `0`, negatives, and non-finite values suppress it.
 */
function shouldRenderBadge(value: number | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Canonical shared toolbar (FR-01). See file-level comment for the binding
 * rules. The component is a plain functional component to preserve React
 * 16/17 compatibility per ADR-022 (no React 18-exclusive APIs).
 */
export const HeaderToolbar: React.FC<IHeaderToolbarProps> = ({ title, iconSlots }) => {
  const styles = useStyles();
  const hasTitle = typeof title === 'string' && title.trim().length > 0;

  return (
    <div className={styles.root} data-testid="header-toolbar">
      {hasTitle ? (
        <Text size={300} className={styles.title} title={title} aria-label={title} data-testid="header-toolbar-title">
          {title}
        </Text>
      ) : (
        // Spacer keeps icons right-aligned when no title is provided.
        <span aria-hidden={true} className={styles.titleSpacer} />
      )}

      <div className={styles.iconSlots} data-testid="header-toolbar-icons">
        {iconSlots.map((slot: IHeaderToolbarSlot) => {
          const showBadge = shouldRenderBadge(slot.badge);

          return (
            // Tooltip with relationship="label" wires aria-label to the
            // icon-only Button per Fluent v9 accessibility guidance.
            <Tooltip
              key={slot.key}
              content={slot.tooltip}
              relationship="label"
              // Skip Fluent's default show-delay for snappier hover on
              // dense toolbars — matches VisualHost CardChrome UX.
              withArrow={false}
            >
              <span className={styles.slotAnchor} data-testid={`header-toolbar-slot-${slot.key}`}>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={slot.icon}
                  onClick={slot.onClick}
                  disabled={slot.disabled === true}
                  aria-label={slot.tooltip}
                  data-testid={`header-toolbar-button-${slot.key}`}
                />
                {showBadge ? (
                  <CounterBadge
                    className={styles.badge}
                    count={slot.badge as number}
                    color="brand"
                    size="small"
                    appearance="filled"
                    data-testid={`header-toolbar-badge-${slot.key}`}
                  />
                ) : null}
              </span>
            </Tooltip>
          );
        })}
      </div>
    </div>
  );
};
