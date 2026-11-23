/**
 * createWebApiFromDataService — the one IDataService → webApi adapter (#2866).
 */
import { createWebApiFromDataService } from '../dataServiceWebApiAdapter';
import { createMockDataService } from '../../../__mocks__/mockDataService';

describe('createWebApiFromDataService', () => {
  it('wraps the created id as { id } and forwards the call', async () => {
    const ds = createMockDataService();
    ds.createRecord.mockResolvedValue('abc');
    const webApi = createWebApiFromDataService(ds);

    await expect(webApi.createRecord('sprk_matter', { sprk_name: 'x' })).resolves.toEqual({ id: 'abc' });
    expect(ds.createRecord).toHaveBeenCalledWith('sprk_matter', { sprk_name: 'x' });
  });

  it('forwards retrieveMultipleRecords without the page size and returns only entities', async () => {
    const ds = createMockDataService();
    ds.retrieveMultipleRecords.mockResolvedValue({ entities: [{ a: 1 }] });
    const webApi = createWebApiFromDataService(ds);

    await expect(webApi.retrieveMultipleRecords('systemuser', '?$top=5', 10)).resolves.toEqual({
      entities: [{ a: 1 }],
    });
    expect(ds.retrieveMultipleRecords).toHaveBeenCalledWith('systemuser', '?$top=5');
  });

  it('forwards retrieveRecord unchanged', async () => {
    const ds = createMockDataService();
    ds.retrieveRecord.mockResolvedValue({ sprk_name: 'n' });
    const webApi = createWebApiFromDataService(ds);

    await expect(webApi.retrieveRecord('sprk_matter', 'id1', '?$select=sprk_name')).resolves.toEqual({
      sprk_name: 'n',
    });
    expect(ds.retrieveRecord).toHaveBeenCalledWith('sprk_matter', 'id1', '?$select=sprk_name');
  });

  it('resolves update and delete to { id } after the call completes', async () => {
    const ds = createMockDataService();
    const webApi = createWebApiFromDataService(ds);

    await expect(webApi.updateRecord('sprk_matter', 'id2', { sprk_name: 'y' })).resolves.toEqual({ id: 'id2' });
    expect(ds.updateRecord).toHaveBeenCalledWith('sprk_matter', 'id2', { sprk_name: 'y' });
    await expect(webApi.deleteRecord('sprk_document', 'id3')).resolves.toEqual({ id: 'id3' });
    expect(ds.deleteRecord).toHaveBeenCalledWith('sprk_document', 'id3');
  });

  it('propagates a data-service failure instead of swallowing it', async () => {
    const ds = createMockDataService();
    ds.createRecord.mockRejectedValue(new Error('boom'));
    const webApi = createWebApiFromDataService(ds);

    await expect(webApi.createRecord('sprk_matter', {})).rejects.toThrow('boom');
  });
});
