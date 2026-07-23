/**
 * userProfileService.test.ts — task 042 write/erase path (FR-E1 / FR-F3 / F5).
 *
 * Covers the acceptance criteria: keyed-upsert idempotency (no find-then-create), N:N associate /
 * disassociate reconcile, cold-start detection, GDPR erasure, and 052 input hygiene.
 */

import {
  createDataverseUserProfilePort,
  saveMyAssistantProfile,
  eraseMyAssistantProfile,
  isProfileComplete,
  sanitizeFreeText,
  sanitizeOffice,
  cleanGuid,
  encodeChipSelection,
  decodeChipSelection,
  PREFERENCE_CHIPS,
  FOCUS_AREA_CHIPS,
  USERPROFILE_SET,
  WORKOFFICE_SET,
  PRACTICEAREA_NAV_PROP,
  SYSTEMUSER_KEY_ATTR,
  type IUserProfilePort,
  type UserProfileRow,
} from '../userProfileService';

// ---------------------------------------------------------------------------
// Fake fetch / Response
// ---------------------------------------------------------------------------

interface FakeResp {
  ok: boolean;
  status: number;
  jsonBody?: unknown;
  entityIdHeader?: string;
}

function makeResponse(r: FakeResp): Response {
  return {
    ok: r.ok,
    status: r.status,
    headers: { get: (name: string) => (name === 'OData-EntityId' ? r.entityIdHeader ?? null : null) },
    json: async () => r.jsonBody ?? {},
    text: async () => (r.jsonBody ? JSON.stringify(r.jsonBody) : ''),
  } as unknown as Response;
}

interface Call {
  url: string;
  method: string;
  body?: unknown;
}

function makeFetch(handler: (call: Call) => FakeResp) {
  const calls: Call[] = [];
  const fetchImpl = jest.fn(async (url: string, init?: RequestInit) => {
    const call: Call = {
      url,
      method: init?.method ?? 'GET',
      body: init?.body ? JSON.parse(init.body as string) : undefined,
    };
    calls.push(call);
    return makeResponse(handler(call));
  });
  return { fetchImpl: fetchImpl as unknown as typeof fetch, calls };
}

const BASE = 'https://org.crm.dynamics.com/api/data/v9.2';
const portDeps = (fetchImpl: typeof fetch) => ({
  getBaseUrl: () => BASE,
  fetchImpl,
  isSameOrigin: () => true,
});

const USER = '11111111-1111-1111-1111-111111111111';
const PROFILE_ID = '22222222-2222-2222-2222-222222222222';

// ---------------------------------------------------------------------------
// Port: keyed upsert (no find-then-create)
// ---------------------------------------------------------------------------

describe('createDataverseUserProfilePort — find-then-write upsert', () => {
  const FIELDS = {
    name: 'Ada Lovelace',
    primaryRole: 100000001,
    focusAreas: 'M&A',
    officeLocation: 'London',
    assistantPreferences: 'Concise',
    profileCompletedOn: '2026-07-16T00:00:00.000Z',
    profileVersion: 1,
  };

  it('PATCHes by primary id when a profile already exists (found via the lookup _value filter)', async () => {
    // UAT 2026-07-18: the lookup-alternate-key URL 400s, so upsert finds by `_value` filter then writes.
    const { fetchImpl, calls } = makeFetch((call) =>
      call.method === 'GET'
        ? { ok: true, status: 200, jsonBody: { value: [{ sprk_userprofileid: PROFILE_ID }] } }
        : { ok: true, status: 204, jsonBody: null }
    );
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));

    const id = await port.upsertProfileByUser(USER, FIELDS);

    expect(id).toBe(PROFILE_ID);
    expect(calls[0].method).toBe('GET');
    expect(calls[0].url).toContain(`$filter=_${SYSTEMUSER_KEY_ATTR}_value eq ${USER}`);
    expect(calls[1].method).toBe('PATCH');
    expect(calls[1].url).toBe(`${BASE}/${USERPROFILE_SET}(${PROFILE_ID})`);
    // No lookup bind on an UPDATE (the row already belongs to the user).
    expect((calls[1].body as Record<string, unknown>)['sprk_SystemUser@odata.bind']).toBeUndefined();
  });

  it('POSTs a create binding the systemuser lookup when no profile exists yet', async () => {
    const { fetchImpl, calls } = makeFetch((call) =>
      call.method === 'GET'
        ? { ok: true, status: 200, jsonBody: { value: [] } }
        : { ok: true, status: 201, jsonBody: { sprk_userprofileid: PROFILE_ID } }
    );
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));

    const id = await port.upsertProfileByUser(USER, FIELDS);

    expect(id).toBe(PROFILE_ID);
    expect(calls[0].method).toBe('GET');
    expect(calls[1].method).toBe('POST');
    expect(calls[1].url).toBe(`${BASE}/${USERPROFILE_SET}`);
    expect((calls[1].body as Record<string, unknown>)['sprk_SystemUser@odata.bind']).toBe(
      `/systemusers(${USER})`
    );
  });

  it('falls back to the OData-EntityId header on create when no representation body id is present', async () => {
    const { fetchImpl } = makeFetch((call) =>
      call.method === 'GET'
        ? { ok: true, status: 200, jsonBody: { value: [] } }
        : {
            ok: true,
            status: 204,
            jsonBody: null,
            entityIdHeader: `${BASE}/${USERPROFILE_SET}(${PROFILE_ID})`,
          }
    );
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    const id = await port.upsertProfileByUser(USER, { ...FIELDS, focusAreas: null });
    expect(id).toBe(PROFILE_ID);
  });
});

// ---------------------------------------------------------------------------
// Port: read (cold-start) + N:N + delete
// ---------------------------------------------------------------------------

describe('createDataverseUserProfilePort — read / N:N / delete', () => {
  it('returns null when the lookup _value filter matches no profile (cold start)', async () => {
    // UAT 2026-07-18: reads by `$filter=_sprk_systemuser_value eq …` → a no-match is an empty
    // collection, not a 404.
    const { fetchImpl } = makeFetch(() => ({ ok: true, status: 200, jsonBody: { value: [] } }));
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    await expect(port.getProfileByUser(USER)).resolves.toBeNull();
  });

  it('parses the filtered GET + expands the N:N practice-area ids', async () => {
    const { fetchImpl, calls } = makeFetch(() => ({
      ok: true,
      status: 200,
      jsonBody: {
        value: [
          {
            sprk_userprofileid: PROFILE_ID,
            sprk_profilecompletedon: '2026-07-16T00:00:00Z',
            sprk_primaryrole: 100000001,
            sprk_focusareas: 'M&A',
            sprk_officelocation: 'London',
            sprk_assistantpreferences: 'Concise',
            sprk_profileversion: 3,
            [PRACTICEAREA_NAV_PROP]: [
              { sprk_practicearea_refid: 'aaa' },
              { sprk_practicearea_refid: 'bbb' },
            ],
          },
        ],
      },
    }));
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    const row = await port.getProfileByUser(USER);
    expect(row?.id).toBe(PROFILE_ID);
    expect(row?.practiceAreaIds).toEqual(['aaa', 'bbb']);
    expect(calls[0].url).toContain(`$filter=_${SYSTEMUSER_KEY_ATTR}_value eq ${USER}`);
    expect(calls[0].url).toContain(`$expand=${PRACTICEAREA_NAV_PROP}`);
  });

  it('associate POSTs to the nav-property $ref with an @odata.id', async () => {
    const { fetchImpl, calls } = makeFetch(() => ({ ok: true, status: 204 }));
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    await port.associatePracticeArea(PROFILE_ID, 'aaa');
    expect(calls[0].method).toBe('POST');
    expect(calls[0].url).toBe(`${BASE}/${USERPROFILE_SET}(${PROFILE_ID})/${PRACTICEAREA_NAV_PROP}/$ref`);
    expect((calls[0].body as Record<string, string>)['@odata.id']).toBe(
      `${BASE}/sprk_practicearea_refs(aaa)`
    );
  });

  it('disassociate DELETEs the keyed $ref', async () => {
    const { fetchImpl, calls } = makeFetch(() => ({ ok: true, status: 204 }));
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    await port.disassociatePracticeArea(PROFILE_ID, 'aaa');
    expect(calls[0].method).toBe('DELETE');
    expect(calls[0].url).toBe(
      `${BASE}/${USERPROFILE_SET}(${PROFILE_ID})/${PRACTICEAREA_NAV_PROP}(aaa)/$ref`
    );
  });

  it('deleteProfile DELETEs the row by id', async () => {
    const { fetchImpl, calls } = makeFetch(() => ({ ok: true, status: 204 }));
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    await port.deleteProfile(PROFILE_ID);
    expect(calls[0].method).toBe('DELETE');
    expect(calls[0].url).toBe(`${BASE}/${USERPROFILE_SET}(${PROFILE_ID})`);
  });

  it('listWorkOffices GETs active offices and maps id + name (MA-3)', async () => {
    const { fetchImpl, calls } = makeFetch(() => ({
      ok: true,
      status: 200,
      jsonBody: {
        value: [
          { sprk_workofficeid: 'wo-chi', sprk_name: 'Chicago' },
          { sprk_workofficeid: 'wo-ny', sprk_name: 'New York' },
          { sprk_workofficeid: 'wo-blank', sprk_name: '' }, // dropped (empty name)
        ],
      },
    }));
    const port = createDataverseUserProfilePort(portDeps(fetchImpl));
    const offices = await port.listWorkOffices();
    expect(offices).toEqual([
      { id: 'wo-chi', name: 'Chicago' },
      { id: 'wo-ny', name: 'New York' },
    ]);
    expect(calls[0].method).toBe('GET');
    expect(calls[0].url).toContain(`/${WORKOFFICE_SET}?`);
    expect(calls[0].url).toContain('statecode eq 0');
  });
});

// ---------------------------------------------------------------------------
// Orchestration: saveMyAssistantProfile
// ---------------------------------------------------------------------------

function fakePort(existing: UserProfileRow | null): jest.Mocked<IUserProfilePort> {
  return {
    getProfileByUser: jest.fn().mockResolvedValue(existing),
    listPracticeAreas: jest.fn().mockResolvedValue([]),
    listWorkOffices: jest.fn().mockResolvedValue([]),
    upsertProfileByUser: jest.fn().mockResolvedValue(PROFILE_ID),
    associatePracticeArea: jest.fn().mockResolvedValue(undefined),
    disassociatePracticeArea: jest.fn().mockResolvedValue(undefined),
    deleteProfile: jest.fn().mockResolvedValue(undefined),
  };
}

describe('saveMyAssistantProfile', () => {
  it('upserts once, sets profilecompletedon, and reconciles the N:N delta', async () => {
    const existing: UserProfileRow = {
      id: PROFILE_ID,
      profileCompletedOn: null,
      primaryRole: null,
      focusAreas: null,
      officeLocation: null,
      assistantPreferences: null,
      profileVersion: 2,
      practiceAreaIds: ['a', 'b'],
    };
    const port = fakePort(existing);

    const result = await saveMyAssistantProfile(port, {
      systemUserId: USER,
      displayName: 'Ada',
      existing,
      form: {
        primaryRole: 100000001,
        practiceAreaIds: ['b', 'c'], // drop a, keep b, add c
        focusAreas: 'M&A',
        officeLocation: 'London',
        assistantPreferences: 'Concise',
      },
    });

    expect(port.upsertProfileByUser).toHaveBeenCalledTimes(1);
    const upsertArgs = port.upsertProfileByUser.mock.calls[0][1];
    expect(upsertArgs.profileCompletedOn).toMatch(/^\d{4}-\d{2}-\d{2}T/); // ISO now
    expect(upsertArgs.profileVersion).toBe(3); // bumped from 2
    expect(result.associated).toEqual(['c']);
    expect(result.disassociated).toEqual(['a']);
    expect(port.associatePracticeArea).toHaveBeenCalledWith(PROFILE_ID, 'c');
    expect(port.disassociatePracticeArea).toHaveBeenCalledWith(PROFILE_ID, 'a');
    expect(port.associatePracticeArea).toHaveBeenCalledTimes(1);
    expect(port.disassociatePracticeArea).toHaveBeenCalledTimes(1);
  });

  it('is idempotent — resubmitting the same practice areas makes no associate/disassociate calls', async () => {
    const existing: UserProfileRow = {
      id: PROFILE_ID,
      profileCompletedOn: '2026-07-16T00:00:00Z',
      primaryRole: 100000001,
      focusAreas: 'M&A',
      officeLocation: 'London',
      assistantPreferences: 'Concise',
      profileVersion: 1,
      practiceAreaIds: ['a', 'b'],
    };
    const port = fakePort(existing);
    const result = await saveMyAssistantProfile(port, {
      systemUserId: USER,
      existing,
      form: {
        primaryRole: 100000001,
        practiceAreaIds: ['a', 'b'],
        focusAreas: 'M&A',
        officeLocation: 'London',
        assistantPreferences: 'Concise',
      },
    });
    expect(result.associated).toEqual([]);
    expect(result.disassociated).toEqual([]);
    expect(port.associatePracticeArea).not.toHaveBeenCalled();
    expect(port.disassociatePracticeArea).not.toHaveBeenCalled();
  });

  it('applies 052 input hygiene (newline-normalize + trim) before write', async () => {
    const port = fakePort(null);
    await saveMyAssistantProfile(port, {
      systemUserId: USER,
      existing: null,
      form: {
        primaryRole: null,
        practiceAreaIds: [],
        focusAreas: '  M&A\r\nand JV  ',
        officeLocation: '  New York  ',
        assistantPreferences: '',
      },
    });
    const args = port.upsertProfileByUser.mock.calls[0][1];
    expect(args.focusAreas).toBe('M&A\nand JV');
    expect(args.officeLocation).toBe('New York');
    expect(args.assistantPreferences).toBeNull(); // empty → null (cleared, not whitespace)
  });
});

// ---------------------------------------------------------------------------
// Orchestration: eraseMyAssistantProfile (F5)
// ---------------------------------------------------------------------------

describe('eraseMyAssistantProfile', () => {
  it('disassociates every intersect row, deletes the row, and erases user memory', async () => {
    const existing: UserProfileRow = {
      id: PROFILE_ID,
      profileCompletedOn: '2026-07-16T00:00:00Z',
      primaryRole: 100000001,
      focusAreas: null,
      officeLocation: null,
      assistantPreferences: null,
      profileVersion: 1,
      practiceAreaIds: ['a', 'b'],
    };
    const port = fakePort(existing);
    const eraseUserMemory = jest.fn().mockResolvedValue(undefined);

    const result = await eraseMyAssistantProfile(port, { systemUserId: USER, eraseUserMemory });

    expect(port.disassociatePracticeArea).toHaveBeenCalledTimes(2);
    expect(port.disassociatePracticeArea).toHaveBeenCalledWith(PROFILE_ID, 'a');
    expect(port.disassociatePracticeArea).toHaveBeenCalledWith(PROFILE_ID, 'b');
    expect(port.deleteProfile).toHaveBeenCalledWith(PROFILE_ID);
    expect(eraseUserMemory).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ deleted: true, disassociated: ['a', 'b'], memoryErased: true });
  });

  it('is a no-op delete when no profile exists, but still attempts memory erase', async () => {
    const port = fakePort(null);
    const eraseUserMemory = jest.fn().mockResolvedValue(undefined);
    const result = await eraseMyAssistantProfile(port, { systemUserId: USER, eraseUserMemory });
    expect(port.deleteProfile).not.toHaveBeenCalled();
    expect(eraseUserMemory).toHaveBeenCalledTimes(1);
    expect(result.deleted).toBe(false);
    expect(result.memoryErased).toBe(true);
  });

  it('never lets a memory-erase failure block the profile delete', async () => {
    const existing: UserProfileRow = {
      id: PROFILE_ID,
      profileCompletedOn: '2026-07-16T00:00:00Z',
      primaryRole: null,
      focusAreas: null,
      officeLocation: null,
      assistantPreferences: null,
      profileVersion: 1,
      practiceAreaIds: [],
    };
    const port = fakePort(existing);
    const eraseUserMemory = jest.fn().mockRejectedValue(new Error('boom'));
    const result = await eraseMyAssistantProfile(port, { systemUserId: USER, eraseUserMemory });
    expect(port.deleteProfile).toHaveBeenCalledWith(PROFILE_ID);
    expect(result.deleted).toBe(true);
    expect(result.memoryErased).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

describe('helpers', () => {
  it('isProfileComplete keys on profilecompletedon (cold-start gate)', () => {
    expect(isProfileComplete(null)).toBe(false);
    expect(isProfileComplete({ profileCompletedOn: null } as UserProfileRow)).toBe(false);
    expect(isProfileComplete({ profileCompletedOn: '2026-07-16T00:00:00Z' } as UserProfileRow)).toBe(true);
  });

  it('sanitizeFreeText normalizes newlines, trims, caps, and empties to null', () => {
    expect(sanitizeFreeText('  a\r\nb  ')).toBe('a\nb');
    expect(sanitizeFreeText('   ')).toBeNull();
    expect(sanitizeFreeText(null)).toBeNull();
    expect(sanitizeFreeText('x'.repeat(5000))!.length).toBe(2000);
  });

  it('sanitizeOffice collapses newlines and caps at 100', () => {
    expect(sanitizeOffice('New\nYork')).toBe('New York');
    expect(sanitizeOffice('y'.repeat(200))!.length).toBe(100);
  });

  it('cleanGuid strips braces and lowercases', () => {
    expect(cleanGuid('{ABCD-1234}')).toBe('abcd-1234');
  });
});

// ---------------------------------------------------------------------------
// Curated profile chips (MA-4)
// ---------------------------------------------------------------------------

describe('profile chips (MA-4)', () => {
  it('encodeChipSelection joins the selected chips\' directive phrases (newline-delimited)', () => {
    const encoded = encodeChipSelection(['concise', 'cite-sources'], PREFERENCE_CHIPS);
    expect(encoded).toBe('Be concise and get to the point.\nAlways cite the source documents you used.');
  });

  it('encodeChipSelection ignores unknown ids', () => {
    expect(encodeChipSelection(['concise', 'not-a-chip'], PREFERENCE_CHIPS)).toBe(
      'Be concise and get to the point.'
    );
  });

  it('decodeChipSelection round-trips an encoded value back to chip ids', () => {
    const ids = ['bullets', 'flag-risks'];
    expect(decodeChipSelection(encodeChipSelection(ids, PREFERENCE_CHIPS), PREFERENCE_CHIPS)).toEqual(ids);
  });

  it('decodeChipSelection tolerates semicolon delimiters + case + whitespace', () => {
    const stored = 'Mergers & acquisitions ;  litigation & disputes ';
    expect(decodeChipSelection(stored, FOCUS_AREA_CHIPS)).toEqual(['ma', 'litigation']);
  });

  it('decodeChipSelection yields no chips for legacy free text that matches no phrase', () => {
    expect(decodeChipSelection('some bespoke note the user typed', PREFERENCE_CHIPS)).toEqual([]);
    expect(decodeChipSelection('', PREFERENCE_CHIPS)).toEqual([]);
    expect(decodeChipSelection(null, PREFERENCE_CHIPS)).toEqual([]);
  });
});
