/**
 * WorkspaceTabManagerComponent.compose-keepalive.test.tsx — UAT round-7 #1.
 *
 * Bug: opening a SECOND file into Compose loads it, but the doc UNLOADS when
 * the user clicks another workspace tab (Daily Briefing ↔ Compose). Root
 * cause: the presenter rendered ONLY the active tab, so switching away
 * UNMOUNTED ComposeWorkspace and destroyed its live local reducer state.
 *
 * Fix under test: the single Compose DIRECT tab (widgetType 'compose') is kept
 * MOUNTED across tab switches and toggled hidden when it is not the active tab.
 * Scoped to 'compose' ONLY — every other inactive tab still unmounts.
 *
 * Contracts asserted:
 *   1. Switching AWAY from the Compose tab keeps its widget instance MOUNTED
 *      (not remounted) — the same DOM node stays, mount count stays at 1, and
 *      the keep-alive host is aria-hidden.
 *   2. Switching BACK to Compose re-shows the SAME instance (still mount
 *      count 1 — never remounted) and clears aria-hidden.
 *   3. A NON-compose active tab renders normally; a non-compose inactive tab
 *      is NOT kept mounted (memory design preserved).
 *
 * Test category per ADR-038: Component Test (KEEP — SpaarkeAi presenter render
 * contract observable by the parent + end user).
 *
 * @see WorkspaceTabManagerComponent.tsx (component under test)
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

import { WorkspaceTabManagerComponent } from '../WorkspaceTabManagerComponent';
import type { WorkspaceTab } from '../WorkspaceTabManager';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

// Module-level counters so we can assert mount/unmount lifecycle across
// rerenders (a remount would run the mount effect a second time).
let composeMountCount = 0;
let composeUnmountCount = 0;

function StubComposeWidget(): React.JSX.Element {
  React.useEffect(() => {
    composeMountCount += 1;
    return () => {
      composeUnmountCount += 1;
    };
  }, []);
  return <div data-testid="stub-compose-widget">Compose widget content</div>;
}

function StubBriefingWidget(): React.JSX.Element {
  return <div data-testid="stub-briefing-widget">Daily Briefing content</div>;
}

function makeTab(
  id: string,
  widgetType: string,
  displayName: string,
  Component: React.ComponentType<unknown>,
): WorkspaceTab {
  return {
    id,
    kind: 'widget',
    widgetType,
    displayName,
    widgetData: null,
    Component: Component as unknown as WorkspaceTab['Component'],
    isLoading: false,
    visibleToAssistant: false,
  };
}

function renderInProviders(node: React.ReactNode): ReturnType<typeof render> {
  const bus = new PaneEventBus();
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>{node}</PaneEventBusProvider>
    </FluentProvider>,
  );
}

beforeEach(() => {
  composeMountCount = 0;
  composeUnmountCount = 0;
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('WorkspaceTabManagerComponent — Compose keep-alive (UAT round-7 #1)', () => {
  const composeTab = makeTab('tab-compose', 'compose', 'Compose', StubComposeWidget);
  const briefingTab = makeTab('tab-briefing', 'workspace', 'Daily Briefing', StubBriefingWidget);
  const tabs = [composeTab, briefingTab];
  const noop = jest.fn();

  it('keeps the Compose widget MOUNTED (not remounted) across a switch away and back', () => {
    const { rerender } = renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId="tab-compose"
        onTabChange={noop}
        onTabClose={noop}
      />,
    );

    // Compose active — mounted + visible.
    expect(screen.getByTestId('stub-compose-widget')).toBeInTheDocument();
    expect(composeMountCount).toBe(1);
    const keepAliveHost = screen.getByTestId('workspace-compose-keepalive');
    expect(keepAliveHost).not.toHaveAttribute('aria-hidden', 'true');
    const composeNode = screen.getByTestId('stub-compose-widget');

    // Switch AWAY to Daily Briefing.
    rerender(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider bus={new PaneEventBus()}>
          <WorkspaceTabManagerComponent
            tabs={tabs}
            activeTabId="tab-briefing"
            onTabChange={noop}
            onTabClose={noop}
          />
        </PaneEventBusProvider>
      </FluentProvider>,
    );

    // Briefing now renders; Compose is STILL mounted (hidden), same instance.
    expect(screen.getByTestId('stub-briefing-widget')).toBeInTheDocument();
    expect(screen.getByTestId('stub-compose-widget')).toBeInTheDocument();
    // Same DOM node — proves no remount.
    expect(screen.getByTestId('stub-compose-widget')).toBe(composeNode);
    expect(composeMountCount).toBe(1); // never remounted
    expect(composeUnmountCount).toBe(0); // never unmounted
    // Keep-alive host is hidden while Compose is inactive.
    expect(screen.getByTestId('workspace-compose-keepalive')).toHaveAttribute(
      'aria-hidden',
      'true',
    );

    // Switch BACK to Compose.
    rerender(
      <FluentProvider theme={webLightTheme}>
        <PaneEventBusProvider bus={new PaneEventBus()}>
          <WorkspaceTabManagerComponent
            tabs={tabs}
            activeTabId="tab-compose"
            onTabChange={noop}
            onTabClose={noop}
          />
        </PaneEventBusProvider>
      </FluentProvider>,
    );

    // Same instance still mounted, now visible again — never remounted.
    expect(screen.getByTestId('stub-compose-widget')).toBe(composeNode);
    expect(composeMountCount).toBe(1);
    expect(composeUnmountCount).toBe(0);
    expect(
      screen.getByTestId('workspace-compose-keepalive'),
    ).not.toHaveAttribute('aria-hidden', 'true');
  });

  it('does NOT keep a non-compose inactive tab mounted (memory design preserved)', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId="tab-compose"
        onTabChange={noop}
        onTabClose={noop}
      />,
    );

    // Compose is active + kept alive; the inactive Daily Briefing widget is
    // NOT mounted (only the active non-compose tab renders).
    expect(screen.getByTestId('stub-compose-widget')).toBeInTheDocument();
    expect(screen.queryByTestId('stub-briefing-widget')).not.toBeInTheDocument();
  });

  it('renders no keep-alive host when there is no Compose tab', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={[briefingTab]}
        activeTabId="tab-briefing"
        onTabChange={noop}
        onTabClose={noop}
      />,
    );

    expect(screen.getByTestId('stub-briefing-widget')).toBeInTheDocument();
    expect(
      screen.queryByTestId('workspace-compose-keepalive'),
    ).not.toBeInTheDocument();
  });
});
