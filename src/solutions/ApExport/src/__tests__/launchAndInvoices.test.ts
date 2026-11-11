import { MAX_SELECTION, parseLaunch } from '../services/launch';
import { formatAmount, invoiceQuery, orderRows, readInvoices, summarize, toInvoiceRow, type InvoiceRow } from '../services/invoices';
import { INV_A, INV_B, INV_C } from './testKit';

describe('parseLaunch', () => {
  it('reads the mode and the ids the ribbon script sends', () => {
    expect(parseLaunch({ mode: 'export', ids: `${INV_A},${INV_B}` })).toEqual({ ok: true, mode: 'export', invoiceIds: [INV_A, INV_B] });
    expect(parseLaunch({ mode: 'MarkPaid', ids: INV_A })).toEqual({ ok: true, mode: 'markpaid', invoiceIds: [INV_A] });
  });

  it('cleans braces and case, and drops repeats', () => {
    const r = parseLaunch({ mode: 'export', ids: `{${INV_A.toUpperCase()}}, ${INV_A},${INV_B}` });
    expect(r).toEqual({ ok: true, mode: 'export', invoiceIds: [INV_A, INV_B] });
  });

  it('refuses an unknown mode, no ids, a malformed id and too many ids', () => {
    expect(parseLaunch({ mode: 'delete', ids: INV_A }).ok).toBe(false);
    expect(parseLaunch({ ids: INV_A }).ok).toBe(false);
    expect(parseLaunch({ mode: 'export', ids: '' }).ok).toBe(false);
    expect(parseLaunch({ mode: 'export' }).ok).toBe(false);
    expect(parseLaunch({ mode: 'export', ids: `${INV_A},not-a-guid` }).ok).toBe(false);
    const many = Array.from({ length: MAX_SELECTION + 1 }, (_, i) => `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`);
    expect(parseLaunch({ mode: 'export', ids: many.join(',') })).toEqual({ ok: false, error: 'Select at most 200 invoices at a time.' });
    expect(parseLaunch({ mode: 'export', ids: many.slice(0, MAX_SELECTION).join(',') }).ok).toBe(true);
  });
});

describe('invoices (display read)', () => {
  const F = '@OData.Community.Display.V1.FormattedValue';
  const entity = (id: string, extra: Record<string, unknown> = {}) => ({
    sprk_invoiceid: id.toUpperCase(),
    sprk_name: 'INV-1',
    sprk_invoicenumber: '1001',
    sprk_trackingnumber: '1001.1',
    [`_sprk_vendororg_value${F}`]: 'Smith LLP',
    sprk_regardingrecordname: 'Matter One',
    sprk_invoicedate: '2026-10-01',
    [`sprk_invoicedate${F}`]: '10/1/2026',
    sprk_approvedtotal: 1000,
    sprk_currency: 'USD',
    statuscode: 659490000,
    [`statuscode${F}`]: 'Approved',
    ...extra,
  });

  it('queries the selection by id with the columns it shows', () => {
    const q = invoiceQuery([INV_A, INV_B]);
    expect(q).toContain(`PropertyValues=['${INV_A}','${INV_B}']`);
    expect(q).toContain('sprk_trackingnumber');
    expect(q).toContain('_sprk_vendororg_value');
  });

  it('maps a row with its formatted values', () => {
    expect(toInvoiceRow(entity(INV_A))).toEqual({
      id: INV_A,
      found: true,
      name: 'INV-1',
      invoiceNumber: '1001',
      trackingNumber: '1001.1',
      firmName: 'Smith LLP',
      recordName: 'Matter One',
      invoiceDate: '10/1/2026',
      approvedTotal: 1000,
      currency: 'USD',
      statusLabel: 'Approved',
    });
  });

  it('keeps the selection order and lists an id the read did not return', () => {
    const rows = orderRows([INV_B, INV_C, INV_A], [entity(INV_A), entity(INV_B)]);
    expect(rows.map(r => [r.id, r.found])).toEqual([
      [INV_B, true],
      [INV_C, false],
      [INV_A, true],
    ]);
  });

  it('a failed read lists the ids only and says so', async () => {
    const webApi = { retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('400')) };
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    const result = await readInvoices(webApi, [INV_A]);
    warn.mockRestore();
    expect(result.readFailed).toBe(true);
    expect(result.rows).toEqual([expect.objectContaining({ id: INV_A, found: false })]);
    expect(await readInvoices(undefined, [INV_A])).toEqual({ rows: [expect.objectContaining({ found: false })], readFailed: true });
  });

  it('sums the approved totals per currency', () => {
    const row = (total: number | null, currency: string | null): InvoiceRow => ({ ...toInvoiceRow(entity(INV_A)), approvedTotal: total, currency });
    expect(summarize([row(100.1, 'USD'), row(200.2, 'USD'), row(-50, 'USD'), row(null, null)])).toEqual({
      count: 4,
      totals: [{ currency: 'USD', amount: 250.3 }],
      withoutTotal: 1,
    });
    expect(summarize([row(1, 'USD'), row(2, 'EUR')]).totals).toEqual([
      { currency: 'USD', amount: 1 },
      { currency: 'EUR', amount: 2 },
    ]);
  });

  it('formats an amount with an ISO code, else a plain number and the code', () => {
    expect(formatAmount(1234.5, 'USD', 'en-US')).toBe('$1,234.50');
    expect(formatAmount(10, 'dollars', 'en-US')).toBe('10.00 dollars');
    expect(formatAmount(10, null, 'en-US')).toBe('10.00');
  });
});
