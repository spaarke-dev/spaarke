/**
 * ReportCardService — the server write path (field-mapping-server-write-path-r1, task 027, FR-16)
 *
 * The Create Report Card wizard no longer maps fields on the client. It sends ONLY what the user entered or picked plus
 * the regarding keys to `POST /api/v1/child-records/sprk_reportcard`; the server applies the mapping (fill-only, FR-02)
 * from the filed-under Matter / Project and the owner (FR-09).
 *
 * Proven here (exact bodies):
 *   (a) under a Matter with only a name: sprk_name + the regarding keys, nothing else;
 *   (b) the regression: Law Firm 1, Assigned External and Assigned Internal each bind their OWN column even when a
 *       look-alike column is listed first (`findNavProp` binds the first column CONTAINING a fragment);
 *   (c) an unpicked picker is ABSENT: no key, no empty string, no null anywhere in the body;
 *   (d) no request reaches any `/field-mappings/` route, and the create URL NAMES the regarding Matter as its
 *       `mappingSource`. It must: the route infers a source only for the tables in `InferMappingSourceTables` (the tables
 *       being created), `sprk_reportcard` is not one, and a create naming none maps nothing (the 2026-11-24 dev check).
 *       With no association no source is named;
 *   (e) a server warning in the 201 body reaches `result.warnings` and the status is `partial`.
 */

import { bffChildWriteFetch } from '../../../__mocks__/bffChildWriteFake';
import type { IDataService } from '../../../types/serviceInterfaces';
import { ReportCardService } from '../reportCardService';
import { buildEmptyReportCardForm } from '../formTypes';
import type { ICreateReportCardFormState } from '../formTypes';
import type { AssociationResult } from '../../AssociateToStep/types';
import {
  _resetNavPropCacheForTests,
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';

const BFF_BASE = 'https://bff.test';
const ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_reportcard`;
const NEW_ID = '99999999-8888-7777-6666-555555555555';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const LAWFIRM1_ID = '55555555-1111-1111-1111-111111111111';
const EXTERNAL_ID = '77777777-1111-1111-1111-111111111111';
const INTERNAL_ID = '88888888-1111-1111-1111-111111111111';

function rel(column: string, navProp: string, referenced: string) {
  return {
    ReferencingAttribute: column,
    ReferencingEntityNavigationPropertyName: navProp,
    ReferencedEntity: referenced,
  };
}

/**
 * sprk_reportcard ManyToOne relationships (dev nav-prop names, incl. the lower-case "to" of sprk_AssignedtoExternal),
 * with look-alike contact and organization lookups DELIBERATELY listed before the slots the wizard means: a substring
 * hint ("external", "internal", "lawfirm1") binds the first column containing it.
 */
const REPORTCARD_RELATIONSHIPS = [
  rel('createdby', 'createdby', 'systemuser'),
  rel('sprk_externalreviewer', 'sprk_ExternalReviewer', 'contact'),
  rel('sprk_internalsponsor', 'sprk_InternalSponsor', 'contact'),
  rel('sprk_priorlawfirm1', 'sprk_PriorLawFirm1', 'sprk_organization'),
  rel('sprk_assignedattorney1', 'sprk_AssignedAttorney1', 'contact'),
  rel('sprk_assignedattorney2', 'sprk_AssignedAttorney2', 'contact'),
  rel('sprk_assignedparalegal1', 'sprk_AssignedParalegal1', 'contact'),
  rel('sprk_assignedparalegal2', 'sprk_AssignedParalegal2', 'contact'),
  rel('sprk_assignedtolawfirm1', 'sprk_AssignedToLawFirm1', 'sprk_organization'),
  rel('sprk_assignedlawfirm2', 'sprk_AssignedLawFirm2', 'sprk_organization'),
  rel('sprk_assignedtoexternal', 'sprk_AssignedtoExternal', 'contact'),
  rel('sprk_assignedtointernal', 'sprk_AssignedToInternal', 'contact'),
  rel('sprk_regardingmatter', 'sprk_RegardingMatter', 'sprk_matter'),
  rel('sprk_regardingproject', 'sprk_RegardingProject', 'sprk_project'),
  rel('sprk_regardingrecordtype', 'sprk_RegardingRecordType', 'sprk_recordtype_ref'),
];

function stubNavPropFetch() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => ({
    ok: true,
    status: 200,
    json: async () => ({ value: url.includes("LogicalName='sprk_reportcard'") ? REPORTCARD_RELATIONSHIPS : [] }),
  }));
}

/** The user's data service: the record-type catalog + the parent read for the resolver. */
function makeDataService(): IDataService {
  return {
    createRecord: jest.fn(async () => NEW_ID),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async (entityName: string, options?: string) => {
      const query = options ?? '';
      if (entityName === 'sprk_recordtype_ref') {
        if (query.includes('sprk_recordtype_refid')) {
          return { entities: [{ sprk_recordtype_refid: RT_MATTER_GUID, sprk_recorddisplayname: 'Matter' }] };
        }
        if (query.includes('sprk_regardingrecordnumberfield')) {
          return { entities: [{ sprk_regardingrecordnumberfield: 'sprk_matternumber' }] };
        }
        if (query.includes('sprk_recorddisplaynamefield')) {
          return { entities: [{ sprk_recorddisplaynamefield: 'sprk_mattername' }] };
        }
        return { entities: [] };
      }
      if (entityName === 'sprk_matter') {
        return { entities: [{ sprk_matternumber: 'MAT-2026-0042', sprk_mattername: 'Smith v. Jones' }] };
      }
      return { entities: [] };
    }),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

const MATTER: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: MATTER_ID_RAW,
  recordName: 'Smith v. Jones',
};

/** The regarding keys a Matter parent writes (ADR-024: the typed lookup + the resolver fields). */
const REGARDING = {
  'sprk_RegardingMatter@odata.bind': `/sprk_matters(${MATTER_ID_CLEAN})`,
  'sprk_RegardingRecordType@odata.bind': `/sprk_recordtype_refs(${RT_MATTER_GUID})`,
  sprk_regardingrecordid: MATTER_ID_CLEAN,
  sprk_regardingrecordname: 'Smith v. Jones',
  sprk_regardingrecordnumber: 'MAT-2026-0042',
  sprk_regardingrecordurl: expect.stringContaining(`etn=sprk_matter&id=${MATTER_ID_CLEAN}`),
};

function makeForm(over: Partial<ICreateReportCardFormState> = {}): ICreateReportCardFormState {
  return { ...buildEmptyReportCardForm(), ...over };
}

interface IPostCall {
  url: string;
  method: string;
  body: Record<string, unknown>;
}

function postsTo(fetchMock: jest.Mock, route: string): IPostCall[] {
  return fetchMock.mock.calls
    .filter(([url]) => String(url).startsWith(route))
    .map(([url, init]) => ({
      url: String(url),
      method: String((init as RequestInit).method),
      body: JSON.parse((init as RequestInit).body as string),
    }));
}

/** A fetch that answers the create route with a 201 carrying `warnings`; any other call gets a 404. */
function warningFetch(warnings: string[]): jest.Mock {
  return jest.fn(async (url: string) => {
    if (String(url).startsWith(ROUTE)) {
      return {
        ok: true,
        status: 201,
        json: async () => ({ id: NEW_ID, warnings }),
        text: async () => '',
      } as unknown as Response;
    }
    return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
  });
}

describe('ReportCardService — the server write path (task 027, FR-16)', () => {
  beforeEach(() => {
    _resetNavPropCacheForTests();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    stubNavPropFetch();
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  // (a) -------------------------------------------------------------------------------------------------------------
  it('under a Matter with only a name, the body is sprk_name plus the regarding keys exactly', async () => {
    const fetchMock = bffChildWriteFetch(makeDataService());
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);

    const result = await service.createReportCard(makeForm({ name: '  Q3 Compliance Review  ' }), MATTER);

    expect(result.status).toBe('success');
    expect(result.reportCardId).toBe(NEW_ID);
    expect(result.warnings).toEqual([]);
    const calls = postsTo(fetchMock, ROUTE);
    expect(calls).toHaveLength(1);
    expect(calls[0].method).toBe('POST');
    expect(calls[0].body).toEqual({ sprk_name: 'Q3 Compliance Review', ...REGARDING });
  });

  // (b) -------------------------------------------------------------------------------------------------------------
  it('Law Firm 1, Assigned External and Assigned Internal bind their own columns (exact body)', async () => {
    const fetchMock = bffChildWriteFetch(makeDataService());
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);

    const result = await service.createReportCard(
      makeForm({
        name: 'Q3 Compliance Review',
        assignedToLawFirm1Id: `{${LAWFIRM1_ID.toUpperCase()}}`,
        assignedToExternalId: EXTERNAL_ID,
        assignedToInternalId: INTERNAL_ID,
      }),
      MATTER
    );

    expect(result.status).toBe('success');
    const [call] = postsTo(fetchMock, ROUTE);
    expect(call.body).toEqual({
      sprk_name: 'Q3 Compliance Review',
      'sprk_AssignedToLawFirm1@odata.bind': `/sprk_organizations(${LAWFIRM1_ID})`,
      'sprk_AssignedtoExternal@odata.bind': `/contacts(${EXTERNAL_ID})`,
      'sprk_AssignedToInternal@odata.bind': `/contacts(${INTERNAL_ID})`,
      ...REGARDING,
    });
  });

  it('all eight pickers, narrative and due date: exactly those keys plus the regarding keys', async () => {
    const fetchMock = bffChildWriteFetch(makeDataService());
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);
    const ids = ['1', '2', '3', '4', '5', '6', '7', '8'].map(n => `${n.repeat(8)}-1111-1111-1111-111111111111`);

    await service.createReportCard(
      makeForm({
        name: 'X',
        narrative: ' Notes ',
        dueDate: '2026-09-30',
        assignedAttorney1Id: ids[0],
        assignedAttorney2Id: ids[1],
        assignedParalegal1Id: ids[2],
        assignedParalegal2Id: ids[3],
        assignedToLawFirm1Id: ids[4],
        assignedLawFirm2Id: ids[5],
        assignedToExternalId: ids[6],
        assignedToInternalId: ids[7],
      }),
      MATTER
    );

    const [call] = postsTo(fetchMock, ROUTE);
    expect(call.body).toEqual({
      sprk_name: 'X',
      sprk_narrative: 'Notes',
      sprk_duedate: '2026-09-30',
      'sprk_AssignedAttorney1@odata.bind': `/contacts(${ids[0]})`,
      'sprk_AssignedAttorney2@odata.bind': `/contacts(${ids[1]})`,
      'sprk_AssignedParalegal1@odata.bind': `/contacts(${ids[2]})`,
      'sprk_AssignedParalegal2@odata.bind': `/contacts(${ids[3]})`,
      'sprk_AssignedToLawFirm1@odata.bind': `/sprk_organizations(${ids[4]})`,
      'sprk_AssignedLawFirm2@odata.bind': `/sprk_organizations(${ids[5]})`,
      'sprk_AssignedtoExternal@odata.bind': `/contacts(${ids[6]})`,
      'sprk_AssignedToInternal@odata.bind': `/contacts(${ids[7]})`,
      ...REGARDING,
    });
  });

  // (c) -------------------------------------------------------------------------------------------------------------
  it('an unpicked picker is absent: no key, no empty string, no null', async () => {
    const fetchMock = bffChildWriteFetch(makeDataService());
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);

    await service.createReportCard(
      makeForm({
        name: 'Q3 Compliance Review',
        // Picked, with the other seven left empty; the names alone (no id) must not leak a key either.
        assignedToInternalId: INTERNAL_ID,
        assignedAttorney1Name: 'Stale display name',
        assignedToExternalName: 'Stale display name',
      }),
      MATTER
    );

    const [call] = postsTo(fetchMock, ROUTE);
    const assignedKeys = Object.keys(call.body).filter(k => /assigned/i.test(k));
    expect(assignedKeys).toEqual(['sprk_AssignedToInternal@odata.bind']);
    for (const [key, value] of Object.entries(call.body)) {
      expect({ key, value }).not.toEqual({ key, value: '' });
      expect({ key, value }).not.toEqual({ key, value: null });
      expect(value).not.toBeUndefined();
    }
    expect(Object.keys(call.body)).not.toContain('sprk_narrative');
    expect(Object.keys(call.body)).not.toContain('sprk_duedate');
  });

  // (d) -------------------------------------------------------------------------------------------------------------
  it('reads no field-mapping profile and names the regarding Matter as the mapping source on the create URL', async () => {
    const fetchMock = bffChildWriteFetch(makeDataService());
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);

    await service.createReportCard(makeForm({ name: 'X' }), MATTER);

    const urls = fetchMock.mock.calls.map(([url]) => String(url));
    expect(urls.filter(u => /field-mappings/i.test(u))).toEqual([]);
    expect(urls).toEqual([`${ROUTE}?mappingSourceEntity=sprk_matter&mappingSourceId=${MATTER_ID_CLEAN}`]);
  });

  it('with no association, names no mapping source and writes no regarding keys', async () => {
    const fetchMock = bffChildWriteFetch(makeDataService());
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);

    const result = await service.createReportCard(makeForm({ name: 'X' }), null);

    expect(result.status).toBe('success');
    const urls = fetchMock.mock.calls.map(([url]) => String(url));
    expect(urls).toEqual([ROUTE]);
    const [call] = postsTo(fetchMock, ROUTE);
    expect(call.body).toEqual({ sprk_name: 'X' });
  });

  it('creates through the BFF route, never through dataService.createRecord (Xrm.WebApi)', async () => {
    const dataService = makeDataService();
    const fetchMock = warningFetch([]);
    const service = new ReportCardService(dataService, fetchMock, BFF_BASE);

    const result = await service.createReportCard(makeForm({ name: 'X' }), MATTER);

    expect(result.status).toBe('success');
    expect(postsTo(fetchMock, ROUTE)).toHaveLength(1);
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });

  // (e) -------------------------------------------------------------------------------------------------------------
  it('server warnings from the 201 body surface in result.warnings and the status is partial', async () => {
    const fetchMock = warningFetch(['One mapped value could not be written.', 'The mapping source could not be read.']);
    const service = new ReportCardService(makeDataService(), fetchMock, BFF_BASE);

    const result = await service.createReportCard(makeForm({ name: 'X' }), MATTER);

    expect(result.status).toBe('partial');
    expect(result.reportCardId).toBe(NEW_ID);
    expect(result.warnings).toEqual([
      'One mapped value could not be written.',
      'The mapping source could not be read.',
    ]);
  });

  it('a refused create returns status error and never throws', async () => {
    const refused = jest.fn(async () => {
      throw Object.assign(new Error('Forbidden'), { status: 403 });
    });
    const service = new ReportCardService(makeDataService(), refused as never, BFF_BASE);

    const result = await service.createReportCard(makeForm({ name: 'X' }), MATTER);

    expect(result.status).toBe('error');
    expect(result.warnings).toEqual([]);
    expect(result.errorMessage).toBeTruthy();
  });
});
