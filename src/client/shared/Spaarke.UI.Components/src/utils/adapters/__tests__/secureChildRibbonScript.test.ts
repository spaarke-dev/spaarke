/**
 * The secure-record child "New" commands' ribbon script (`src/client/webresources/js/sprk_secure_child_ribbon.js`) —
 * unified-access-control-r2 task 147 r1c (owner round 28 item 2, "E2"). The script pairs with this folder's
 * `bffChildWriteAdapter` (the same BFF route, `POST /api/v1/child-records/{table}`), and there is no web-resource test
 * runner in the repository, so its behaviour is pinned here: the script file itself runs in a sandbox with a fake `Xrm`,
 * `fetch` and sign-in helper (ADR-038: the real code, only the platform seams faked).
 *
 * Pinned: the platform "+ New" shows ONLY for a root read (through Xrm.WebApi) as not secure, or a party host; a secure
 * root, an unreadable or empty flag, and any other host show the BFF command instead (fail closed); the BFF command opens
 * the wizard filed under the host, or creates the row through the BFF and opens it; a refusal shows the server's words
 * and creates nothing.
 */

import * as fs from 'fs';
import * as path from 'path';
import * as vm from 'vm';

const SCRIPT = fs.readFileSync(
  path.join(__dirname, '..', '..', '..', '..', '..', '..', 'webresources', 'js', 'sprk_secure_child_ribbon.js'),
  'utf8'
);

const HOST_ID = 'a1470000-0000-4000-8000-000000000001';
const NEW_ID = 'b1470000-0000-4000-8000-000000000002';

interface IRibbon {
  nativeNewAllowed(primaryControl: unknown): boolean | Promise<boolean>;
  newChildAvailable(primaryControl: unknown): boolean | Promise<boolean>;
  newChild(primaryControl: unknown, selectedControl: unknown, childTable: string): void;
}

interface IHarness {
  ribbon: IRibbon;
  reads: string[];
  alerts: Array<{ title: string; text: string }>;
  pages: Array<{ name: string; data: string }>;
  forms: Array<{ entityName: string; entityId: string }>;
  posts: Array<{ url: string; body: Record<string, unknown> }>;
}

function harness(options: { flag?: boolean | null | 'throw'; bffStatus?: number; bffBody?: unknown }): IHarness {
  const h: IHarness = {
    ribbon: undefined as unknown as IRibbon,
    reads: [],
    alerts: [],
    pages: [],
    forms: [],
    posts: [],
  };
  const flag = options.flag;
  const context: Record<string, unknown> = {
    console: { warn: () => undefined, error: () => undefined, log: () => undefined },
    Promise,
    Object,
    JSON,
    String,
    encodeURIComponent,
    Xrm: {
      WebApi: {
        retrieveRecord: (entity: string, id: string, query: string) => {
          h.reads.push(`${entity}(${id})${query}`);
          if (flag === 'throw') return Promise.reject(new Error('no read'));
          return Promise.resolve({ sprk_issecure: flag });
        },
      },
      Navigation: {
        openAlertDialog: (a: { title: string; text: string }) => {
          h.alerts.push(a);
          return Promise.resolve();
        },
        navigateTo: (page: { webresourceName: string; data: string }) => {
          h.pages.push({ name: page.webresourceName, data: page.data });
          return Promise.resolve();
        },
        openForm: (f: { entityName: string; entityId: string }) => {
          h.forms.push(f);
          return Promise.resolve();
        },
      },
      Utility: { getGlobalContext: () => ({ getClientUrl: () => 'https://org.crm.dynamics.com' }) },
    },
    fetch: (url: string, init?: { method?: string; body?: string }) => {
      if (url.includes('/ManyToOneRelationships')) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              value: [
                {
                  ReferencingAttribute: 'sprk_matter',
                  ReferencingEntityNavigationPropertyName: 'sprk_Matter',
                  ReferencedEntity: 'sprk_matter',
                },
              ],
            }),
        });
      }
      if (url.includes("EntityDefinitions(LogicalName='sprk_matter')")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ EntitySetName: 'sprk_matters' }) });
      }
      if (url.includes('EntityDefinitions(LogicalName=')) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ PrimaryNameAttribute: 'sprk_name' }) });
      }
      h.posts.push({ url, body: JSON.parse(init?.body ?? '{}') as Record<string, unknown> });
      const status = options.bffStatus ?? 201;
      const body = options.bffBody ?? { id: NEW_ID };
      return Promise.resolve({
        ok: status >= 200 && status < 300,
        status,
        json: () => Promise.resolve(body),
        text: () => Promise.resolve(JSON.stringify(body)),
      });
    },
  };
  // The two helper libraries the commands load first (bff_auth.js, assignedaccess_postsave.js).
  context.Spaarke = {
    BffAuth: { getToken: () => Promise.resolve('token') },
    AssignedAccess: { getApiBaseUrl: () => Promise.resolve('https://bff.example') },
  };
  vm.createContext(context);
  vm.runInContext(SCRIPT, context);
  h.ribbon = (context.Spaarke as { SecureChild: { Ribbon: IRibbon } }).SecureChild.Ribbon;
  return h;
}

function formOf(entity: string, id = HOST_ID, name = 'Host record') {
  return {
    data: {
      entity: {
        getId: () => `{${id.toUpperCase()}}`,
        getEntityName: () => entity,
        getPrimaryAttributeValue: () => name,
      },
    },
  };
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));

describe('sprk_secure_child_ribbon.js — which "New" a subgrid shows (task 147 r1c)', () => {
  it.each(['sprk_project', 'sprk_matter', 'sprk_workassignment'])(
    'a %s read as NOT secure keeps the platform "+ New" and hides the BFF command',
    async root => {
      const h = harness({ flag: false });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf(root)))).resolves.toBe(true);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf(root)))).resolves.toBe(false);
      expect(h.reads[0]).toBe(`${root}(${HOST_ID})?$select=sprk_issecure`);
    }
  );

  it.each([
    ['secure', true as boolean | null | 'throw'],
    ['empty', null],
    ['unreadable', 'throw'],
  ])(
    'a root whose flag is %s hides the platform "+ New" and shows the BFF command (fail closed)',
    async (_label, flag) => {
      const h = harness({ flag: flag as boolean | null | 'throw' });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf('sprk_matter')))).resolves.toBe(false);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf('sprk_matter')))).resolves.toBe(true);
    }
  );

  it.each(['sprk_event', 'sprk_document', 'sprk_invoice', 'sprk_analysis', 'sprk_budget'])(
    'a %s host (a child of a child: its secure ancestor is not known here) shows the BFF command, reading no flag',
    async host => {
      const h = harness({ flag: false });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf(host)))).resolves.toBe(false);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf(host)))).resolves.toBe(true);
      expect(h.reads).toEqual([]);
    }
  );

  it.each(['contact', 'sprk_organization', 'account'])(
    'a party host (%s) keeps the platform "+ New" — never an ownership parent; no flag is read',
    async party => {
      const h = harness({ flag: 'throw' });
      expect(h.ribbon.nativeNewAllowed(formOf(party))).toBe(true);
      expect(h.ribbon.newChildAvailable(formOf(party))).toBe(false);
      expect(h.reads).toEqual([]);
    }
  );

  it('an unsaved host keeps the platform command (it prefills nothing) and offers no BFF command', () => {
    const h = harness({ flag: true });
    const unsaved = { data: { entity: { getId: () => '', getEntityName: () => 'sprk_matter' } } };
    expect(h.ribbon.nativeNewAllowed(unsaved)).toBe(true);
    expect(h.ribbon.newChildAvailable(unsaved)).toBe(false);
  });
});

describe('sprk_secure_child_ribbon.js — the BFF-backed "New <thing>"', () => {
  it('opens the table wizard filed under the host (entityType, entityId, recordName)', async () => {
    const h = harness({ flag: true });
    h.ribbon.newChild(formOf('sprk_matter', HOST_ID, 'Acme v Beta'), null, 'sprk_todo');
    await flush();

    expect(h.pages).toHaveLength(1);
    expect(h.pages[0].name).toBe('sprk_createtodowizard');
    expect(h.pages[0].data).toContain(`entityType=sprk_matter&entityId=${HOST_ID}`);
    expect(h.pages[0].data).toContain(`recordName=${encodeURIComponent('Acme v Beta')}`);
    expect(h.pages[0].data).toContain(`bffBaseUrl=${encodeURIComponent('https://bff.example')}`);
    expect(h.posts).toEqual([]);
  });

  it.each(['sprk_budget', 'sprk_kpiassessment', 'sprk_billingevent'])(
    'creates a %s through POST /api/v1/child-records/{table}, filed under the host, then opens it',
    async table => {
      const h = harness({ flag: true });
      h.ribbon.newChild(formOf('sprk_matter'), { getRelationship: () => ({ attributeName: 'sprk_matter' }) }, table);
      for (let i = 0; i < 10 && h.forms.length === 0; i++) await flush();

      expect(h.posts).toHaveLength(1);
      expect(h.posts[0].url).toBe(`https://bff.example/api/v1/child-records/${table}`);
      expect(h.posts[0].body['sprk_Matter@odata.bind']).toBe(`/sprk_matters(${HOST_ID})`);
      expect(Object.keys(h.posts[0].body).some(k => /^ownerid/i.test(k))).toBe(false);
      expect(h.forms).toEqual([{ entityName: table, entityId: NEW_ID }]);
    }
  );

  it('a BFF refusal shows the server message and opens nothing', async () => {
    const h = harness({
      flag: true,
      bffStatus: 404,
      bffBody: { detail: 'A record this budget is filed under was not found. The budget was not saved.' },
    });
    h.ribbon.newChild(
      formOf('sprk_matter'),
      { getRelationship: () => ({ attributeName: 'sprk_matter' }) },
      'sprk_budget'
    );
    for (let i = 0; i < 10 && h.alerts.length === 0; i++) await flush();

    expect(h.alerts).toHaveLength(1);
    expect(h.alerts[0].text).toContain('A record this budget is filed under was not found.');
    expect(h.alerts[0].text).toContain('Nothing was created.');
    expect(h.forms).toEqual([]);
  });
});
