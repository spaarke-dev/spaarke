/**
 * ProjectService — Field Mapping Framework engine wiring (task 020, spec FR-12)
 *
 * Scope: verify `ProjectService.createProject` calls `applyFieldMappings` AFTER
 * the lookup-binding block and BEFORE `createRecord`, when an `association`
 * (the AssociateToStep selection) AND the BFF deps (`authenticatedFetch`/
 * `bffBaseUrl`) are supplied:
 *   - a matching, active profile writes its mapped fields onto the create
 *     payload and surfaces engine warnings on the result;
 *   - no profile (404) / no association / no BFF deps are all graceful
 *     no-ops — identical to pre-task-020 behavior.
 */

import { ProjectService } from '../projectService';
import type { ICreateProjectFormState } from '../projectFormTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';

const EMPTY_FORM: ICreateProjectFormState = {
  projectTypeId: '',
  projectTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  projectName: 'Test Project',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  description: '',
  isSecure: false,
};

function makeDataService(): { service: IDataService; createSpy: jest.Mock } {
  const createSpy = jest.fn().mockResolvedValue('00000000-0000-0000-0000-000000000001');
  const service: IDataService = {
    createRecord: createSpy,
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
  return { service, createSpy };
}

const BFF_BASE = 'https://example.test';

const MATTER_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: 'matter-guid-0001',
  recordName: 'Smith v. Jones',
};

beforeEach(() => {
  // Nav-prop discovery (EntityDefinitions metadata fetch) — empty is fine, ProjectService
  // gracefully skips lookups it can't resolve.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).fetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ value: [] }),
  });
});

describe('ProjectService — Field Mapping Framework engine wiring (task 020)', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('applies mapped fields onto the create payload when an active profile exists for the association pair', async () => {
    const { service, createSpy } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        id: 'profile-1',
        name: 'Matter -> Project',
        sourceEntity: 'sprk_matter',
        targetEntity: 'sprk_project',
        syncMode: 'OneTime',
        isActive: true,
        rules: [
          {
            id: 'rule-1',
            sourceField: 'sprk_practicearea',
            targetField: 'sprk_projectdescription',
            sourceFieldType: 'Text',
            targetFieldType: 'Text',
            priority: 1,
            mappingType: 'Default',
            defaultValue: 'Inherited from Matter',
            expression: null,
            isRequired: false,
            compatibilityMode: 'Strict',
          },
        ],
      }),
    } as unknown as Response);

    const projectService = new ProjectService(service, authFetch, BFF_BASE);
    const result = await projectService.createProject(EMPTY_FORM, undefined, MATTER_ASSOCIATION);

    expect(result.success).toBe(true);
    expect(authFetch).toHaveBeenCalledWith(`${BFF_BASE}/api/v1/field-mappings/profiles/sprk_matter/sprk_project`, {
      method: 'GET',
    });
    const payload = createSpy.mock.calls[0][1] as Record<string, unknown>;
    expect(payload['sprk_projectdescription']).toBe('Inherited from Matter');
    expect(result.warnings).toEqual([]);
  });

  it('is a graceful no-op (unchanged behavior) when no profile is configured for the pair (404)', async () => {
    const { service, createSpy } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({ ok: false, status: 404 } as unknown as Response);

    const projectService = new ProjectService(service, authFetch, BFF_BASE);
    const result = await projectService.createProject(EMPTY_FORM, undefined, MATTER_ASSOCIATION);

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual([]);
    const payload = createSpy.mock.calls[0][1] as Record<string, unknown>;
    expect(payload['sprk_projectdescription']).toBeUndefined();
  });

  it('does not call the engine when no association is supplied', async () => {
    const { service } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({ ok: false, status: 404 } as unknown as Response);

    const projectService = new ProjectService(service, authFetch, BFF_BASE);
    const result = await projectService.createProject(EMPTY_FORM);

    expect(result.success).toBe(true);
    expect(authFetch).not.toHaveBeenCalled();
    expect(result.warnings).toEqual([]);
  });

  it('does not call the engine when authenticatedFetch/bffBaseUrl are not supplied (lookup-only construction site)', async () => {
    const { service } = makeDataService();
    const projectService = new ProjectService(service); // no authenticatedFetch/bffBaseUrl injected

    const result = await projectService.createProject(EMPTY_FORM, undefined, MATTER_ASSOCIATION);

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual([]);
  });
});
