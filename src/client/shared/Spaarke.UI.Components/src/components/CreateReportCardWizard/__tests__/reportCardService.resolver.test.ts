/**
 * ReportCardService — ADR-024 resolver field population tests (task 040)
 *
 * Scope: verify that `ReportCardService.createReportCard`:
 *   1. Writes the manifest fields (sprk_name, sprk_narrative, sprk_duedate)
 *      to the create payload, with sprk_name trimmed and required.
 *   2. Binds the 8 assigned-resource lookups (contacts + organizations) when
 *      supplied, using the asymmetric real schema field-name hints
 *      (assignedtolawfirm1 / assignedlawfirm2) — not "corrected".
 *   3. When a host association (Matter/Project) is supplied, writes the
 *      entity-specific `@odata.bind` lookup AND all 5 denormalized resolver
 *      fields via the shared `applyResolverFields` primitive (ADR-024 mutual
 *      exclusion + full field set).
 *   4. Writes NO resolver fields when no association is supplied.
 *   5. Never sets `sprk_reportcardnumber` client-side (manifest: autonumber /
 *      server-side, out of scope for Enter Info).
 *
 * These assert on the REAL create payloads captured from the mocked
 * `IDataService.createRecord` — no `Mock<HttpMessageHandler>`, no
 * DI-registration assertions, no ctor null-checks (ADR-038).
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see ../reportCardService.ts
 * @see CreateInvoiceWizard/__tests__/invoiceService.resolver.test.ts (sibling reference shape)
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { ReportCardService, _resetReportCardServiceNavPropCacheForTests } from '../reportCardService';
import { buildEmptyReportCardForm } from '../formTypes';
import type { ICreateReportCardFormState } from '../formTypes';
import type { AssociationResult } from '../../AssociateToStep/types';
import {
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const NEW_REPORTCARD_GUID = '99999999-8888-7777-6666-555555555555';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const PROJECT_ID_CLEAN = '55555555-6666-7777-8888-999999999999';
const ATTORNEY1_ID = '11111111-1111-1111-1111-111111111111';
const ATTORNEY2_ID = '22222222-2222-2222-2222-222222222222';
const PARALEGAL1_ID = '33333333-3333-3333-3333-333333333333';
const PARALEGAL2_ID = '44444444-4444-4444-4444-444444444444';
const LAWFIRM1_ID = '55555555-1111-1111-1111-111111111111';
const LAWFIRM2_ID = '66666666-1111-1111-1111-111111111111';
const EXTERNAL_ID = '77777777-1111-1111-1111-111111111111';
const INTERNAL_ID = '88888888-1111-1111-1111-111111111111';

const CATALOG: Record<
  string,
  {
    recordTypeId: string;
    recordTypeName: string;
    numberField: string;
    displayNameField: string;
    target: Record<string, unknown>;
  }
> = {
  sprk_matter: {
    recordTypeId: RT_MATTER_GUID,
    recordTypeName: 'Matter',
    numberField: 'sprk_matternumber',
    displayNameField: 'sprk_mattername',
    target: { sprk_matternumber: 'MAT-2026-0042', sprk_mattername: 'Smith v. Jones' },
  },
};

/** Mock IDataService capturing every create payload by entity name. */
function makeDataService(): IDataService & { _captured: Record<string, Record<string, unknown>[]> } {
  const captured: Record<string, Record<string, unknown>[]> = {};

  return {
    _captured: captured,

    createRecord: jest.fn(async (entityName: string, data: Record<string, unknown>) => {
      captured[entityName] = captured[entityName] ?? [];
      captured[entityName].push(data);
      return entityName === 'sprk_reportcard' ? NEW_REPORTCARD_GUID : `${entityName}-generated-id`;
    }),

    retrieveRecord: jest.fn(async () => ({})),

    retrieveMultipleRecords: jest.fn(async (entityName: string, options?: string) => {
      const query = options ?? '';

      if (entityName === 'sprk_recordtype_ref') {
        const logical = /sprk_recordlogicalname eq '([^']+)'/.exec(query)?.[1];
        const entry = logical ? CATALOG[logical] : undefined;
        if (!entry) return { entities: [] };
        if (query.includes('sprk_recordtype_refid')) {
          return {
            entities: [{ sprk_recordtype_refid: entry.recordTypeId, sprk_recorddisplayname: entry.recordTypeName }],
          };
        }
        if (query.includes('sprk_regardingrecordnumberfield')) {
          return { entities: [{ sprk_regardingrecordnumberfield: entry.numberField }] };
        }
        if (query.includes('sprk_recorddisplaynamefield')) {
          return { entities: [{ sprk_recorddisplaynamefield: entry.displayNameField }] };
        }
        return { entities: [] };
      }

      const entry = CATALOG[entityName];
      return entry ? { entities: [entry.target] } : { entities: [] };
    }),

    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

/** Stub global fetch for nav-prop discovery — sprk_reportcard's ManyToOneRelationships. */
function stubFetchNavProps() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => {
    const isReportCard = url.includes("LogicalName='sprk_reportcard'");

    if (isReportCard) {
      return {
        ok: true,
        status: 200,
        json: async () => ({
          value: [
            {
              ReferencingAttribute: 'sprk_regardingmatter',
              ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
              ReferencedEntity: 'sprk_matter',
            },
            {
              ReferencingAttribute: 'sprk_regardingproject',
              ReferencingEntityNavigationPropertyName: 'sprk_RegardingProject',
              ReferencedEntity: 'sprk_project',
            },
            {
              ReferencingAttribute: 'sprk_regardingrecordtype',
              ReferencingEntityNavigationPropertyName: 'sprk_RegardingRecordType',
              ReferencedEntity: 'sprk_recordtype_ref',
            },
            {
              ReferencingAttribute: 'sprk_assignedattorney1',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney1',
              ReferencedEntity: 'contact',
            },
            {
              ReferencingAttribute: 'sprk_assignedattorney2',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney2',
              ReferencedEntity: 'contact',
            },
            {
              ReferencingAttribute: 'sprk_assignedparalegal1',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedParalegal1',
              ReferencedEntity: 'contact',
            },
            {
              ReferencingAttribute: 'sprk_assignedparalegal2',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedParalegal2',
              ReferencedEntity: 'contact',
            },
            {
              ReferencingAttribute: 'sprk_assignedtolawfirm1',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedToLawFirm1',
              ReferencedEntity: 'sprk_organization',
            },
            {
              ReferencingAttribute: 'sprk_assignedlawfirm2',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedLawFirm2',
              ReferencedEntity: 'sprk_organization',
            },
            {
              ReferencingAttribute: 'sprk_assignedtoexternal',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedToExternal',
              ReferencedEntity: 'contact',
            },
            {
              ReferencingAttribute: 'sprk_assignedtointernal',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedToInternal',
              ReferencedEntity: 'contact',
            },
          ],
        }),
      } as Response;
    }

    return { ok: true, status: 200, json: async () => ({ value: [] }) } as Response;
  });
}

/**
 * Authenticated-fetch stub for the field-mapping engine's profile fetch
 * (task 022 wiring) — always reports "no profile configured" (404), which is
 * the engine's graceful no-op path (`profileFound:false`, no warning). These
 * tests exercise resolver/manifest behavior, not field-mapping application,
 * and `ReportCardService` has no other `authenticatedFetch` consumer (no
 * file-upload pipeline, unlike `InvoiceService`).
 */
function stubAuthenticatedFetch(): jest.Mock {
  return jest.fn(async () => ({
    ok: false,
    status: 404,
    statusText: 'Not Found',
    text: async () => '',
  })) as unknown as jest.Mock;
}

function makeForm(overrides?: Partial<ICreateReportCardFormState>): ICreateReportCardFormState {
  return { ...buildEmptyReportCardForm(), ...overrides };
}

function reportCardPayload(ds: ReturnType<typeof makeDataService>): Record<string, unknown> {
  const p = ds._captured['sprk_reportcard']?.[0];
  expect(p).toBeDefined();
  return p!;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ReportCardService.createReportCard', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    stubFetchNavProps();
    _resetReportCardServiceNavPropCacheForTests();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
    jest.restoreAllMocks();
  });

  describe('manifest fields', () => {
    it('writes sprk_name (trimmed), sprk_narrative, and sprk_duedate', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      const form = makeForm({
        name: '  Q3 Compliance Review  ',
        narrative: 'Quarterly review notes',
        dueDate: '2026-09-30',
      });

      const result = await service.createReportCard(form, null);

      expect(result.status).toBe('success');
      expect(result.reportCardId).toBe(NEW_REPORTCARD_GUID);
      const payload = reportCardPayload(ds);
      expect(payload['sprk_name']).toBe('Q3 Compliance Review');
      expect(payload['sprk_narrative']).toBe('Quarterly review notes');
      expect(payload['sprk_duedate']).toBe('2026-09-30');
    });

    it('omits sprk_narrative and sprk_duedate when not supplied', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      await service.createReportCard(makeForm({ name: 'Minimal Report Card' }), null);

      const payload = reportCardPayload(ds);
      expect('sprk_narrative' in payload).toBe(false);
      expect('sprk_duedate' in payload).toBe(false);
    });

    it('NEVER sets sprk_reportcardnumber client-side (autonumber/server-side per manifest)', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      await service.createReportCard(makeForm({ name: 'X' }), null);

      const payload = reportCardPayload(ds);
      expect('sprk_reportcardnumber' in payload).toBe(false);
    });
  });

  describe('assigned-resource lookups (8 fields, manifest §)', () => {
    it('binds all 8 assigned-resource lookups using the real (asymmetric) schema field names when supplied', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      await service.createReportCard(
        makeForm({
          name: 'X',
          assignedAttorney1Id: ATTORNEY1_ID,
          assignedAttorney2Id: ATTORNEY2_ID,
          assignedParalegal1Id: PARALEGAL1_ID,
          assignedParalegal2Id: PARALEGAL2_ID,
          assignedToLawFirm1Id: LAWFIRM1_ID,
          assignedLawFirm2Id: LAWFIRM2_ID,
          assignedToExternalId: EXTERNAL_ID,
          assignedToInternalId: INTERNAL_ID,
        }),
        null
      );

      const payload = reportCardPayload(ds);
      expect(payload['sprk_AssignedAttorney1@odata.bind']).toBe(`/contacts(${ATTORNEY1_ID})`);
      expect(payload['sprk_AssignedAttorney2@odata.bind']).toBe(`/contacts(${ATTORNEY2_ID})`);
      expect(payload['sprk_AssignedParalegal1@odata.bind']).toBe(`/contacts(${PARALEGAL1_ID})`);
      expect(payload['sprk_AssignedParalegal2@odata.bind']).toBe(`/contacts(${PARALEGAL2_ID})`);
      // Asymmetric naming — "assignedtolawfirm1" vs "assignedlawfirm2" — real schema, not "fixed".
      expect(payload['sprk_AssignedToLawFirm1@odata.bind']).toBe(`/sprk_organizations(${LAWFIRM1_ID})`);
      expect(payload['sprk_AssignedLawFirm2@odata.bind']).toBe(`/sprk_organizations(${LAWFIRM2_ID})`);
      expect(payload['sprk_AssignedToExternal@odata.bind']).toBe(`/contacts(${EXTERNAL_ID})`);
      expect(payload['sprk_AssignedToInternal@odata.bind']).toBe(`/contacts(${INTERNAL_ID})`);
    });

    it('omits all 8 resource-lookup binds when none are supplied (all optional)', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      await service.createReportCard(makeForm({ name: 'X' }), null);

      const payload = reportCardPayload(ds);
      const resourceBindKeys = [
        'sprk_AssignedAttorney1@odata.bind',
        'sprk_AssignedAttorney2@odata.bind',
        'sprk_AssignedParalegal1@odata.bind',
        'sprk_AssignedParalegal2@odata.bind',
        'sprk_AssignedToLawFirm1@odata.bind',
        'sprk_AssignedLawFirm2@odata.bind',
        'sprk_AssignedToExternal@odata.bind',
        'sprk_AssignedToInternal@odata.bind',
      ];
      for (const key of resourceBindKeys) {
        expect(key in payload).toBe(false);
      }
    });
  });

  describe('ADR-024 resolver fields (host regarding — Matter/Project only)', () => {
    const MATTER_ASSOCIATION: AssociationResult = {
      entityType: 'sprk_matter',
      recordId: MATTER_ID_RAW,
      recordName: 'picker-provided fallback',
    };

    it('writes the entity-specific lookup AND all 5 resolver fields for a Matter host', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      const result = await service.createReportCard(makeForm({ name: 'X' }), MATTER_ASSOCIATION);

      expect(result.status).toBe('success');
      const payload = reportCardPayload(ds);

      expect(payload['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);
      expect(payload['sprk_RegardingRecordType@odata.bind']).toBe(`/sprk_recordtype_refs(${RT_MATTER_GUID})`);
      expect(payload['sprk_regardingrecordid']).toBe(MATTER_ID_CLEAN);
      expect(payload['sprk_regardingrecordname']).toBe('Smith v. Jones');
      expect(payload['sprk_regardingrecordnumber']).toBe('MAT-2026-0042');
      expect(typeof payload['sprk_regardingrecordurl']).toBe('string');
    });

    it('populates only ONE entity-specific regarding lookup (mutual exclusion) — Project NOT bound for a Matter host', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      await service.createReportCard(makeForm({ name: 'X' }), MATTER_ASSOCIATION);

      const payload = reportCardPayload(ds);
      expect('sprk_RegardingMatter@odata.bind' in payload).toBe(true);
      expect('sprk_RegardingProject@odata.bind' in payload).toBe(false);
    });

    it('writes NO resolver fields when no association is supplied', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      const result = await service.createReportCard(makeForm({ name: 'X' }), null);

      expect(result.status).toBe('success');
      const payload = reportCardPayload(ds);
      expect('sprk_regardingrecordid' in payload).toBe(false);
      expect('sprk_RegardingMatter@odata.bind' in payload).toBe(false);
      expect('sprk_RegardingRecordType@odata.bind' in payload).toBe(false);
    });

    it('graceful degradation (NFR-06): missing catalog row for Project still links + falls back on name, never throws', async () => {
      const ds = makeDataService();
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      const result = await service.createReportCard(makeForm({ name: 'X' }), {
        entityType: 'sprk_project',
        recordId: PROJECT_ID_CLEAN,
        recordName: 'Fallback Project Name',
      });

      expect(result.status).toBe('success');
      const payload = reportCardPayload(ds);
      expect(payload['sprk_RegardingProject@odata.bind']).toBe(`/sprk_projects(${PROJECT_ID_CLEAN})`);
      expect(payload['sprk_regardingrecordname']).toBe('Fallback Project Name');
      expect('sprk_RegardingRecordType@odata.bind' in payload).toBe(false);
      expect('sprk_regardingrecordnumber' in payload).toBe(false);
    });
  });

  describe('error handling', () => {
    it('never throws — returns status "error" with a message when createRecord rejects', async () => {
      const ds = makeDataService();
      (ds.createRecord as jest.Mock).mockRejectedValueOnce(new Error('Dataverse unavailable'));
      const service = new ReportCardService(ds, stubAuthenticatedFetch(), 'https://bff.example');

      const result = await service.createReportCard(makeForm({ name: 'X' }), null);

      expect(result.status).toBe('error');
      expect(result.errorMessage).toContain('Dataverse unavailable');
    });
  });
});
