/**
 * InvoiceService.createInvoice — an uploaded file's sprk_document binds the parent column `sprk_invoice`, and the
 * host record's parent column (`sprk_matter` / `sprk_project`), never the `sprk_related*` twin
 * (ontology task 162, UAT round 1 of task 114).
 *
 * Live metadata (spaarkedev1, 2026-10-22): sprk_document has `sprk_invoice` + `sprk_relatedinvoice`, `sprk_matter` +
 * `sprk_relatedmatter`, `sprk_project` + `sprk_relatedproject`. The Invoice / Matter / Project forms' Documents
 * subgrids read the parent column. The host bind used the fragment hint `matter` / `project`, which both column names
 * contain, so it filed under whichever Dataverse listed first. The order is Dataverse's, so both are exercised.
 */

import { bffChildWriteFetch } from '../../../__mocks__/bffChildWriteFake';
import type { IDataService } from '../../../types/serviceInterfaces';
import { EntityCreationService } from '../../../services/EntityCreationService';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';
import type { AssociationResult } from '../../AssociateToStep/types';
import { InvoiceService } from '../invoiceService';
import { buildEmptyInvoiceForm } from '../formTypes';

const INVOICE_ID = '99999999-8888-7777-6666-555555555555';
const HOST_ID = '39cde3e3-9d15-4b29-9a1e-1234567890ab';

const rel = (column: string, navProp: string, referenced: string) => ({
  ReferencingAttribute: column,
  ReferencingEntityNavigationPropertyName: navProp,
  ReferencedEntity: referenced,
});

const INVOICE_PARENT = rel('sprk_invoice', 'sprk_Invoice', 'sprk_invoice');
const INVOICE_RELATED = rel('sprk_relatedinvoice', 'sprk_RelatedInvoice', 'sprk_invoice');
const MATTER_PARENT = rel('sprk_matter', 'sprk_Matter', 'sprk_matter');
const MATTER_RELATED = rel('sprk_relatedmatter', 'sprk_RelatedMatter', 'sprk_matter');
const PROJECT_PARENT = rel('sprk_project', 'sprk_Project', 'sprk_project');
const PROJECT_RELATED = rel('sprk_relatedproject', 'sprk_RelatedProject', 'sprk_project');

function stubFetch(documentRelationships: unknown[]) {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => ({
    ok: true,
    status: 200,
    json: async () => ({ value: url.includes("LogicalName='sprk_document'") ? documentRelationships : [] }),
  }));
}

function makeDataService(): IDataService {
  return {
    createRecord: jest.fn(async () => INVOICE_ID),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

const FILE = { id: 'item-1', name: 'invoice.pdf', size: 10, webUrl: 'https://example/invoice.pdf', driveId: 'b!drive' };

describe('InvoiceService.createInvoice — document parent binds (task 162)', () => {
  let createDocs: jest.SpyInstance;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(EntityCreationService.prototype, 'uploadFilesToSpe').mockResolvedValue({
      success: true,
      successCount: 1,
      failureCount: 0,
      uploadedFiles: [FILE],
      errors: [],
    } as never);
    jest.spyOn(EntityCreationService.prototype, 'indexUploadedFiles').mockResolvedValue([]);
    createDocs = jest
      .spyOn(EntityCreationService.prototype, 'createDocumentRecords')
      .mockResolvedValue({ success: true, linkedCount: 1, createdDocumentIds: ['doc-1'], warnings: [] });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  async function run(documentRelationships: unknown[], association: AssociationResult | null) {
    stubFetch(documentRelationships);
    const ds = makeDataService();
    const service = new InvoiceService(
      ds,
      bffChildWriteFetch(
        ds,
        async () => ({ ok: false, status: 404, json: async () => ({}), text: async () => '' }) as Response
      ),
      'https://bff.example',
      undefined,
      undefined,
      () => null
    );
    const result = await service.createInvoice(buildEmptyInvoiceForm({ name: 'zz-doc-bind' } as never), association, [
      { id: 'l1', name: 'invoice.pdf' } as unknown as IUploadedFile,
    ]);
    expect(result.status).not.toBe('error');
    expect(createDocs).toHaveBeenCalledTimes(1);
    return createDocs.mock.calls[0] as [string, string, string, unknown, { additionalBinds?: Array<{ navProp: string }> }];
  }

  it.each([
    ['parent column listed first', [INVOICE_PARENT, INVOICE_RELATED]],
    ['related column listed first', [INVOICE_RELATED, INVOICE_PARENT]],
  ])('binds the document through sprk_Invoice (%s)', async (_label, rels) => {
    const [entitySet, parentId, navProp, , options] = await run(rels, null);
    expect(entitySet).toBe('sprk_invoices');
    expect(parentId).toBe(INVOICE_ID);
    expect(navProp).toBe('sprk_Invoice');
    expect(options.additionalBinds).toBeUndefined();
  });

  it.each([
    ['sprk_matter', 'sprk_Matter', [MATTER_PARENT, MATTER_RELATED]],
    ['sprk_matter', 'sprk_Matter', [MATTER_RELATED, MATTER_PARENT]],
    ['sprk_project', 'sprk_Project', [PROJECT_PARENT, PROJECT_RELATED]],
    ['sprk_project', 'sprk_Project', [PROJECT_RELATED, PROJECT_PARENT]],
  ])('also binds a %s host through its parent column nav-prop %s, whatever the order', async (entityType, navProp, hostRels) => {
    const [, , invoiceNavProp, , options] = await run(
      [INVOICE_RELATED, INVOICE_PARENT, ...hostRels],
      { entityType, recordId: HOST_ID, recordName: 'Host' }
    );
    expect(invoiceNavProp).toBe('sprk_Invoice');
    expect(options.additionalBinds?.map(b => b.navProp)).toEqual([navProp]);
  });

  it('with a single relationship per parent (unchanged path) binds those', async () => {
    const [, , invoiceNavProp, , options] = await run([INVOICE_PARENT, MATTER_PARENT], {
      entityType: 'sprk_matter',
      recordId: HOST_ID,
      recordName: 'Host',
    });
    expect(invoiceNavProp).toBe('sprk_Invoice');
    expect(options.additionalBinds?.map(b => b.navProp)).toEqual(['sprk_Matter']);
  });
});
