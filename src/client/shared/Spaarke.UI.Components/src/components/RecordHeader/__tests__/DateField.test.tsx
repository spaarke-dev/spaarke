/**
 * DateField — unit tests (FR-06, FR-10, FR-11, D-10).
 *
 * A standalone test file per task 010 constraints (task 015 owns
 * `fields.test.tsx` / `fields/index.ts` — this file must not touch either).
 */

import * as React from 'react';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { DateField } from '../fields/DateField';

const EMPTY_VALUE_GLYPH = '—';

// Fixed reference instant so formatted-output assertions are deterministic
// regardless of when the suite runs.
const ISO_DATE_ONLY = '2026-08-21T00:00:00.000Z';
const ISO_DATE_TIME = '2026-08-21T14:30:00.000Z';

const formatExpected = (iso: string, opts: Intl.DateTimeFormatOptions): string =>
  new Intl.DateTimeFormat(undefined, opts).format(new Date(iso));

describe('DateField', () => {
  // ──────────────────────────────────────────────────────────────────────
  // Read mode — format switching (one component, mode via prop)
  // ──────────────────────────────────────────────────────────────────────

  it('renders a locale date with no time component when format="date"', () => {
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} />);
    const expected = formatExpected(ISO_DATE_ONLY, { dateStyle: 'short' });
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(expected);
  });

  it('renders locale date AND time when format="datetime"', () => {
    renderWithProviders(<DateField label="Planned Start" span={1} format="datetime" value={ISO_DATE_TIME} />);
    const expected = formatExpected(ISO_DATE_TIME, { dateStyle: 'short', timeStyle: 'short' });
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(expected);
  });

  it('accepts a Date instance directly (not just ISO strings)', () => {
    const d = new Date(ISO_DATE_ONLY);
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={d} />);
    const expected = formatExpected(ISO_DATE_ONLY, { dateStyle: 'short' });
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(expected);
  });

  // ──────────────────────────────────────────────────────────────────────
  // Empty / invalid value handling (FR-11, NFR-10)
  // ──────────────────────────────────────────────────────────────────────

  it('renders an em-dash when value is null', () => {
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={null} />);
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(EMPTY_VALUE_GLYPH);
  });

  it('renders an em-dash when value is undefined', () => {
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={undefined} />);
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(EMPTY_VALUE_GLYPH);
  });

  it('renders an em-dash when value is an empty string', () => {
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value="" />);
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(EMPTY_VALUE_GLYPH);
  });

  it('renders an em-dash and console.warns (never throws, never "Invalid Date") for an unparseable string', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      expect(() =>
        renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value="not-a-date" />)
      ).not.toThrow();
      const valueEl = screen.getByTestId('record-header-date-field-value');
      expect(valueEl).toHaveTextContent(EMPTY_VALUE_GLYPH);
      expect(valueEl.textContent).not.toMatch(/invalid date/i);
      expect(warnSpy).toHaveBeenCalled();
    } finally {
      warnSpy.mockRestore();
    }
  });

  // ──────────────────────────────────────────────────────────────────────
  // FieldGrid integration — self-applied gridColumn (FR-03 contract)
  // ──────────────────────────────────────────────────────────────────────

  it('applies gridColumn: span N inline style per span prop', () => {
    const { rerender } = renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} />
    );
    const cell1 = screen.getByTestId('record-header-date-field');
    expect(cell1.style.gridColumn).toBe('span 1');
    expect(cell1.getAttribute('data-span')).toBe('1');

    rerender(<DateField label="Invoice Date" span={2} format="date" value={ISO_DATE_ONLY} />);
    expect(screen.getByTestId('record-header-date-field').style.gridColumn).toBe('span 2');

    rerender(<DateField label="Invoice Date" span={3} format="date" value={ISO_DATE_ONLY} />);
    expect(screen.getByTestId('record-header-date-field').style.gridColumn).toBe('span 3');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Editable gate
  // ──────────────────────────────────────────────────────────────────────

  it('is read-only (no role=button) when onSave is not provided', () => {
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} />);
    const valueEl = screen.getByTestId('record-header-date-field-value');
    expect(valueEl).not.toHaveAttribute('role', 'button');
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editable')).toBe('false');
  });

  it('is read-only when onSave is provided but disabled=true', () => {
    renderWithProviders(
      <DateField
        label="Invoice Date"
        span={1}
        format="date"
        value={ISO_DATE_ONLY}
        onSave={jest.fn().mockResolvedValue(undefined)}
        disabled
      />
    );
    const valueEl = screen.getByTestId('record-header-date-field-value');
    expect(valueEl).not.toHaveAttribute('role', 'button');
  });

  it('is editable (role=button, click enters edit mode) when onSave is provided alone', () => {
    renderWithProviders(
      <DateField
        label="Invoice Date"
        span={1}
        format="date"
        value={ISO_DATE_ONLY}
        onSave={jest.fn().mockResolvedValue(undefined)}
      />
    );
    const valueEl = screen.getByTestId('record-header-date-field-value');
    expect(valueEl).toHaveAttribute('role', 'button');

    fireEvent.click(valueEl);

    expect(screen.getByRole('combobox')).toBeInTheDocument();
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('true');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Edit-mode commit / cancel contract (mirrors TextField)
  // ──────────────────────────────────────────────────────────────────────

  it('Escape (calendar closed) cancels — discards draft, exits edit mode, no save call', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    const combobox = screen.getByRole('combobox');

    fireEvent.keyDown(combobox, { key: 'Escape' });

    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('false');
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(
      formatExpected(ISO_DATE_ONLY, { dateStyle: 'short' })
    );
  });

  it('Enter (calendar closed, unchanged value) exits edit mode without calling onSave', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    const combobox = screen.getByRole('combobox');

    fireEvent.keyDown(combobox, { key: 'Enter' });

    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('false');
  });

  it('selecting a calendar day commits immediately (form-buffer dirty on selection)', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    fireEvent.click(screen.getByRole('combobox'));

    const gridcells = await screen.findAllByRole('gridcell');
    fireEvent.click(gridcells[10]);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const [committed] = onSave.mock.calls[0];
    expect(committed).toBeInstanceOf(Date);

    await waitFor(() =>
      expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('false')
    );
  });

  it('reverts the draft and STAYS in edit mode when onSave rejects', async () => {
    const onSave = jest.fn().mockRejectedValue(new Error('save failed'));
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    fireEvent.click(screen.getByRole('combobox'));

    const gridcells = await screen.findAllByRole('gridcell');
    fireEvent.click(gridcells[10]);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));

    // Stays in edit mode.
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('true');
    expect(screen.getByRole('combobox')).toBeInTheDocument();

    // Draft reverted — the DatePicker's displayed input value is re-derived
    // from `value`, which is back to the original date once the draft state
    // reverts.
    await waitFor(() => {
      const input = screen.getByRole('combobox') as HTMLInputElement;
      expect(input.value).toBe(formatExpected(ISO_DATE_ONLY, { dateStyle: 'short' }));
    });
  });

  // ──────────────────────────────────────────────────────────────────────
  // datetime mode — time input alongside the date picker
  // ──────────────────────────────────────────────────────────────────────

  it('renders a time input alongside the date picker only in datetime mode', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    const { rerender } = renderWithProviders(
      <DateField label="Planned Start" span={1} format="date" value={ISO_DATE_TIME} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    expect(screen.queryByTestId('record-header-date-field-time-input')).not.toBeInTheDocument();

    // Still in edit mode — only the `format` prop changes, so no second
    // click is needed (and the read-mode value cell no longer exists).
    rerender(<DateField label="Planned Start" span={1} format="datetime" value={ISO_DATE_TIME} onSave={onSave} />);
    expect(screen.getByTestId('record-header-date-field-time-input')).toBeInTheDocument();
  });

  it('typing a new time and blurring commits a Date combining the original date-part with the new time-part', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Planned Start" span={1} format="datetime" value={ISO_DATE_TIME} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    const timeInput = screen.getByTestId('record-header-date-field-time-input');
    fireEvent.change(timeInput, { target: { value: '09:15' } });
    fireEvent.blur(timeInput);

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const [committed]: [Date] = onSave.mock.calls[0];
    expect(committed.getHours()).toBe(9);
    expect(committed.getMinutes()).toBe(15);
    // Date-part preserved from the original value.
    const original = new Date(ISO_DATE_TIME);
    expect(committed.getFullYear()).toBe(original.getFullYear());
    expect(committed.getMonth()).toBe(original.getMonth());
    expect(committed.getDate()).toBe(original.getDate());
  });

  // ──────────────────────────────────────────────────────────────────────
  // Saving state — Spinner + disabled inputs
  // ──────────────────────────────────────────────────────────────────────

  it('shows a tiny Spinner and disables inputs while saving', async () => {
    let resolveSave: () => void = () => undefined;
    const onSave = jest.fn(
      () =>
        new Promise<void>(resolve => {
          resolveSave = resolve;
        })
    );
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    fireEvent.click(screen.getByRole('combobox'));
    const gridcells = await screen.findAllByRole('gridcell');
    fireEvent.click(gridcells[10]);

    await waitFor(() => expect(screen.getByTestId('record-header-date-field-spinner')).toBeInTheDocument());
    expect(screen.getByRole('combobox')).toHaveAttribute('disabled');

    resolveSave();
    await waitFor(() => expect(screen.queryByTestId('record-header-date-field-spinner')).not.toBeInTheDocument());
  });

  // ──────────────────────────────────────────────────────────────────────
  // D-10 / FR-11 — required is accepted but visually inert
  // ──────────────────────────────────────────────────────────────────────

  it('renders NO marker when required=true (D-10: DateField never shows the asterisk)', () => {
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} required />
    );
    expect(screen.queryByText('*')).not.toBeInTheDocument();
  });

  it('renders NO marker when required is omitted', () => {
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} />);
    expect(screen.queryByText('*')).not.toBeInTheDocument();
  });
});
