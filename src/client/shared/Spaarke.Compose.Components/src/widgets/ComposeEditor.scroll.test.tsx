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

describe('ComposeEditor — editor-surface scrolling (UAT round 2 #5)', () => {
  // This suite REPLACES "FIX #9 hidden scrollbar + scroll-for-more FAB". That design hid the native
  // scrollbar (`scrollbarWidth: 'none'`) and substituted a floating down-arrow FAB — the exact
  // down-arrow control `src/client/shared/CLAUDE.md` bans, and ADR-051 requires the shared thin
  // scrollbar on every scroll container. The old tests asserted the banned behaviour, so they are
  // rewritten to assert the replacement rather than deleted: the surface must still scroll, and it
  // must now do so with a VISIBLE scrollbar and no FAB.

  it('the editor surface scrolls, and no longer suppresses its native scrollbar', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');
    const surface = screen.getByTestId('compose-editor-surface');
    const style = getComputedStyle(surface);
    expect(style.overflow).toBe('auto');
    // The whole point of the change: 'none' was the old value and is the regression to catch.
    expect(style.scrollbarWidth).toBe('thin');
  });

  it('renders NO down-arrow scroll FAB, at any scroll position', async () => {
    renderComposeEditor();
    await screen.findByRole('textbox');
    const surface = screen.getByTestId('compose-editor-surface');

    // The geometry that used to SHOW the FAB (content below the fold) must now show nothing.
    setScrollGeometry(surface, { scrollHeight: 1000, clientHeight: 400, scrollTop: 0 });
    act(() => {
      fireEvent.scroll(surface);
    });
    expect(screen.queryByTestId('compose-editor-scroll-down')).not.toBeInTheDocument();

    setScrollGeometry(surface, { scrollHeight: 1000, clientHeight: 400, scrollTop: 600 });
    act(() => {
      fireEvent.scroll(surface);
    });
    expect(screen.queryByTestId('compose-editor-scroll-down')).not.toBeInTheDocument();
  });
});
