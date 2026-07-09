/**
 * InvoiceService — ADR-024 resolver + dual-bind tests (task 030)
 *
 * Scope: verify that `InvoiceService.createInvoice`:
 *   1. Writes the manifest fields (sprk_invoicenumber, sprk_name, sprk_description,
 *      sprk_vendororg bind, sprk_invoicedate) to the create payload, with
 *      sprk_invoicedate defaulting to today when the form omits it.
 *   2. When a host association (Matter/Project) is supplied, writes the
 *      entity-specific `@odata.bind` lookup AND all 5 denormalized resolver
 *      fields via the shared `applyResolverFields` primitive (ADR-024
 *      mutual exclusion + full field set).
 *   3. Writes NO resolver fields when no association is supplied.
 *   4. Dual-binds an uploaded file's `sprk_document` to BOTH the new Invoice
 *      (primary bind) AND the host record (`additionalBinds`) — FR-12 / design §5.8.
 *
 * These assert on the REAL create payloads captured from the mocked
 * `IDataService.createRecord` — no `Mock<HttpMessageHandler>`, no
 * DI-registration assertions, no ctor null-checks (ADR-038).
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see ../invoiceService.ts
 * @see CreateEventWizard/__tests__/eventService.resolver.test.ts (sibling reference shape)
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { InvoiceService, _resetInvoiceServiceNavPropCacheForTests } from '../invoiceService';
import { buildEmptyInvoiceForm, todayIsoDate } from '../formTypes';
import type { ICreateInvoiceFormState } from '../formTypes';
import type { AssociationResult } from '../../AssociateToStep/types';
import {
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const NEW_INVOICE_GUID = '99999999-8888-7777-6666-555555555555';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const PROJECT_ID_CLEAN = '55555555-6666-7777-8888-999999999999';
const VENDOR_ORG_ID = 'dddddddd-1111-2222-3333-444444444444';

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
      return entityName === 'sprk_invoice' ? NEW_INVOICE_GUID : `${entityName}-generated-id`;
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

/** Stub global fetch for nav-prop discovery — different relationships per entity. */
function stubFetchNavProps() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => {
    const isDocument = url.includes("LogicalName='sprk_document'");
    const isInvoice = url.includes("LogicalName='sprk_invoice'");

    if (isInvoice) {
      return {
        ok: true,
        status: 200,
        json: async () => ({
          value: [
            {
              ReferencingAttribute: 'sprk_matter',
              ReferencingEntityNavigationPropertyName: 'sprk_Matter',
              ReferencedEntity: 'sprk_matter',
            },
            {
              ReferencingAttribute: 'sprk_project',
              ReferencingEntityNavigationPropertyName: 'sprk_Project',
              ReferencedEntity: 'sprk_project',
            },
            {
              ReferencingAttribute: 'sprk_vendororg',
              ReferencingEntityNavigationPropertyName: 'sprk_VendorOrg',
              ReferencedEntity: 'sprk_organization',
            },
            {
              ReferencingAttribute: 'sprk_regardingrecordtype',
              ReferencingEntityNavigationPropertyName: 'sprk_RegardingRecordType',
              ReferencedEntity: 'sprk_recordtype_ref',
            },
          ],
        }),
      } as Response;
    }

    if (isDocument) {
      return {
        ok: true,
        status: 200,
        json: async () => ({
          value: [
            {
              ReferencingAttribute: 'sprk_invoice',
              ReferencingEntityNavigationPropertyName: 'sprk_Invoice',
              ReferencedEntity: 'sprk_invoice',
            },
            {
              ReferencingAttribute: 'sprk_matter',
              ReferencingEntityNavigationPropertyName: 'sprk_Matter',
              ReferencedEntity: 'sprk_matter',
            },
            {
              ReferencingAttribute: 'sprk_project',
              ReferencingEntityNavigationPropertyName: 'sprk_Project',
              ReferencedEntity: 'sprk_project',
            },
          ],
        }),
      } as Response;
    }

    return { ok: true, status: 200, json: async () => ({ value: [] }) } as Response;
  });
}

function makeForm(overrides?: Partial<ICreateInvoiceFormState>): ICreateInvoiceFormState {
  return { ...buildEmptyInvoiceForm(), ...overrides };
}

function invoicePayload(ds: ReturnType<typeof makeDataService>): Record<string, unknown> {
  const p = ds._captured['sprk_invoice']?.[0];
  expect(p).toBeDefined();
  return p!;
}

/** Authenticated-fetch stub for the SPE upload PUT — always succeeds. */
function stubAuthenticatedFetch(): jest.Mock {
  return jest.fn(async () => ({
    ok: true,
    json: async () => ({ id: 'drive-item-1', name: 'invoice.pdf', size: 1024, driveId: 'drive-1' }),
  })) as unknown as jest.Mock;
}

function makeUploadedFile(name = 'invoice.pdf'): IUploadedFile {
  return {
    id: 'local-1',
    name,
    sizeBytes: 1024,
    file: new File(['x'], name, { type: 'application/pdf' }),
  } as unknown as IUploadedFile;
}

// No-cascade override — isolates the resolver/manifest payload from the BU cascade.
const NO_CASCADE_OVERRIDE = () => null;

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('InvoiceService.createInvoice', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    stubFetchNavProps();
    _resetInvoiceServiceNavPropCacheForTests();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
    jest.restoreAllMocks();
  });

  describe('manifest fields', () => {
    it('writes sprk_name, sprk_invoicenumber, sprk_description, and defaults sprk_invoicedate to today when omitted', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const form = makeForm({
        invoiceNumber: 'INV-1001',
        name: 'April Legal Fees',
        description: 'Monthly retainer',
        invoiceDate: '',
      });

      const result = await service.createInvoice(form, null, []);

      expect(result.status).toBe('success');
      expect(result.invoiceId).toBe(NEW_INVOICE_GUID);
      const payload = invoicePayload(ds);
      expect(payload['sprk_name']).toBe('April Legal Fees');
      expect(payload['sprk_invoicenumber']).toBe('INV-1001');
      expect(payload['sprk_description']).toBe('Monthly retainer');
      expect(payload['sprk_invoicedate']).toBe(todayIsoDate());
    });

    it('preserves an explicit sprk_invoicedate rather than overwriting with today', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const result = await service.createInvoice(makeForm({ name: 'X', invoiceDate: '2026-01-15' }), null, []);

      expect(result.status).toBe('success');
      const payload = invoicePayload(ds);
      expect(payload['sprk_invoicedate']).toBe('2026-01-15');
    });

    it('binds the vendor org lookup when vendorOrgId is supplied', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      await service.createInvoice(
        makeForm({ name: 'X', vendorOrgId: VENDOR_ORG_ID, vendorOrgName: 'Acme Vendor Co' }),
        null,
        []
      );

      const payload = invoicePayload(ds);
      expect(payload['sprk_VendorOrg@odata.bind']).toBe(`/sprk_organizations(${VENDOR_ORG_ID})`);
    });
  });

  describe('ADR-024 resolver fields (host regarding)', () => {
    const MATTER_ASSOCIATION: AssociationResult = {
      entityType: 'sprk_matter',
      recordId: MATTER_ID_RAW,
      recordName: 'picker-provided fallback',
    };

    it('writes the entity-specific lookup AND all 5 resolver fields for a Matter host', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const result = await service.createInvoice(makeForm({ name: 'X' }), MATTER_ASSOCIATION, []);

      expect(result.status).toBe('success');
      const payload = invoicePayload(ds);

      expect(payload['sprk_Matter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);
      expect(payload['sprk_RegardingRecordType@odata.bind']).toBe(`/sprk_recordtype_refs(${RT_MATTER_GUID})`);
      expect(payload['sprk_regardingrecordid']).toBe(MATTER_ID_CLEAN);
      expect(payload['sprk_regardingrecordname']).toBe('Smith v. Jones');
      expect(payload['sprk_regardingrecordnumber']).toBe('MAT-2026-0042');
      expect(typeof payload['sprk_regardingrecordurl']).toBe('string');
    });

    it('populates only ONE entity-specific regarding lookup (mutual exclusion) — Project NOT bound for a Matter host', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      await service.createInvoice(makeForm({ name: 'X' }), MATTER_ASSOCIATION, []);

      const payload = invoicePayload(ds);
      expect('sprk_Matter@odata.bind' in payload).toBe(true);
      expect('sprk_Project@odata.bind' in payload).toBe(false);
    });

    it('writes NO resolver fields when no association is supplied', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const result = await service.createInvoice(makeForm({ name: 'X' }), null, []);

      expect(result.status).toBe('success');
      const payload = invoicePayload(ds);
      expect('sprk_regardingrecordid' in payload).toBe(false);
      expect('sprk_Matter@odata.bind' in payload).toBe(false);
      expect('sprk_RegardingRecordType@odata.bind' in payload).toBe(false);
    });

    it('graceful degradation (NFR-06): missing catalog row for Project still links + falls back on name, never throws', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const result = await service.createInvoice(
        makeForm({ name: 'X' }),
        { entityType: 'sprk_project', recordId: PROJECT_ID_CLEAN, recordName: 'Fallback Project Name' },
        []
      );

      expect(result.status).toBe('success');
      const payload = invoicePayload(ds);
      expect(payload['sprk_Project@odata.bind']).toBe(`/sprk_projects(${PROJECT_ID_CLEAN})`);
      expect(payload['sprk_regardingrecordname']).toBe('Fallback Project Name');
      expect('sprk_RegardingRecordType@odata.bind' in payload).toBe(false);
      expect('sprk_regardingrecordnumber' in payload).toBe(false);
    });
  });

  describe('file dual-bind (FR-12 / design §5.8)', () => {
    it('binds an uploaded file to BOTH the new Invoice (primary) and the host Matter (additionalBinds)', async () => {
      const ds = makeDataService();
      const authFetch = stubAuthenticatedFetch();
      const service = new InvoiceService(
        ds,
        authFetch,
        'https://bff.example',
        'container-1',
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const association: AssociationResult = {
        entityType: 'sprk_matter',
        recordId: MATTER_ID_CLEAN,
        recordName: 'Smith v. Jones',
      };

      const result = await service.createInvoice(makeForm({ name: 'X' }), association, [makeUploadedFile()]);

      expect(result.status === 'success' || result.status === 'partial').toBe(true);
      const docPayload = ds._captured['sprk_document']?.[0];
      expect(docPayload).toBeDefined();

      // Primary bind: the new Invoice.
      expect(docPayload!['sprk_Invoice@odata.bind']).toBe(`/sprk_invoices(${NEW_INVOICE_GUID})`);
      // Additional bind: the host Matter.
      expect(docPayload!['sprk_Matter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);
    });

    it('binds an uploaded file to the Invoice ONLY when no host association is supplied', async () => {
      const ds = makeDataService();
      const authFetch = stubAuthenticatedFetch();
      const service = new InvoiceService(
        ds,
        authFetch,
        'https://bff.example',
        'container-1',
        undefined,
        NO_CASCADE_OVERRIDE
      );

      await service.createInvoice(makeForm({ name: 'X' }), null, [makeUploadedFile()]);

      const docPayload = ds._captured['sprk_document']?.[0];
      expect(docPayload).toBeDefined();
      expect(docPayload!['sprk_Invoice@odata.bind']).toBe(`/sprk_invoices(${NEW_INVOICE_GUID})`);
      expect('sprk_Matter@odata.bind' in docPayload!).toBe(false);
      expect('sprk_Project@odata.bind' in docPayload!).toBe(false);
    });

    it('skips upload entirely (with a warning) when files are present but no container ID is configured', async () => {
      const ds = makeDataService();
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const result = await service.createInvoice(makeForm({ name: 'X' }), null, [makeUploadedFile()]);

      expect(result.status).toBe('partial');
      expect(result.warnings.some(w => w.toLowerCase().includes('no spe container configured'))).toBe(true);
      expect(ds._captured['sprk_document']).toBeUndefined();
    });
  });

  describe('error handling', () => {
    it('never throws — returns status "error" with a message when createRecord rejects', async () => {
      const ds = makeDataService();
      (ds.createRecord as jest.Mock).mockRejectedValueOnce(new Error('Dataverse unavailable'));
      const service = new InvoiceService(
        ds,
        stubAuthenticatedFetch(),
        'https://bff.example',
        undefined,
        undefined,
        NO_CASCADE_OVERRIDE
      );

      const result = await service.createInvoice(makeForm({ name: 'X' }), null, []);

      expect(result.status).toBe('error');
      expect(result.errorMessage).toContain('Dataverse unavailable');
    });
  });
});
