/**
 * unified-access-control-r2 task 147 r1 (owner round 28 item 1): the ONE client seam for child-record writes. A product
 * writer's child create / re-file goes to the BFF with the SAME Web API payload; reads, deletes and other tables pass
 * through; a refusal surfaces the server's message and nothing falls back to Xrm.WebApi.
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { fakeResponse } from '../../../__mocks__/bffChildWriteFake';
import {
  BFF_CHILD_CREATE_TABLES,
  ChildRecordWriteError,
  createChildRecordViaBff,
  createRecordViaBffWithWarnings,
  FILING_ONLY_REFILE_TABLES,
  isFilingKey,
  splitFilingPayload,
  updateChildRecordViaBff,
  withBffChildWrites,
} from '../bffChildWriteAdapter';

const BFF = 'https://bff.example.com/';
const ID = '11111111-2222-3333-4444-555555555555';

function json(status: number, body: unknown): Response {
  return fakeResponse(status, body);
}

function innerService(): jest.Mocked<IDataService> {
  return {
    createRecord: jest.fn().mockResolvedValue('inner-id'),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

describe('BFF child-record writes (task 147 r1)', () => {
  it('lists exactly the census create tables', () => {
    expect([...BFF_CHILD_CREATE_TABLES].sort()).toEqual(
      [
        'sprk_analysis',
        'sprk_document',
        'sprk_event',
        'sprk_invoice',
        'sprk_memo',
        'sprk_reportcard',
        'sprk_todo',
        // Ontology task 046 (D-113): a work assignment is created through the same route (a root; the server plans it).
        'sprk_workassignment',
      ].sort()
    );
  });

  it("returns the server's warnings with the id, and none when the body carries none (task 046)", async () => {
    const fetchFn = jest
      .fn()
      .mockResolvedValueOnce(json(201, { id: ID, warnings: ['securing it could not be finished yet', 7, ''] }))
      .mockResolvedValueOnce(json(201, { id: ID }));

    const withWarning = await createRecordViaBffWithWarnings(fetchFn, BFF, 'sprk_workassignment', { sprk_name: 'x' });
    const without = await createRecordViaBffWithWarnings(fetchFn, BFF, 'sprk_workassignment', { sprk_name: 'x' });

    expect(withWarning).toEqual({ id: ID, warnings: ['securing it could not be finished yet'] });
    expect(without).toEqual({ id: ID, warnings: [] });
    expect(fetchFn.mock.calls[0][0]).toBe('https://bff.example.com/api/v1/child-records/sprk_workassignment');
  });

  it('creates a child through POST /api/v1/child-records/{table} with the unchanged Web API payload', async () => {
    const fetchFn = jest.fn().mockResolvedValue(json(201, { id: `{${ID.toUpperCase()}}` }));
    const payload = { sprk_name: 'Call back', 'sprk_RegardingMatter@odata.bind': `/sprk_matters(${ID})` };

    const id = await createChildRecordViaBff(fetchFn, BFF, 'sprk_todo', payload);

    expect(id).toBe(ID);
    expect(fetchFn).toHaveBeenCalledTimes(1);
    const [url, init] = fetchFn.mock.calls[0];
    expect(url).toBe('https://bff.example.com/api/v1/child-records/sprk_todo');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual(payload);
  });

  it('surfaces the server ProblemDetails message and reason code on a refusal', async () => {
    const fetchFn = jest.fn().mockResolvedValue(
      json(409, {
        title: 'Record owner unresolved',
        detail: 'The to-do was not saved: the project is marked secure but is not isolated.',
        reasonCode: 'secure_parent_not_isolated',
      })
    );

    const attempt = createChildRecordViaBff(fetchFn, BFF, 'sprk_todo', { sprk_name: 'x' });

    await expect(attempt).rejects.toBeInstanceOf(ChildRecordWriteError);
    await expect(attempt).rejects.toMatchObject({
      message: 'The to-do was not saved: the project is marked secure but is not isolated.',
      status: 409,
      reasonCode: 'secure_parent_not_isolated',
    });
  });

  it.each([
    ['sprk_todo', `/api/v1/child-records/sprk_todo/${ID}`],
    ['sprk_memo', `/api/v1/child-records/sprk_memo/${ID}`],
    ['sprk_invoice', `/api/v1/child-records/sprk_invoice/${ID}`],
    ['sprk_analysis', `/api/v1/child-records/sprk_analysis/${ID}`],
    ['sprk_event', `/api/v1/events/${ID}/filing`],
    ['sprk_communication', `/api/communications/${ID}/filing`],
  ])('re-files %s through its one route (%s)', async (table, path) => {
    const fetchFn = jest.fn().mockResolvedValue(fakeResponse(204));

    await updateChildRecordViaBff(fetchFn, BFF, table, `{${ID}}`, { 'sprk_RegardingMatter@odata.bind': null });

    expect(fetchFn.mock.calls[0][0]).toBe(`https://bff.example.com${path}`);
    expect(fetchFn.mock.calls[0][1].method).toBe('PATCH');
  });

  it('refuses a table that is not created through the BFF, without calling it', async () => {
    const fetchFn = jest.fn();
    await expect(createChildRecordViaBff(fetchFn, BFF, 'sprk_matter', {})).rejects.toBeInstanceOf(
      ChildRecordWriteError
    );
    expect(fetchFn).not.toHaveBeenCalled();
  });

  describe('withBffChildWrites', () => {
    it('routes child creates and re-files to the BFF and never to the inner (Xrm) service', async () => {
      const inner = innerService();
      const fetchFn = jest
        .fn()
        .mockResolvedValueOnce(json(201, { id: ID }))
        .mockResolvedValueOnce(fakeResponse(204));
      const service = withBffChildWrites(inner, fetchFn, BFF);

      await expect(service.createRecord('sprk_event', { sprk_eventname: 'x' })).resolves.toBe(ID);
      await service.updateRecord('sprk_todo', ID, { sprk_name: 'y' });

      expect(inner.createRecord).not.toHaveBeenCalled();
      expect(inner.updateRecord).not.toHaveBeenCalled();
      expect(fetchFn).toHaveBeenCalledTimes(2);
    });

    it('passes reads, deletes and non-child writes through unchanged', async () => {
      const inner = innerService();
      const fetchFn = jest.fn();
      const service = withBffChildWrites(inner, fetchFn, BFF);

      await service.createRecord('sprk_matter', { sprk_mattername: 'm' });
      await service.updateRecord('sprk_project', ID, { sprk_projectname: 'p' });
      await service.retrieveRecord('sprk_todo', ID, '?$select=sprk_name');
      await service.retrieveMultipleRecords('sprk_todo', '?$top=1');
      await service.deleteRecord('sprk_todo', ID);

      expect(fetchFn).not.toHaveBeenCalled();
      expect(inner.createRecord).toHaveBeenCalledWith('sprk_matter', { sprk_mattername: 'm' });
      expect(inner.updateRecord).toHaveBeenCalledWith('sprk_project', ID, { sprk_projectname: 'p' });
      expect(inner.deleteRecord).toHaveBeenCalledWith('sprk_todo', ID);
    });

    it('REFUSES a child write when the host wired no BFF fetch — never sent to the inner (Xrm) service', async () => {
      const inner = innerService();
      const service = withBffChildWrites(inner, undefined, undefined);

      await expect(service.createRecord('sprk_todo', { sprk_name: 'x' })).rejects.toMatchObject({
        reasonCode: 'child_record.bff_not_configured',
      });
      await expect(service.updateRecord('sprk_event', ID, { sprk_eventname: 'y' })).rejects.toBeInstanceOf(
        ChildRecordWriteError
      );
      // A non-child write still goes through.
      await service.createRecord('sprk_matter', { sprk_mattername: 'm' });

      expect(inner.createRecord).toHaveBeenCalledTimes(1);
      expect(inner.createRecord).toHaveBeenCalledWith('sprk_matter', { sprk_mattername: 'm' });
      expect(inner.updateRecord).not.toHaveBeenCalled();
    });

    it('an empty base URL is a configured host (relative /api paths)', async () => {
      const fetchFn = jest.fn().mockResolvedValue(json(201, { id: ID }));
      const service = withBffChildWrites(innerService(), fetchFn, '');

      await service.createRecord('sprk_memo', { sprk_name: 'n' });

      expect(fetchFn.mock.calls[0][0]).toBe('/api/v1/child-records/sprk_memo');
    });

    it('is idempotent — re-wrapping keeps the host-wired connection (a follow-on writer cannot lose it)', async () => {
      const fetchFn = jest.fn().mockResolvedValue(json(201, { id: ID }));
      const wired = withBffChildWrites(innerService(), fetchFn, BFF);

      const rewrapped = withBffChildWrites(wired, undefined, undefined);

      expect(rewrapped).toBe(wired);
      await expect(rewrapped.createRecord('sprk_todo', { sprk_name: 'x' })).resolves.toBe(ID);
    });

    it('keeps a host decorator that inherits from a routed service in the call path (a create listener still fires)', async () => {
      // Task 147 r1c: the CreateTodoWizard code page decorates its BFF-routed service with a create broadcast
      // (Object.create(routed)). The service the wizard wraps again must call THAT decorator, not go straight to the BFF
      // past it — otherwise the listener (the cross-iframe refetch) silently never fires.
      const fetchFn = jest.fn().mockResolvedValue(json(201, { id: ID }));
      const routed = withBffChildWrites(innerService(), fetchFn, BFF);
      const heard: string[] = [];
      const decorated: IDataService = Object.assign(Object.create(routed) as IDataService, {
        createRecord: async (entity: string, data: Record<string, unknown>) => {
          const id = await routed.createRecord(entity, data);
          heard.push(`${entity}:${id}`);
          return id;
        },
      });

      const wrapped = withBffChildWrites(decorated, undefined, undefined);
      await wrapped.createRecord('sprk_todo', { sprk_name: 'x' });

      expect(wrapped).toBe(decorated);
      expect(heard).toEqual([`sprk_todo:${ID}`]);
      expect(fetchFn).toHaveBeenCalledTimes(1);
    });

    it('does NOT fall back to the inner service when the BFF refuses — nothing is left user-owned', async () => {
      const inner = innerService();
      const fetchFn = jest
        .fn()
        .mockResolvedValue(json(404, { detail: 'A record this to-do is filed under was not found.' }));
      const service = withBffChildWrites(inner, fetchFn, BFF);

      await expect(service.createRecord('sprk_todo', { sprk_name: 'x' })).rejects.toThrow(
        'A record this to-do is filed under was not found.'
      );
      expect(inner.createRecord).not.toHaveBeenCalled();
    });

    // Task 147 r1c (owner round 36): the event's and the communication's family routes take ONLY the filing.
    it.each(['sprk_event', 'sprk_communication'])(
      'splits a %s update — the filing to its family route FIRST, every other column to the inner (Xrm) service',
      async table => {
        const inner = innerService();
        const fetchFn = jest.fn().mockResolvedValue(fakeResponse(204));
        const service = withBffChildWrites(inner, fetchFn, BFF);

        await service.updateRecord(table, ID, {
          'sprk_RegardingMatter@odata.bind': `/sprk_matters(${ID})`,
          sprk_regardingrecordid: ID,
          statuscode: 2,
          'sprk_CompletedBy@odata.bind': `/systemusers(${ID})`,
        });

        expect(fetchFn).toHaveBeenCalledTimes(1);
        expect(JSON.parse(fetchFn.mock.calls[0][1].body)).toEqual({
          'sprk_RegardingMatter@odata.bind': `/sprk_matters(${ID})`,
          sprk_regardingrecordid: ID,
        });
        expect(inner.updateRecord).toHaveBeenCalledWith(table, ID, {
          statuscode: 2,
          'sprk_CompletedBy@odata.bind': `/systemusers(${ID})`,
        });
        expect(fetchFn.mock.invocationCallOrder[0]).toBeLessThan(inner.updateRecord.mock.invocationCallOrder[0]);
      }
    );

    it('an event update with no filing never calls the BFF; a filing-only one never calls the inner service', async () => {
      const inner = innerService();
      const fetchFn = jest.fn().mockResolvedValue(fakeResponse(204));
      const service = withBffChildWrites(inner, fetchFn, BFF);

      await service.updateRecord('sprk_event', ID, { statuscode: 3 });
      expect(fetchFn).not.toHaveBeenCalled();
      expect(inner.updateRecord).toHaveBeenCalledTimes(1);

      await service.updateRecord('sprk_event', ID, { 'sprk_RegardingProject@odata.bind': null });
      expect(fetchFn).toHaveBeenCalledTimes(1);
      expect(inner.updateRecord).toHaveBeenCalledTimes(1);
    });

    it('a refused event re-file writes nothing else either', async () => {
      const inner = innerService();
      const fetchFn = jest.fn().mockResolvedValue(json(403, { detail: 'You cannot move this event.' }));
      const service = withBffChildWrites(inner, fetchFn, BFF);

      await expect(
        service.updateRecord('sprk_event', ID, { 'sprk_RegardingMatter@odata.bind': null, statuscode: 2 })
      ).rejects.toThrow('You cannot move this event.');
      expect(inner.updateRecord).not.toHaveBeenCalled();
    });

    it('a to-do update is NOT split — its route is the general child-record re-file', async () => {
      const inner = innerService();
      const fetchFn = jest.fn().mockResolvedValue(fakeResponse(204));
      const service = withBffChildWrites(inner, fetchFn, BFF);

      await service.updateRecord('sprk_todo', ID, { 'sprk_RegardingMatter@odata.bind': null, sprk_name: 'y' });

      expect(JSON.parse(fetchFn.mock.calls[0][1].body)).toEqual({
        'sprk_RegardingMatter@odata.bind': null,
        sprk_name: 'y',
      });
      expect(inner.updateRecord).not.toHaveBeenCalled();
    });
  });

  describe('the filing (owner round 36)', () => {
    it.each([
      ['sprk_RegardingMatter@odata.bind', true],
      ['sprk_regardingmatter', true],
      ['sprk_RegardingRecordType@odata.bind', true],
      ['sprk_regardingrecordid', true],
      ['sprk_regardingrecordname', true],
      ['sprk_regardingrecordurl', true],
      ['sprk_regardingrecordnumber', true],
      ['sprk_regarding', false],
      ['sprk_name', false],
      ['statuscode', false],
      ['sprk_CompletedBy@odata.bind', false],
      ['ownerid@odata.bind', false],
      ['@odata.etag', false],
    ])('%s is filing: %s', (key, filing) => {
      expect(isFilingKey(key)).toBe(filing);
    });

    it('splits a payload into the filing and the rest', () => {
      expect(
        splitFilingPayload({ 'sprk_RegardingEvent@odata.bind': null, sprk_regardingrecordid: null, sprk_name: 'x' })
      ).toEqual({
        filing: { 'sprk_RegardingEvent@odata.bind': null, sprk_regardingrecordid: null },
        rest: { sprk_name: 'x' },
      });
    });

    it('lists the two filing-only tables', () => {
      expect([...FILING_ONLY_REFILE_TABLES].sort()).toEqual(['sprk_communication', 'sprk_event']);
    });
  });
});
