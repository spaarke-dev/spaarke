/**
 * createXrmEmailComposeHandlers.recipientPicks.test.ts - ontology task 238 (b).
 *
 * The people-picker handler turns picks into recipient chips and REPORTS the picks it could not turn into an address
 * (record not found / no access, no email on the record, not a contact or user) on the result's `skipped` list, instead
 * of logging and dropping them silently. Picks that resolve are always returned.
 */
import { createXrmEmailComposeHandlers, resolveRecipientPicks } from '../createXrmEmailComposeHandlers';
import type { IRecipientLookupResult } from '../EmailComposer.types';

const mockGetXrm = jest.fn();
jest.mock('../../../utils/xrmContext', () => ({
  ...jest.requireActual('../../../utils/xrmContext'),
  getXrm: () => mockGetXrm(),
}));

const GOOD = 'aaaaaaaa-1111-2222-3333-444444444444';
const GONE = 'bbbbbbbb-1111-2222-3333-444444444444';
const NO_EMAIL = 'cccccccc-1111-2222-3333-444444444444';
const DENIED = 'dddddddd-1111-2222-3333-444444444444';
const HTTP_404 = 'eeeeeeee-1111-2222-3333-444444444444';
const TRANSIENT = 'ffffffff-1111-2222-3333-444444444444';
const UNKNOWN = '99999999-1111-2222-3333-444444444444';

function xrmWith(picks: Array<{ id: string; name: string; entityType: string }>, retrieveRecord: jest.Mock) {
  return {
    WebApi: { retrieveRecord },
    Utility: { lookupObjects: jest.fn().mockResolvedValue(picks) },
  };
}

/** retrieveRecord stand-in: GOOD has an address, GONE/DENIED/HTTP_404 are not found / access denied (as Dataverse reports them), TRANSIENT and UNKNOWN are other failures, NO_EMAIL has none. */
const retrieve = jest.fn(async (_entityType: string, id: string) => {
  if (id === GOOD) return { emailaddress1: 'pat@example.com' };
  if (id === NO_EMAIL) return { emailaddress1: null };
  if (id === GONE) throw { errorCode: 0x80040217, message: 'Contact With Id = x Does Not Exist' };
  if (id === DENIED) throw { errorCode: 0x80040220, message: 'SecLib::AccessCheckEx failed' };
  if (id === HTTP_404) throw { status: 404, message: 'Not Found' };
  if (id === TRANSIENT) throw { errorCode: 0x80072321, message: 'Too many requests. secret-internal-detail' };
  throw new Error('network down: secret-internal-detail');
});

describe('onLookupRecipients - unresolvable picks are reported (task 238 b)', () => {
  let warn: jest.SpyInstance;
  beforeEach(() => {
    mockGetXrm.mockReset();
    retrieve.mockClear();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => warn.mockRestore());

  async function lookup(picks: Array<{ id: string; name: string; entityType: string }>) {
    mockGetXrm.mockReturnValue(xrmWith(picks, retrieve));
    return createXrmEmailComposeHandlers().onLookupRecipients('to');
  }

  it('must fire: a pick whose record is not found adds nothing and is listed with a reason', async () => {
    const result = await lookup([{ id: GONE, name: 'Jane Smith', entityType: 'systemuser' }]);

    expect(result).toHaveLength(0);
    expect(result?.skipped).toEqual([
      { name: 'Jane Smith', reason: expect.stringMatching(/could not be read.*deleted/i) },
    ]);
    expect(warn).toHaveBeenCalled(); // the diagnostic log stays
  });

  it('must fire: a record with no email address adds nothing and is listed with that reason', async () => {
    const result = await lookup([{ id: NO_EMAIL, name: 'Sam Nomail', entityType: 'contact' }]);

    expect(result).toHaveLength(0);
    expect(result?.skipped).toEqual([{ name: 'Sam Nomail', reason: expect.stringMatching(/no email address/i) }]);
  });

  it('must fire: a pick that is not a contact or user is listed instead of skipped silently', async () => {
    const result = await lookup([{ id: GOOD, name: 'Acme Ltd', entityType: 'account' }]);

    expect(result?.skipped).toEqual([{ name: 'Acme Ltd', reason: expect.stringMatching(/only contacts and users/i) }]);
    expect(retrieve).not.toHaveBeenCalled();
  });

  it('must not fire: a resolvable pick yields a chip and no `skipped`', async () => {
    const result = await lookup([{ id: GOOD, name: 'Pat Contact', entityType: 'contact' }]);

    expect(result).toEqual([
      { email: 'pat@example.com', displayName: 'Pat Contact', resolved: true, sourceId: GOOD, entityType: 'contact' },
    ]);
    expect(result?.skipped).toBeUndefined();
  });

  it('a mixed multi-select returns the good picks and lists only the bad ones', async () => {
    const result = (await lookup([
      { id: GOOD, name: 'Pat Contact', entityType: 'contact' },
      { id: GONE, name: 'Jane Smith', entityType: 'systemuser' },
      { id: NO_EMAIL, name: 'Sam Nomail', entityType: 'contact' },
    ])) as IRecipientLookupResult;

    expect(result.map(r => r.email)).toEqual(['pat@example.com']);
    expect(result.skipped?.map(s => s.name)).toEqual(['Jane Smith', 'Sam Nomail']);
  });

  it('must fire: access denied and an HTTP 404 get the not-found / no-access wording', async () => {
    const result = (await lookup([
      { id: DENIED, name: 'Dee Nied', entityType: 'contact' },
      { id: HTTP_404, name: 'Hal Four', entityType: 'contact' },
    ])) as IRecipientLookupResult;

    expect(result.skipped?.map(s => s.reason)).toEqual([
      expect.stringMatching(/deleted.*access/i),
      expect.stringMatching(/deleted.*access/i),
    ]);
  });

  it('must not fire: a transient or unknown failure is not reported as deleted / no access, and shows no raw text', async () => {
    const result = (await lookup([
      { id: TRANSIENT, name: 'Tess Throttle', entityType: 'contact' },
      { id: UNKNOWN, name: 'Una Known', entityType: 'systemuser' },
    ])) as IRecipientLookupResult;

    expect(result.skipped).toHaveLength(2);
    for (const s of result.skipped!) {
      expect(s.reason).toMatch(/right now.*try again/i);
      expect(s.reason).not.toMatch(/deleted|access/i);
      expect(s.reason).not.toMatch(/secret-internal-detail/);
    }
  });

  it('a cancelled picker is still null', async () => {
    expect(await lookup([])).toBeNull();
  });

  it('a pick with no display name is listed under a generic label', async () => {
    const result = await resolveRecipientPicks([{ id: GONE, name: '  ', entityType: 'systemuser' }], retrieve);

    expect(result?.skipped?.[0].name).toBe('That record');
  });
});
