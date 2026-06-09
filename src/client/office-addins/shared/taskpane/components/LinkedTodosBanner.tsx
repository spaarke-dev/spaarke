import React from 'react';
import {
  Link,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';

/**
 * LinkedTodosBanner — pinned indicator at the top of the Outlook add-in taskpane
 * showing how many Spaarke `sprk_todo` records are already linked to the current
 * email via `sprk_regardingcommunication`.
 *
 * Per smart-todo-decoupling-r3 FR-28 / A-1:
 * - Hidden when count is 0 (and when there is no error / loading state to display).
 * - When >=1, renders "This email has N Spaarke to-do(s)" with an optional
 *   "View list" link that delegates to the host (`onViewList`).
 * - Loading + error states surface as their own MessageBar intents.
 *
 * Per NFR-01: Fluent v9 + semantic tokens + Griffel `makeStyles`. No inline styles.
 * Per NFR-10: aria-live region on the banner, accessible link semantics.
 *
 * @see projects/smart-todo-decoupling-r3/design.md §7.2
 */

const useStyles = makeStyles({
  root: {
    // Standard banner gap above content; uses semantic spacing token.
    marginBottom: tokens.spacingVerticalS,
  },
  messageBar: {
    // Ensure long subject lines / counts don't get clipped on narrow taskpanes.
    width: '100%',
  },
  loadingRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
});

/**
 * Props for the LinkedTodosBanner component.
 */
export interface LinkedTodosBannerProps {
  /**
   * Number of Spaarke to-dos linked to the current email. When 0, the banner
   * is suppressed unless `isLoading` or `error` is set.
   */
  count: number;
  /**
   * True while the indicator query is in flight. Renders a low-key
   * informational MessageBar with a Spinner so users see something is happening.
   */
  isLoading?: boolean;
  /**
   * Error message to display. When set (and not loading), an error MessageBar
   * is rendered in place of the success banner.
   */
  error?: string | null;
  /**
   * Optional callback invoked when the user clicks "View list". When omitted,
   * no link is rendered. The host (App.tsx) is responsible for navigating to
   * the SmartTodo Code Page filtered by the communication id — keeps URLs out
   * of this product-portable component.
   */
  onViewList?: () => void;
  /**
   * Optional className for host-side styling overrides.
   * Placed AFTER component classes in `mergeClasses` per Spaarke convention.
   */
  className?: string;
}

/**
 * Pluralize "to-do" for the banner title.
 */
function formatTitle(count: number): string {
  return count === 1 ? 'This email has 1 Spaarke to-do' : `This email has ${count} Spaarke to-dos`;
}

export const LinkedTodosBanner: React.FC<LinkedTodosBannerProps> = ({
  count,
  isLoading = false,
  error = null,
  onViewList,
  className,
}) => {
  const styles = useStyles();

  // Loading state — only visible during the initial round-trip (cached hits
  // skip this entirely per useLinkedTodosForCommunication.ts).
  if (isLoading) {
    return (
      <div
        className={mergeClasses(styles.root, className)}
        // Politely announce loading without interrupting screen reader users.
        aria-live="polite"
        aria-busy="true"
      >
        <MessageBar
          className={styles.messageBar}
          intent="info"
          role="status"
          aria-label="Checking for linked Spaarke to-dos"
        >
          <MessageBarBody>
            <span className={styles.loadingRow}>
              <Spinner size="tiny" aria-hidden="true" />
              Checking for linked Spaarke to-dos…
            </span>
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  // Error state — non-blocking; the rest of the taskpane is still usable.
  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)} aria-live="polite">
        <MessageBar
          className={styles.messageBar}
          intent="error"
          role="alert"
          aria-label="Failed to load linked Spaarke to-dos"
        >
          <MessageBarBody>
            <MessageBarTitle>Couldn&apos;t load linked to-dos</MessageBarTitle>
            {error}
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  // Suppress banner entirely when nothing is linked (FR-28 acceptance criterion).
  if (count <= 0) {
    return null;
  }

  const title = formatTitle(count);

  return (
    <div className={mergeClasses(styles.root, className)} aria-live="polite">
      <MessageBar
        className={styles.messageBar}
        intent="info"
        role="status"
        // Pair the visible title with an aria-label so screen-reader users get
        // the full sentence even if their reader skips MessageBarTitle.
        aria-label={title}
      >
        <MessageBarBody>
          <MessageBarTitle>{title}</MessageBarTitle>
        </MessageBarBody>
        {onViewList ? (
          <MessageBarActions>
            <Link
              as="button"
              appearance="default"
              onClick={onViewList}
              // Explicit aria-label so SR users hear "View list of N Spaarke to-dos"
              // rather than the bare word "View list".
              aria-label={`View list of ${count} Spaarke to-do${count === 1 ? '' : 's'} linked to this email`}
            >
              View list
            </Link>
          </MessageBarActions>
        ) : null}
      </MessageBar>
    </div>
  );
};
