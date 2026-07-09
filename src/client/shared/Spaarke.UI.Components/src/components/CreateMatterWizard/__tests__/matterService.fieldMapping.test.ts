/**
 * MatterService — Field Mapping Framework engine wiring (task 020, spec FR-12)
 *
 * Scope: verify `MatterService.createMatter` calls `applyFieldMappings` AFTER
 * the lookup-binding block and BEFORE `createRecord`, when an `association`
 * (the AssociateToStep selection) is supplied:
 *   - a matching, active profile writes its mapped fields onto the create
 *     payload and surfaces engine warnings on the result;
 *   - no profile (404) / no association are graceful no-ops — identical to
 *     pre-task-020 behavior;
 *   - a Matter parent (matter -> matter, same-entity) is passed through
 *     unchanged — no `source === target` guard (ADR-024/design.md §10
 *     decision 9).
 */

import { MatterService } from '../matterService';
import type { ICreateMatterFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';

function makeForm(overrides?: Partial<ICreateMatterFormState>): ICreateMatterFormState {
  return {
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    matterName: 'Test Matter',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    summary: '',
    ...overrides,
  };
}

function makeDataService(): {
  dataService: IDataService;
  createCalls: Array<{ entity: string; payload: Record<string, unknown> }>;
} {
  const createCalls: Array<{ entity: string; payload: Record<string, unknown> }> = [];
  const dataService: IDataService = {
    createRecord: jest.fn().mockImplementation(async (entity: string, payload: Record<string, unknown>) => {
      createCalls.push({ entity, payload });
      return 'created-record-guid';
    }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
  return { dataService, createCalls };
}

const BFF_BASE = 'https://example.test';

const PROJECT_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_project',
  recordId: 'proj-guid-0001',
  recordName: 'Alpha Project',
};

const MATTER_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: 'matter-guid-parent-0001',
  recordName: 'Parent Matter',
};

describe('MatterService — Field Mapping Framework engine wiring (task 020)', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('applies mapped fields onto the create payload when an active profile exists for the association pair', async () => {
    const { dataService, createCalls } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        id: 'profile-1',
        name: 'Project -> Matter',
        sourceEntity: 'sprk_project',
        targetEntity: 'sprk_matter',
        syncMode: 'OneTime',
        isActive: true,
        rules: [
          {
            id: 'rule-1',
            sourceField: 'sprk_practicearea',
            targetField: 'sprk_matterdescription',
            sourceFieldType: 'Text',
            targetFieldType: 'Text',
            priority: 1,
            mappingType: 'Default',
            defaultValue: 'Inherited from Project',
            expression: null,
            isRequired: false,
            compatibilityMode: 'Strict',
          },
        ],
      }),
    } as unknown as Response);

    const service = new MatterService(dataService, authFetch, BFF_BASE);
    const result = await service.createMatter(makeForm(), [], {}, undefined, undefined, PROJECT_ASSOCIATION);

    expect(result.status).toBe('success');
    expect(authFetch).toHaveBeenCalledWith(`${BFF_BASE}/api/v1/field-mappings/profiles/sprk_project/sprk_matter`, {
      method: 'GET',
    });
    expect(createCalls[0].payload['sprk_matterdescription']).toBe('Inherited from Project');
    expect(result.warnings).toEqual([]);
  });

  it('passes a Matter parent (matter -> matter) through unchanged -- no source===target guard', async () => {
    const { dataService, createCalls } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        id: 'profile-2',
        name: 'Matter -> Matter',
        sourceEntity: 'sprk_matter',
        targetEntity: 'sprk_matter',
        syncMode: 'OneTime',
        isActive: true,
        rules: [
          {
            id: 'rule-2',
            sourceField: 'sprk_practicearea',
            targetField: 'sprk_practicearea',
            sourceFieldType: 'Text',
            targetFieldType: 'Text',
            priority: 1,
            mappingType: 'Default',
            defaultValue: 'Inherited from parent Matter',
            expression: null,
            isRequired: false,
            compatibilityMode: 'Strict',
          },
        ],
      }),
    } as unknown as Response);

    const service = new MatterService(dataService, authFetch, BFF_BASE);
    const result = await service.createMatter(makeForm(), [], {}, undefined, undefined, MATTER_ASSOCIATION);

    expect(result.status).toBe('success');
    expect(authFetch).toHaveBeenCalledWith(`${BFF_BASE}/api/v1/field-mappings/profiles/sprk_matter/sprk_matter`, {
      method: 'GET',
    });
    expect(createCalls[0].payload['sprk_practicearea']).toBe('Inherited from parent Matter');
  });

  it('is a graceful no-op (unchanged behavior) when no profile is configured for the pair (404)', async () => {
    const { dataService, createCalls } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({ ok: false, status: 404 } as unknown as Response);

    const service = new MatterService(dataService, authFetch, BFF_BASE);
    const result = await service.createMatter(makeForm(), [], {}, undefined, undefined, PROJECT_ASSOCIATION);

    expect(result.status).toBe('success');
    expect(result.warnings).toEqual([]);
    expect(createCalls[0].payload['sprk_matterdescription']).toBeUndefined();
  });

  it('does not call the engine when no association is supplied', async () => {
    const { dataService } = makeDataService();
    const authFetch = jest.fn().mockResolvedValue({ ok: false, status: 404 } as unknown as Response);

    const service = new MatterService(dataService, authFetch, BFF_BASE);
    const result = await service.createMatter(makeForm(), [], {});

    expect(result.status).toBe('success');
    expect(authFetch).not.toHaveBeenCalled();
    expect(result.warnings).toEqual([]);
  });
});
