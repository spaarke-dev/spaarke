/**
 * EmptyState.tsx
 *
 * Shared Fluent v9 empty-state shell: a centered icon + heading + optional
 * description + optional footer, with `role="status"` / `aria-live="polite"`
 * so assistive tech announces the state change without the caller wiring it
 * up itself.
 *
 * Hoisted into `@spaarke/ui-components` (spaarke-ontology-platform-r1 task
 * 081 / C-11) — three hand-rolled copies carried the identical shape
 * (centered icon, semibold heading, muted description, `role="status"`)
 * with only their icon/text/footer varying:
 *   - `Spaarke.DailyBriefing.Components/src/components/EmptyState.tsx`
 *     ("You're all caught up!" + a "Last checked at …" caption footer)
 *   - `Spaarke.SmartTodo.Components/src/components/SmartToDo/SmartToDo.tsx`
 *     (local `TodoEmptyState` — heading + description, no icon)
 *   - `Spaarke.AI.Widgets/src/widgets/context/PlaybookGalleryWidget.tsx`
 *     (local `PlaybookGalleryEmptyState` — icon + title + body)
 * A fourth copy found during this task's recount (not in the original
 * audit's named three) — `src/solutions/LegalWorkspace/.../NotificationPanel/EmptyState.tsx`
 * — was converged onto this component too, since it is the same shape.
 *
 * Deliberately NOT hoisted: the PCF `SemanticSearchControl/components/EmptyState.tsx`.
 * It is a materially different component (query echo + a `Dismiss` button +
 * suggestion text, its own `IEmptyStateProps`) living across the PCF
 * React-16 platform-library boundary (ADR-022) — not a copy of this shape.
 *
 * Each caller supplies its own icon element (so it keeps its own color/size
 * choice verbatim) and its own heading/description/footer text — this
 * component only owns the shared layout + accessibility contract.
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: '48px',
    paddingBottom: '48px',
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
    flex: '1 1 auto',
  },
  iconWrapper: {
    fontSize: '48px',
    lineHeight: '1',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  heading: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    textAlign: 'center',
  },
  description: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    maxWidth: '320px',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface EmptyStateProps {
  /**
   * Icon rendered above the heading, at 48px. The caller owns its own color
   * (e.g. `<CheckmarkCircleRegular style={{ color: tokens.colorPaletteGreenForeground1 }} />`)
   * — this component only sizes + centers it. Omit for a text-only empty state.
   */
  icon?: React.ReactElement;
  /** Primary heading text (e.g. "You're all caught up!", "No playbooks available"). */
  heading: string;
  /** Supporting description text shown below the heading. Omit if not needed. */
  description?: string;
  /**
   * Optional content rendered below the description — e.g. a "Last checked at …"
   * caption, or a retry button. Rendered as-is (not wrapped in `Text`).
   */
  footer?: React.ReactNode;
  /**
   * Accessible label for the whole region, for cases with no visible heading
   * context (mirrors the PlaybookGallery `aria-label="No playbooks available"`
   * precedent). Optional — `role="status"` + the visible heading are usually
   * enough.
   */
  ariaLabel?: string;
}

/**
 * Generic centered empty-state shell: icon + heading + description + footer.
 * `role="status"` + `aria-live="polite"` announce the state to assistive
 * tech without the caller wiring it up.
 */
export const EmptyState: React.FC<EmptyStateProps> = ({ icon, heading, description, footer, ariaLabel }) => {
  const styles = useStyles();

  return (
    <div className={styles.container} role="status" aria-live="polite" aria-label={ariaLabel}>
      {icon && <div className={styles.iconWrapper} aria-hidden="true">{icon}</div>}
      <Text size={500} className={styles.heading}>
        {heading}
      </Text>
      {description && (
        <Text size={300} className={styles.description}>
          {description}
        </Text>
      )}
      {footer}
    </div>
  );
};
