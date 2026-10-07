/**
 * Task 107 (owner UAT round 9): in Word the Expand button sat on top of the last tab when the row was too narrow.
 * The toolbar now steps down until it fits — Expand into "⋮", then icon-only tabs — and steps back up when wide.
 */
import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneToolbar } from '../TaskPaneToolbar';
import { nextToolbarFitLevel } from '../../hooks/useToolbarFit';

describe('nextToolbarFitLevel (task 107)', () => {
  it('steps down one level when the tabs overflow, recording the width that level needed', () => {
    const needed: number[] = [];
    expect(nextToolbarFitLevel(0, 40, 380, needed)).toBe(1);
    expect(needed[0]).toBe(420);
    expect(nextToolbarFitLevel(1, 10, 380, needed)).toBe(2);
    expect(needed[1]).toBe(390);
  });

  it('never goes past icon-only', () => {
    expect(nextToolbarFitLevel(2, 50, 200, [400, 300])).toBe(2);
  });

  it('steps back up only once the header is wider than the previous level needed (no flicker)', () => {
    const needed = [420, 390];
    expect(nextToolbarFitLevel(2, 0, 389, needed)).toBe(2);
    expect(nextToolbarFitLevel(2, 0, 391, needed)).toBe(1);
    expect(nextToolbarFitLevel(1, 0, 420, needed)).toBe(1);
    expect(nextToolbarFitLevel(1, 0, 421, needed)).toBe(0);
  });

  it('stays put when it fits at the top level', () => {
    expect(nextToolbarFitLevel(0, 0, 300, [])).toBe(0);
  });
});

describe('TaskPaneToolbar fitting (task 107)', () => {
  // jsdom does no layout: drive the measured widths. The tab container overflows by `overflow`; the header is
  // `headerWidth` wide.
  let overflow = 0;
  let headerWidth = 1000;
  const originals = {
    scroll: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollWidth'),
    client: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth'),
    resizeObserver: globalThis.ResizeObserver,
  };
  // A ResizeObserver the test can fire, standing in for the host resizing the pane. Only observers watching the
  // toolbar itself are fired (Fluent components observe other elements).
  const toolbarObservers = new Set<() => void>();
  const resizePane = () => act(() => toolbarObservers.forEach(cb => cb()));

  // Installed before EACH test: an earlier interaction (the "⋮" menu) can leave another ResizeObserver in place.
  beforeEach(() => {
    globalThis.ResizeObserver = class {
      private readonly cb: () => void;
      constructor(cb: () => void) {
        this.cb = cb;
      }
      observe(target: Element) {
        if (target.getAttribute('role') === 'banner') toolbarObservers.add(this.cb);
      }
      unobserve() {
        /* not used by the toolbar */
      }
      disconnect() {
        toolbarObservers.delete(this.cb);
      }
    } as unknown as typeof ResizeObserver;
  });

  beforeAll(() => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get(this: HTMLElement) {
        return this.getAttribute('role') === 'banner' ? headerWidth : 300;
      },
    });
    Object.defineProperty(HTMLElement.prototype, 'scrollWidth', {
      configurable: true,
      get(this: HTMLElement) {
        return this.querySelector(':scope > [role="tablist"]') ? 300 + overflow : 300;
      },
    });
  });

  afterAll(() => {
    globalThis.ResizeObserver = originals.resizeObserver;
    if (originals.scroll) Object.defineProperty(HTMLElement.prototype, 'scrollWidth', originals.scroll);
    if (originals.client) Object.defineProperty(HTMLElement.prototype, 'clientWidth', originals.client);
  });

  const toolbar = (onToggleExpand = jest.fn()) => (
    <FluentProvider theme={webLightTheme}>
      <TaskPaneToolbar
        hostType="word"
        capabilities={{ canEmailFromPane: true }}
        isAuthenticated
        selectedTab="save"
        onThemeChange={() => undefined}
        onToggleExpand={onToggleExpand}
      />
    </FluentProvider>
  );

  it('fits: labelled tabs and an inline Expand button (pane icon)', () => {
    overflow = 0;
    headerWidth = 1000;
    render(toolbar());
    expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent('Send');
    expect(screen.getByRole('button', { name: 'Expand pane' })).toBeInTheDocument();
  });

  it('too narrow: Expand moves into "⋮" and the tabs go icon-only, keeping their names', async () => {
    overflow = 60;
    headerWidth = 380;
    const onToggleExpand = jest.fn();
    render(toolbar(onToggleExpand));

    await waitFor(() => expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent(''));
    for (const name of ['Save', 'To Do', 'Find', 'Send']) {
      expect(screen.getByRole('tab', { name })).toBeInTheDocument();
    }
    expect(screen.queryByRole('button', { name: 'Expand pane' })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'More options' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Expand pane' }));
    expect(onToggleExpand).toHaveBeenCalledTimes(1);
  });

  it('widened again: steps back up to labelled tabs and the inline button', async () => {
    overflow = 60;
    headerWidth = 380;
    const { rerender } = render(toolbar());
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Expand pane' })).toBeNull());

    overflow = 0;
    headerWidth = 1000;
    rerender(toolbar());
    resizePane();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Expand pane' })).toBeInTheDocument());
    expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent('Send');
  });
});
