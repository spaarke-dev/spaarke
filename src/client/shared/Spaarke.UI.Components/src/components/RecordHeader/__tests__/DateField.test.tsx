/**
 * DateField — unit tests (FR-06, FR-10, FR-11, NFR-02, D-10).
 *
 * A standalone test file per task 010 constraints (task 015 owns
 * `fields.test.tsx` / `fields/index.ts` — this file must not touch either).
 *
 * The editor under test is the Fluent `Input` in native date mode
 * (`type="date"` / `type="datetime-local"`), which replaced
 * `@fluentui/react-datepicker-compat` to resolve the NFR-02 bundle breach.
 * The gestures therefore changed — there is no calendar popup to open and no
 * separate time input — but every FR-10 SEMANTIC below is unchanged.
 */

import * as React from 'react';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { DateField, fromInputValue, toInputValue } from '../fields/DateField';

const EMPTY_VALUE_GLYPH = '—';

// Fixed reference instant so formatted-output assertions are deterministic
// regardless of when the suite runs.
const ISO_DATE_ONLY = '2026-08-21T00:00:00.000Z';
const ISO_DATE_TIME = '2026-08-21T14:30:00.000Z';

const formatExpected = (iso: string, opts: Intl.DateTimeFormatOptions): string =>
  new Intl.DateTimeFormat(undefined, opts).format(new Date(iso));

/** The editor input — one control now, whatever the `format`. */
const editorInput = (): HTMLInputElement =>
  screen.getByTestId('record-header-date-field-input') as HTMLInputElement;

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

    expect(editorInput()).toBeInTheDocument();
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('true');
  });

  // ──────────────────────────────────────────────────────────────────────
  // NFR-02 — the editor is a native-typed Fluent Input, NOT a popup picker
  // ──────────────────────────────────────────────────────────────────────

  it('edits through a single Input whose type follows `format` (date vs datetime-local)', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    const { rerender } = renderWithProviders(
      <DateField label="Planned Start" span={1} format="date" value={ISO_DATE_TIME} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    expect(editorInput()).toHaveAttribute('type', 'date');
    // No second control: the retired picker needed a companion time input.
    expect(screen.queryByTestId('record-header-date-field-time-input')).not.toBeInTheDocument();

    // Still in edit mode — only the `format` prop changes, so no second click
    // is needed (and the read-mode value cell no longer exists).
    rerender(<DateField label="Planned Start" span={1} format="datetime" value={ISO_DATE_TIME} onSave={onSave} />);
    expect(editorInput()).toHaveAttribute('type', 'datetime-local');
    expect(screen.queryByTestId('record-header-date-field-time-input')).not.toBeInTheDocument();
  });

  it('seeds the editor with the local wall-clock form of the current value', () => {
    renderWithProviders(
      <DateField
        label="Planned Start"
        span={1}
        format="datetime"
        value={ISO_DATE_TIME}
        onSave={jest.fn().mockResolvedValue(undefined)}
      />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    expect(editorInput().value).toBe(toInputValue(new Date(ISO_DATE_TIME), 'datetime'));
  });

  // ──────────────────────────────────────────────────────────────────────
  // Edit-mode commit / cancel contract (mirrors TextField)
  // ──────────────────────────────────────────────────────────────────────

  it('Escape cancels — discards draft, exits edit mode, no save call', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    fireEvent.change(editorInput(), { target: { value: '2026-09-04' } });
    fireEvent.keyDown(editorInput(), { key: 'Escape' });

    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('false');
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(
      formatExpected(ISO_DATE_ONLY, { dateStyle: 'short' })
    );
  });

  it('Enter with an unchanged value exits edit mode without calling onSave', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    fireEvent.keyDown(editorInput(), { key: 'Enter' });

    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('false');
  });

  it('picking a new day then pressing Enter commits that exact local day', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    fireEvent.change(editorInput(), { target: { value: '2026-09-04' } });
    fireEvent.keyDown(editorInput(), { key: 'Enter' });

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const [committed]: [Date] = onSave.mock.calls[0];
    expect(committed).toBeInstanceOf(Date);
    // The day the user typed, in LOCAL terms — never shifted by a UTC round trip.
    expect(committed.getFullYear()).toBe(2026);
    expect(committed.getMonth()).toBe(8); // September
    expect(committed.getDate()).toBe(4);

    await waitFor(() =>
      expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('false')
    );
  });

  it('clearing the input commits null', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    fireEvent.change(editorInput(), { target: { value: '' } });
    fireEvent.blur(editorInput());

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave.mock.calls[0][0]).toBeNull();
  });

  it('reverts the draft and STAYS in edit mode when onSave rejects', async () => {
    const onSave = jest.fn().mockRejectedValue(new Error('save failed'));
    renderWithProviders(
      <DateField label="Invoice Date" span={1} format="date" value={ISO_DATE_ONLY} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    fireEvent.change(editorInput(), { target: { value: '2026-09-04' } });
    fireEvent.keyDown(editorInput(), { key: 'Enter' });

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));

    // Stays in edit mode.
    await waitFor(() =>
      expect(screen.getByTestId('record-header-date-field').getAttribute('data-editing')).toBe('true')
    );

    // Draft reverted to the wall-clock form of the ORIGINAL value.
    await waitFor(() =>
      expect(editorInput().value).toBe(toInputValue(new Date(ISO_DATE_ONLY), 'date'))
    );
  });

  // ──────────────────────────────────────────────────────────────────────
  // datetime mode — one datetime-local input carries both halves
  // ──────────────────────────────────────────────────────────────────────

  it('typing a new time and blurring commits a Date combining the date-part with the new time-part', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <DateField label="Planned Start" span={1} format="datetime" value={ISO_DATE_TIME} onSave={onSave} />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    const original = new Date(ISO_DATE_TIME);
    const datePart = toInputValue(original, 'datetime').slice(0, 10);
    fireEvent.change(editorInput(), { target: { value: `${datePart}T09:15` } });
    fireEvent.blur(editorInput());

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const [committed]: [Date] = onSave.mock.calls[0];
    expect(committed.getHours()).toBe(9);
    expect(committed.getMinutes()).toBe(15);
    // Date-part preserved from the original value (its LOCAL day).
    expect(committed.getFullYear()).toBe(original.getFullYear());
    expect(committed.getMonth()).toBe(original.getMonth());
    expect(committed.getDate()).toBe(original.getDate());
  });

  it('preserves the existing time-of-day when only the day changes in date mode', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    // A value with a non-midnight LOCAL time-of-day, expressed in local terms
    // so the assertion holds in every timezone the suite may run in.
    const base = new Date(2026, 7, 21, 17, 45, 0, 0);
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value={base} onSave={onSave} />);
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));

    fireEvent.change(editorInput(), { target: { value: '2026-08-22' } });
    fireEvent.keyDown(editorInput(), { key: 'Enter' });

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    const [committed]: [Date] = onSave.mock.calls[0];
    expect(committed.getDate()).toBe(22);
    expect(committed.getHours()).toBe(17);
    expect(committed.getMinutes()).toBe(45);
  });

  // ──────────────────────────────────────────────────────────────────────
  // Saving state — Spinner + disabled input
  // ──────────────────────────────────────────────────────────────────────

  it('shows a tiny Spinner and disables the input while saving', async () => {
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
    fireEvent.change(editorInput(), { target: { value: '2026-09-04' } });
    fireEvent.keyDown(editorInput(), { key: 'Enter' });

    await waitFor(() => expect(screen.getByTestId('record-header-date-field-spinner')).toBeInTheDocument());
    expect(editorInput()).toBeDisabled();

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

// ════════════════════════════════════════════════════════════════════════
// Timezone / wall-clock conversion — the classic day-shift failure mode
//
// These drive the pure converters directly so the assertions hold under ANY
// TZ the suite runs in (CI is usually UTC; developer machines are not).
// ════════════════════════════════════════════════════════════════════════

describe('DateField wall-clock conversion (NFR-02 editor)', () => {
  it('toInputValue reads LOCAL calendar fields, never toISOString (no UTC day shift)', () => {
    // 23:30 local on the 21st. `toISOString().slice(0,10)` would say the 22nd
    // in any zone east of UTC and the 21st west of it — the shift this guards.
    const late = new Date(2026, 7, 21, 23, 30, 0, 0);
    expect(toInputValue(late, 'date')).toBe('2026-08-21');
    expect(toInputValue(late, 'datetime')).toBe('2026-08-21T23:30');

    // And 00:30 local on the 21st, the mirror-image case.
    const early = new Date(2026, 7, 21, 0, 30, 0, 0);
    expect(toInputValue(early, 'date')).toBe('2026-08-21');
    expect(toInputValue(early, 'datetime')).toBe('2026-08-21T00:30');
  });

  it('toInputValue renders the empty string for null', () => {
    expect(toInputValue(null, 'date')).toBe('');
    expect(toInputValue(null, 'datetime')).toBe('');
  });

  it('fromInputValue builds a LOCAL date, never a UTC-midnight instant', () => {
    const parsed = fromInputValue('2026-08-21', 'date', null) as Date;
    expect(parsed.getFullYear()).toBe(2026);
    expect(parsed.getMonth()).toBe(7);
    expect(parsed.getDate()).toBe(21);
    expect(parsed.getHours()).toBe(0);
  });

  it('fromInputValue round-trips through toInputValue with no drift', () => {
    for (const iso of ['2026-01-01T00:00', '2026-06-30T12:00', '2026-12-31T23:59']) {
      const back = fromInputValue(iso, 'datetime', null) as Date;
      expect(toInputValue(back, 'datetime')).toBe(iso);
    }
  });

  it('fromInputValue returns null for empty or incomplete input', () => {
    expect(fromInputValue('', 'date', null)).toBeNull();
    expect(fromInputValue('2026-08', 'date', null)).toBeNull();
  });

  it('a bare yyyy-MM-dd Dataverse value parses as LOCAL midnight, not UTC midnight', () => {
    // `new Date('2026-08-21')` is UTC midnight by spec — in any zone west of
    // UTC that renders as Aug 20, blanking a day off every DateOnly column.
    renderWithProviders(<DateField label="Invoice Date" span={1} format="date" value="2026-08-21" />);
    const expected = new Intl.DateTimeFormat(undefined, { dateStyle: 'short' }).format(
      new Date(2026, 7, 21, 0, 0, 0, 0)
    );
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(expected);
  });

  it('a bare yyyy-MM-dd value seeds the editor with the SAME day it displays', () => {
    renderWithProviders(
      <DateField
        label="Invoice Date"
        span={1}
        format="date"
        value="2026-08-21"
        onSave={jest.fn().mockResolvedValue(undefined)}
      />
    );
    fireEvent.click(screen.getByTestId('record-header-date-field-value'));
    expect(editorInput().value).toBe('2026-08-21');
  });

  it('an ISO instant carrying an explicit offset is honored as a real instant', () => {
    // Not a bare date — must keep instant semantics and localize normally.
    const withOffset = '2026-08-21T14:30:00.000Z';
    renderWithProviders(<DateField label="Planned Start" span={1} format="datetime" value={withOffset} />);
    const expected = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' }).format(
      new Date(withOffset)
    );
    expect(screen.getByTestId('record-header-date-field-value')).toHaveTextContent(expected);
  });
});
