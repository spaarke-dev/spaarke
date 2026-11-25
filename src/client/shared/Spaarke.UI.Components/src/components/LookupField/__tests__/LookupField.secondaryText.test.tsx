/**
 * LookupField - the optional secondary line (spaarke-ontology-platform-r1 task 211, #3062).
 *
 * `ILookupItem.secondaryText` is shown under the name in the result list so that two options with the same name (three active
 * contacts called the same, each with their own email) can be told apart. The contract:
 *  - an option WITH `secondaryText` shows it, as its own line, under the name
 *  - must-not-fire: an option WITHOUT it renders exactly as before (the name's `Text` and nothing else), so every other consumer
 *    of the shared field is unchanged
 *  - picking an option still reports the whole item through `onChange`, by mouse and by keyboard
 */

import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { LookupField } from '../LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';

const LABEL = 'Assign to';

const TWINS: ILookupItem[] = [
  { id: '1', name: 'Sam Rivera', secondaryText: 'sam.rivera@example.com' },
  { id: '2', name: 'Sam Rivera', secondaryText: 'srivera@example.com' },
  { id: '3', name: 'Sam Rivera' },
];

function renderField(items: ILookupItem[]) {
  const onChange = jest.fn();
  const onSearch = jest.fn(async () => items);
  renderWithProviders(<LookupField label={LABEL} value={null} onChange={onChange} onSearch={onSearch} />);
  return { onChange, onSearch };
}

const openList = async () => {
  fireEvent.click(screen.getByRole('button', { name: `Browse ${LABEL}` }));
  await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());
  return within(screen.getByRole('listbox')).getAllByRole('option');
};

describe('LookupField secondary text', () => {
  it('shows each option secondary line under its name, so same-name options are told apart', async () => {
    renderField(TWINS);

    const options = await openList();

    expect(options).toHaveLength(3);
    expect(options[0]).toHaveTextContent('Sam Rivera');
    expect(within(options[0]).getByText('sam.rivera@example.com')).toBeInTheDocument();
    expect(within(options[1]).getByText('srivera@example.com')).toBeInTheDocument();
    // The name is still its own element, and the secondary line is a separate one after it.
    expect(
      within(options[0])
        .getAllByText(/./)
        .map(n => n.textContent)
    ).toEqual(['Sam Rivera', 'sam.rivera@example.com']);
  });

  it('must-not-fire: an option with no secondary text renders only its name (the shape every other consumer gets)', async () => {
    renderField(TWINS);

    const options = await openList();

    expect(options[2].children).toHaveLength(1);
    expect(options[2].children[0].tagName).toBe('SPAN');
    expect(options[2].textContent).toBe('Sam Rivera');
  });

  it('must-not-fire: a list of items without secondary text has the same option markup as before', async () => {
    renderField([
      { id: 'a', name: 'Commercial Transactions' },
      { id: 'b', name: 'Mergers & Acquisitions', email: 'ignored@example.com' },
    ]);

    const options = await openList();

    // `email` alone (what the recipient pickers set) is never shown by the shared field.
    expect(options.map(o => o.textContent)).toEqual(['Commercial Transactions', 'Mergers & Acquisitions']);
    for (const o of options) expect(o.children).toHaveLength(1);
  });

  it('a mouse pick reports the whole item, secondary text included', async () => {
    const { onChange } = renderField(TWINS);

    const options = await openList();
    fireEvent.click(options[1]);

    expect(onChange).toHaveBeenCalledWith(TWINS[1]);
  });

  it('a keyboard pick reports the highlighted item', async () => {
    const { onChange } = renderField(TWINS);
    const input = screen.getByRole('textbox', { name: new RegExp(LABEL) });

    fireEvent.focus(input);
    fireEvent.click(screen.getByRole('button', { name: `Browse ${LABEL}` }));
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'Enter' });

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0][0].id).toBe('2');
  });
});
