/**
 * fileProjectUnderMatter — unified-access-control-r2 task 195 (#2352).
 *
 * A project is filed under its matter through its ADR-024 regarding pair, which only the BFF writes. Pinned: the call is
 * `PATCH /api/v1/child-records/sprk_project/{projectId}` with ONLY the pair (the matter's id and the matter record type
 * read from the catalog); a refusal surfaces the server's words (thrown by the host's fetch, or returned); nothing is sent
 * when the screen has no BFF connection or the matter record type cannot be read.
 *
 * Each test loads a fresh module: `resolveRecordType` caches the record type per page.
 */
import type { IPolymorphicWebApi } from '../../../services/PolymorphicResolverService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

type Adapter = typeof import('../bffChildWriteAdapter');

function load(): Adapter {
  let mod: Adapter | undefined;
  jest.isolateModules(() => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    mod = require('../bffChildWriteAdapter') as Adapter;
  });
  return mod!;
}

const PROJECT_ID = 'a1950000-0000-4000-8000-000000000001';
const MATTER_ID = 'b1950000-0000-4000-8000-000000000002';
const MATTER_TYPE_ID = 'e8547bb4-8600-f111-8407-7c1e520aa4df';
const BFF = 'https://bff.example';

function catalog(
  entities: Record<string, unknown>[] = [{ sprk_recordtype_refid: MATTER_TYPE_ID, sprk_recorddisplayname: 'Matter' }]
) {
  return {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities }),
  } satisfies IPolymorphicWebApi;
}

const noContent = { ok: true, status: 204, text: async () => '' } as unknown as Response;

describe('fileProjectUnderMatter (task 195)', () => {
  let warn: jest.SpyInstance;
  beforeEach(() => {
    warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });
  afterEach(() => warn.mockRestore());

  it('PATCHes the project route with the matter as the regarding pair, and nothing else', async () => {
    const { fileProjectUnderMatter } = load();
    const fetchMock = jest.fn().mockResolvedValue(noContent);
    const types = catalog();

    await fileProjectUnderMatter(fetchMock, BFF, types, PROJECT_ID, MATTER_ID);

    expect(types.retrieveMultipleRecords).toHaveBeenCalledWith(
      'sprk_recordtype_ref',
      expect.stringContaining("sprk_recordlogicalname eq 'sprk_matter'")
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe(`${BFF}/api/v1/child-records/sprk_project/${PROJECT_ID}`);
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(init.body)).toEqual({
      sprk_regardingrecordid: MATTER_ID,
      'sprk_regardingrecordtype@odata.bind': `/sprk_recordtype_refs(${MATTER_TYPE_ID})`,
    });
  });

  it('normalises braced, upper-case ids (lookup dialogs return them) and a trailing slash on the BFF url', async () => {
    const { fileProjectUnderMatter } = load();
    const fetchMock = jest.fn().mockResolvedValue(noContent);

    await fileProjectUnderMatter(
      fetchMock,
      `${BFF}/`,
      catalog([{ sprk_recordtype_refid: `{${MATTER_TYPE_ID.toUpperCase()}}` }]),
      `{${PROJECT_ID.toUpperCase()}}`,
      `{${MATTER_ID.toUpperCase()}}`
    );

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe(`${BFF}/api/v1/child-records/sprk_project/${PROJECT_ID}`);
    expect(JSON.parse(init.body)).toEqual({
      sprk_regardingrecordid: MATTER_ID,
      'sprk_regardingrecordtype@odata.bind': `/sprk_recordtype_refs(${MATTER_TYPE_ID})`,
    });
  });

  it("a refusal thrown by the host's fetch reaches the caller with the server's words", async () => {
    const { fileProjectUnderMatter } = load();
    const fetchMock = jest
      .fn()
      .mockRejectedValue(
        apiErrorFor(403, { title: 'Forbidden', detail: 'You cannot file records under this matter.' })
      );

    await expect(fileProjectUnderMatter(fetchMock, BFF, catalog(), PROJECT_ID, MATTER_ID)).rejects.toThrow(
      'You cannot file records under this matter.'
    );
  });

  it('a returned failure (a fetch that resolves non-2xx) is a ChildRecordWriteError with the ProblemDetails', async () => {
    const { fileProjectUnderMatter, ChildRecordWriteError } = load();
    const problem = {
      title: 'Conflict',
      detail: 'The project changed; try again.',
      reasonCode: 'child_record.conflict',
    };
    const fetchMock = jest
      .fn()
      .mockResolvedValue({ ok: false, status: 409, text: async () => JSON.stringify(problem) } as unknown as Response);

    const error = await fileProjectUnderMatter(fetchMock, BFF, catalog(), PROJECT_ID, MATTER_ID).catch(e => e);

    expect(error).toBeInstanceOf(ChildRecordWriteError);
    expect(error.message).toBe('The project changed; try again.');
    expect(error.status).toBe(409);
    expect(error.reasonCode).toBe('child_record.conflict');
  });

  it('sends nothing, and refuses, when the screen has no BFF connection', async () => {
    const { fileProjectUnderMatter } = load();
    const types = catalog();

    await expect(fileProjectUnderMatter(undefined, BFF, types, PROJECT_ID, MATTER_ID)).rejects.toMatchObject({
      reasonCode: 'child_record.bff_not_configured',
    });
    await expect(fileProjectUnderMatter(jest.fn(), undefined, types, PROJECT_ID, MATTER_ID)).rejects.toMatchObject({
      reasonCode: 'child_record.bff_not_configured',
    });
    expect(types.retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('sends nothing, and refuses, when the matter record type cannot be read', async () => {
    const { fileProjectUnderMatter } = load();
    const fetchMock = jest.fn();

    await expect(fileProjectUnderMatter(fetchMock, BFF, catalog([]), PROJECT_ID, MATTER_ID)).rejects.toThrow(
      /matter record type could not be read/
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
