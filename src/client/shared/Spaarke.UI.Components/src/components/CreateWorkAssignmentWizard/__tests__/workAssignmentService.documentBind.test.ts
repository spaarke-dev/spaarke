/**
 * WorkAssignmentService.createWorkAssignment — an uploaded file's sprk_document binds the parent column
 * `sprk_workassignment` (ontology task 162, UAT round 1 of task 114).
 *
 * Live metadata (spaarkedev1, 2026-10-22): sprk_document → sprk_workassignment has TWO lookups, `sprk_workassignment`
 * (nav prop `sprk_WorkAssignment`, what the Work Assignment form's Documents subgrid reads —
 * `sprk_WorkAssignment_Document_1n`) and `sprk_relatedworkassignment` (`sprk_RelatedWorkAssignment`). The wizard took the
 * first relationship Dataverse listed, so a document could be filed under the related column and miss the subgrid.
 * The order is Dataverse's, so both are exercised.
 */

import { bffChildWriteFetch } from '../../../__mocks__/bffChildWriteFake';
import type { IDataService } from '../../../types/serviceInterfaces';
import { EntityCreationService } from '../../../services/EntityCreationService';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import type { IUploadedFile } from '../../FileUpload/fileUploadTypes';
import { WorkAssignmentService } from '../workAssignmentService';
import type { ICreateWorkAssignmentFormState } from '../formTypes';

const WA_ID = '33333333-3333-3333-3333-333333333333';

const PARENT_REL = {
  ReferencingAttribute: 'sprk_workassignment',
  ReferencingEntityNavigationPropertyName: 'sprk_WorkAssignment',
  ReferencedEntity: 'sprk_workassignment',
};
const RELATED_REL = {
  ReferencingAttribute: 'sprk_relatedworkassignment',
  ReferencingEntityNavigationPropertyName: 'sprk_RelatedWorkAssignment',
  ReferencedEntity: 'sprk_workassignment',
};

function stubFetch(documentRelationships: unknown[]) {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => ({
    ok: true,
    status: 200,
    json: async () => ({ value: url.includes("LogicalName='sprk_document'") ? documentRelationships : [] }),
  }));
}

function makeDataService(): IDataService {
  return {
    createRecord: jest.fn(async () => WA_ID),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

const FILE = { id: 'item-1', name: 'brief.pdf', size: 10, webUrl: 'https://example/brief.pdf', driveId: 'b!drive' };

function form(): ICreateWorkAssignmentFormState {
  return { name: 'zz-doc-bind', priority: 1 } as unknown as ICreateWorkAssignmentFormState;
}

describe('WorkAssignmentService.createWorkAssignment — document parent bind (task 162)', () => {
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

  async function run(documentRelationships: unknown[]) {
    stubFetch(documentRelationships);
    const ds = makeDataService();
    const service = new WorkAssignmentService(
      ds,
      bffChildWriteFetch(
        ds,
        async () => ({ ok: false, status: 404, json: async () => ({}), text: async () => '' }) as Response
      ),
      'https://bff.example',
      undefined
    );
    const result = await service.createWorkAssignment(form(), [], [
      { id: 'l1', name: 'brief.pdf' } as unknown as IUploadedFile,
    ]);
    expect(result.status).not.toBe('error');
    expect(createDocs).toHaveBeenCalledTimes(1);
    return createDocs.mock.calls[0];
  }

  it.each([
    ['parent column listed first', [PARENT_REL, RELATED_REL]],
    ['related column listed first', [RELATED_REL, PARENT_REL]],
  ])('binds the document through sprk_WorkAssignment (%s)', async (_label, rels) => {
    const [entitySet, parentId, navProp] = await run(rels);
    expect(entitySet).toBe('sprk_workassignments');
    expect(parentId).toBe(WA_ID);
    expect(navProp).toBe('sprk_WorkAssignment');
  });

  it('with a single relationship to the work assignment (unchanged path) binds that one', async () => {
    const [, , navProp] = await run([PARENT_REL]);
    expect(navProp).toBe('sprk_WorkAssignment');
  });
});
