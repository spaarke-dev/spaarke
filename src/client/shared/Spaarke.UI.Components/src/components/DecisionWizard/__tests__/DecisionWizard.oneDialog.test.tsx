/**
 * DecisionWizard.oneDialog - the decision wizard shows ONE dialog from launch to close (ontology task 165, UAT round 1
 * re-check, check 2). Loading, a load error and Try again, ready, moving between items and recording change the content of
 * the same dialog; the `role="dialog"` DOM node is never unmounted and remounted (no close-and-reopen, no second open
 * animation, no focus jump to the page behind).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DecisionWizard } from '../DecisionWizard';
import {
  PATH_B_ID,
  THRESHOLD_ID,
  TODAY,
  approveFirstPlan,
  pathBItem,
  pathBPlan,
  thresholdItem,
} from '../__fixtures__/decisionWizardFixtures';
import type { DecisionWizardItem } from '../decisionWizardTypes';
import { makeLoader, makeLookup, makeService } from './decisionTestKit';

const plans = { [PATH_B_ID]: pathBPlan, [THRESHOLD_ID]: approveFirstPlan };

const exact = (name: string) => new RegExp(`^${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`, 'i');
const button = (name: string) => screen.getByRole('button', { name: exact(name) });
const click = (element: Element) => {
  if (element instanceof HTMLInputElement) fireEvent.focus(element);
  fireEvent.click(element);
};
const dialog = () => screen.getByRole('dialog');
const stepLabels = () =>
  within(screen.getByRole('navigation', { name: 'Wizard steps' }))
    .getAllByRole('listitem')
    .map(li => (li.textContent ?? '').trim());
const found = () => screen.findByTestId('step-found');

/** A loader whose reads are held until the test releases them. */
function heldLoader(items: DecisionWizardItem[]) {
  const byId = new Map(items.map(i => [i.row.signalId, i]));
  const held = new Map<string, () => void>();
  const loadItem = jest.fn(
    (id: string) =>
      new Promise<DecisionWizardItem>((resolve, reject) => {
        held.set(id, () => {
          const item = byId.get(id);
          if (item) resolve(item);
          else reject(new Error(`No item ${id}`));
        });
      })
  );
  const release = async (id: string) => {
    await waitFor(() => expect(held.has(id)).toBe(true));
    await act(async () => {
      held.get(id)?.();
      held.delete(id);
    });
  };
  return { loadItem, release };
}

function renderOpen(props: Partial<React.ComponentProps<typeof DecisionWizard>> & { items: DecisionWizardItem[] }) {
  const { items, ...rest } = props;
  const service = makeService({ plans });
  const onClose = jest.fn();
  const ui = (
    <FluentProvider theme={webLightTheme}>
      <DecisionWizard
        service={service}
        loadItem={makeLoader(items)}
        itemIds={items.map(i => i.row.signalId)}
        startItemId={items[0].row.signalId}
        onClose={onClose}
        lookup={makeLookup()}
        today={TODAY}
        {...rest}
      />
    </FluentProvider>
  );
  return { ...render(ui), service, onClose };
}

describe('one dialog from launch to close', () => {
  it('(a) loading -> ready keeps the same dialog node, with the steps changing in place', async () => {
    const held = heldLoader([pathBItem]);
    renderOpen({ items: [pathBItem], loadItem: held.loadItem });
    await screen.findByTestId('item-loading');
    const first = dialog();
    expect(stepLabels()).toEqual(['Loading']);

    await held.release(PATH_B_ID);
    await found();
    expect(dialog()).toBe(first);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(stepLabels()[0]).toBe('What was found');
    expect(stepLabels().length).toBeGreaterThan(1);
  });

  it('(b) loading -> error -> Try again -> ready keeps the same dialog node', async () => {
    const items = [pathBItem];
    const loadItem = jest
      .fn()
      .mockRejectedValueOnce(new Error('The item could not be read.'))
      .mockImplementation(async (id: string) => items.find(i => i.row.signalId === id));
    renderOpen({ items, loadItem });
    expect(await screen.findByTestId('item-error')).toHaveTextContent('The item could not be read.');
    const first = dialog();
    expect(stepLabels()).toEqual(['Unavailable']);

    click(button('Try again'));
    await found();
    expect(dialog()).toBe(first);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
  });

  it('(c) moving to the next and previous item, including one still loading, keeps the same dialog node and starts at the first step', async () => {
    const items = [pathBItem, thresholdItem];
    const byId = new Map(items.map(i => [i.row.signalId, i]));
    const second = heldLoader([thresholdItem]);
    const loadItem = jest.fn((id: string) =>
      id === THRESHOLD_ID ? second.loadItem(id) : Promise.resolve(byId.get(id) as DecisionWizardItem)
    );
    renderOpen({ items, loadItem });
    await found();
    const first = dialog();
    // Be partway through item 1 so "starts at the first step" means something.
    click(button('Next'));
    await screen.findByTestId('step-action-send-budget-inquiry');

    click(screen.getByRole('button', { name: /next record/i }));
    await screen.findByTestId('item-loading');
    expect(dialog()).toBe(first);
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
    expect(stepLabels()).toEqual(['Loading']);

    await second.release(THRESHOLD_ID);
    await found();
    expect(dialog()).toBe(first);
    expect(stepLabels()[0]).toBe('What was found');

    // Back to item 1: it starts at its first step again, in the same dialog.
    click(screen.getByRole('button', { name: /previous record/i }));
    expect(await screen.findByText('1 of 2')).toBeInTheDocument();
    await found();
    expect(dialog()).toBe(first);
    expect(stepLabels()[0]).toBe('What was found');
    expect(screen.queryByTestId('step-action-send-budget-inquiry')).toBeNull();
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
  });

  it('(d) recording keeps the same dialog node and shows the read-only Confirm step', async () => {
    const { service } = renderOpen({ items: [thresholdItem] });
    await found();
    const first = dialog();
    click(button('Next'));
    fireEvent.change(screen.getByRole('textbox', { name: /^Approved overage/ }), { target: { value: '10' } });
    click(button('Next'));
    click(button('Skip'));
    click(button('Next'));
    await screen.findByTestId('step-confirm');
    expect(dialog()).toBe(first);

    click(button('Record decision'));
    await waitFor(() => expect(service.commit).toHaveBeenCalledTimes(1));
    const bar = await screen.findByTestId('status-bar');
    await waitFor(() => expect(bar).toHaveFocus());
    expect(await screen.findByTestId('step-confirm')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: exact('Record decision') })).toBeNull();
    expect(dialog()).toBe(first);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
  });

  it('must-not-fire: closing the wizard removes the dialog', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(true);
      return open ? (
        <FluentProvider theme={webLightTheme}>
          <DecisionWizard
            service={makeService({ plans })}
            loadItem={makeLoader([pathBItem])}
            itemIds={[PATH_B_ID]}
            startItemId={PATH_B_ID}
            onClose={() => setOpen(false)}
            today={TODAY}
          />
        </FluentProvider>
      ) : null;
    };
    render(<Host />);
    await found();
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    click(button('Close'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });
});
