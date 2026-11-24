/**
 * useGridSettings — the host side of the shared grid's opt-in persisted settings
 * (spaarke-SPA-external-access-platform-r4 task 032, FR-19, #2489).
 *
 * One call per grid mount site. It loads the grid's saved widths and sort from `gridSettingsStore` (by configuration id),
 * hands them to the grid as `initialSettings`, stores whatever the grid reports through `onSettingsChange`, and builds the
 * "Clear all" host command (the grid's generic `hostCommands` slot): the command clears the stored entry and bumps
 * `settingsResetKey`, which makes the grid return to its configured widths and order and drop its column filters, without a
 * reload (owner 2026-11-23: it clears everything the user set on the grid: widths, sort and filters).
 *
 * Spread `gridProps` onto `<ExternalDataGrid>` and add `resetCommand` to its `hostCommands` (after the host's own).
 * The store never throws (see `gridSettingsStore`), so a blocked or full `localStorage` just means nothing is remembered.
 *
 * The shared grid reads `initialSettings` once, when it mounts. A host that remounts the grid (GridWidgetBody does after a
 * delete) gets the LATEST settings, because the hook keeps what the grid last reported, not only what it loaded.
 */
import * as React from 'react';
import { RECORD_ACTION_ICONS } from '@spaarke/ui-components/components/RecordCommandBar';
import type { ExternalDataGridProps } from './ExternalDataGrid';
import {
  clearGridSettings,
  isEmpty,
  loadGridSettings,
  saveGridSettings,
  type GridSettings,
} from '../services/gridSettingsStore';

/** One command of the shared grid's command bar, read off the wrapper's props. */
export type GridHostCommand = NonNullable<ExternalDataGridProps['hostCommands']>[number];

/**
 * The id of the "Clear all" command, which clears the user's column widths, sort and column filters (it must not equal a
 * built-in or configured command id; none is this).
 */
export const CLEAR_ALL_COMMAND_ID = 'clear-all';
/** Its label: the visible text, the accessible name and the tooltip (it clears widths, sort and filters). */
export const CLEAR_ALL_LABEL = 'Clear all';

export interface GridSettingsBinding {
  /** Spread onto the grid. */
  gridProps: Pick<ExternalDataGridProps, 'initialSettings' | 'onSettingsChange' | 'settingsResetKey'>;
  /** The "Clear all" command (widths, sort and filters), for the grid's `hostCommands` (stable identity). */
  resetCommand: GridHostCommand;
}

export function useGridSettings(configId: string): GridSettingsBinding {
  // The settings a (re)mounting grid should start with: the stored entry at first, then what the grid last reported.
  const latest = React.useRef<{ settings: GridSettings | undefined } | null>(null);
  if (latest.current === null) latest.current = { settings: loadGridSettings(configId) };
  const [resetKey, setResetKey] = React.useState(0);

  const onSettingsChange = React.useCallback(
    (settings: GridSettings) => {
      if (latest.current) latest.current.settings = isEmpty(settings) ? undefined : settings;
      saveGridSettings(configId, settings);
    },
    [configId]
  );

  const reset = React.useCallback(() => {
    if (latest.current) latest.current.settings = undefined;
    clearGridSettings(configId);
    setResetKey(k => k + 1);
  }, [configId]);

  const resetCommand = React.useMemo<GridHostCommand>(() => {
    const ResetIcon = RECORD_ACTION_ICONS.reset;
    return {
      id: CLEAR_ALL_COMMAND_ID,
      label: CLEAR_ALL_LABEL,
      icon: React.createElement(ResetIcon),
      onInvoke: reset,
    };
  }, [reset]);

  return {
    gridProps: { initialSettings: latest.current.settings, onSettingsChange, settingsResetKey: resetKey },
    resetCommand,
  };
}
