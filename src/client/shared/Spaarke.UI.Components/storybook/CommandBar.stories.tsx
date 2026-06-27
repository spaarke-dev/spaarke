/**
 * CommandBar.stories — Storybook stories for the DataGrid CommandBar primitive.
 *
 * Four stories per task 008 POML:
 *   1. Default — 6 default actions visible, primary=create-form, light + dark
 *   2. CustomHandlerRegistered — a 'mark-paid' custom action via `registerCommandHandler`
 *   3. BulkDeleteConfirmation — user selects 3 rows, clicks Delete, sees Fluent <Dialog>
 *   4. CsvExportTrigger — user clicks Export to Excel; downloads RFC 4180 CSV
 *
 * **Storybook config**: like the DataGrid story, this file lives OUTSIDE `src/`
 * so the library build (`tsc`) does not include it. Storybook (when wired) picks
 * it up via the standard `stories: ['../storybook/**\/*.stories.@(ts|tsx)']` pattern.
 *
 * **NFR-03**: every story wraps the surface in `<FluentProvider applyStylesToPortals theme={…} />`
 * AND passes `theme={…}` to `<CommandBar />` so the Dialog portal also re-wraps.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/008-command-bar.poml
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { CommandBar } from '../src/components/DataGrid/commandBar/CommandBar';
import { registerCommandHandler, unregisterCommandHandler } from '../src/components/DataGrid/commandBar/registry';
import type { CommandBarConfig, CommandBarItem } from '../src/types/DataGridConfiguration';
import type { ResolvedColumn } from '../src/components/DataGrid/configResolution';

// ─────────────────────────────────────────────────────────────────────────────
// Shared test fixtures
// ─────────────────────────────────────────────────────────────────────────────

const sampleColumns: ResolvedColumn[] = [
  {
    name: 'sprk_eventname',
    label: 'Event Name',
    width: 220,
    renderer: 'link',
    align: 'left',
    hidden: false,
    isPrimaryName: true,
  },
  {
    name: 'sprk_status',
    label: 'Status',
    width: 140,
    renderer: 'badge',
    align: 'left',
    hidden: false,
    isPrimaryName: false,
  },
  {
    name: 'sprk_amount',
    label: 'Amount',
    width: 120,
    renderer: 'currency',
    align: 'right',
    hidden: false,
    isPrimaryName: false,
  },
];

const sampleRecords: Record<string, unknown>[] = Array.from({ length: 25 }, (_, i) => ({
  sprk_eventid: `evt-${String(i + 1).padStart(4, '0')}`,
  sprk_eventname: `Event ${i + 1}${i === 0 ? ', with comma' : ''}${i === 1 ? ' with "quote"' : ''}`,
  sprk_status: ['Open', 'In progress', 'Closed'][i % 3],
  sprk_amount: 1000 + i * 37.5,
}));

const refreshNoop = (): void => {
  // eslint-disable-next-line no-console
  console.log('[Storybook] refresh() called');
};

// ─────────────────────────────────────────────────────────────────────────────
// Storybook meta
// ─────────────────────────────────────────────────────────────────────────────

export default {
  title: 'DataGrid/CommandBar',
  component: CommandBar,
  argTypes: {
    theme: {
      control: { type: 'radio' },
      options: ['light', 'dark'],
    },
  },
};

interface StoryArgs {
  theme: 'light' | 'dark';
}

const withFluentProvider = (
  themeName: 'light' | 'dark',
  content: (theme: typeof webLightTheme) => React.ReactNode
): React.ReactNode => {
  const theme = themeName === 'dark' ? webDarkTheme : webLightTheme;
  // `applyStylesToPortals` is MANDATORY per NFR-03 — Toolbar overflow Menu +
  // Dialog surface render in portals and must inherit the active theme.
  return (
    <FluentProvider applyStylesToPortals theme={theme} style={{ padding: '16px', minHeight: '300px' }}>
      {content(theme)}
    </FluentProvider>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: Default — 6 default actions
// ─────────────────────────────────────────────────────────────────────────────

export const Default = (args: StoryArgs) => {
  // Empty config → buildEffectiveItems synthesizes defaults (incl. edit-columns
  // when explicitly opted-in below — story shows the "edit-columns visible" path).
  const config: CommandBarConfig = {
    showDefaultCommands: { editColumns: true },
  };
  return withFluentProvider(args.theme, theme => (
    <CommandBar
      config={config}
      entityName="sprk_event"
      selectedIds={[]}
      records={sampleRecords}
      columns={sampleColumns}
      currentView="All Events"
      refresh={refreshNoop}
      theme={theme}
      onCommandInvoke={(commandId, ids) =>
        // eslint-disable-next-line no-console
        console.log('[Storybook] onCommandInvoke:', commandId, ids)
      }
    />
  ));
};
Default.args = { theme: 'light' as const };
Default.storyName = 'Default (6 actions, light + dark)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: Custom handler registered
// ─────────────────────────────────────────────────────────────────────────────

export const CustomHandlerRegistered = (args: StoryArgs) => {
  React.useEffect(() => {
    registerCommandHandler('mark-paid', async ctx => {
      // eslint-disable-next-line no-console
      console.log(`[Storybook] mark-paid invoked for ${ctx.selectedIds.length} record(s)`);
    });
    // Demonstrate the conflict warning — re-register with same id.
    registerCommandHandler('mark-paid', async () => {
      // eslint-disable-next-line no-console
      console.log('[Storybook] mark-paid (overwritten) invoked');
    });
    return () => {
      unregisterCommandHandler('mark-paid');
    };
  }, []);

  const customAction: CommandBarItem = {
    id: 'mark-paid',
    label: 'Mark paid',
    icon: 'Add20Regular', // re-use a registered icon
    action: 'custom',
    customHandlerId: 'mark-paid',
    requiresSelection: 'multi',
    appearance: 'subtle',
  };

  const config: CommandBarConfig = {
    primary: [customAction],
  };

  return withFluentProvider(args.theme, theme => (
    <CommandBar
      config={config}
      entityName="sprk_invoice"
      selectedIds={['inv-001', 'inv-002']}
      records={sampleRecords}
      columns={sampleColumns}
      currentView="Open Invoices"
      refresh={refreshNoop}
      theme={theme}
    />
  ));
};
CustomHandlerRegistered.args = { theme: 'light' as const };
CustomHandlerRegistered.storyName = "Custom handler ('mark-paid')";

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: Bulk delete confirmation
// ─────────────────────────────────────────────────────────────────────────────

export const BulkDeleteConfirmation = (args: StoryArgs) => {
  // Pre-select 3 records so user can immediately exercise the Dialog flow.
  const selectedIds = ['evt-0001', 'evt-0002', 'evt-0003'];
  const config: CommandBarConfig = {};
  return withFluentProvider(args.theme, theme => (
    <CommandBar
      config={config}
      entityName="sprk_event"
      selectedIds={selectedIds}
      records={sampleRecords}
      columns={sampleColumns}
      currentView="All Events"
      refresh={refreshNoop}
      theme={theme}
      onCommandInvoke={(commandId, ids) =>
        // eslint-disable-next-line no-console
        console.log('[Storybook] onCommandInvoke:', commandId, ids)
      }
    />
  ));
};
BulkDeleteConfirmation.args = { theme: 'light' as const };
BulkDeleteConfirmation.storyName = 'Bulk delete confirmation (Dialog)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: CSV export trigger
// ─────────────────────────────────────────────────────────────────────────────

export const CsvExportTrigger = (args: StoryArgs) => {
  const config: CommandBarConfig = {
    // Show only export action to keep the story focused.
    showDefaultCommands: {
      newRecord: false,
      delete: false,
      refresh: false,
      exportExcel: true,
      editColumns: false,
      editFilters: false,
    },
  };
  return withFluentProvider(args.theme, theme => (
    <CommandBar
      config={config}
      entityName="sprk_event"
      selectedIds={[]}
      records={sampleRecords}
      columns={sampleColumns}
      currentView="All Events"
      refresh={refreshNoop}
      theme={theme}
    />
  ));
};
CsvExportTrigger.args = { theme: 'light' as const };
CsvExportTrigger.storyName = 'CSV export trigger';
