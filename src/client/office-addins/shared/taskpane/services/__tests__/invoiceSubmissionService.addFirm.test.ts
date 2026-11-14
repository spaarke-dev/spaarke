/**
 * Task 134 slice C: the add-in's client for ebilling 095g's firm routes
 * (`projects/ebilling-r1/notes/095g-assign-firm.md`, "Contract for word-add-in-r1 (as built)"):
 * `GET /api/finance/firms` (listLawFirms) and `POST /api/finance/core-records/{entityType}/{entityId}/firms` (addFirmToRecord).
 * The wire shapes, the one-key body, and the rule that a refusal is keyed on `errorCode ?? reasonCode` and never on `detail`.
 */
import { addFirmToRecord, listLawFirms, LAW_FIRM_PAGE_SIZE, LAW_FIRM_SEARCH_MAX } from '../invoiceSubmissionService';
import { FIRM_ASSIGN_GENERIC_MESSAGE, FIRM_LIST_GENERIC_MESSAGE } from '../../utils/invoiceSubmissionMessages';

const BASE = 'https://bff.example';
const config = { apiBaseUrl: BASE, getAccessToken: async () => 'token' };
const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

function res(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => null },
    json: async () => {
      if (body === undefined) throw new Error('no body');
      return body;
    },
  };
}

const MATTER_ID = '6F1C0E8A-2B1D-4C7E-9A52-0D3F1B7E9C41';
const MATTER_CLEAN = '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41';
const FIRM_ID = '8B0C6F1E-4C2A-4F7E-9A51-2F3D1C0E9B77';
const FIRM_CLEAN = '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77';
const target = { entityType: 'Matter' as const, entityId: `{${MATTER_ID}}` };

beforeEach(() => mockFetch.mockReset());

describe('listLawFirms (GET /api/finance/firms)', () => {
  it('asks for the page with search, page and pageSize 25, trimmed', async () => {
    mockFetch.mockResolvedValue(
      res(200, { items: [{ firmId: 'f1', name: 'Ravenscroft & Vance' }], page: 2, hasMore: true })
    );
    const result = await listLawFirms({ search: '  rave & vance ', page: 2 }, config);
    const [url, init] = mockFetch.mock.calls[0]!;
    expect(url).toBe(`${BASE}/api/finance/firms?search=rave%20%26%20vance&page=2&pageSize=${LAW_FIRM_PAGE_SIZE}`);
    expect(LAW_FIRM_PAGE_SIZE).toBe(25);
    expect(init.method).toBe('GET');
    expect(init.headers.Authorization).toBe('Bearer token');
    expect(result).toEqual({ items: [{ firmId: 'f1', name: 'Ravenscroft & Vance' }], page: 2, hasMore: true });
  });

  it('caps the search at 100 CODE POINTS, so a surrogate pair is never cut and nothing throws (K4)', async () => {
    mockFetch.mockResolvedValue(res(200, { items: [], page: 1, hasMore: false }));
    // 150 characters, every one an emoji (two UTF-16 units each): 300 units, 150 code points.
    const emoji = '\u{1F600}';
    const search = emoji.repeat(150);
    expect(search.length).toBe(300);
    await expect(listLawFirms({ search }, config)).resolves.toEqual({ items: [], page: 1, hasMore: false });
    const url = new URL(mockFetch.mock.calls[0]![0] as string);
    const sent = url.searchParams.get('search')!;
    expect(Array.from(sent)).toHaveLength(LAW_FIRM_SEARCH_MAX);
    expect(sent).toBe(emoji.repeat(100));
    // A mix: the cut falls after the 100th code point, on a pair boundary.
    mockFetch.mockClear();
    await listLawFirms({ search: 'a'.repeat(99) + emoji + emoji }, config);
    const mixed = new URL(mockFetch.mock.calls[0]![0] as string).searchParams.get('search')!;
    expect(mixed).toBe('a'.repeat(99) + emoji);
  });

  it('a lone surrogate in the search is dropped; the request is still sent (encodeURIComponent would throw on it)', async () => {
    mockFetch.mockResolvedValue(res(200, { items: [], page: 1, hasMore: false }));
    await expect(listLawFirms({ search: 'ab\uD83D' }, config)).resolves.toBeDefined();
    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}/api/finance/firms?search=ab&page=1&pageSize=25`);
    mockFetch.mockClear();
    await expect(listLawFirms({ search: '\uDE00cd' }, config)).resolves.toBeDefined();
    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}/api/finance/firms?search=cd&page=1&pageSize=25`);
  });

  it('omits search when blank, and caps it at 100 characters', async () => {
    mockFetch.mockResolvedValue(res(200, { items: [], page: 1, hasMore: false }));
    await listLawFirms({ search: '   ' }, config);
    await listLawFirms({}, config);
    await listLawFirms({ search: 'x'.repeat(150) }, config);
    expect(mockFetch.mock.calls[0]![0]).toBe(`${BASE}/api/finance/firms?page=1&pageSize=25`);
    expect(mockFetch.mock.calls[1]![0]).toBe(`${BASE}/api/finance/firms?page=1&pageSize=25`);
    expect(mockFetch.mock.calls[2]![0]).toBe(
      `${BASE}/api/finance/firms?search=${'x'.repeat(LAW_FIRM_SEARCH_MAX)}&page=1&pageSize=25`
    );
  });

  it('reads the page tolerantly: bad rows are dropped, hasMore is true only when the server says true', async () => {
    mockFetch.mockResolvedValue(
      res(200, {
        items: [{ firmId: 'f1', name: 'A' }, { firmId: 7, name: 'B' }, null, { firmId: 'f3' }],
        hasMore: 'yes',
      })
    );
    expect(await listLawFirms({ page: 3 }, config)).toEqual({
      items: [{ firmId: 'f1', name: 'A' }],
      page: 3,
      hasMore: false,
    });
  });

  it('throws the sentence for the code, never the detail', async () => {
    mockFetch.mockResolvedValue(
      res(404, { errorCode: 'sdap.access.deny.record_unavailable', detail: 'internal text' })
    );
    // The list's uniform 404 is about listing, not about a record or a firm.
    await expect(listLawFirms({}, config)).rejects.toThrow("You can't list law firms in Spaarke.");
    mockFetch.mockResolvedValue(res(503, { errorCode: 'ebilling.submission.record_unavailable', detail: 'x' }));
    await expect(listLawFirms({}, config)).rejects.toThrow("Spaarke couldn't do this right now. Try again.");
    mockFetch.mockResolvedValue(res(503, { errorCode: 'ebilling.disabled' }));
    await expect(listLawFirms({}, config)).rejects.toThrow('Invoice submission is turned off in Spaarke.');
    mockFetch.mockResolvedValue(res(400, { title: 'One or more validation errors occurred.' }));
    await expect(listLawFirms({}, config)).rejects.toThrow(FIRM_LIST_GENERIC_MESSAGE);
    mockFetch.mockResolvedValue(res(200, { notItems: true }));
    await expect(listLawFirms({}, config)).rejects.toThrow(FIRM_LIST_GENERIC_MESSAGE);
  });

  it('says it could not reach Spaarke when there is no answer', async () => {
    mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(listLawFirms({}, config)).rejects.toThrow(/Couldn't reach Spaarke to list the law firms/);
  });
});

describe('addFirmToRecord (POST /api/finance/core-records/{entityType}/{entityId}/firms)', () => {
  const ok = {
    changed: true,
    firms: [{ firmId: FIRM_CLEAN, name: 'Firm F' }],
    assigned: [{ slot: 1, firmId: FIRM_CLEAN, name: 'Firm F', active: true }],
    access: { state: 'applied', granted: [{ firmId: FIRM_CLEAN, name: 'Firm F' }], revoked: [] },
    recordActive: true,
    notice: "Firm F's people will get access to this record.",
  };

  it('must-fire: POSTs exactly {"add": "<cleanGuid>"} to the route with the canonical record id', async () => {
    mockFetch.mockResolvedValue(res(200, ok));
    await addFirmToRecord(target, `{${FIRM_ID}}`, config);
    const [url, init] = mockFetch.mock.calls[0]!;
    expect(url).toBe(`${BASE}/api/finance/core-records/Matter/${MATTER_CLEAN}/firms`);
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify({ add: FIRM_CLEAN }));
    // Pinned to ONE key: the pane never removes, replaces or clears a firm, so it never revokes a firm's access.
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(Object.keys(body)).toEqual(['add']);
    expect(body).not.toHaveProperty('remove');
    expect(body).not.toHaveProperty('confirmRevoke');
  });

  it('maps each core record type onto the route (Matter, Project, WorkAssignment)', async () => {
    mockFetch.mockResolvedValue(res(200, ok));
    for (const entityType of ['Matter', 'Project', 'WorkAssignment'] as const) {
      await addFirmToRecord({ entityType, entityId: MATTER_ID }, FIRM_ID, config);
    }
    expect(mockFetch.mock.calls.map(c => (c[0] as string).split('/')[6])).toEqual([
      'Matter',
      'Project',
      'WorkAssignment',
    ]);
  });

  it('reads the 200: notice, firms, access state and recordActive', async () => {
    mockFetch.mockResolvedValue(res(200, ok));
    expect(await addFirmToRecord(target, FIRM_ID, config)).toEqual({
      kind: 'added',
      changed: true,
      firms: [{ firmId: FIRM_CLEAN, name: 'Firm F' }],
      accessState: 'applied',
      recordActive: true,
      notice: "Firm F's people will get access to this record.",
    });
  });

  it('reads a 200 tolerantly: firms null, pending, an inactive record, no notice, changed false', async () => {
    mockFetch.mockResolvedValue(
      res(200, { changed: false, firms: null, access: { state: 'pending' }, recordActive: false, notice: null })
    );
    expect(await addFirmToRecord(target, FIRM_ID, config)).toEqual({
      kind: 'added',
      changed: false,
      firms: null,
      accessState: 'pending',
      recordActive: false,
      notice: null,
    });
    // A bare 200: the write happened, so it is a success the view reloads from.
    mockFetch.mockResolvedValue(res(200, {}));
    expect(await addFirmToRecord(target, FIRM_ID, config)).toEqual({
      kind: 'added',
      changed: true,
      firms: null,
      accessState: 'applied',
      recordActive: true,
      notice: null,
    });
  });

  it.each([
    [
      404,
      'sdap.access.deny.record_unavailable',
      "This record or firm isn't available to you, or you can't change this record.",
    ],
    [
      409,
      'ebilling.firm_assign.record_restricted',
      'This record is Restricted to internal users, so no firm can be given access to it.',
    ],
    [409, 'ebilling.firm_assign.slots_full', 'Two firms are already assigned. Replace one of them in Spaarke.'],
    [409, 'ebilling.firm_assign.firm_already_assigned', 'That firm is already assigned to this record.'],
    [503, 'ebilling.submission.record_unavailable', "Spaarke couldn't do this right now. Try again."],
    [503, 'ebilling.disabled', 'Invoice submission is turned off in Spaarke.'],
  ])('refusal %s %s shows its sentence, with the code', async (status, code, message) => {
    mockFetch.mockResolvedValue(res(status, { errorCode: code }));
    expect(await addFirmToRecord(target, FIRM_ID, config)).toEqual({ kind: 'refused', status, code, message });
  });

  it('reads the code as errorCode ?? reasonCode', async () => {
    mockFetch.mockResolvedValue(res(422, { reasonCode: 'ebilling.firm_assign.firm_inactive' }));
    expect(await addFirmToRecord(target, FIRM_ID, config)).toMatchObject({
      code: 'ebilling.firm_assign.firm_inactive',
      message: 'This firm is inactive. Choose another firm.',
    });
    mockFetch.mockResolvedValue(
      res(422, { errorCode: 'ebilling.firm_assign.not_a_law_firm', reasonCode: 'ebilling.firm_assign.firm_inactive' })
    );
    expect(await addFirmToRecord(target, FIRM_ID, config)).toMatchObject({
      code: 'ebilling.firm_assign.not_a_law_firm',
    });
  });

  it('must-not-fire: two 503 bodies with the same code and different detail give the SAME text', async () => {
    mockFetch.mockResolvedValueOnce(
      res(503, {
        errorCode: 'ebilling.submission.record_unavailable',
        detail: 'The firm assignment could not be saved.',
      })
    );
    const route = await addFirmToRecord(target, FIRM_ID, config);
    mockFetch.mockResolvedValueOnce(
      res(503, { errorCode: 'ebilling.submission.record_unavailable', detail: 'Invoice submissions are unavailable.' })
    );
    const filter = await addFirmToRecord(target, FIRM_ID, config);
    expect(route.kind === 'refused' && filter.kind === 'refused').toBe(true);
    expect(route.kind === 'refused' && route.message).toBe(filter.kind === 'refused' && filter.message);
    expect(route.kind === 'refused' && route.message).toBe("Spaarke couldn't do this right now. Try again.");
  });

  it('a 400, a 404 with no code, and an unknown 4xx code all show the generic sentence', async () => {
    mockFetch.mockResolvedValueOnce(
      res(400, { title: 'One or more validation errors occurred.', errors: { add: ['x'] } })
    );
    mockFetch.mockResolvedValueOnce(res(404, undefined));
    mockFetch.mockResolvedValueOnce(res(409, { errorCode: 'ebilling.firm_assign.something_new', detail: 'new' }));
    for (const status of [400, 404, 409]) {
      const outcome = await addFirmToRecord(target, FIRM_ID, config);
      expect(outcome).toMatchObject({ kind: 'refused', status, message: FIRM_ASSIGN_GENERIC_MESSAGE });
    }
    expect(FIRM_ASSIGN_GENERIC_MESSAGE).toBe("Spaarke couldn't add the firm right now. Nothing was changed.");
  });

  it('a 5xx with no code, or one this build does not know, says it could not confirm (never "Nothing was changed")', async () => {
    const unconfirmed = "Spaarke couldn't confirm the firm was added. Try again; adding the same firm again is safe.";
    mockFetch.mockResolvedValueOnce(res(500, undefined));
    mockFetch.mockResolvedValueOnce(res(502, { title: 'Bad gateway' }));
    mockFetch.mockResolvedValueOnce(res(500, { errorCode: 'constructor' }));
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.something_new' }));
    for (const status of [500, 502, 500, 503]) {
      const outcome = await addFirmToRecord(target, FIRM_ID, config);
      expect(outcome).toMatchObject({ kind: 'refused', status, message: unconfirmed });
    }
    // A 503 with the contract's own code keeps the contract sentence.
    mockFetch.mockResolvedValueOnce(res(503, { errorCode: 'ebilling.submission.record_unavailable' }));
    expect(await addFirmToRecord(target, FIRM_ID, config)).toMatchObject({
      status: 503,
      message: "Spaarke couldn't do this right now. Try again.",
    });
  });

  it('with no answer at all, says the firm may not have been added', async () => {
    mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));
    expect(await addFirmToRecord(target, FIRM_ID, config)).toMatchObject({
      kind: 'refused',
      status: 0,
      message: expect.stringContaining('may not have been added'),
    });
  });

  it('must-not-fire: an id that cleans to nothing sends nothing', async () => {
    expect(await addFirmToRecord(target, '{}', config)).toMatchObject({ kind: 'refused', status: 0 });
    expect(await addFirmToRecord({ ...target, entityId: '' }, FIRM_ID, config)).toMatchObject({ kind: 'refused' });
    expect(mockFetch).not.toHaveBeenCalled();
  });
});
