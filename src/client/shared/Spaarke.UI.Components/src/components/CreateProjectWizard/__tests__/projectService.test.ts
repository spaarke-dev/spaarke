/**
 * projectService BU-cascade unit tests (Task 022 — FR-WIZ-02 + G2 latent-gap fix).
 *
 * Scope: ProjectService.createProject's BU cascade behavior for `sprk_containerid` and
 * `sprk_searchindexname`. The cascade itself lives in
 * `EntityCreationService.applyUserBuDefaults` (already covered by
 * `EntityCreationService.cascade.test.ts`). These tests verify that:
 *
 *   - ProjectService wires the helper correctly (both fields appear on the create payload)
 *   - INV-5 is preserved for both fields when the payload already has a value
 *     (defensive: ProjectService's current payload doesn't pre-set these, so we mutate
 *      the payload via a side-channel — see "INV-5 wiring proof" tests)
 *
 * NOTE: We mock `_dataService.createRecord` and inspect the payload argument it
 * receives. The fetch-based nav-prop discovery is also mocked.
 */

import { ProjectService } from '../projectService';
import { EntityCreationService, type IUserBuCascadeDefaults } from '../../../services/EntityCreationService';
import type { ICreateProjectFormState } from '../projectFormTypes';
import type { IDataService } from '../../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

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

// Mock the EntityDefinitions metadata fetch (nav-prop discovery). The minimum
// viable response is an empty `value` array — ProjectService falls back to
// skipping lookups it can't resolve.
beforeEach(() => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).fetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ value: [] }),
  });
});

// ---------------------------------------------------------------------------
// Cascade application — both fields when both BU values are set
// ---------------------------------------------------------------------------

describe('ProjectService.createProject — FR-WIZ-02 BU cascade (G2 fix)', () => {
  it('sets BOTH sprk_containerid AND sprk_searchindexname on the create payload when BU has both', async () => {
    const { service, createSpy } = makeDataService();
    const projectService = new ProjectService(service);

    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };

    const result = await projectService.createProject(EMPTY_FORM, defaults);
    expect(result.success).toBe(true);

    expect(createSpy).toHaveBeenCalledTimes(1);
    const [entityName, payload] = createSpy.mock.calls[0];
    expect(entityName).toBe('sprk_project');
    expect(payload['sprk_containerid']).toBe('bu-container-abc');
    expect(payload['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('sets only sprk_containerid when BU has containerId but no searchIndexName (Spaarke Dev 1 / Test 1 scenario)', async () => {
    const { service, createSpy } = makeDataService();
    const projectService = new ProjectService(service);

    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: undefined,
    };

    await projectService.createProject(EMPTY_FORM, defaults);
    const payload = createSpy.mock.calls[0][1];

    expect(payload['sprk_containerid']).toBe('bu-container-abc');
    expect('sprk_searchindexname' in payload).toBe(false);
  });

  it('leaves both fields unset when cascadeDefaults is omitted (legacy behavior — backwards-compat)', async () => {
    const { service, createSpy } = makeDataService();
    const projectService = new ProjectService(service);

    await projectService.createProject(EMPTY_FORM);
    const payload = createSpy.mock.calls[0][1];

    expect('sprk_containerid' in payload).toBe(false);
    expect('sprk_searchindexname' in payload).toBe(false);
  });

  it('leaves both fields unset when cascadeDefaults has both fields undefined (BU exists but unset)', async () => {
    const { service, createSpy } = makeDataService();
    const projectService = new ProjectService(service);

    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: undefined,
      searchIndexName: undefined,
    };

    await projectService.createProject(EMPTY_FORM, defaults);
    const payload = createSpy.mock.calls[0][1];

    expect('sprk_containerid' in payload).toBe(false);
    expect('sprk_searchindexname' in payload).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// INV-5 wiring proof — verify the helper is called and honored.
//
// The ProjectService form (ICreateProjectFormState) does NOT currently expose
// `sprk_containerid` or `sprk_searchindexname` as form fields, so an in-form
// override is not possible from the wizard UI today. We verify the INV-5
// contract is honored by directly testing the helper-application boundary
// (i.e. that ProjectService delegates to `applyUserBuDefaults` rather than
// reimplementing — which IS where INV-5 lives).
// ---------------------------------------------------------------------------

describe('ProjectService.createProject — INV-5 wiring (FR-WIZ-08)', () => {
  it('delegates BU cascade to EntityCreationService.applyUserBuDefaults (INV-5 guard centralized)', async () => {
    const applySpy = jest.spyOn(EntityCreationService, 'applyUserBuDefaults');

    const { service } = makeDataService();
    const projectService = new ProjectService(service);

    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };

    await projectService.createProject(EMPTY_FORM, defaults);

    expect(applySpy).toHaveBeenCalledTimes(1);
    // The payload arg is the first positional arg; defaults the second.
    const [payloadArg, defaultsArg] = applySpy.mock.calls[0];
    expect(payloadArg).toEqual(expect.objectContaining({ sprk_projectname: 'Test Project' }));
    expect(defaultsArg).toBe(defaults);

    applySpy.mockRestore();
  });

  // Defense-in-depth: even though the wizard UI doesn't expose override inputs today,
  // verify the static helper IS INV-5-safe in the projectService caller path by
  // wiring a payload pre-populated with explicit values via the helper directly.
  // This is the canonical INV-5 test from EntityCreationService.cascade.test.ts,
  // re-asserted here at the ProjectService caller boundary.
  it('static applyUserBuDefaults preserves explicit values on the payload (INV-5)', () => {
    const entity: Record<string, unknown> = {
      sprk_projectname: 'Protected Project',
      sprk_containerid: 'explicit-override-container',
      sprk_searchindexname: 'spaarke-file-index',
    };
    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };

    const applied = EntityCreationService.applyUserBuDefaults(entity, defaults);

    expect(applied).toEqual({ containerIdSet: false, searchIndexNameSet: false });
    expect(entity['sprk_containerid']).toBe('explicit-override-container');
    expect(entity['sprk_searchindexname']).toBe('spaarke-file-index');
  });
});

// ---------------------------------------------------------------------------
// Error path — cascade does not affect failure handling
// ---------------------------------------------------------------------------

describe('ProjectService.createProject — error handling unchanged by cascade', () => {
  it('returns success=false when createRecord throws (cascade does not mask the error)', async () => {
    const createSpy = jest.fn().mockRejectedValue(new Error('Dataverse boom'));
    const service: IDataService = {
      createRecord: createSpy,
      retrieveRecord: jest.fn().mockResolvedValue({}),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn().mockResolvedValue(undefined),
      deleteRecord: jest.fn().mockResolvedValue(undefined),
    };
    const projectService = new ProjectService(service);

    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };

    const result = await projectService.createProject(EMPTY_FORM, defaults);
    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('Dataverse boom');
  });
});
