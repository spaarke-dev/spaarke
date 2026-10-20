/**
 * worklistLookup - the decision wizard's Person / Lookup search for the Worklist widget (task 059).
 * `@spaarke/ui-components` is mocked at the module boundary: the shared contact / organization helpers have their own tests.
 */
const searchContactsAsLookup = jest.fn(async (..._args: unknown[]) => [{ id: 'c1', name: 'Pat Contact' }]);
const searchOrganizationsAsLookup = jest.fn(async (..._args: unknown[]) => [{ id: 'o1', name: 'Smith LLP' }]);
jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  searchContactsAsLookup: (...a: unknown[]) => searchContactsAsLookup(...a),
  searchOrganizationsAsLookup: (...a: unknown[]) => searchOrganizationsAsLookup(...a),
}));

import { createWorklistLookup } from '../worklistLookup';

const MATTER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const PROJECT = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function setup(entities: Record<string, unknown>[] = []) {
  const retrieveMultipleRecords = jest.fn(async (_entity: string, _options?: string) => ({ entities }));
  return { retrieveMultipleRecords, lookup: createWorklistLookup({ retrieveMultipleRecords }) };
}

describe('createWorklistLookup', () => {
  beforeEach(() => jest.clearAllMocks());

  it('Person is a contact search on what was typed', async () => {
    const { lookup, retrieveMultipleRecords } = setup();
    const out = await lookup({ kind: 'Person', parameterCode: 'person', query: ' pat ', core: null });
    expect(out).toEqual([{ id: 'c1', name: 'Pat Contact' }]);
    expect(searchContactsAsLookup).toHaveBeenCalledWith(expect.anything(), 'pat');
    expect(retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('sprk_organization is an organization search', async () => {
    const { lookup } = setup();
    const out = await lookup({
      kind: 'Lookup',
      entity: 'sprk_organization',
      parameterCode: 'firm',
      query: 'smi',
      core: null,
    });
    expect(out).toEqual([{ id: 'o1', name: 'Smith LLP' }]);
    expect(searchOrganizationsAsLookup).toHaveBeenCalledWith(expect.anything(), 'smi');
  });

  it("sprk_budget with an empty query offers the matter's own budgets, scoped to that matter", async () => {
    const { lookup, retrieveMultipleRecords } = setup([
      { sprk_budgetid: 'b1', sprk_name: 'FY26 budget' },
      { sprk_name: 'no id' },
    ]);
    const out = await lookup({
      kind: 'Lookup',
      entity: 'sprk_budget',
      parameterCode: 'budget',
      query: '',
      core: { recordType: 'sprk_matter', recordId: `{${MATTER.toUpperCase()}}` },
    });
    expect(out).toEqual([{ id: 'b1', name: 'FY26 budget' }]);
    const [entity, options] = retrieveMultipleRecords.mock.calls[0];
    expect(entity).toBe('sprk_budget');
    expect(options).toContain(`_sprk_matter_value eq ${MATTER.toUpperCase()}`);
    expect(options).toContain('statecode eq 0');
    expect(options).not.toContain('contains(');
  });

  it('a typed name narrows the budgets and a quote in it cannot break out of the filter', async () => {
    const { lookup, retrieveMultipleRecords } = setup();
    await lookup({
      kind: 'Lookup',
      entity: 'sprk_budget',
      parameterCode: 'budget',
      query: "O'Neil",
      core: { recordType: 'sprk_project', recordId: PROJECT },
    });
    const options = retrieveMultipleRecords.mock.calls[0][1];
    expect(options).toContain(`_sprk_project_value eq ${PROJECT}`);
    expect(options).toContain("contains(sprk_name,'O''Neil')");
  });

  it('a core id that is not a GUID reads nothing; a Not filed item needs a typed name', async () => {
    const { lookup, retrieveMultipleRecords } = setup();
    expect(
      await lookup({
        kind: 'Lookup',
        entity: 'sprk_budget',
        parameterCode: 'budget',
        query: '',
        core: { recordType: 'sprk_matter', recordId: "x') or (1 eq 1" },
      })
    ).toEqual([]);
    expect(
      await lookup({ kind: 'Lookup', entity: 'sprk_budget', parameterCode: 'budget', query: '', core: null })
    ).toEqual([]);
    expect(retrieveMultipleRecords).not.toHaveBeenCalled();
    await lookup({ kind: 'Lookup', entity: 'sprk_budget', parameterCode: 'budget', query: 'fy', core: null });
    expect(retrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });

  it('any other entity returns no candidates and reads nothing', async () => {
    const { lookup, retrieveMultipleRecords } = setup();
    expect(
      await lookup({ kind: 'Lookup', entity: 'sprk_unknown', parameterCode: 'x', query: 'abc', core: null })
    ).toEqual([]);
    expect(await lookup({ kind: 'Lookup', entity: null, parameterCode: 'x', query: 'abc', core: null })).toEqual([]);
    expect(retrieveMultipleRecords).not.toHaveBeenCalled();
  });
});
