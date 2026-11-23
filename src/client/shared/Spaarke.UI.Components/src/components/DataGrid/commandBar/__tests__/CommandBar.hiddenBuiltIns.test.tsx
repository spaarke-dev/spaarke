/**
 * CommandBar.hiddenBuiltIns.test — `hiddenBuiltInCommands` (#2908).
 *
 * A host that replaces a built-in with its own host commands hides the built-in (the communications grid hides
 * `create-form`, the OOB form, and adds "New Email" / "New Message"). Contract:
 *  - a hidden built-in is not in the bar, inline or in the overflow menu, whether auto-added or authored in config;
 *  - a configured `custom` item is never removed by the list;
 *  - the hidden id stays reserved: a host command still cannot take it;
 *  - absent: unchanged (control).
 */
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CommandBar, type CommandBarProps, type DataGridHostCommand } from '../CommandBar';
import type { CommandBarConfig } from '../../../../types/DataGridConfiguration';

const BASE_PROPS: Omit<CommandBarProps, 'config'> = {
  entityName: 'sprk_communication',
  selectedIds: [],
  records: [],
  columns: [],
  currentView: 'Active Communications',
  refresh: jest.fn(),
};

function renderBar(props: Partial<CommandBarProps> & { config?: CommandBarConfig } = {}) {
  const { config = {}, ...rest } = props;
  return render(
    <FluentProvider theme={webLightTheme}>
      <CommandBar {...BASE_PROPS} config={config} inlineLimit={3} {...rest} />
    </FluentProvider>
  );
}

function barButtons(): string[] {
  const bar = screen.getByRole('toolbar', { name: 'Grid actions' });
  return Array.from(bar.querySelectorAll('button')).map(b => b.getAttribute('aria-label') ?? b.textContent ?? '');
}

async function overflowItems(): Promise<string[]> {
  fireEvent.click(screen.getByRole('button', { name: 'More commands' }));
  const items = await screen.findAllByRole('menuitem');
  return items.map(i => i.textContent ?? '');
}

const HOST: ReadonlyArray<DataGridHostCommand> = [
  { id: 'new-email', label: 'New Email', onInvoke: jest.fn() },
  { id: 'new-message', label: 'New Message', onInvoke: jest.fn() },
];

describe('CommandBar — hiddenBuiltInCommands', () => {
  afterEach(() => jest.restoreAllMocks());

  it('control: without the prop the built-in New is in the bar', () => {
    renderBar();
    expect(barButtons()).toEqual(['Refresh', 'Delete', 'New', 'More commands']);
  });

  it('hides the auto-added built-in New, inline and in the overflow menu', async () => {
    renderBar({ hiddenBuiltInCommands: ['create-form'] });
    expect(barButtons()).toEqual(['Refresh', 'Delete', 'Export to Excel', 'More commands']);
    expect((await overflowItems()).some(t => /^New$/.test(t.trim()))).toBe(false);
  });

  it('the communications grid: New Email and New Message replace New, which is nowhere in the bar', async () => {
    renderBar({ hostCommands: HOST, hiddenBuiltInCommands: ['create-form'] });
    expect(barButtons()).toEqual(['New Email', 'New Message', 'Refresh', 'More commands']);
    const overflow = (await overflowItems()).map(t => t.trim());
    expect(overflow).toEqual(expect.arrayContaining(['Delete', 'Export to Excel']));
    expect(overflow).not.toContain('New');
  });

  it('also hides a built-in that the configuration authors explicitly', () => {
    const config: CommandBarConfig = {
      primary: [{ id: 'cfg-new', label: 'Create', icon: 'Add20Regular', action: 'create-form', appearance: 'subtle' }],
    };
    renderBar({ config, hiddenBuiltInCommands: ['create-form'] });
    expect(barButtons()).not.toContain('Create');
  });

  it('never removes a configured custom item, even when "custom" is passed', () => {
    const config: CommandBarConfig = {
      primary: [
        { id: 'mark', label: 'Mark read', icon: '', action: 'custom', customHandlerId: 'mark', appearance: 'subtle' },
      ],
    };
    renderBar({ config, hiddenBuiltInCommands: ['custom', 'create-form'] });
    expect(barButtons()[0]).toBe('Mark read');
  });

  it('the hidden id stays reserved: a host command with id "create-form" is still skipped', () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    renderBar({
      hostCommands: [{ id: 'create-form', label: 'Sneaky New', onInvoke: jest.fn() }],
      hiddenBuiltInCommands: ['create-form'],
    });
    expect(barButtons()).not.toContain('Sneaky New');
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('"create-form" skipped'));
  });
});
