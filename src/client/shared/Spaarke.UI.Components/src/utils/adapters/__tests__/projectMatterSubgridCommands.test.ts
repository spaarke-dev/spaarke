/**
 * The Project ↔ Matter subgrid commands — unified-access-control-r2 task 195 (#2352; owner decision 2026-10-31: a matter
 * is added to a project's matter list, and a project to a matter's, through the Create wizards only).
 *
 * `src/client/webresources/js/sprk_wizard_commands.js` (deployed as `sprk_wizard_commands`) runs in a sandbox with a fake
 * `Xrm` (ADR-038: the real script, only the platform seams faked), as `secureChildRibbonScript.test.ts` does. The two
 * entity ribbons that use it are read as files. Pinned:
 *   - only a subgrid of the N:N `sprk_Project_Matter_nn` counts as the link grid; any other subgrid, or one whose
 *     relationship cannot be read, keeps the platform commands;
 *   - "New" on that subgrid opens the matching wizard (project form → Create Matter, matter form → Create Project) with
 *     the form's record, and refreshes the subgrid when the dialog closes;
 *   - the solution copy of the script is the source copy;
 *   - in both ribbons, the platform "New" and both "Add Existing" commands carry the not-the-link-grid rule, and the
 *     wizard command carries the link-grid rule.
 */

import * as fs from 'fs';
import * as path from 'path';
import * as vm from 'vm';

const REPO = path.join(__dirname, '..', '..', '..', '..', '..', '..', '..', '..');
const SOURCE = path.join(REPO, 'src', 'client', 'webresources', 'js', 'sprk_wizard_commands.js');
const SOLUTION = path.join(REPO, 'src', 'dataverse', 'solutions', 'SpaarkeMaster');
const SCRIPT = fs.readFileSync(SOURCE, 'utf8');

const HOST_ID = 'a1950000-0000-4000-8000-0000000000aa';

interface IWizards {
  isProjectMatterLinkGrid(selectedControl: unknown): boolean;
  isNotProjectMatterLinkGrid(selectedControl: unknown): boolean;
  openLinkedWizardFromSubGrid(primaryControl: unknown, selectedControl: unknown): void;
}

interface IHarness {
  wizards: IWizards;
  pages: Array<{ name: string; data: string; title: string }>;
}

function harness(): IHarness {
  const h: IHarness = { wizards: undefined as unknown as IWizards, pages: [] };
  const context: Record<string, unknown> = {
    console: { warn: () => undefined, error: () => undefined, log: () => undefined },
    Promise,
    Object,
    encodeURIComponent,
    window: { localStorage: { getItem: () => null }, getComputedStyle: () => ({ backgroundColor: '' }) },
    document: { body: {} },
    Xrm: {
      WebApi: {
        retrieveMultipleRecords: (entity: string) =>
          Promise.resolve(
            entity === 'environmentvariabledefinition'
              ? { entities: [{ environmentvariabledefinitionid: 'def', defaultvalue: 'https://bff.example' }] }
              : { entities: [] }
          ),
      },
      Navigation: {
        navigateTo: (page: { webresourceName: string; data: string }, options: { title: string }) => {
          h.pages.push({ name: page.webresourceName, data: page.data, title: options.title });
          return Promise.resolve();
        },
      },
    },
  };
  vm.createContext(context);
  vm.runInContext(SCRIPT, context);
  h.wizards = (context.Spaarke as { Commands: { Wizards: IWizards } }).Commands.Wizards;
  return h;
}

function gridOf(relationshipName: string | null | 'throw') {
  return {
    refresh: jest.fn(),
    getRelationship: () => {
      if (relationshipName === 'throw') throw new Error('not a relationship grid');
      return relationshipName === null ? null : { name: relationshipName, relationshipType: 1 };
    },
  };
}

function formOf(entity: string, name = 'Host record', id = HOST_ID) {
  return {
    data: {
      refresh: jest.fn(),
      entity: {
        getId: () => (id ? `{${id.toUpperCase()}}` : ''),
        getEntityName: () => entity,
        getPrimaryAttributeValue: () => name,
      },
    },
  };
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));

function dataOf(data: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const part of data.split('&')) {
    const [k, v] = part.split('=');
    out[k] = decodeURIComponent(v ?? '');
  }
  return out;
}

describe('sprk_wizard_commands.js — which subgrid is the Project ↔ Matter link grid (task 195)', () => {
  it.each(['sprk_Project_Matter_nn', 'sprk_project_matter_nn'])('%s is the link grid', name => {
    const { wizards } = harness();
    expect(wizards.isProjectMatterLinkGrid(gridOf(name))).toBe(true);
    expect(wizards.isNotProjectMatterLinkGrid(gridOf(name))).toBe(false);
  });

  it.each([
    ['another relationship', 'sprk_matter_account'],
    ['no relationship (a view grid)', null],
    ['an unreadable relationship', 'throw'],
  ])('%s keeps the platform commands', (_label, name) => {
    const { wizards } = harness();
    expect(wizards.isProjectMatterLinkGrid(gridOf(name as string | null | 'throw'))).toBe(false);
    expect(wizards.isNotProjectMatterLinkGrid(gridOf(name as string | null | 'throw'))).toBe(true);
  });

  it('a control with no getRelationship keeps the platform commands', () => {
    const { wizards } = harness();
    expect(wizards.isProjectMatterLinkGrid({})).toBe(false);
    expect(wizards.isNotProjectMatterLinkGrid(undefined)).toBe(true);
  });
});

describe('sprk_wizard_commands.js — "New" on the link grid opens the matching wizard (task 195)', () => {
  it.each([
    ['sprk_project', 'sprk_creatematterwizard', 'Create New Matter'],
    ['sprk_matter', 'sprk_createprojectwizard', 'Create New Project'],
  ])('on a %s form it opens %s with the form record as the association', async (host, page, title) => {
    const h = harness();
    const grid = gridOf('sprk_Project_Matter_nn');
    const form = formOf(host, 'Acme & Co');

    h.wizards.openLinkedWizardFromSubGrid(form, grid);
    await flush();
    await flush();

    expect(h.pages).toHaveLength(1);
    expect(h.pages[0].name).toBe(page);
    expect(h.pages[0].title).toBe(title);
    expect(dataOf(h.pages[0].data)).toEqual({
      entityType: host,
      entityId: HOST_ID.toUpperCase(),
      recordName: 'Acme & Co',
      bffBaseUrl: 'https://bff.example',
    });
    expect(grid.refresh).toHaveBeenCalledTimes(1);
    expect(form.data.refresh).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['another table', formOf('account')],
    ['an unsaved record', formOf('sprk_project', 'New', '')],
  ])('opens nothing on %s', async (_label, form) => {
    const h = harness();
    h.wizards.openLinkedWizardFromSubGrid(form, gridOf('sprk_Project_Matter_nn'));
    await flush();
    expect(h.pages).toEqual([]);
  });
});

describe('the Project ↔ Matter ribbons (task 195)', () => {
  it('the solution copy of the script is the source copy', () => {
    const solutionCopy = fs.readFileSync(path.join(SOLUTION, 'WebResources', 'sprk_wizard_commands'), 'utf8');
    expect(solutionCopy.replace(/\r\n/g, '\n')).toBe(SCRIPT.replace(/\r\n/g, '\n'));
  });

  it.each([
    ['sprk_Matter', 'sprk_matter'],
    ['sprk_Project', 'sprk_project'],
  ])(
    '%s: the platform New / Add Existing are off on the link grid; the wizard command is on it only',
    (folder, entity) => {
      const xml = fs.readFileSync(path.join(SOLUTION, 'Entities', folder, 'RibbonDiff.xml'), 'utf8');
      const doc = new DOMParser().parseFromString(xml, 'application/xml');
      const command = (id: string) =>
        Array.from(doc.getElementsByTagName('CommandDefinition')).find(c => c.getAttribute('Id') === id);
      const enableRules = (id: string) =>
        Array.from(command(id)?.getElementsByTagName('EnableRule') ?? []).map(r => r.getAttribute('Id'));
      const prefix = `sprk.ProjectMatterLink.${entity}`;

      for (const platform of [
        'Mscrm.AddNewRecordFromSubGridStandard',
        'Mscrm.AddExistingRecordFromSubGridAssociated',
        'Mscrm.AddExistingRecordFromSubGridStandard',
      ]) {
        expect(enableRules(platform)).toContain(`${prefix}.NotLinkGrid.EnableRule`);
      }
      expect(enableRules(`${prefix}.New.Command`)).toEqual([`${prefix}.IsLinkGrid.EnableRule`]);

      const ruleFunction = (id: string) =>
        Array.from(doc.getElementsByTagName('EnableRule'))
          .find(r => r.getAttribute('Id') === id && r.getElementsByTagName('CustomRule').length > 0)
          ?.getElementsByTagName('CustomRule')[0];
      expect(ruleFunction(`${prefix}.IsLinkGrid.EnableRule`)?.getAttribute('FunctionName')).toBe(
        'Spaarke.Commands.Wizards.isProjectMatterLinkGrid'
      );
      expect(ruleFunction(`${prefix}.IsLinkGrid.EnableRule`)?.getAttribute('Default')).toBe('false');
      expect(ruleFunction(`${prefix}.NotLinkGrid.EnableRule`)?.getAttribute('FunctionName')).toBe(
        'Spaarke.Commands.Wizards.isNotProjectMatterLinkGrid'
      );
      expect(ruleFunction(`${prefix}.NotLinkGrid.EnableRule`)?.getAttribute('Default')).toBe('true');

      const button = Array.from(doc.getElementsByTagName('CustomAction')).find(
        a => a.getAttribute('Id') === `${prefix}.SubGrid.New.CustomAction`
      );
      expect(button?.getAttribute('Location')).toBe(`Mscrm.SubGrid.${entity}.MainTab.Management.Controls._children`);
    }
  );
});
