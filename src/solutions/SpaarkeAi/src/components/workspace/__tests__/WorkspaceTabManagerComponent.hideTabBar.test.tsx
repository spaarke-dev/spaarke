/**
 * WorkspaceTabManagerComponent.hideTabBar.test.tsx — spaarkeai-compose-r1
 * task 100 (Phase 10 polish, FR-S7).
 *
 * Verifies the two behavior contracts of the `hideTabBar` prop:
 *
 *   1. `hideTabBar={false}` (default): the tab-bar strip is rendered — user
 *      can see + switch between multiple workspace tabs.
 *
 *   2. `hideTabBar={true}` (compose-launch mode): the tab-bar strip is
 *      NOT rendered — but the ACTIVE tab's widget content still mounts
 *      and renders normally, so single-widget compose UX works.
 *
 * The active-widget mount contract is critical: task 100's acceptance says
 * "widget-add extensibility for the Workspace pane MUST stay functional".
 * That extensibility flows through `widget_load` events which create tabs
 * in the manager state; in compose mode the tab bar is hidden but the tab
 * still exists and its widget still renders — so any future `widget_load`
 * dispatched by the compose surface (e.g. an inline research widget) would
 * become the active widget instead of just being lost.
 *
 * Test category per ADR-038: **Component Tests** (KEEP category for
 * SpaarkeAi presenter behavior). Not a DI-registration test; asserts
 * the RENDER contract observable by the parent (WorkspacePane) and by the
 * end user (visibility of the tab strip).
 *
 * @see WorkspaceTabManagerComponent.tsx (component under test — task 100
 *      added `hideTabBar` prop)
 * @see WorkspacePane.tsx (consumer — passes `hideTabBar={isComposeLaunchMode}`)
 * @see projects/spaarkeai-compose-r1/tasks/100-workspace-tab-suppression-compose-mode.poml
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { WorkspaceTabManagerComponent } from '../WorkspaceTabManagerComponent';
import type { WorkspaceTab } from '../WorkspaceTabManager';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/**
 * A stub active-widget component. Rendered by ActiveWidgetContent when a
 * tab's `Component` field is non-null; the test asserts its presence to
 * prove that the active widget still mounts even when the tab bar is hidden.
 */
function StubComposeWidget(): React.JSX.Element {
  return <div data-testid="stub-compose-widget">Compose widget content</div>;
}

function makeTab(id: string, displayName: string): WorkspaceTab {
  // Cast through `unknown` per WorkspaceTab.Component's typing (React.ComponentType<any>).
  // Fields match the WorkspaceTabManager.ts schema (kind: 'home'|'widget'; no `error` field
  // — errors surface via the isLoading/Component pair per `ActiveWidgetContent`).
  return {
    id,
    kind: 'widget',
    widgetType: 'compose-editor',
    displayName,
    widgetData: null,
    Component: StubComposeWidget as unknown as WorkspaceTab['Component'],
    isLoading: false,
    visibleToAssistant: true,
  };
}

function renderInProviders(node: React.ReactNode): void {
  render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('WorkspaceTabManagerComponent hideTabBar (task 100)', () => {
  const tabs = [makeTab('tab-compose', 'Compose')];
  const noop = jest.fn();

  it('renders the tab-bar scroll arrows by default (hideTabBar not set)', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId="tab-compose"
        onTabChange={noop}
        onTabClose={noop}
      />,
    );

    // Both scroll arrows are always rendered when the tab bar is visible
    // (visibility toggles via CSS `visibility: hidden`; the DOM node stays).
    expect(
      screen.getByTestId('workspace-tabs-scroll-left'),
    ).toBeInTheDocument();
    expect(
      screen.getByTestId('workspace-tabs-scroll-right'),
    ).toBeInTheDocument();
    // The active widget is also rendered.
    expect(screen.getByTestId('stub-compose-widget')).toBeInTheDocument();
  });

  it('renders the tab-bar scroll arrows when hideTabBar={false}', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId="tab-compose"
        onTabChange={noop}
        onTabClose={noop}
        hideTabBar={false}
      />,
    );

    expect(
      screen.getByTestId('workspace-tabs-scroll-left'),
    ).toBeInTheDocument();
    expect(screen.getByTestId('stub-compose-widget')).toBeInTheDocument();
  });

  it('suppresses the tab bar when hideTabBar={true} (compose-launch mode)', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId="tab-compose"
        onTabChange={noop}
        onTabClose={noop}
        hideTabBar={true}
      />,
    );

    // Neither scroll arrow — nor the tab-bar strip they live inside —
    // is rendered when hideTabBar is true.
    expect(
      screen.queryByTestId('workspace-tabs-scroll-left'),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByTestId('workspace-tabs-scroll-right'),
    ).not.toBeInTheDocument();
    // Individual per-tab rows are also gone.
    expect(
      screen.queryByTestId('workspace-tab-tab-compose'),
    ).not.toBeInTheDocument();
  });

  it('renders the ACTIVE widget content even when hideTabBar={true}', () => {
    // This is the load-bearing contract for task 100 — the tab bar is a
    // pure UI switcher; the underlying tab + widget must still mount so
    // the Compose surface renders full-pane, and any future widget_load
    // dispatched in compose mode becomes the visible active widget.
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={tabs}
        activeTabId="tab-compose"
        onTabChange={noop}
        onTabClose={noop}
        hideTabBar={true}
      />,
    );

    expect(screen.getByTestId('stub-compose-widget')).toBeInTheDocument();
  });
});
