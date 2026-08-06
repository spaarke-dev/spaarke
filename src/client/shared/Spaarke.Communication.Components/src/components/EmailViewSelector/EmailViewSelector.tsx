/**
 * EmailViewSelector.tsx
 *
 * Thin container that renders the REUSED `DataGridViewSelector`
 * (`@spaarke/ui-components`, ADR-012 — no fork, no re-implementation) for the
 * Email surface's view picker, plus a small FR-19 error affordance when the
 * saved-view load or a per-view FetchXML run has failed.
 *
 * This component is intentionally "dumb" about data access — it does NOT call
 * `IDataverseClient` itself. The host wires it against `useEmailViews` (in
 * this same folder), e.g.:
 *
 * ```tsx
 * const { views, selectedViewId, setSelectedViewId, rows, isLoading, error } =
 *   useEmailViews(client);
 *
 * <EmailViewSelector
 *   views={views}
 *   activeViewId={selectedViewId}
 *   onViewChange={setSelectedViewId}
 *   isLoading={isLoading}
 *   error={error}
 * />
 * // `rows` feeds <EmailCardList items={...} /> in the host (task 032).
 * ```
 *
 * Fluent v9 tokens only (ADR-021) — themes correctly via the host
 * `FluentProvider` in both light and dark mode. No `as React.ComponentType`
 * cast (ADR-022/NFR-05).
 */
import * as React from 'react';
import { DataGridViewSelector, type SavedView } from '@spaarke/ui-components';
import {
  MessageBar,
  MessageBarBody,
  Spinner,
  makeStyles,
  mergeClasses,
  tokens,
  webLightTheme,
  type Theme,
} from '@fluentui/react-components';

export interface EmailViewSelectorProps {
  /** All views to choose from (already mapped from `SavedQuerySummary` by `useEmailViews`). */
  views: ReadonlyArray<SavedView>;
  /** Currently active view id. Undefined before the initial view-list load resolves. */
  activeViewId: string | undefined;
  /** Fires when the user picks a different view from the reused `ViewSelector` menu. */
  onViewChange: (viewId: string) => void;
  /** True while the view list or the active view's rows are (re)loading. */
  isLoading?: boolean;
  /** Non-null when the view-list load or a FetchXML run failed (FR-19). */
  error?: Error | null;
  /** Optional back-arrow callback — forwarded to the reused `ViewSelector`. */
  onBack?: () => void;
  /** Active Fluent v9 theme — forwarded so the reused `ViewSelector`'s portal-out menu resolves dark mode (ADR-021). */
  theme?: Theme;
  /** Additional class merged after component classes (Spaarke convention). */
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    minWidth: 0,
  },
});

/**
 * EmailViewSelector — see file-level JSDoc for the wiring contract.
 *
 * Renders an error `MessageBar` in place of the picker when `error` is set
 * (FR-19); otherwise renders the reused `DataGridViewSelector` with an
 * optional inline loading spinner alongside it.
 */
export const EmailViewSelector: React.FC<EmailViewSelectorProps> = ({
  views,
  activeViewId,
  onViewChange,
  isLoading = false,
  error = null,
  onBack,
  theme = webLightTheme,
  className,
}) => {
  const styles = useStyles();

  if (error) {
    return (
      <MessageBar intent="error" className={className}>
        <MessageBarBody>{error.message}</MessageBarBody>
      </MessageBar>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <DataGridViewSelector
        views={views}
        activeViewId={activeViewId ?? ''}
        onViewChange={onViewChange}
        onBack={onBack}
        theme={theme}
      />
      {isLoading && <Spinner size="tiny" aria-label="Loading view" />}
    </div>
  );
};

EmailViewSelector.displayName = 'EmailViewSelector';
