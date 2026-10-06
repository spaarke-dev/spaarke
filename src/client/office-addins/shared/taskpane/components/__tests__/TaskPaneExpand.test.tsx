/**
 * TaskPaneShell/Toolbar (task 103, UAT round 6): hidden main scroll bar + capability-gated expand/collapse button.
 */
import { act, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneShell } from '../TaskPaneShell';

function installHost(supported: boolean): jest.Mock {
  const setWidth = jest.fn((width: number) => {
    if (width > 500) return; // web cap: silently ignored
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
    window.dispatchEvent(new Event('resize'));
  });
  (globalThis as unknown as { Office: unknown }).Office = {
    context: { platform: 'OfficeOnline', requirements: { isSetSupported: jest.fn().mockReturnValue(supported) } },
    extensionLifeCycle: { taskpane: { setWidth } },
  };
  return setWidth;
}

const renderShell = () =>
  render(
    <FluentProvider theme={webLightTheme}>
      <TaskPaneShell isAuthenticated showNavigation hostType="word">
        <div data-testid="content">Content</div>
      </TaskPaneShell>
    </FluentProvider>
  );

describe('TaskPaneShell scroll bar + expand button (task 103)', () => {
  const originalOffice = (globalThis as unknown as { Office?: unknown }).Office;
  const originalWidth = window.innerWidth;
  beforeEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 330 });
  });
  afterEach(() => {
    (globalThis as unknown as { Office?: unknown }).Office = originalOffice;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: originalWidth });
    jest.useRealTimers();
  });

  it('hides the scroll bar on the main container only, keeping it scrollable', () => {
    installHost(false);
    renderShell();
    const main = screen.getByRole('main');
    // Griffel inserts rules through the CSSOM (insertRule), so read them back from the sheets.
    const css = Array.from(document.styleSheets)
      .flatMap(sheet => Array.from(sheet.cssRules).map(rule => rule.cssText))
      .join('\n');
    const classes = Array.from(main.classList);
    const ruleFor = (needle: RegExp): boolean =>
      classes.some(cls => new RegExp(`\\.${cls}[^{]*\\{[^}]*${needle.source}`).test(css));
    expect(ruleFor(/scrollbar-width:\s*none/)).toBe(true);
    expect(css).toMatch(/::-webkit-scrollbar/);
    // overflow stays 'auto' so wheel/touch/keyboard scrolling still work.
    expect(ruleFor(/overflow(-x|-y)?:\s*(auto|hidden auto|auto auto)/)).toBe(true);
  });

  it('renders no expand button without TaskPaneApi 1.1', () => {
    installHost(false);
    renderShell();
    expect(screen.queryByRole('button', { name: 'Expand pane' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Collapse pane' })).toBeNull();
  });

  it('expands (aria-pressed + name follow), then collapses to the round-4 width', async () => {
    jest.useFakeTimers();
    const setWidth = installHost(true);
    renderShell();
    const expand = screen.getByRole('button', { name: 'Expand pane' });
    expect(expand).toHaveAttribute('aria-pressed', 'false');

    await act(async () => {
      fireEvent.click(expand);
      await jest.advanceTimersByTimeAsync(5000);
    });
    const collapse = screen.getByRole('button', { name: 'Collapse pane' });
    expect(collapse).toHaveAttribute('aria-pressed', 'true');
    expect(window.innerWidth).toBeLessThanOrEqual(500);
    expect(window.innerWidth).toBeGreaterThan(330);

    await act(async () => {
      fireEvent.click(collapse);
      await jest.advanceTimersByTimeAsync(1000);
    });
    expect(setWidth).toHaveBeenLastCalledWith(405);
    expect(screen.getByRole('button', { name: 'Expand pane' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('stays collapsed when the host ignores every expand request', async () => {
    jest.useFakeTimers();
    const setWidth = installHost(true);
    setWidth.mockImplementation(() => undefined);
    renderShell();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Expand pane' }));
      await jest.advanceTimersByTimeAsync(5000);
    });
    expect(screen.getByRole('button', { name: 'Expand pane' })).toHaveAttribute('aria-pressed', 'false');
  });
});
