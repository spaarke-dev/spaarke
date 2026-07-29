/**
 * OpenFullFormButton.tsx
 *
 * FR-15's "Open full form" trigger — a small Fluent v9 icon button that calls
 * `useEmailComposeActions().openFullForm(communicationId)`. Deliberately a
 * STANDALONE, un-mounted-by-default component: this task's edit scope
 * excludes `EmailReadingPaneShell/**` (including `EmailToolbar.tsx`), so it
 * cannot add a 7th toolbar button itself. The assembling host (task 040, or
 * whichever slot renderer wants the affordance — e.g. the task-034 header
 * slot) drops this component in wherever "Open full form" should appear,
 * wired to the SAME `openFullForm` this task builds — no duplicate
 * navigation logic.
 *
 * Fluent v9 tokens only (ADR-021) — themes correctly via the host
 * `FluentProvider` in light and dark mode. `React.FC` + standard event types
 * only (ADR-022/NFR-05) — no `as React.ComponentType` cast.
 */
import * as React from 'react';
import { Tooltip, ToolbarButton } from '@fluentui/react-components';
import { Open20Regular } from '@fluentui/react-icons';

export interface OpenFullFormButtonProps {
  /** `sprk_communication` id the full form opens for. */
  communicationId: string;
  /** `useEmailComposeActions().openFullForm` — opens the OOB Email main form as an 85% `navigateTo` modal. */
  onOpenFullForm: (communicationId: string) => Promise<void> | void;
  /** Disables the trigger (e.g. while no record is selected). Defaults to `false`. */
  disabled?: boolean;
}

export const OpenFullFormButton: React.FC<OpenFullFormButtonProps> = ({
  communicationId,
  onOpenFullForm,
  disabled = false,
}) => {
  const handleClick = React.useCallback(() => {
    void onOpenFullForm(communicationId);
  }, [communicationId, onOpenFullForm]);

  return (
    <Tooltip content="Open full form" relationship="label">
      <ToolbarButton
        icon={<Open20Regular />}
        aria-label="Open full form"
        disabled={disabled}
        onClick={handleClick}
      />
    </Tooltip>
  );
};

OpenFullFormButton.displayName = 'OpenFullFormButton';
