/**
 * Task 108 (owner UAT round 10): one toolbar rule for Word and Outlook — icon-only tabs at a normal pane width,
 * labels once the pane is wide (expanded); Expand/Collapse is always the right-most item. Safety net (task 107):
 * labels that would overflow stay hidden, and come back only once the toolbar is wider than they needed.
 */
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneToolbar } from '../TaskPaneToolbar';
import { nextShowLabels, TOOLBAR_LABELS_MIN_WIDTH } from '../../hooks/useToolbarFit';

describe('nextShowLabels (task 108)', () => {
  it('icons only below the minimum width, whatever the overflow', () => {
    expect(nextShowLabels(true, 0, TOOLBAR_LABELS_MIN_WIDTH - 1, { width: 0 })).toBe(false);
    expect(nextShowLabels(false, 0, 395, { width: 0 })).toBe(false);
  });

  it('labels once the toolbar is at least the minimum width and they fit', () => {
    expect(nextShowLabels(false, 0, TOOLBAR_LABELS_MIN_WIDTH, { width: 0 })).toBe(true);
    expect(nextShowLabels(true, 0, 640, { width: 0 })).toBe(true);
  });

  it('labels that overflow are hidden, recording the width they needed', () => {
    const needed = { width: 0 };
    expect(nextShowLabels(true, 40, 500, needed)).toBe(false);
    expect(needed.width).toBe(540);
  });

  it('labels return only once the toolbar is wider than they needed (no flicker)', () => {
    const needed = { width: 540 };
    expect(nextShowLabels(false, 0, 540, needed)).toBe(false);
    expect(nextShowLabels(false, 0, 541, needed)).toBe(true);
  });
});

describe('TaskPaneToolbar (task 108)', () => {
  // jsdom does no layout: drive the measured widths. The header is `headerWidth` wide; the tab row overflows by
  // `overflow`.
  let overflow = 0;
  let headerWidth = 395;
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

  const toolbar = (host: 'word' | 'outlook', extra: Record<string, unknown> = {}) => (
    <FluentProvider theme={webLightTheme}>
      <TaskPaneToolbar
        hostType={host}
        capabilities={{ canEmailFromPane: host === 'word' }}
        isAuthenticated
        selectedTab="save"
        onThemeChange={() => undefined}
        {...extra}
      />
    </FluentProvider>
  );

  it('Word at a normal width: icon-only tabs that keep their names; Expand is the right-most item', () => {
    overflow = 0;
    headerWidth = 395;
    render(toolbar('word', { onToggleExpand: jest.fn() }));

    for (const name of ['Save', 'To Do', 'Find', 'Send']) {
      expect(screen.getByRole('tab', { name })).toHaveTextContent('');
    }
    const buttons = screen.getAllByRole('button');
    expect(buttons[buttons.length - 1]).toHaveAccessibleName('Expand pane');
  });

  it('Outlook at a normal width: the same icon-only tabs, and Send is icon-only too', () => {
    overflow = 0;
    headerWidth = 395;
    render(toolbar('outlook', { onSendEmail: jest.fn() }));

    for (const name of ['Save', 'To Do', 'Find']) {
      expect(screen.getByRole('tab', { name })).toHaveTextContent('');
    }
    expect(screen.getByRole('button', { name: 'Send' })).toHaveTextContent('');
  });

  it('expanded (wide): labels show', async () => {
    overflow = 0;
    headerWidth = 640;
    render(toolbar('outlook', { onSendEmail: jest.fn() }));
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Find' })).toHaveTextContent('Find'));
    expect(screen.getByRole('button', { name: 'Send' })).toHaveTextContent('Send');
  });

  it('resizing the pane switches between icons and labels', async () => {
    overflow = 0;
    headerWidth = 395;
    const onToggleExpand = jest.fn();
    const { rerender } = render(toolbar('word', { onToggleExpand }));
    expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent('');

    headerWidth = 640;
    rerender(toolbar('word', { onToggleExpand, isExpanded: true }));
    resizePane();
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent('Send'));
    expect(screen.getByRole('button', { name: 'Collapse pane' })).toBeInTheDocument();

    headerWidth = 395;
    rerender(toolbar('word', { onToggleExpand, isExpanded: false }));
    resizePane();
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent(''));
  });

  it('wide but the labels would overflow: they stay hidden, and Expand still works inline', async () => {
    overflow = 60;
    headerWidth = 500;
    const onToggleExpand = jest.fn();
    render(toolbar('word', { onToggleExpand }));
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Send' })).toHaveTextContent(''));
    fireEvent.click(screen.getByRole('button', { name: 'Expand pane' }));
    expect(onToggleExpand).toHaveBeenCalledTimes(1);
  });
});
