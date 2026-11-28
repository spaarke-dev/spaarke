/**
 * GetStartedCardsWidget `cardIds` and the reconciliation CreateRecordChooserModal (ebilling-r1 def071 review, F1).
 *
 * The chooser offers only what it can launch: Matter, Project and Assign Work. Before, it rendered every card of the shared widget,
 * and a click on any other (Create Invoice among them) closed the chooser and launched nothing. The widget's other consumers pass no
 * `cardIds` and keep every card.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { GetStartedCardsWidget } from '../GetStartedCardsWidget';
import type { GetStartedCardId } from '../GetStartedCardsWidget';
import { CreateRecordChooserModal } from '../CreateRecordChooserModal';

const mockLaunchSurface = jest.fn();
jest.mock('../../../services/surfaceHandoff/launchSurface', () => ({
  launchSurface: (...args: unknown[]) => mockLaunchSurface(...args),
}));

const ALL_LABELS = [
  'Create Matter',
  'Create Project',
  'Assign Work',
  'Create Invoice',
  'Summarize Files',
  'Find Similar',
  'Send Email',
  'Schedule Meeting',
];

/** The labels of the cards inside the widget, in display order (each card is a role="button" in a gridcell). */
function cardLabels(): string[] {
  const grid = screen.getByTestId('getstartedcards-widget');
  return within(grid)
    .getAllByRole('gridcell')
    .map(cell => within(cell).getByRole('button').getAttribute('aria-label') ?? '')
    .map(label => label.split(' — ')[0]);
}

describe('GetStartedCardsWidget — cardIds', () => {
  it("must not fire: without cardIds every card renders (the widget's other consumers are unchanged)", () => {
    renderWithProviders(<GetStartedCardsWidget />);
    expect(cardLabels()).toEqual(ALL_LABELS);
  });

  it('must fire: with cardIds only those cards render, in the widget display order, and a click reports the card id', () => {
    const onCardClick = jest.fn();
    const ids: GetStartedCardId[] = ['assign-work', 'create-matter-wizard'];
    renderWithProviders(<GetStartedCardsWidget cardIds={ids} onCardClick={onCardClick} />);
    expect(cardLabels()).toEqual(['Create Matter', 'Assign Work']);

    fireEvent.click(screen.getByRole('button', { name: /^Assign Work/ }));
    expect(onCardClick).toHaveBeenCalledTimes(1);
    expect(onCardClick).toHaveBeenCalledWith('assign-work');
  });

  it('an empty list renders no card', () => {
    renderWithProviders(<GetStartedCardsWidget cardIds={[]} />);
    expect(screen.queryAllByRole('gridcell')).toHaveLength(0);
  });
});

describe('CreateRecordChooserModal — offers only the cards it can launch', () => {
  beforeEach(() => {
    mockLaunchSurface.mockReset();
    mockLaunchSurface.mockResolvedValue({ launched: true, result: { committed: true, recordId: 'rec-1' } });
  });

  function mountChooser() {
    const onResult = jest.fn();
    const onClose = jest.fn();
    renderWithProviders(
      <CreateRecordChooserModal open bffBaseUrl="https://bff.example.test" onResult={onResult} onClose={onClose} />
    );
    return { onResult, onClose };
  }

  it('shows exactly the three record cards: no Create Invoice, Summarize, Find Similar, Email or Meeting', () => {
    mountChooser();
    expect(cardLabels()).toEqual(['Create Matter', 'Create Project', 'Assign Work']);
    expect(screen.queryByRole('button', { name: /^Create Invoice/ })).toBeNull();
  });

  it('a record card still launches its wizard and reports the created record', async () => {
    const { onResult, onClose } = mountChooser();
    fireEvent.click(screen.getByRole('button', { name: /^Create Matter/ }));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(mockLaunchSurface).toHaveBeenCalledWith(expect.objectContaining({ consumerType: 'create-matter' }));
    await waitFor(() => expect(onResult).toHaveBeenCalledWith({ id: 'rec-1', entityType: 'sprk_matter' }));
  });
});
