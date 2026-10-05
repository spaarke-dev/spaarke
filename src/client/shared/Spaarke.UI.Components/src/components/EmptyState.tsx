/**
 * EmptyState.tsx
 *
 * Shared Fluent v9 empty-state shell: a centered icon + heading + optional
 * description + optional footer, with `role="status"` / `aria-live="polite"`
 * so assistive tech announces the state change without the caller wiring it
 * up itself.
 *
 * Hoisted into `@spaarke/ui-components` (spaarke-ontology-platform-r1 task
 * 081 / C-11). Hand-rolled copies of the same shape (centered icon, semibold
 * heading, muted description, `role="status"`) were converged onto it:
 *   - DailyBriefing `EmptyState.tsx` — `size="default"`, 64px vertical
 *     padding kept via `className`
 *   - SmartTodo `SmartToDo.tsx` (`TodoEmptyState`) — `size="compact"`, its
 *     pre-081 look kept via `className` / `headingClassName` /
 *     `descriptionClassName` (smaller muted heading, no vertical band)
 *   - AI.Widgets `PlaybookGalleryWidget.tsx` — `size="compact"`, its
 *     `spacingVerticalM` gap and 240px description width kept via classes
 *   - LegalWorkspace `NotificationPanel/EmptyState.tsx` — `size="compact"`,
 *     48px vertical padding kept via `className`
 *   - LegalWorkspace `ActivityFeed/EmptyState.tsx` — `size="compact"`, 56px
 *     vertical padding kept via `className` and 300px description width via
 *     `descriptionClassName`, its "Show all updates" button in `footer`
 *
 * Deliberately NOT hoisted: the PCF `SemanticSearchControl/components/EmptyState.tsx`.
 * It is a materially different component (query echo + a `Dismiss` button +
 * suggestion text, its own `IEmptyStateProps`), not a copy of this shape.
 *
 * Sizes:
 *   - `default` — page-level empty state: heading `Text` size 500 in
 *     `colorNeutralForeground1`, description size 300, 48px vertical padding,
 *     `spacingVerticalM` gap, description max-width 320px.
 *   - `compact` — panel / column / list empty state: heading size 400 in
 *     `colorNeutralForeground2`, description size 200, `spacingHorizontalXL`
 *     padding (no 48px band), `spacingVerticalS` gap, max-width 280px.
 * Both: semibold heading, description in `colorNeutralForeground3`, icon
 * box 48px (the caller owns the icon's colour).
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Text } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: '1 1 auto',
    textAlign: 'center',
  },
  containerDefault: {
    paddingTop: '48px',
    paddingBottom: '48px',
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
  },
  containerCompact: {
    paddingTop: tokens.spacingHorizontalXL,
    paddingBottom: tokens.spacingHorizontalXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    gap: tokens.spacingVerticalS,
  },
  iconWrapper: {
    fontSize: '48px',
    lineHeight: '1',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  heading: {
    fontWeight: tokens.fontWeightSemibold,
    textAlign: 'center',
  },
  headingDefault: {
    color: tokens.colorNeutralForeground1,
  },
  headingCompact: {
    color: tokens.colorNeutralForeground2,
  },
  description: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  descriptionDefault: {
    maxWidth: '320px',
  },
  descriptionCompact: {
    maxWidth: '280px',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface EmptyStateProps {
  /**
   * Icon rendered above the heading, in a 48px box. The caller owns its own
   * color (e.g. `<CheckmarkCircleRegular className={styles.successIcon} />`)
   * — this component only sizes + centers it. Omit for a text-only empty state.
   */
  icon?: React.ReactElement;
  /** Primary heading text (e.g. "You're all caught up!", "No playbooks available"). */
  heading: string;
  /** Supporting description text shown below the heading. Omit if not needed. */
  description?: string;
  /**
   * Optional content rendered below the description — e.g. a "Last checked at …"
   * caption, or a retry / clear-filter button. Rendered as-is (not wrapped in `Text`).
   */
  footer?: React.ReactNode;
  /**
   * Accessible label for the whole region, for cases with no visible heading
   * context. Optional — `role="status"` + the visible heading are usually enough.
   */
  ariaLabel?: string;
  /**
   * `'default'` (page-level) or `'compact'` (panel / column / list). See the
   * module header for the exact typography and spacing of each.
   * @default 'default'
   */
  size?: 'compact' | 'default';
  /**
   * Extra class merged onto the container (last, so it wins) — for a caller
   * that needs one spacing difference from its size, e.g. a taller band.
   */
  className?: string;
  /** Extra class merged onto the heading `Text` (last, so it wins). */
  headingClassName?: string;
  /** Extra class merged onto the description `Text` (last, so it wins) — e.g. a different max-width. */
  descriptionClassName?: string;
}

/**
 * Generic centered empty-state shell: icon + heading + description + footer.
 * `role="status"` + `aria-live="polite"` announce the state to assistive
 * tech without the caller wiring it up.
 */
export const EmptyState: React.FC<EmptyStateProps> = ({
  icon,
  heading,
  description,
  footer,
  ariaLabel,
  size = 'default',
  className,
  headingClassName,
  descriptionClassName,
}) => {
  const styles = useStyles();
  const compact = size === 'compact';

  return (
    <div
      className={mergeClasses(styles.container, compact ? styles.containerCompact : styles.containerDefault, className)}
      role="status"
      aria-live="polite"
      aria-label={ariaLabel}
      data-size={size}
    >
      {icon && (
        <div className={styles.iconWrapper} aria-hidden="true">
          {icon}
        </div>
      )}
      <Text
        size={compact ? 400 : 500}
        className={mergeClasses(
          styles.heading,
          compact ? styles.headingCompact : styles.headingDefault,
          headingClassName
        )}
      >
        {heading}
      </Text>
      {description && (
        <Text
          size={compact ? 200 : 300}
          className={mergeClasses(
            styles.description,
            compact ? styles.descriptionCompact : styles.descriptionDefault,
            descriptionClassName
          )}
        >
          {description}
        </Text>
      )}
      {footer}
    </div>
  );
};
