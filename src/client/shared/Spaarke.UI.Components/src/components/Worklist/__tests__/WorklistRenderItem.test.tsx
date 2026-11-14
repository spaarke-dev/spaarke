/**
 * The optional `renderItem` seam of `MatterCard` and `WorklistLanePanel` (spaarkeai-mcp-bff-api-r1 task 031, FR-12).
 *  - with no `renderItem`, every item is the standard `IssueLine` (every existing host is unchanged);
 *  - with it, a node replaces that item's line, and `null` / `undefined` keeps the standard line for that item;
 *  - the lane panel passes it to every card, and the counts and the start button do not depend on it.
 *  - `belowHeader` renders directly under the lane header, before the cards and the list; absent renders nothing extra.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { MatterCard } from '../MatterCard';
import { WorklistLanePanel } from '../WorklistLanePanel';
import { buildLaneView } from '../worklistView';
import { parseWorklistResponse } from '../worklistService';
import { A1, A2, body, group, matterCore, routeItem } from '../__fixtures__/worklistRouteFixtures';
import { TODAY, pathBItem, thresholdItem } from '../__fixtures__/worklistFixtures';

const renderThemed = (ui: React.ReactElement) => render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

describe('MatterCard renderItem seam', () => {
  it('without renderItem every item is a standard line', () => {
    renderThemed(<MatterCard core={matterCore} items={[pathBItem, thresholdItem]} today={TODAY} />);
    expect(screen.getAllByTestId('issue-line')).toHaveLength(2);
  });

  it('a returned node replaces that item only; null and undefined keep the standard line', () => {
    const renderItem = jest.fn((item: { signalId: string }) =>
      item.signalId === pathBItem.signalId ? (
        <span data-testid="custom-row">custom</span>
      ) : item.signalId === 'x' ? null : undefined
    );
    renderThemed(
      <MatterCard core={matterCore} items={[pathBItem, thresholdItem]} today={TODAY} renderItem={renderItem} />
    );
    expect(screen.getAllByTestId('custom-row')).toHaveLength(1);
    const lines = screen.getAllByTestId('issue-line');
    expect(lines).toHaveLength(1);
    expect(lines[0]).toHaveAttribute('data-signal-id', thresholdItem.signalId);
    expect(renderItem).toHaveBeenCalledTimes(2);
  });
});

describe('WorklistLanePanel renderItem seam', () => {
  const data = parseWorklistResponse(
    body(
      'lanes',
      [
        group(matterCore, [
          routeItem(A1, 'Decide', { workType: 'askOutsideCounsel' }),
          routeItem(A2, 'Decide', { workType: 'chaseReply' }),
        ]),
      ],
      []
    ),
    'lanes'
  );

  it('reaches every card, and the count and start button are unchanged by it', () => {
    window.localStorage.setItem('spaarke.worklist.laneOpen', JSON.stringify({ Decide: true }));
    const view = buildLaneView(data.Decide, 'Decide', 'all');
    renderThemed(
      <WorklistLanePanel
        lane="Decide"
        view={view}
        today={TODAY}
        onSelectKey={() => undefined}
        onOpenItem={() => undefined}
        onStart={() => undefined}
        renderItem={item => <span data-testid="custom-row" data-id={item.signalId} />}
      />
    );
    const region = screen.getByTestId('lane-region-Decide');
    expect(
      within(region)
        .getAllByTestId('custom-row')
        .map(e => e.getAttribute('data-id'))
    ).toEqual([A1, A2]);
    expect(within(region).queryAllByTestId('issue-line')).toHaveLength(0);
    expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('2');
    expect(screen.getByTestId('lane-start-Decide')).toBeEnabled();
    window.localStorage.clear();
  });

  it('belowHeader renders after the header and before the cards and the list; absent adds nothing', () => {
    window.localStorage.setItem('spaarke.worklist.laneOpen', JSON.stringify({ Decide: true }));
    const view = buildLaneView(data.Decide, 'Decide', 'all');
    const props = {
      lane: 'Decide' as const,
      view,
      today: TODAY,
      onSelectKey: () => undefined,
      onOpenItem: () => undefined,
      onStart: () => undefined,
    };
    const { rerender } = renderThemed(<WorklistLanePanel {...props} belowHeader={<div data-testid="below" />} />);
    const below = screen.getByTestId('below');
    const start = screen.getByTestId('lane-start-Decide');
    const region = screen.getByTestId('lane-region-Decide');
    expect(start.compareDocumentPosition(below) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(below.compareDocumentPosition(region) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(below.compareDocumentPosition(screen.getAllByTestId('issue-line')[0]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    rerender(
      <FluentProvider theme={webLightTheme}>
        <WorklistLanePanel {...props} />
      </FluentProvider>
    );
    expect(screen.queryByTestId('below')).not.toBeInTheDocument();
    window.localStorage.clear();
  });
});

