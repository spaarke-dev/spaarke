/**
 * NumberField — unit tests (FR-07, FR-10, FR-11 + D-10).
 *
 * Standalone file per task 011 — does NOT append to `fields.test.tsx`
 * (owned by task 015 / the barrel-wiring task). See task POML constraint
 * "Parallel-safety (scope: this task)".
 */

import * as React from 'react';
import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { NumberField, EMPTY_VALUE_PLACEHOLDER } from '../fields/NumberField';

describe('NumberField', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Formatting (FR-07 acceptance criterion 1)
  // ─────────────────────────────────────────────────────────────────────

  describe('formatting', () => {
    it('formats kind="money" with currency symbol and precision', () => {
      renderWithProviders(
        <NumberField span={1} label="Total Amount" value={12500} kind="money" precision={2} currencySymbol="$" />
      );
      expect(screen.getByText('$12,500.00')).toBeInTheDocument();
    });

    it('formats kind="integer" with no fraction digits', () => {
      renderWithProviders(<NumberField span={1} label="Count" value={42} kind="integer" />);
      expect(screen.getByText('42')).toBeInTheDocument();
    });

    it('formats kind="integer" ignoring a supplied precision (always 0 fraction digits)', () => {
      renderWithProviders(<NumberField span={1} label="Count" value={42} kind="integer" precision={3} />);
      expect(screen.getByText('42')).toBeInTheDocument();
    });

    it('formats kind="decimal" with the supplied precision', () => {
      renderWithProviders(<NumberField span={1} label="Rate" value={1.5} kind="decimal" precision={3} />);
      expect(screen.getByText('1.500')).toBeInTheDocument();
    });

    it('formats kind="double" with the supplied precision', () => {
      renderWithProviders(<NumberField span={1} label="Rate" value={1.5} kind="double" precision={3} />);
      expect(screen.getByText('1.500')).toBeInTheDocument();
    });

    it('defaults precision to 2 for decimal/double/money when omitted', () => {
      renderWithProviders(<NumberField span={1} label="Rate" value={1.5} kind="decimal" />);
      expect(screen.getByText('1.50')).toBeInTheDocument();
    });

    it('omits the currency symbol prefix for non-money kinds even if currencySymbol is supplied', () => {
      renderWithProviders(
        <NumberField span={1} label="Rate" value={1.5} kind="decimal" precision={2} currencySymbol="$" />
      );
      expect(screen.getByText('1.50')).toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Empty / invalid values (FR-11)
  // ─────────────────────────────────────────────────────────────────────

  describe('empty and invalid values', () => {
    it('renders "—" when value is null', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={null} kind="money" />);
      expect(screen.getByText(EMPTY_VALUE_PLACEHOLDER)).toBeInTheDocument();
    });

    it('renders "—" when value is undefined', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={undefined} kind="money" />);
      expect(screen.getByText(EMPTY_VALUE_PLACEHOLDER)).toBeInTheDocument();
    });

    it('renders "—" when value is an empty string', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value="" kind="money" />);
      expect(screen.getByText(EMPTY_VALUE_PLACEHOLDER)).toBeInTheDocument();
    });

    it('renders formatted "0" (NOT the em-dash) when value is the number 0 — strict empty check, never falsy', () => {
      renderWithProviders(<NumberField span={1} label="Count" value={0} kind="integer" />);
      expect(screen.getByText('0')).toBeInTheDocument();
      expect(screen.queryByText(EMPTY_VALUE_PLACEHOLDER)).not.toBeInTheDocument();
    });

    it('renders formatted "0.00" for money kind 0 value', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={0} kind="money" currencySymbol="$" />);
      expect(screen.getByText('$0.00')).toBeInTheDocument();
    });

    it('renders "—" and emits console.warn for a non-numeric string value — never a throw, never "NaN" text', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      try {
        renderWithProviders(<NumberField span={1} label="Total Amount" value="not-a-number" kind="money" />);
        expect(screen.getByText(EMPTY_VALUE_PLACEHOLDER)).toBeInTheDocument();
        expect(screen.queryByText('NaN')).not.toBeInTheDocument();
        expect(warnSpy).toHaveBeenCalled();
      } finally {
        warnSpy.mockRestore();
      }
    });

    it('parses a numeric string value correctly', () => {
      renderWithProviders(<NumberField span={1} label="Count" value="42" kind="integer" />);
      expect(screen.getByText('42')).toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Alignment (FR-07: numeric/money read right-aligned; label stays left)
  // ─────────────────────────────────────────────────────────────────────

  describe('alignment', () => {
    it('right-aligns the read-mode value cell', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={12500} kind="money" currencySymbol="$" />);
      const valueEl = screen.getByTestId('record-header-number-field-value');
      const cs = window.getComputedStyle(valueEl);
      expect(cs.justifyContent).toBe('flex-end');
      expect(cs.textAlign).toBe('right');
    });

    it('right-aligns the edit-mode input', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <NumberField span={1} label="Total Amount" value={12500} kind="money" currencySymbol="$" onSave={onSave} />
      );
      const valueEl = screen.getByTestId('record-header-number-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });
      const input = screen.getByTestId('record-header-number-field-input');
      expect(window.getComputedStyle(input).textAlign).toBe('right');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Grid span (FieldGrid integration)
  // ─────────────────────────────────────────────────────────────────────

  describe('grid span', () => {
    it('applies gridColumn: span N inline style per span prop', () => {
      const { rerender } = renderWithProviders(<NumberField span={1} label="X" value={1} kind="integer" />);
      const cell1 = screen.getByTestId('record-header-number-field');
      expect(cell1.style.gridColumn).toBe('span 1');
      expect(cell1.getAttribute('data-span')).toBe('1');

      rerender(<NumberField span={2} label="X" value={1} kind="integer" />);
      const cell2 = screen.getByTestId('record-header-number-field');
      expect(cell2.style.gridColumn).toBe('span 2');
      expect(cell2.getAttribute('data-span')).toBe('2');

      rerender(<NumberField span={3} label="X" value={1} kind="integer" />);
      const cell3 = screen.getByTestId('record-header-number-field');
      expect(cell3.style.gridColumn).toBe('span 3');
      expect(cell3.getAttribute('data-span')).toBe('3');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Editability gating
  // ─────────────────────────────────────────────────────────────────────

  describe('editability gating', () => {
    it('is read-only with no onSave — value is not clickable', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={12500} kind="money" currencySymbol="$" />);
      const valueEl = screen.getByTestId('record-header-number-field-value');
      expect(valueEl.getAttribute('role')).toBeNull();
      expect(valueEl.getAttribute('tabIndex')).toBeNull();
      const cell = screen.getByTestId('record-header-number-field');
      expect(cell.getAttribute('data-editable')).toBe('false');
    });

    it('is read-only when onSave is supplied but disabled=true', () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <NumberField span={1} label="Total Amount" value={12500} kind="money" onSave={onSave} disabled />
      );
      const cell = screen.getByTestId('record-header-number-field');
      expect(cell.getAttribute('data-editable')).toBe('false');
    });

    it('enters edit mode on click when onSave alone is supplied, showing the RAW unformatted number', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <NumberField
          span={1}
          label="Total Amount"
          value={12500.5}
          kind="money"
          precision={2}
          currencySymbol="$"
          onSave={onSave}
        />
      );
      const valueEl = screen.getByTestId('record-header-number-field-value');
      expect(valueEl.textContent).toBe('$12,500.50');

      await act(async () => {
        await userEvent.click(valueEl);
      });

      const input = screen.getByTestId('record-header-number-field-input') as HTMLInputElement;
      expect(input.value).toBe('12500.5');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Edit round-trip (FR-10 contract, copied verbatim from TextField)
  // ─────────────────────────────────────────────────────────────────────

  describe('edit round-trip', () => {
    it('Enter commits the parsed numeric value', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input');
      await act(async () => {
        await userEvent.clear(input);
        await userEvent.type(input, '25');
        await userEvent.keyboard('{Enter}');
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith(25);
    });

    it('Escape cancels and reverts the draft without calling onSave', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input') as HTMLInputElement;
      await act(async () => {
        await userEvent.clear(input);
        await userEvent.type(input, '999');
        await userEvent.keyboard('{Escape}');
      });

      expect(onSave).not.toHaveBeenCalled();
      // Back in read mode showing the original formatted value.
      expect(screen.getByTestId('record-header-number-field-value').textContent).toBe('10');
    });

    it('blur commits the draft', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input');
      await act(async () => {
        await userEvent.clear(input);
        await userEvent.type(input, '30');
      });
      await act(async () => {
        input.blur();
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith(30);
    });

    it('committing an unchanged draft is a no-op — onSave is called zero times', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input');
      await act(async () => {
        input.blur();
      });

      expect(onSave).not.toHaveBeenCalled();
    });

    it('an empty draft commits null', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input');
      await act(async () => {
        await userEvent.clear(input);
        input.blur();
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith(null);
    });

    it('a non-numeric draft never reaches onSave and the component stays in edit mode', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input');
      await act(async () => {
        await userEvent.clear(input);
        await userEvent.type(input, 'abc');
        await userEvent.keyboard('{Enter}');
      });

      expect(onSave).not.toHaveBeenCalled();
      // Still in edit mode — the input is still present.
      expect(screen.getByTestId('record-header-number-field-input')).toBeInTheDocument();
      const cell = screen.getByTestId('record-header-number-field');
      expect(cell.getAttribute('data-editing')).toBe('true');
    });

    it('reverts the draft and STAYS in edit mode when onSave rejects', async () => {
      const onSave = jest.fn().mockRejectedValue(new Error('save failed'));
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input') as HTMLInputElement;
      await act(async () => {
        await userEvent.clear(input);
        await userEvent.type(input, '999');
        await userEvent.keyboard('{Enter}');
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      // Still editing, draft reverted to the prior raw value.
      const cell = screen.getByTestId('record-header-number-field');
      expect(cell.getAttribute('data-editing')).toBe('true');
      const revertedInput = screen.getByTestId('record-header-number-field-input') as HTMLInputElement;
      expect(revertedInput.value).toBe('10');
    });

    it('shows a disabled input + tiny Spinner while saving', async () => {
      let resolveSave: () => void = () => undefined;
      const onSave = jest.fn(
        () =>
          new Promise<void>(resolve => {
            resolveSave = resolve;
          })
      );
      renderWithProviders(<NumberField span={1} label="Count" value={10} kind="integer" onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-number-field-value'));
      });
      const input = screen.getByTestId('record-header-number-field-input') as HTMLInputElement;
      await act(async () => {
        await userEvent.clear(input);
        await userEvent.type(input, '30');
        await userEvent.keyboard('{Enter}');
      });

      expect(screen.getByTestId('record-header-number-field-spinner')).toBeInTheDocument();
      expect(screen.getByTestId('record-header-number-field-input')).toBeDisabled();

      await act(async () => {
        resolveSave();
        await Promise.resolve();
      });
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // D-10 — required marker is deliberately TextField-only
  // ─────────────────────────────────────────────────────────────────────

  describe('required marker (D-10)', () => {
    it('renders NO "*" marker even when required=true', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={12500} kind="money" required />);
      expect(screen.queryByText('*')).not.toBeInTheDocument();
    });

    it('renders NO "*" marker when required is omitted', () => {
      renderWithProviders(<NumberField span={1} label="Total Amount" value={12500} kind="money" />);
      expect(screen.queryByText('*')).not.toBeInTheDocument();
    });
  });
});
