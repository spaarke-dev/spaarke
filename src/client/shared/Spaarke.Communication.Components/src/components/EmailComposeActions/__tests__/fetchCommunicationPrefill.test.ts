/**
 * fetchCommunicationPrefill.test.ts (email-communication-solution-r5 task 036)
 */
import type { IDataService } from '@spaarke/ui-components';
import { fetchCommunicationPrefill } from '../fetchCommunicationPrefill';

describe('fetchCommunicationPrefill', () => {
  it('maps the sprk_communication $select fields to RecordPrefill, using the formatted createdon value for `sent`', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({
      sprk_from: 'sender@example.com',
      sprk_to: 'to@example.com',
      sprk_cc: 'cc@example.com',
      sprk_subject: 'Subject',
      sprk_body: '<p>Body</p>',
      'createdon@OData.Community.Display.V1.FormattedValue': '7/1/2026 9:00 AM',
    });
    const dataService = { retrieveRecord } as unknown as IDataService;

    const result = await fetchCommunicationPrefill(dataService, 'comm-1');

    expect(result).toEqual({
      from: 'sender@example.com',
      to: 'to@example.com',
      cc: 'cc@example.com',
      subject: 'Subject',
      body: '<p>Body</p>',
      sent: '7/1/2026 9:00 AM',
    });
    expect(retrieveRecord).toHaveBeenCalledWith(
      'sprk_communication',
      'comm-1',
      '?$select=sprk_from,sprk_to,sprk_cc,sprk_subject,sprk_body,createdon'
    );
  });

  it('defaults missing fields to empty strings rather than throwing', async () => {
    const dataService = { retrieveRecord: jest.fn().mockResolvedValue({}) } as unknown as IDataService;
    const result = await fetchCommunicationPrefill(dataService, 'comm-2');
    expect(result).toEqual({ from: '', to: '', cc: '', subject: '', body: '', sent: '' });
  });

  it('propagates a read failure to the caller (best-effort handling lives in useEmailComposeActions)', async () => {
    const dataService = {
      retrieveRecord: jest.fn().mockRejectedValue(new Error('network down')),
    } as unknown as IDataService;
    await expect(fetchCommunicationPrefill(dataService, 'comm-3')).rejects.toThrow('network down');
  });
});
