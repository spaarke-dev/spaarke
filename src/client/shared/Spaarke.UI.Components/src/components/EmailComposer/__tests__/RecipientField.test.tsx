/**
 * RecipientField.test.tsx (task 023, W2)
 *
 * Behavior contract for THE single canonical recipient-normalization surface
 * (design §5.6.1): `;`/`,`/mixed paste parsing, chip removal, a11y labels, and
 * directory resolution via the injected `onSearch` boundary
 * (`searchUsersAndContacts`, mocked).
 *
 * RecipientField is a controlled component (value + onChange). These tests wrap
 * it in a tiny stateful harness so the parse → onChange → re-render loop is
 * exercised the way the engine drives it — the boundary that is mocked is the
 * directory search, nothing internal to React.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { RecipientField } from '../subcomponents/RecipientField';
import type { IRecipient } from '../EmailComposer.types';
import type { ILookupItem } from '../../../types/LookupTypes';

interface HarnessProps {
  label?: string;
  required?: boolean;
  initial?: IRecipient[];
  onSearch?: (query: string) => Promise<ILookupItem[]>;
  onChangeSpy?: (r: IRecipient[]) => void;
}

function Harness({ label = 'To', required, initial = [], onSearch, onChangeSpy }: HarnessProps) {
  const [value, setValue] = React.useState<IRecipient[]>(initial);
  return (
    <RecipientField
      label={label}
      required={required}
      value={value}
      onChange={next => {
        setValue(next);
        onChangeSpy?.(next);
      }}
      onSearch={onSearch}
    />
  );
}

function getInput(label = 'To'): HTMLInputElement {
  return screen.getByRole('textbox', { name: label }) as HTMLInputElement;
}

describe('RecipientField — canonical ;/,/mixed normalization', () => {
  it('parses a mixed "; and ," paste into three recipients', () => {
    renderWithProviders(<Harness />);
    const input = getInput();

    // The last separator is the comma before charlie; the component commits
    // everything up to it and keeps the remainder as the live draft, so a
    // trailing blur commits the final token.
    fireEvent.change(input, {
      target: { value: 'Alice <alice@example.com>; bob@example.com,charlie@example.com' },
    });
    fireEvent.blur(input);

    const group = screen.getByRole('group', { name: 'To' });
    expect(within(group).getByText(/alice@example\.com/)).toBeInTheDocument();
    expect(within(group).getByText('bob@example.com')).toBeInTheDocument();
    expect(within(group).getByText('charlie@example.com')).toBeInTheDocument();
  });

  it('commits a single free-text address on Enter', () => {
    const onChangeSpy = jest.fn();
    renderWithProviders(<Harness onChangeSpy={onChangeSpy} />);
    const input = getInput();

    fireEvent.change(input, { target: { value: 'dave@example.com' } });
    fireEvent.keyDown(input, { key: 'Enter' });

    expect(onChangeSpy).toHaveBeenCalledWith([expect.objectContaining({ email: 'dave@example.com' })]);
  });

  it('de-duplicates a repeated address (case-insensitive)', () => {
    const onChangeSpy = jest.fn();
    renderWithProviders(<Harness initial={[{ email: 'a@example.com', resolved: false }]} onChangeSpy={onChangeSpy} />);
    const input = getInput();

    fireEvent.change(input, { target: { value: 'A@Example.com;' } });

    // The only committed token duplicates an existing recipient → no onChange.
    expect(onChangeSpy).not.toHaveBeenCalled();
  });

  it('accepts an invalid free-text token as a chip (validity is enforced by validateState, not the field)', () => {
    const onChangeSpy = jest.fn();
    renderWithProviders(<Harness onChangeSpy={onChangeSpy} />);
    const input = getInput();

    fireEvent.change(input, { target: { value: 'not-an-email' } });
    fireEvent.keyDown(input, { key: 'Enter' });

    // The field intentionally commits the raw token; the engine's validateState
    // raises TO_INVALID_EMAIL (see validation.test.ts) — the chip is not silently dropped.
    expect(onChangeSpy).toHaveBeenCalledWith([expect.objectContaining({ email: 'not-an-email' })]);
  });
});

describe('RecipientField — chip removal', () => {
  it('Backspace on an empty draft removes the last chip', () => {
    const onChangeSpy = jest.fn();
    renderWithProviders(
      <Harness
        initial={[
          { email: 'a@example.com', resolved: false },
          { email: 'b@example.com', resolved: false },
        ]}
        onChangeSpy={onChangeSpy}
      />
    );
    const input = getInput();

    fireEvent.keyDown(input, { key: 'Backspace' });

    expect(onChangeSpy).toHaveBeenCalledWith([{ email: 'a@example.com', resolved: false }]);
  });
});

describe('RecipientField — accessibility labels', () => {
  it.each(['To', 'Cc', 'Bcc'])('exposes an aria-labelled group + input for %s', label => {
    renderWithProviders(<Harness label={label} />);
    expect(screen.getByRole('group', { name: label })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: label })).toBeInTheDocument();
  });
});

describe('RecipientField — directory resolution via onSearch boundary', () => {
  it('resolves a directory match into a filled chip carrying the extracted email', async () => {
    const onSearch = jest.fn().mockResolvedValue([{ id: 'user-1', name: 'Erin Example (erin@example.com)' }]);
    const onChangeSpy = jest.fn();
    renderWithProviders(<Harness onSearch={onSearch} onChangeSpy={onChangeSpy} />);
    const input = getInput();

    fireEvent.change(input, { target: { value: 'erin' } });

    // Debounced search (300 ms) → results listbox appears.
    const option = await screen.findByRole('option', { name: /Erin Example/ }, { timeout: 2000 });
    expect(onSearch).toHaveBeenCalledWith('erin');

    fireEvent.click(option);

    await waitFor(() =>
      expect(onChangeSpy).toHaveBeenCalledWith([
        expect.objectContaining({
          email: 'erin@example.com',
          displayName: 'Erin Example',
          resolved: true,
          sourceId: 'user-1',
        }),
      ])
    );
  });

  it('does not search for a draft shorter than two characters', async () => {
    const onSearch = jest.fn().mockResolvedValue([]);
    renderWithProviders(<Harness onSearch={onSearch} />);
    const input = getInput();

    fireEvent.change(input, { target: { value: 'e' } });

    // Give the debounce window a chance to (not) fire.
    await new Promise(r => setTimeout(r, 350));
    expect(onSearch).not.toHaveBeenCalled();
  });
});
