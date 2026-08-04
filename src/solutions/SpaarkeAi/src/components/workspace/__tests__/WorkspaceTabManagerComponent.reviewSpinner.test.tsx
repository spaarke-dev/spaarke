/**
 * WorkspaceTabManagerComponent.reviewSpinner.test.tsx — UAT round-4 item #10a.
 *
 * After "Continue working in background", the dismissed progress card is fully unmounted and the run's
 * liveness moves to the WORKSPACE tab strip: a tiny circular progress indicator (Fluent Spinner) on the
 * running Compose tab header, visible until the run completes.
 *
 * Contract pinned here:
 *   1. composeReviewRunning=true  → a spinner renders on the `compose` tab header;
 *   2. composeReviewRunning=false → no spinner (this is how the spinner CLEARS on completion);
 *   3. prop omitted               → no spinner (no behaviour change for existing consumers);
 *   4. a NON-compose tab never shows the spinner even while a review is running (reviews only run on
 *      documents open in a Compose tab).
 *
 * @see WorkspaceTabManagerComponent.tsx — component under test (`composeReviewRunning` prop)
 * @see WorkspacePane.tsx — consumer (subscribes to `nda_review_background_run`, passes the flag)
 * @see useNdaReviewRunProgress.ts — emitter of the background-run signal
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

import { WorkspaceTabManagerComponent } from '../WorkspaceTabManagerComponent';
import type { WorkspaceTab } from '../WorkspaceTabManager';

function StubWidget(): React.JSX.Element {
  return <div data-testid="stub-widget">content</div>;
}

function makeTab(id: string, displayName: string, widgetType: string): WorkspaceTab {
  return {
    id,
    kind: 'widget',
    widgetType,
    displayName,
    widgetData: null,
    Component: StubWidget as unknown as WorkspaceTab['Component'],
    isLoading: false,
    visibleToAssistant: true,
  };
}

function renderInProviders(node: React.ReactNode): void {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>{node}</PaneEventBusProvider>
    </FluentProvider>,
  );
}

const noop = jest.fn();

describe('WorkspaceTabManagerComponent — UAT round-4 item #10a (background-review tab spinner)', () => {
  it('shows a tiny spinner on the Compose tab header when composeReviewRunning is true', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={[makeTab('t-compose', 'Contract.docx', 'compose')]}
        activeTabId="t-compose"
        onTabChange={noop}
        onTabClose={noop}
        composeReviewRunning
      />,
    );
    expect(screen.getByTestId('workspace-tab-review-spinner-t-compose')).toBeInTheDocument();
  });

  it('does NOT show the spinner when composeReviewRunning is false (this is the clear-on-completion path)', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={[makeTab('t-compose', 'Contract.docx', 'compose')]}
        activeTabId="t-compose"
        onTabChange={noop}
        onTabClose={noop}
        composeReviewRunning={false}
      />,
    );
    expect(screen.queryByTestId('workspace-tab-review-spinner-t-compose')).not.toBeInTheDocument();
  });

  it('does NOT show the spinner by default (prop omitted — no change for existing consumers)', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={[makeTab('t-compose', 'Contract.docx', 'compose')]}
        activeTabId="t-compose"
        onTabChange={noop}
        onTabClose={noop}
      />,
    );
    expect(screen.queryByTestId('workspace-tab-review-spinner-t-compose')).not.toBeInTheDocument();
  });

  it('does NOT show the spinner on a NON-compose tab even while a review is running', () => {
    renderInProviders(
      <WorkspaceTabManagerComponent
        tabs={[makeTab('t-matters', 'Matters', 'matters-list')]}
        activeTabId="t-matters"
        onTabChange={noop}
        onTabClose={noop}
        composeReviewRunning
      />,
    );
    expect(screen.queryByTestId('workspace-tab-review-spinner-t-matters')).not.toBeInTheDocument();
  });
});
