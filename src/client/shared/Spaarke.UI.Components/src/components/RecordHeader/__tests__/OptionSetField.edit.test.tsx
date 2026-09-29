/**
 * OptionSetField — edit-mode unit tests (FR-09, FR-10).
 *
 * Standalone file per task 013 — does NOT append to `fields.test.tsx`
 * (owned by task 015 / the barrel-wiring task) and does NOT modify it. The
 * pre-existing read-only OptionSetField tests live there and MUST keep
 * passing unmodified; this file covers ONLY the new edit-mode surface.
 */

import * as React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { OptionSetField, type IOptionSetFieldOption } from '../fields/OptionSetField';

/** Hyphen glyph used for null / undefined / '' values (FR-04). */
const EMPTY_VALUE_GLYPH = '—';

const STATUS_OPTIONS: IOptionSetFieldOption[] = [
  { value: 1, label: 'Open' },
  { value: 2, label: 'Closed' },
];

describe('OptionSetField — edit mode (FR-09, FR-10)', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Editability gating
  // ─────────────────────────────────────────────────────────────────────

  describe('editability gating', () => {
    it('is read-only with no onSave — value is not clickable', () => {
      renderWithProviders(<OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} />);
      const valueEl = screen.getByTestId('record-header-optionset-field-value');
      expect(valueEl.getAttribute('role')).toBeNull();
      expect(valueEl.getAttribute('tabIndex')).toBeNull();
      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editable')).toBe('false');
    });

    it('is read-only when onSave is supplied but disabled=true', () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} disabled />
      );
      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editable')).toBe('false');
    });

    it('is read-only when onSave is supplied without an options array, and warns once', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      const onSave = jest.fn().mockResolvedValue(undefined);
      try {
        renderWithProviders(<OptionSetField span={1} label="Status" value="Open" onSave={onSave} />);
        const cell = screen.getByTestId('record-header-optionset-field');
        expect(cell.getAttribute('data-editable')).toBe('false');
        expect(warnSpy).toHaveBeenCalledTimes(1);
      } finally {
        warnSpy.mockRestore();
      }
    });

    it('is read-only when onSave is supplied with an EMPTY options array, and warns once', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      const onSave = jest.fn().mockResolvedValue(undefined);
      try {
        renderWithProviders(<OptionSetField span={1} label="Status" value="Open" options={[]} onSave={onSave} />);
        const cell = screen.getByTestId('record-header-optionset-field');
        expect(cell.getAttribute('data-editable')).toBe('false');
        expect(warnSpy).toHaveBeenCalledTimes(1);
      } finally {
        warnSpy.mockRestore();
      }
    });

    it('is editable when onSave + a non-empty options array are both supplied', () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} />
      );
      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editable')).toBe('true');
      const valueEl = screen.getByTestId('record-header-optionset-field-value');
      expect(valueEl.getAttribute('role')).toBe('button');
      expect(valueEl.getAttribute('tabIndex')).toBe('0');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Opening edit mode (spec success criterion 13)
  // ─────────────────────────────────────────────────────────────────────

  describe('opening edit mode', () => {
    it('clicking the value opens a Fluent Dropdown listing every option, with the current value selected', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-optionset-field-value'));
      });

      expect(screen.getByTestId('record-header-optionset-field-dropdown')).toBeInTheDocument();
      const openOption = screen.getByRole('option', { name: 'Open' });
      const closedOption = screen.getByRole('option', { name: 'Closed' });
      expect(openOption).toBeInTheDocument();
      expect(closedOption).toBeInTheDocument();
      expect(openOption.getAttribute('aria-selected')).toBe('true');
      expect(closedOption.getAttribute('aria-selected')).toBe('false');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Edit round-trip (FR-10 contract)
  // ─────────────────────────────────────────────────────────────────────

  describe('edit round-trip', () => {
    it('selecting a different option calls onSave exactly once with the numeric value, and exits edit mode on resolve', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-optionset-field-value'));
      });
      await act(async () => {
        await userEvent.click(screen.getByRole('option', { name: 'Closed' }));
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith(2);

      // The renderer is controlled — read mode falls back to the `value`
      // prop (unchanged in this test), matching TextField/NumberField: the
      // consumer is responsible for updating `value` after a successful
      // save (e.g. re-reading the attribute's FormattedValue). Exiting
      // edit mode with exactly one onSave call is the contract under test.
      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editing')).toBe('false');
    });

    it('selecting the CURRENT value exits edit mode with zero onSave calls', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-optionset-field-value'));
      });
      await act(async () => {
        await userEvent.click(screen.getByRole('option', { name: 'Open' }));
      });

      expect(onSave).not.toHaveBeenCalled();
      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editing')).toBe('false');
    });

    it('Escape exits edit mode with zero onSave calls', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-optionset-field-value'));
      });

      const dropdown = screen.getByTestId('record-header-optionset-field-dropdown');
      act(() => {
        fireEvent.keyDown(dropdown, { key: 'Escape' });
      });

      expect(onSave).not.toHaveBeenCalled();
      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editing')).toBe('false');
      expect(screen.getByTestId('record-header-optionset-field-value').textContent).toBe('Open');
    });

    it('reverts the draft and STAYS in edit mode when onSave rejects; shows a disabled Dropdown + tiny Spinner while saving', async () => {
      let rejectSave: (err: Error) => void = () => undefined;
      const onSave = jest.fn(
        () =>
          new Promise<void>((_resolve, reject) => {
            rejectSave = reject;
          })
      );
      renderWithProviders(
        <OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-optionset-field-value'));
      });
      await act(async () => {
        await userEvent.click(screen.getByRole('option', { name: 'Closed' }));
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(screen.getByTestId('record-header-optionset-field-spinner')).toBeInTheDocument();
      expect(screen.getByTestId('record-header-optionset-field-dropdown')).toBeDisabled();

      await act(async () => {
        rejectSave(new Error('save failed'));
        await Promise.resolve().catch(() => undefined);
      });

      const cell = screen.getByTestId('record-header-optionset-field');
      expect(cell.getAttribute('data-editing')).toBe('true');
      expect(screen.getByTestId('record-header-optionset-field-dropdown')).not.toBeDisabled();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // D-10 — no required marker anywhere; em-dash empty state unaffected
  // ─────────────────────────────────────────────────────────────────────

  describe('D-10 (no required marker) + empty-state parity', () => {
    it('renders no "*" marker anywhere in the component', () => {
      renderWithProviders(<OptionSetField span={1} label="Status" value="Open" options={STATUS_OPTIONS} />);
      expect(screen.queryByText('*')).not.toBeInTheDocument();
    });

    it('renders the em-dash when value is null even with options + onSave supplied', () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(
        <OptionSetField span={1} label="Status" value={null} options={STATUS_OPTIONS} onSave={onSave} />
      );
      expect(screen.getByTestId('record-header-optionset-field-value').textContent).toBe(EMPTY_VALUE_GLYPH);
    });
  });
});
