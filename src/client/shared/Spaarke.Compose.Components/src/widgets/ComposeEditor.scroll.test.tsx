/**
 * ComposeEditor.scroll.test.tsx — FIX #9 (spaarkeai-compose-r2 UAT) coverage for
 * the editor scroll affordance: the native scrollbar is hidden (CSS) while the
 * surface stays scrollable, and a floating circular "scroll for more" button
 * appears only when content sits below the fold, scrolling the surface on click.
 *
 * jsdom has no layout engine (scrollHeight/clientHeight are 0), so the "more
 * below" state is simulated by defining the scroll metrics on the surface element
 * and firing a `scroll` event to drive the component's measure effect.
 */

import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBusProvider } from '@spaarke/ai-widgets/events';
import { ComposeEditor } from './ComposeEditor';

jest.mock('@spaarke/auth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-access-token',
    authenticatedFetch: jest.fn(),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

function renderComposeEditor() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider>
        <ComposeEditor docxBytes={null} sessionId="session-scroll" />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

/** Define read-only scroll geometry on an element (jsdom leaves these at 0). */
function setScrollGeometry(
  el: HTMLElement,
  geo: { scrollHeight: number; clientHeight: number; scrollTop: number }
): void {
  Object.defineProperty(el, 'scrollHeight', { configurable: true, get: () => geo.scrollHeight });
  Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => geo.clientHeight });
  Object.defineProperty(el, 'scrollTop', { configurable: true, writable: true, value: geo.scrollTop });
}

describe('ComposeEditor — FIX #9 hidden scrollbar + scroll-for-more FAB', () => {
  it('the editor surface hides its native scrollbar while remaining scrollable', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');
    const surface = screen.getByTestId('compose-editor-surface');
    const style = getComputedStyle(surface);
    expect(style.overflow).toBe('auto');
    // Native scrollbar suppressed (Firefox) — the FAB is the affordance instead.
    expect(style.scrollbarWidth).toBe('none');
  });

  it('shows the circular down-arrow FAB only when content sits below the fold, and hides it at the bottom', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');
    const surface = screen.getByTestId('compose-editor-surface');

    // Not scrolled to bottom → FAB visible.
    setScrollGeometry(surface, { scrollHeight: 1000, clientHeight: 400, scrollTop: 0 });
    act(() => {
      fireEvent.scroll(surface);
    });
    expect(screen.getByTestId('compose-editor-scroll-down')).toBeInTheDocument();

    // Scrolled to the bottom → FAB gone.
    setScrollGeometry(surface, { scrollHeight: 1000, clientHeight: 400, scrollTop: 600 });
    act(() => {
      fireEvent.scroll(surface);
    });
    expect(screen.queryByTestId('compose-editor-scroll-down')).not.toBeInTheDocument();
  });

  it('clicking the FAB scrolls the surface down', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');
    const surface = screen.getByTestId('compose-editor-surface');

    const scrollBy = jest.fn();
    (surface as unknown as { scrollBy: typeof scrollBy }).scrollBy = scrollBy;

    setScrollGeometry(surface, { scrollHeight: 1000, clientHeight: 400, scrollTop: 0 });
    act(() => {
      fireEvent.scroll(surface);
    });

    fireEvent.click(screen.getByTestId('compose-editor-scroll-down'));
    expect(scrollBy).toHaveBeenCalledWith(expect.objectContaining({ top: expect.any(Number), behavior: 'smooth' }));
  });
});
