/**
 * DataGrid.moneySort.test — Money columns sort by amount, and export what they display
 * (spaarke-SPA-external-access-platform-r4 task 033, FR-20, #2446 / #2447). Pure functions only; the rendered grid
 * (Fluent's own sort, the host-controlled sort, the Export command) is DataGrid.moneySort.render.test.tsx.
 *
 * Wire shape (FetchService.ProjectEntity): a Money column arrives as `{ value: n, extensionData: null }` with its
 * display text under `@formattedValues[column]`. The comparator also takes a plain number and a numeric string.
 *
 * Empty rule: a row with no amount sorts LOWEST (first ascending, last descending); two empties tie. Fluent multiplies
 * the comparator by -1 for descending, which `sortRows` below does the same way.
 */
import { compareColumnValues, moneyAmountOf, moneyExportText, renderCellValue } from '../DataGrid';

const MONEY = { name: 'sprk_totalamount', renderer: 'currency' };

const money = (n: number) => ({ value: n, extensionData: null });

/** Fluent's useTableSort: compare(a, b) * (ascending ? 1 : -1). */
function sortRows<T extends Record<string, unknown>>(
  rows: T[],
  column: { name: string; renderer: string },
  direction: 'ascending' | 'descending'
): T[] {
  const mod = direction === 'ascending' ? 1 : -1;
  return [...rows].sort((a, b) => compareColumnValues(a, b, column) * mod);
}

const ids = (rows: Array<Record<string, unknown>>) => rows.map(r => r.id);

describe('moneyAmountOf — the value shapes a Money cell can carry', () => {
  it.each([
    ['the SDK object', money(1200.5), 1200.5],
    ['the SDK object with a capitalised key', { Value: 7, extensionData: null }, 7],
    ['a plain number', 9, 9],
    ['zero', 0, 0],
    ['a negative amount', money(-4.25), -4.25],
    ['a numeric string', '10', 10],
    ['a padded decimal string', ' 100.50 ', 100.5],
    ['an object carrying a numeric string', { value: '12.5' }, 12.5],
  ])('reads %s', (_name, raw, expected) => {
    expect(moneyAmountOf(raw)).toBe(expected);
  });

  it.each([
    ['null', null],
    ['undefined', undefined],
    ['an empty string', ''],
    ['a blank string', '   '],
    ['a display label', '$1,200.00'],
    ['a hex-looking string', '0x10'],
    ['NaN', Number.NaN],
    ['Infinity', Number.POSITIVE_INFINITY],
    // Review K1 (task 033): numeric text that overflows a double used to read as Infinity.
    ['a numeric string that overflows to Infinity', '1e400'],
    ['an object carrying an overflowing numeric string', { value: '-1e400' }],
    ['an object with no value', { extensionData: null }],
    ['an object with a null value', { value: null }],
    ['an array', [5]],
  ])('has no amount for %s', (_name, raw) => {
    expect(moneyAmountOf(raw)).toBeNull();
  });
});

describe('compareColumnValues — Money (renderer currency)', () => {
  const rows = [
    { id: 'ten', sprk_totalamount: money(10), '@formattedValues': { sprk_totalamount: '$10.00' } },
    { id: 'nine', sprk_totalamount: money(9), '@formattedValues': { sprk_totalamount: '$9.00' } },
    { id: 'hundred', sprk_totalamount: money(100), '@formattedValues': { sprk_totalamount: '$100.00' } },
  ];

  it('orders $9, $10, $100 by value ascending, and $100, $10, $9 descending', () => {
    expect(ids(sortRows(rows, MONEY, 'ascending'))).toEqual(['nine', 'ten', 'hundred']);
    expect(ids(sortRows(rows, MONEY, 'descending'))).toEqual(['hundred', 'ten', 'nine']);
  });

  it('orders by value when the amounts arrive as a mix of object, number and numeric string', () => {
    const mixed = [
      { id: 'ten', sprk_totalamount: '10' },
      { id: 'nine', sprk_totalamount: 9 },
      { id: 'hundred', sprk_totalamount: money(100) },
    ];
    expect(ids(sortRows(mixed, MONEY, 'ascending'))).toEqual(['nine', 'ten', 'hundred']);
    expect(ids(sortRows(mixed, MONEY, 'descending'))).toEqual(['hundred', 'ten', 'nine']);
  });

  it('orders negative amounts and zero below positive ones', () => {
    const signed = [
      { id: 'one', sprk_totalamount: money(1) },
      { id: 'minus', sprk_totalamount: money(-3) },
      { id: 'zero', sprk_totalamount: money(0) },
    ];
    expect(ids(sortRows(signed, MONEY, 'ascending'))).toEqual(['minus', 'zero', 'one']);
  });

  it('puts empty amounts lowest: first ascending, last descending, and two empties tie', () => {
    const withEmpties = [
      { id: 'nine', sprk_totalamount: money(9) },
      { id: 'null', sprk_totalamount: null },
      { id: 'ten', sprk_totalamount: money(10) },
      { id: 'missing' },
      { id: 'blank', sprk_totalamount: '' },
    ];
    const asc = ids(sortRows(withEmpties, MONEY, 'ascending'));
    expect(asc.slice(0, 3).sort()).toEqual(['blank', 'missing', 'null']);
    expect(asc.slice(3)).toEqual(['nine', 'ten']);
    const desc = ids(sortRows(withEmpties, MONEY, 'descending'));
    expect(desc.slice(0, 2)).toEqual(['ten', 'nine']);
    expect(desc.slice(2).sort()).toEqual(['blank', 'missing', 'null']);
    expect(compareColumnValues({ x: null }, { x: '' }, { name: 'x', renderer: 'currency' })).toBe(0);
  });
});

describe('compareColumnValues — every other column sorts as before (must-not-fire pair)', () => {
  it('a text column that holds digits still sorts as text, not by value', () => {
    const rows = [
      { id: 'ten', sprk_note: '10' },
      { id: 'nine', sprk_note: '9' },
      { id: 'hundred', sprk_note: '100' },
    ];
    const note = { name: 'sprk_note', renderer: 'default' };
    expect(ids(sortRows(rows, note, 'ascending'))).toEqual(['ten', 'hundred', 'nine']);
  });

  it('a plain number column (not currency) sorts by number', () => {
    const rows = [
      { id: 'ten', n: 10 },
      { id: 'nine', n: 9 },
      { id: 'hundred', n: 100 },
    ];
    expect(ids(sortRows(rows, { name: 'n', renderer: 'default' }, 'ascending'))).toEqual(['nine', 'ten', 'hundred']);
  });

  it('an option-set column sorts on its label, an empty one first (the cell text is empty)', () => {
    const row = (id: string, label?: string) => ({
      id,
      status: label ? { value: label.length, extensionData: null } : null,
      '@formattedValues': label ? { status: label } : undefined,
    });
    const rows = [row('sub', 'Submitted'), row('none'), row('app', 'Approved')];
    const status = { name: 'status', renderer: 'badge' };
    expect(ids(sortRows(rows, status, 'ascending'))).toEqual(['none', 'app', 'sub']);
  });

  it('a date column still compares the raw ISO text', () => {
    const rows = [
      { id: 'later', d: '2026-11-02T00:00:00Z' },
      { id: 'sooner', d: '2026-02-11T00:00:00Z' },
    ];
    expect(ids(sortRows(rows, { name: 'd', renderer: 'datetime' }, 'ascending'))).toEqual(['sooner', 'later']);
  });
});

describe('moneyExportText — the CSV text of a Money cell is its displayed text', () => {
  it('uses the server label for the SDK object, exactly what renderCellValue shows', () => {
    const record = { sprk_totalamount: money(1200), '@formattedValues': { sprk_totalamount: '$1,200.00' } };
    expect(moneyExportText(record, MONEY)).toBe('$1,200.00');
    expect(moneyExportText(record, MONEY)).toBe(renderCellValue(record.sprk_totalamount, 'currency', '$1,200.00'));
  });

  it('formats a plain number the way the cell does', () => {
    const record = { sprk_totalamount: 100 };
    expect(moneyExportText(record, MONEY)).toBe(renderCellValue(100, 'currency'));
  });

  it('is empty for an empty amount, as the cell is', () => {
    expect(moneyExportText({ sprk_totalamount: null }, MONEY)).toBe('');
    expect(moneyExportText({}, MONEY)).toBe('');
  });

  it('declines every other renderer, so their export is left alone', () => {
    const record = {
      sprk_note: 'x',
      status: { value: 1, extensionData: null },
      '@formattedValues': { status: 'Draft' },
    };
    for (const renderer of ['default', 'badge', 'link', 'date', 'datetime', 'dateonly', 'percentage']) {
      expect(moneyExportText(record, { name: 'status', renderer })).toBeUndefined();
    }
  });
});

describe('renderCellValue — a Money object (currency renderer)', () => {
  it('without a label, shows the client currency format of its amount, not JSON', () => {
    const shown = renderCellValue({ value: 1234.5, extensionData: null }, 'currency');
    expect(shown).toBe(renderCellValue(1234.5, 'currency'));
    expect(shown).not.toContain('extensionData');
    expect(shown).toMatch(/1\D?234\D50/); // the amount, with the locale's grouping
  });

  it('with a label, still shows the label (must-not-fire)', () => {
    expect(renderCellValue({ value: 1234.5, extensionData: null }, 'currency', 'EUR 1.234,50')).toBe('EUR 1.234,50');
  });

  it('a plain number and a numeric string format as before', () => {
    expect(renderCellValue(9, 'currency')).toBe(renderCellValue('9', 'currency'));
  });
});
