import { createFirmChoices, firmNameQuery, invoiceFirmQuery, INVOICE_LOOKUP, type WebApiLike } from '../services/firmChoices';
import { FENWICK, MATTER_ID, OKAFOR } from './fixtures';

const rows = (...entities: Array<Record<string, unknown>>) => Promise.resolve({ entities });
const fakeApi = (handler: (entity: string, options: string) => Promise<{ entities: Array<Record<string, unknown>> }>): WebApiLike => ({
  retrieveMultipleRecords: jest.fn(handler),
});

describe('invoicedBy', () => {
  it('reads the record invoices, then only the firms the caller can Read, by name', async () => {
    const api = fakeApi((entity) => {
      if (entity === 'sprk_invoice') {
        // Two invoices from Fenwick (one id in upper case), one from Okafor, one from a firm the caller cannot Read.
        return rows(
          { _sprk_vendororg_value: FENWICK.id },
          { _sprk_vendororg_value: FENWICK.id.toUpperCase() },
          { _sprk_vendororg_value: OKAFOR.id },
          { _sprk_vendororg_value: '99999999-0000-4000-8000-000000000009' }
        );
      }
      // The organization read as the caller returns only the rows the caller can Read.
      return rows(
        { sprk_organizationid: OKAFOR.id, sprk_organizationname: OKAFOR.name },
        { sprk_organizationid: FENWICK.id, sprk_organizationname: FENWICK.name }
      );
    });

    const result = await createFirmChoices(() => api).invoicedBy('sprk_matter', MATTER_ID);

    expect(result.complete).toBe(true);
    // Sorted by name; the unreadable firm is not offered.
    expect(result.firms).toEqual([
      { id: FENWICK.id, name: FENWICK.name },
      { id: OKAFOR.id, name: OKAFOR.name },
    ]);
    const calls = (api.retrieveMultipleRecords as jest.Mock).mock.calls;
    expect(calls[0]).toEqual(['sprk_invoice', invoiceFirmQuery('sprk_matter', MATTER_ID)]);
    expect(calls[1][0]).toBe('sprk_organization');
    // Every distinct firm id on the invoices is asked for once (the unreadable one too: the read is what filters it out).
    expect(calls[1][1]).toBe(firmNameQuery([FENWICK.id, OKAFOR.id, '99999999-0000-4000-8000-000000000009']));
  });

  it('asks the invoice lookup of the record kind, and reads no Procedure, override or rule table', async () => {
    expect(invoiceFirmQuery('sprk_matter', MATTER_ID)).toContain(`_sprk_matter_value eq ${MATTER_ID}`);
    expect(invoiceFirmQuery('sprk_project', MATTER_ID)).toContain('_sprk_project_value eq');
    expect(invoiceFirmQuery('sprk_workassignment', MATTER_ID)).toContain('_sprk_regardingworkassignment_value eq');
    expect(Object.keys(INVOICE_LOOKUP).sort()).toEqual(['sprk_matter', 'sprk_project', 'sprk_workassignment']);

    const api = fakeApi(() => rows());
    await createFirmChoices(() => api).invoicedBy('sprk_project', MATTER_ID);
    const entities = (api.retrieveMultipleRecords as jest.Mock).mock.calls.map(c => c[0]);
    expect(entities).toEqual(['sprk_invoice']);
  });

  it('no invoices is an empty, complete list and reads no organization', async () => {
    const api = fakeApi(() => rows());
    expect(await createFirmChoices(() => api).invoicedBy('sprk_matter', MATTER_ID)).toEqual({ firms: [], complete: true });
    expect(api.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });

  it('a failed invoice read, a failed name read and no Xrm are each an incomplete list, never a throw', async () => {
    const failing = fakeApi(() => Promise.reject(new Error('boom')));
    expect(await createFirmChoices(() => failing).invoicedBy('sprk_matter', MATTER_ID)).toEqual({ firms: [], complete: false });

    const namesFail = fakeApi(entity => (entity === 'sprk_invoice' ? rows({ _sprk_vendororg_value: FENWICK.id }) : Promise.reject(new Error('boom'))));
    expect(await createFirmChoices(() => namesFail).invoicedBy('sprk_matter', MATTER_ID)).toEqual({ firms: [], complete: false });

    expect(await createFirmChoices(() => undefined).invoicedBy('sprk_matter', MATTER_ID)).toEqual({ firms: [], complete: false });
  });

  it('a record id that is not a GUID reads nothing (the id goes into an OData filter)', async () => {
    const api = fakeApi(() => rows());
    expect(await createFirmChoices(() => api).invoicedBy('sprk_matter', "x' or 1 eq 1")).toEqual({ firms: [], complete: false });
    expect(api.retrieveMultipleRecords).not.toHaveBeenCalled();
  });
});
