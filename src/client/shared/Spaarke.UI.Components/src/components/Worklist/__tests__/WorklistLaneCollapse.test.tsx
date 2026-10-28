/**
 * Worklist lane subtitles and the collapsible card list - unit tests (task 169, D-195, UAT round 2 item 4).
 * The stated contract:
 *  - each lane keeps its title and shows the D-195 subtitle ("Items that need your decision", "Your work that is due soon or overdue")
 *  - the card list starts collapsed on first use; the title, subtitle, count, start button and metric cards stay visible
 *  - the toggle ("Show items" / "Hide items", aria-expanded, aria-controls) opens and closes it; the open state is stored per lane
 *    under one key and a re-mount restores it; the two lanes are independent
 *  - storage that throws gives a collapsed list and a working toggle
 *  - a metric card opens its lane's list; the start button works in either state
 *  - the lane keys and test ids are unchanged
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { WorklistWidget } from '..';
import type { WorklistService } from '..';
import type { DecisionWizardLaunchRequest } from '../../DecisionWizard';
import { LANE_OPEN_STORAGE_KEY } from '../useLaneOpen';
import { A1, fakeService } from '../__fixtures__/worklistRouteFixtures';

const TODAY = new Date(2026, 9, 9, 10, 30);

type Launch = jest.Mock<Promise<{ opened: boolean }>, [DecisionWizardLaunchRequest]>;

function renderWidget(service: WorklistService = fakeService()) {
  const launchWizard: Launch = jest.fn(async () => ({ opened: true }));
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <WorklistWidget service={service} today={TODAY} launchWizard={launchWizard} />
    </FluentProvider>
  );
  return { ...utils, launchWizard };
}

const ready = () => screen.findByTestId('worklist-widget');
const lane = (name: 'Decide' | 'Do') => screen.getByTestId(`worklist-lane-${name}`);
const toggle = (name: 'Decide' | 'Do') => within(lane(name)).getByTestId(`lane-toggle-${name}`);
const region = (name: 'Decide' | 'Do') => within(lane(name)).getByTestId(`lane-region-${name}`);
const stored = () => JSON.parse(window.localStorage.getItem(LANE_OPEN_STORAGE_KEY) ?? 'null');

beforeEach(() => window.localStorage.clear());
afterEach(() => jest.restoreAllMocks());

describe('lane subtitles', () => {
  it('shows the D-195 subtitle under each unchanged title, and describes the section by it', async () => {
    renderWidget();
    await ready();
    expect(within(lane('Decide')).getByRole('heading', { name: 'Decide' })).toBeInTheDocument();
    expect(within(lane('Do')).getByRole('heading', { name: 'Do' })).toBeInTheDocument();
    expect(within(lane('Decide')).getByText('Items that need your decision')).toBeVisible();
    expect(within(lane('Do')).getByText('Your work that is due soon or overdue')).toBeVisible();
    expect(lane('Decide')).toHaveAccessibleDescription('Items that need your decision');
    expect(lane('Do')).toHaveAccessibleDescription('Your work that is due soon or overdue');
    // Button labels and test ids are unchanged.
    expect(within(lane('Decide')).getByRole('button', { name: 'Start review' })).toBeVisible();
    expect(within(lane('Do')).getByRole('button', { name: 'Work through these' })).toBeVisible();
  });
});

describe('collapsible card list', () => {
  it('starts collapsed with no stored value, and keeps the title, subtitle, count, button and metric cards visible', async () => {
    renderWidget();
    await ready();
    for (const name of ['Decide', 'Do'] as const) {
      const l = within(lane(name));
      expect(toggle(name)).toHaveAttribute('aria-expanded', 'false');
      expect(toggle(name)).toHaveTextContent('Show items');
      expect(region(name)).not.toBeVisible();
      // The author CSS must not override the hidden attribute: a `display: flex` class on a hidden element still shows it.
      expect(window.getComputedStyle(region(name)).display).toBe('none');
      expect(l.getByRole('button', { name: /^All/ })).toBeVisible();
      expect(l.getByRole('heading', { name })).toBeVisible();
      expect(l.getByTestId(`lane-subtitle-${name}`)).toBeVisible();
      expect(l.getByTestId(`lane-count-${name}`)).toBeVisible();
      expect(l.getByTestId(`lane-start-${name}`)).toBeVisible();
    }
    expect(window.localStorage.getItem(LANE_OPEN_STORAGE_KEY)).toBeNull();
  });

  it('the toggle opens and closes the list, wires aria-controls to the region, and stores the state', async () => {
    renderWidget();
    await ready();
    expect(toggle('Decide')).toHaveAttribute('aria-controls', region('Decide').id);

    await userEvent.click(toggle('Decide'));
    expect(toggle('Decide')).toHaveAttribute('aria-expanded', 'true');
    expect(toggle('Decide')).toHaveTextContent('Hide items');
    expect(region('Decide')).toBeVisible();
    expect(within(region('Decide')).getAllByTestId('issue-line').length).toBeGreaterThan(0);
    expect(stored()).toEqual({ Decide: true });

    await userEvent.click(toggle('Decide'));
    expect(toggle('Decide')).toHaveAttribute('aria-expanded', 'false');
    expect(region('Decide')).not.toBeVisible();
    expect(stored()).toEqual({ Decide: false });
  });

  it('a re-mount restores each lane from storage, and the two lanes are independent', async () => {
    const first = renderWidget();
    await ready();
    await userEvent.click(toggle('Do'));
    expect(stored()).toEqual({ Do: true });
    first.unmount();

    renderWidget();
    await ready();
    expect(toggle('Do')).toHaveAttribute('aria-expanded', 'true');
    expect(region('Do')).toBeVisible();
    expect(toggle('Decide')).toHaveAttribute('aria-expanded', 'false');
    expect(region('Decide')).not.toBeVisible();

    // Opening Decide keeps Do's stored state.
    await userEvent.click(toggle('Decide'));
    expect(stored()).toEqual({ Do: true, Decide: true });
  });

  it('storage that throws gives a collapsed list and a working toggle', async () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage blocked');
    });
    renderWidget();
    await ready();
    expect(toggle('Decide')).toHaveAttribute('aria-expanded', 'false');
    expect(region('Decide')).not.toBeVisible();

    await userEvent.click(toggle('Decide'));
    expect(toggle('Decide')).toHaveAttribute('aria-expanded', 'true');
    expect(region('Decide')).toBeVisible();
    await userEvent.click(toggle('Decide'));
    expect(region('Decide')).not.toBeVisible();
  });

  it('corrupt stored JSON is treated as no stored value (collapsed)', async () => {
    window.localStorage.setItem(LANE_OPEN_STORAGE_KEY, '{not json');
    renderWidget();
    await ready();
    expect(toggle('Do')).toHaveAttribute('aria-expanded', 'false');
  });

  it('a metric card opens its own lane only, and stores the open state', async () => {
    renderWidget();
    await ready();
    const card = within(lane('Do'))
      .getAllByRole('button', { name: /./ })
      .find(b => b.getAttribute('aria-pressed') === 'false' && !b.hasAttribute('disabled'))!;
    await userEvent.click(card);
    expect(toggle('Do')).toHaveAttribute('aria-expanded', 'true');
    expect(region('Do')).toBeVisible();
    expect(within(region('Do')).getByTestId('lane-showing-Do')).toBeVisible();
    expect(toggle('Decide')).toHaveAttribute('aria-expanded', 'false');
    expect(stored()).toEqual({ Do: true });
  });

  it('Start review works while the list is collapsed, and walks the lane full visible list', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    expect(region('Decide')).not.toBeVisible();
    await userEvent.click(within(lane('Decide')).getByRole('button', { name: 'Start review' }));
    expect(launchWizard.mock.calls[0][0].itemId).toBe(A1);
    expect(launchWizard.mock.calls[0][0].visibleList.length).toBeGreaterThan(1);
    expect(region('Decide')).not.toBeVisible();
  });
});
