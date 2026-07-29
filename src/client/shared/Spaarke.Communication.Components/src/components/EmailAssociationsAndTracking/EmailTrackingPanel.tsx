/**
 * EmailTrackingPanel.tsx
 *
 * Reading-pane TRACKING sub-view (email-communication-solution-r5 task 035,
 * FR-14). Thin composition over the entity-agnostic `TrackingFieldTrio` core
 * lifted to `@spaarke/ui-components` in task 023 — this file adds a section
 * header + inline error surface, nothing else. It bakes in NO
 * `sprk_communication`-specific option integers or Dataverse field names
 * (task 023's design decision applies here verbatim): the host (task 040
 * assembly) supplies current values, `accessPermissionOptions`, and the
 * write callbacks — exactly the same "values in, onChange callbacks out"
 * contract `TrackingFieldTrio` itself already has. This keeps the actual
 * Dataverse field mapping in exactly one place in the tree (the eventual
 * host wiring), per FR-14's entity-agnostic requirement.
 *
 * Fluent v9 tokens only (ADR-021, dark-mode correct). No `as
 * React.ComponentType` cast (NFR-05) — `TrackingFieldTrio` is consumed via
 * the `@spaarke/ui-components` barrel import (React 19 code-page side; no
 * deep-dist-path or cast needed there per task 023's notes).
 */
import * as React from 'react';
import { makeStyles, tokens, Text, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { TrackingFieldTrio } from '@spaarke/ui-components';
import type { EmailTrackingPanelProps } from './EmailAssociationsAndTracking.types';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  header: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  disabled: { opacity: 0.6, pointerEvents: 'none' },
});

export function EmailTrackingPanel(props: EmailTrackingPanelProps): React.ReactElement {
  const {
    monitor,
    highPriority,
    accessPermission,
    accessPermissionOptions,
    onMonitorChange,
    onHighPriorityChange,
    onAccessPermissionChange,
    monitorLabel = 'Monitor',
    highPriorityLabel = 'High priority',
    accessPermissionLabel = 'Access',
    readOnly = false,
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
  const handleAccessPermissionChange = React.useCallback(
    (value: number) => {
      void runChange(() => onAccessPermissionChange(value));
    },
    [runChange, onAccessPermissionChange]
  );

  return (
    <div className={s.root} data-testid="email-tracking-panel">
      <Text className={s.header}>Tracking</Text>
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      <div className={readOnly ? s.disabled : undefined} aria-disabled={readOnly || undefined}>
        <TrackingFieldTrio
          monitor={monitor}
          highPriority={highPriority}
          accessPermission={accessPermission}
          accessPermissionOptions={accessPermissionOptions}
          monitorLabel={monitorLabel}
          highPriorityLabel={highPriorityLabel}
          accessPermissionLabel={accessPermissionLabel}
          onMonitorChange={handleMonitorChange}
          onHighPriorityChange={handleHighPriorityChange}
          onAccessPermissionChange={handleAccessPermissionChange}
        />
      </div>
    </div>
  );
}
