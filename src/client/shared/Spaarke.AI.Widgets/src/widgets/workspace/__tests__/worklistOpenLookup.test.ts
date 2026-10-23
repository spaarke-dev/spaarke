/**
 * worklistLookup (task 158; D-182): the decision wizard's side-pane lookup and record modal, as the Worklist hosts build them.
 * The pane must be scoped exactly as the inline search is (`createWorklistLookup`), and no wider.
 */
jest.mock('@spaarke/ui-components', () => ({
  __esModule: true,
  searchContactsAsLookup: jest.fn(),
  searchOrganizationsAsLookup: jest.fn(),
}));

import { createWorklistOpenLookup, createWorklistOpenRecord } from '../worklistLookup';

const MATTER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const PROJECT = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

type Picked = { id: string; name: string; entityType: string };
const nav = (picked: Picked[] = []) => ({ openLookup: jest.fn(async (_options: unknown) => picked) });
const filtersOf = (n: ReturnType<typeof nav>) =>
  (n.openLookup.mock.calls[0][0] as { filters?: { entityLogicalName: string; filterXml: string }[] }).filters;

describe('createWorklistOpenLookup (the side-pane lookup, scoped exactly as the inline search)', () => {
  it('Person opens the contact pane, single select, on active contacts', async () => {
    const n = nav([{ id: 'c1', name: 'Pat Contact', entityType: 'contact' }]);
    const out = await createWorklistOpenLookup(n)({
      kind: 'Person',
      entity: 'contact',
      parameterCode: 'assignee',
      core: null,
    });
    expect(out).toEqual({ id: 'c1', name: 'Pat Contact' });
    expect(n.openLookup).toHaveBeenCalledTimes(1);
    expect(n.openLookup).toHaveBeenCalledWith({
      entityType: 'contact',
      entityTypes: ['contact'],
      allowMultiSelect: false,
      filters: [
        {
          entityLogicalName: 'contact',
          filterXml: '<filter type="and"><condition attribute="statecode" operator="eq" value="0" /></filter>',
        },
      ],
    });
  });

  it("a budget is active and on the item's own matter", async () => {
    const n = nav([{ id: 'b1', name: 'FY26', entityType: 'sprk_budget' }]);
    const out = await createWorklistOpenLookup(n)({
      kind: 'Lookup',
      entity: 'sprk_budget',
      parameterCode: 'budget',
      core: { recordType: 'sprk_matter', recordId: `{${MATTER}}` },
    });
    expect(out).toEqual({ id: 'b1', name: 'FY26' });
    expect(n.openLookup).toHaveBeenCalledWith(
      expect.objectContaining({ entityType: 'sprk_budget', entityTypes: ['sprk_budget'], allowMultiSelect: false })
    );
    expect(filtersOf(n)).toEqual([
      {
        entityLogicalName: 'sprk_budget',
        filterXml:
          '<filter type="and"><condition attribute="statecode" operator="eq" value="0" />' +
          `<condition attribute="sprk_matter" operator="eq" value="${MATTER}" /></filter>`,
      },
    ]);
  });

  it('a budget of a project is scoped to the project', async () => {
    const n = nav();
    await createWorklistOpenLookup(n)({
      kind: 'Lookup',
      entity: 'sprk_budget',
      parameterCode: 'budget',
      core: { recordType: 'sprk_project', recordId: PROJECT },
    });
    const xml = filtersOf(n)?.[0].filterXml ?? '';
    expect(xml).toContain(`<condition attribute="sprk_project" operator="eq" value="${PROJECT}" />`);
    expect(xml).toContain('<condition attribute="statecode" operator="eq" value="0" />');
  });

  it('a budget with no core is active only: the inline search offers a typed name across budgets, no wider', async () => {
    const n = nav();
    await createWorklistOpenLookup(n)({ kind: 'Lookup', entity: 'sprk_budget', parameterCode: 'budget', core: null });
    expect(filtersOf(n)).toEqual([
      {
        entityLogicalName: 'sprk_budget',
        filterXml: '<filter type="and"><condition attribute="statecode" operator="eq" value="0" /></filter>',
      },
    ]);
  });

  it('a core id that is not a GUID opens nothing (it never widens)', async () => {
    const n = nav();
    const out = await createWorklistOpenLookup(n)({
      kind: 'Lookup',
      entity: 'sprk_budget',
      parameterCode: 'budget',
      core: { recordType: 'sprk_matter', recordId: "x' or 1=1" },
    });
    expect(out).toBeNull();
    expect(n.openLookup).not.toHaveBeenCalled();
  });

  it('sprk_organization is the firm pane with no extra filter', async () => {
    const n = nav([{ id: 'o1', name: 'Smith LLP', entityType: 'sprk_organization' }]);
    const out = await createWorklistOpenLookup(n)({
      kind: 'Lookup',
      entity: 'sprk_organization',
      parameterCode: 'outsideFirm',
      core: null,
    });
    expect(out).toEqual({ id: 'o1', name: 'Smith LLP' });
    expect(n.openLookup).toHaveBeenCalledWith({
      entityType: 'sprk_organization',
      entityTypes: ['sprk_organization'],
      allowMultiSelect: false,
    });
  });

  it('an entity the catalog does not name opens nothing, as the inline search returns no candidates for it', async () => {
    const n = nav();
    const out = await createWorklistOpenLookup(n)({
      kind: 'Lookup',
      entity: 'sprk_matter',
      parameterCode: 'x',
      core: null,
    });
    expect(out).toBeNull();
    expect(n.openLookup).not.toHaveBeenCalled();
  });

  it('cancelling the pane gives null', async () => {
    const n = nav([]);
    const out = await createWorklistOpenLookup(n)({
      kind: 'Person',
      entity: 'contact',
      parameterCode: 'assignee',
      core: null,
    });
    expect(out).toBeNull();
  });

  it('K2: declares exactly the tables it handles, so the wizard keeps the inline field for any other', async () => {
    const open = createWorklistOpenLookup(nav());
    expect([...(open.supportedEntities ?? [])].sort()).toEqual(['contact', 'sprk_budget', 'sprk_organization']);
    // Every declared table opens the pane (never the null a dead field would come from).
    for (const entity of open.supportedEntities ?? []) {
      const n = nav([{ id: 'x1', name: 'Picked', entityType: entity }]);
      const out = await createWorklistOpenLookup(n)({
        kind: entity === 'contact' ? 'Person' : 'Lookup',
        entity,
        parameterCode: 'p',
        core: null,
      });
      expect(out).toEqual({ id: 'x1', name: 'Picked' });
    }
  });
});

describe('createWorklistOpenRecord (the subject opens in a modal, never as a page navigation)', () => {
  it('opens the record with openRecordModal', async () => {
    const openRecordModal = jest.fn(async (_entity: string, _id: string) => undefined);
    await createWorklistOpenRecord({ openRecordModal })?.('sprk_matter', MATTER);
    expect(openRecordModal).toHaveBeenCalledWith('sprk_matter', MATTER);
  });

  it('gives no opener when the host cannot open a modal, so the wizard shows text and never replaces the page', () => {
    expect(createWorklistOpenRecord({})).toBeUndefined();
  });
});
