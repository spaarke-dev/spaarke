/**
 * Create Project — the organization searches use sprk_organization's real name column (F-R4-1, ontology UAT 2026-11-12).
 *
 * sprk_organization has no `sprk_name`; its name column is `sprk_organizationname`. Both Create Project searches queried
 * `sprk_name`, so Dataverse refused every search and "Assigned Outside Counsel" found nothing:
 *   - the Assign Work follow-on step (the wizard config's `searchOrganizations`);
 *   - the Enter Info step's outside counsel (`ProjectService.searchOrganizations`).
 * Pinned here: both select, filter and order on `sprk_organizationname` and return its value as the item name, and
 * neither ever names `sprk_name` (must-fire: the previous query fails both).
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import { CreateProjectWizard } from '../CreateProjectWizard';
import { ProjectService } from '../projectService';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const ORG_ID = 'a0000000-0000-4000-8000-0000000000f1';

function organizationDataService(): { service: IDataService; retrieveMultiple: jest.Mock } {
  const retrieveMultiple = jest.fn(async (entity: string) =>
    entity === 'sprk_organization'
      ? { entities: [{ sprk_organizationid: ORG_ID, sprk_organizationname: 'Jones & Smith PC' }] }
      : { entities: [] }
  );
  const service = {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: retrieveMultiple,
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  } as unknown as IDataService;
  return { service, retrieveMultiple };
}

function organizationQuery(retrieveMultiple: jest.Mock): string {
  const call = retrieveMultiple.mock.calls.find(c => c[0] === 'sprk_organization');
  expect(call).toBeDefined();
  return call![1] as string;
}

function expectNameColumnQuery(query: string): void {
  expect(query).toContain('$select=sprk_organizationid,sprk_organizationname');
  expect(query).toContain("contains(sprk_organizationname,'Jones')");
  expect(query).toContain('$orderby=sprk_organizationname asc');
  expect(query).not.toMatch(/\bsprk_name\b/);
}

describe('Create Project — organization search uses sprk_organizationname (F-R4-1)', () => {
  beforeEach(() => {
    mockShell.config = undefined;
  });

  it('the Assign Work step search queries sprk_organizationname and returns the organization name', async () => {
    const { service, retrieveMultiple } = organizationDataService();
    renderWithProviders(<CreateProjectWizard open onClose={jest.fn()} dataService={service} />);
    const search = mockShell.config!.searchOrganizations!;

    const items = await search('Jones');

    expectNameColumnQuery(organizationQuery(retrieveMultiple));
    expect(items).toEqual([{ id: ORG_ID, name: 'Jones & Smith PC' }]);
  });

  it('ProjectService.searchOrganizations (Enter Info outside counsel) queries sprk_organizationname and returns the name', async () => {
    const { service, retrieveMultiple } = organizationDataService();

    const items = await new ProjectService(service).searchOrganizations('Jones');

    expectNameColumnQuery(organizationQuery(retrieveMultiple));
    expect(items).toEqual([{ id: ORG_ID, name: 'Jones & Smith PC' }]);
  });

  it('neither search queries Dataverse for fewer than two characters', async () => {
    const { service, retrieveMultiple } = organizationDataService();
    renderWithProviders(<CreateProjectWizard open onClose={jest.fn()} dataService={service} />);

    await expect(mockShell.config!.searchOrganizations!('J')).resolves.toEqual([]);
    await expect(new ProjectService(service).searchOrganizations(' J ')).resolves.toEqual([]);
    expect(retrieveMultiple.mock.calls.filter(c => c[0] === 'sprk_organization')).toHaveLength(0);
  });
});
