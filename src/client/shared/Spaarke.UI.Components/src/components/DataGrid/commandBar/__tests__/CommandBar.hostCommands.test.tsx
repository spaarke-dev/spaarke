/**
 * CommandBar.hostCommands.test — the host-supplied command slot (`hostCommands`).
 *
 * Contract under test (see `DataGridHostCommand` JSDoc in CommandBar.tsx):
 *  - absent / empty: the bar is exactly what the configuration + built-ins give (control);
 *  - present: rendered FIRST, with label + icon, subtle; invoking calls `onInvoke` and the
 *    `onCommandInvoke` observer, and runs no built-in handler;
 *  - past `inlineLimit` the command goes to the `...` overflow menu and is still invokable;
 *  - an id equal to a configured or built-in item replaces that item; duplicate host ids: first wins;
 *  - `disabled` is honoured; a throwing `onInvoke` is logged, not rethrown.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CommandBar, type CommandBarProps, type DataGridHostCommand } from '../CommandBar';
import type { CommandBarConfig } from '../../../../types/DataGridConfiguration';
import { clearCommandHandlers, registerCommandHandler } from '../registry';

const BASE_PROPS: Omit<CommandBarProps, 'config'> = {
  entityName: 'sprk_thing',
  selectedIds: [],
  records: [],
  columns: [],
  currentView: 'All things',
  refresh: jest.fn(),
};

/** Built-ins only (Refresh, Delete, New, Export, Edit filters, in that order). The grid mounts the bar with inlineLimit 3. */
const DEFAULT_CONFIG: CommandBarConfig = {};

function renderBar(props: Partial<CommandBarProps> & { config?: CommandBarConfig } = {}) {
  const { config = DEFAULT_CONFIG, ...rest } = props;
  return render(
    <FluentProvider theme={webLightTheme}>
      <CommandBar {...BASE_PROPS} refresh={jest.fn()} config={config} inlineLimit={3} {...rest} />
    </FluentProvider>
  );
}

/** Accessible names of the toolbar's buttons, in DOM order. */
function barButtons(): string[] {
  const bar = screen.getByRole('toolbar', { name: 'Grid actions' });
  return Array.from(bar.querySelectorAll('button')).map(b => b.getAttribute('aria-label') ?? b.textContent ?? '');
}

const cmd = (over: Partial<DataGridHostCommand> = {}): DataGridHostCommand => ({
  id: 'host-go',
  label: 'Go now',
  icon: <span data-testid="host-icon">+</span>,
  onInvoke: jest.fn(),
  ...over,
});

describe('CommandBar — hostCommands', () => {
  afterEach(() => {
    clearCommandHandlers();
    jest.restoreAllMocks();
  });

  describe('absent or empty (control: unchanged)', () => {
    it('shows only the built-ins when hostCommands is absent', () => {
      renderBar();
      expect(barButtons()).toEqual(['Refresh', 'Delete', 'New', 'More commands']);
    });

    it('an empty array gives the same bar as no prop', () => {
      const { unmount } = renderBar();
      const without = barButtons();
      unmount();
      renderBar({ hostCommands: [] });
      expect(barButtons()).toEqual(without);
    });
  });

  describe('present', () => {
    it('renders the command first, with its label and icon, ahead of the built-ins', () => {
      renderBar({ hostCommands: [cmd()] });
      expect(barButtons()).toEqual(['Go now', 'Refresh', 'Delete', 'More commands']);
      expect(screen.getByRole('button', { name: 'Go now' })).toBeInTheDocument();
      expect(screen.getByTestId('host-icon')).toBeInTheDocument();
    });

    it('invoking calls onInvoke and the onCommandInvoke observer, and no built-in handler', async () => {
      const onInvoke = jest.fn();
      const onCommandInvoke = jest.fn();
      const refresh = jest.fn();
      renderBar({ hostCommands: [cmd({ onInvoke })], onCommandInvoke, refresh, selectedIds: ['a', 'b'] });

      fireEvent.click(screen.getByRole('button', { name: 'Go now' }));

      await waitFor(() => expect(onInvoke).toHaveBeenCalledTimes(1));
      expect(onCommandInvoke).toHaveBeenCalledWith('host-go', ['a', 'b']);
      expect(refresh).not.toHaveBeenCalled();
    });

    it('keeps host order, and every host command precedes the configured items', () => {
      const config: CommandBarConfig = {
        primary: [{ id: 'cfg-1', label: 'Configured', icon: '', action: 'custom', customHandlerId: 'h' }],
        showDefaultCommands: {
          delete: false,
          newRecord: false,
          exportExcel: false,
          refresh: false,
          editFilters: false,
        },
      };
      renderBar({
        config,
        inlineLimit: 5,
        hostCommands: [cmd({ id: 'h1', label: 'Host one' }), cmd({ id: 'h2', label: 'Host two' })],
      });
      expect(barButtons()).toEqual(['Host one', 'Host two', 'Configured']);
    });
  });

  describe('overflow', () => {
    it('a host command past inlineLimit is in the overflow menu and still invokes', async () => {
      const second = jest.fn();
      renderBar({
        inlineLimit: 1,
        hostCommands: [cmd({ id: 'h1', label: 'Host one' }), cmd({ id: 'h2', label: 'Host two', onInvoke: second })],
      });
      // Inline: only the first host command (+ the overflow trigger).
      expect(barButtons()).toEqual(['Host one', 'More commands']);

      fireEvent.click(screen.getByRole('button', { name: 'More commands' }));
      fireEvent.click(await screen.findByRole('menuitem', { name: /Host two/ }));

      await waitFor(() => expect(second).toHaveBeenCalledTimes(1));
    });

    it('with inlineLimit 0 the host command is in the menu, not inline', async () => {
      renderBar({ inlineLimit: 0, hostCommands: [cmd()] });
      expect(barButtons()).toEqual(['More commands']);
      fireEvent.click(screen.getByRole('button', { name: 'More commands' }));
      expect(await screen.findByRole('menuitem', { name: /Go now/ })).toBeInTheDocument();
    });
  });

  describe('id collisions', () => {
    it('a host id equal to a configured item is skipped: the configured item stays and runs, a warning names the id', async () => {
      const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      const configured = jest.fn();
      registerCommandHandler('cfg-handler', configured);
      const onInvoke = jest.fn();
      const config: CommandBarConfig = {
        primary: [
          { id: 'submit', label: 'Configured submit', icon: '', action: 'custom', customHandlerId: 'cfg-handler' },
        ],
        showDefaultCommands: {
          delete: false,
          newRecord: false,
          exportExcel: false,
          refresh: false,
          editFilters: false,
        },
      };
      renderBar({ config, hostCommands: [cmd({ id: 'submit', label: 'Host submit', onInvoke })] });

      expect(barButtons()).toEqual(['Configured submit']);
      expect(screen.queryByRole('button', { name: 'Host submit' })).not.toBeInTheDocument();
      expect(warn).toHaveBeenCalledTimes(1);
      expect(String(warn.mock.calls[0][0])).toContain('"submit"');

      fireEvent.click(screen.getByRole('button', { name: 'Configured submit' }));
      await waitFor(() => expect(configured).toHaveBeenCalledTimes(1));
      expect(onInvoke).not.toHaveBeenCalled();
    });

    it('a host id equal to a built-in (refresh) is skipped: the built-in stays and runs, a warning names the id', async () => {
      const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      const onInvoke = jest.fn();
      const refresh = jest.fn();
      renderBar({ refresh, hostCommands: [cmd({ id: 'refresh', label: 'Reload', onInvoke })] });

      expect(barButtons()).toEqual(['Refresh', 'Delete', 'New', 'More commands']);
      expect(screen.queryByRole('button', { name: 'Reload' })).not.toBeInTheDocument();
      expect(warn).toHaveBeenCalledTimes(1);
      expect(String(warn.mock.calls[0][0])).toContain('"refresh"');

      fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
      await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
      expect(onInvoke).not.toHaveBeenCalled();
    });

    it('a host id equal to a built-in that is HIDDEN is still skipped (no replacing, no impersonating)', () => {
      const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      renderBar({
        config: { showDefaultCommands: { delete: false } },
        hostCommands: [cmd({ id: 'delete-selected', label: 'Remove them' })],
      });
      expect(screen.queryByRole('button', { name: 'Remove them' })).not.toBeInTheDocument();
      expect(barButtons()).not.toContain('Delete');
      expect(warn).toHaveBeenCalledTimes(1);
      expect(String(warn.mock.calls[0][0])).toContain('"delete-selected"');
    });

    it('a colliding host command does not hide a valid one beside it, and a bar with no collision does not warn', () => {
      const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      renderBar({
        hostCommands: [cmd({ id: 'refresh', label: 'Reload' }), cmd({ id: 'mine', label: 'Mine' })],
      });
      expect(barButtons()[0]).toBe('Mine');
      expect(warn).toHaveBeenCalledTimes(1);

      warn.mockClear();
      renderBar({ hostCommands: [cmd({ id: 'mine-2', label: 'Mine two' })] });
      expect(warn).not.toHaveBeenCalled();
    });

    it('duplicate host ids: the first wins', () => {
      renderBar({
        hostCommands: [cmd({ id: 'dup', label: 'First' }), cmd({ id: 'dup', label: 'Second' })],
      });
      expect(barButtons()).toEqual(['First', 'Refresh', 'Delete', 'More commands']);
    });
  });

  describe('state and errors', () => {
    it('a disabled host command does not invoke', () => {
      const onInvoke = jest.fn();
      renderBar({ hostCommands: [cmd({ onInvoke, disabled: true })] });
      const btn = screen.getByRole('button', { name: 'Go now' });
      expect((btn as HTMLButtonElement).disabled).toBe(true);
      fireEvent.click(btn);
      expect(onInvoke).not.toHaveBeenCalled();
    });

    it('a throwing onInvoke is logged and does not escape', async () => {
      const error = jest.spyOn(console, 'error').mockImplementation(() => undefined);
      const onInvoke = jest.fn(async () => {
        throw new Error('boom');
      });
      renderBar({ hostCommands: [cmd({ onInvoke })] });

      fireEvent.click(screen.getByRole('button', { name: 'Go now' }));

      await waitFor(() => expect(error).toHaveBeenCalled());
      expect(String(error.mock.calls[0][0])).toContain('Host command "host-go" threw');
      // The bar is still there.
      expect(screen.getByRole('button', { name: 'Go now' })).toBeInTheDocument();
    });
  });
});
