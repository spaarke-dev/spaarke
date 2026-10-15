/**
 * EmailTrackingPanel.tsx
 *
 * Reading-pane TRACKING sub-view (email-communication-solution-r5 task 035,
 * FR-14). Thin composition over the entity-agnostic `TrackingFieldTrio` core
 * lifted to `@spaarke/ui-components` in task 023 — this file adds a section
 * header + inline error surface, nothing else. It bakes in NO
 * `sprk_communication`-specific Dataverse field names (task 023's design
 * decision applies here verbatim): the host (task 040 assembly) supplies
 * current values and the write callbacks — exactly the same "values in,
 * onChange callbacks out"
 * contract `TrackingFieldTrio` itself already has. This keeps the actual
 * Dataverse field mapping in exactly one place in the tree (the eventual
 * host wiring), per FR-14's entity-agnostic requirement.
 *
 * No Access Permission (unified-access-control-r2 task 138, owner Q6; task 173,
 * owner round 81): a communication's access comes from its parent. Its own
 * `sprk_accesspermission` is a display copy of the parent's value that only the
 * BFF writes and enforcement never reads, so the trio renders WITHOUT the pill
 * (`showAccessPermission={false}`; no TrackingFieldTrio pill on Communication).
 *
 * Fluent v9 tokens only (ADR-021, dark-mode correct). No `as
 * React.ComponentType` cast (NFR-05) — `TrackingFieldTrio` is consumed via
 * the `@spaarke/ui-components` barrel import (React 19 code-page side; no
 * deep-dist-path or cast needed there per task 023's notes).
 */
import * as React from 'react';
import { makeStyles, tokens, mergeClasses, Text, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { TrackingFieldTrio } from '@spaarke/ui-components';
import type { EmailTrackingPanelProps } from './EmailAssociationsAndTracking.types';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  rootCompact: { gap: 0 },
  header: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  disabled: { opacity: 0.6, pointerEvents: 'none' },
});

/** The trio's pill props are required by its contract; with the pill hidden they carry nothing (task 138). */
const NO_ACCESS_PERMISSION_OPTIONS: never[] = [];
const IGNORE_ACCESS_PERMISSION_CHANGE = (): void => undefined;

export function EmailTrackingPanel(props: EmailTrackingPanelProps): React.ReactElement {
  const {
    monitor,
    highPriority,
    onMonitorChange,
    onHighPriorityChange,
    monitorLabel = 'Monitor',
    highPriorityLabel = 'High priority',
    readOnly = false,
    compact = false,
  } = props;
  const s = useStyles();
  const [error, setError] = React.useState<string | null>(null);

  const runChange = React.useCallback(async (fn: () => void | Promise<void>): Promise<void> => {
    setError(null);
    try {
      await fn();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the change.');
    }
  }, []);

  const handleMonitorChange = React.useCallback(
    (value: boolean) => {
      void runChange(() => onMonitorChange(value));
    },
    [runChange, onMonitorChange]
  );
  const handleHighPriorityChange = React.useCallback(
    (value: boolean) => {
      void runChange(() => onHighPriorityChange(value));
    },
    [runChange, onHighPriorityChange]
  );

  return (
    <div className={mergeClasses(s.root, compact && s.rootCompact)} data-testid="email-tracking-panel">
      {!compact && <Text className={s.header}>Tracking</Text>}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      <div className={readOnly ? s.disabled : undefined} aria-disabled={readOnly || undefined}>
        <TrackingFieldTrio
          monitor={monitor}
          highPriority={highPriority}
          showTitle={!compact}
          monitorLabel={monitorLabel}
          highPriorityLabel={highPriorityLabel}
          onMonitorChange={handleMonitorChange}
          onHighPriorityChange={handleHighPriorityChange}
          disabled={readOnly}
          // Task 138 (owner Q6): a communication has no Access Permission of its own.
          showAccessPermission={false}
          accessPermission={null}
          accessPermissionOptions={NO_ACCESS_PERMISSION_OPTIONS}
          accessPermissionLabel=""
          onAccessPermissionChange={IGNORE_ACCESS_PERMISSION_CHANGE}
        />
      </div>
    </div>
  );
}
