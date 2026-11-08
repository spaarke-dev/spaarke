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
  nativeNewAllowed(primaryControl: unknown, table?: string): boolean | Promise<boolean>;
  newChildAvailable(primaryControl: unknown, table?: string): boolean | Promise<boolean>;
  newChild(primaryControl: unknown, selectedControl: unknown, childTable: string): void;
  TABLES: string[];
  WIZARDS: Record<string, { page: string }>;
  CREATE_THEN_OPEN: Record<string, unknown>;
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
                {
                  ReferencingAttribute: 'sprk_project',
                  ReferencingEntityNavigationPropertyName: 'sprk_Project',
                  ReferencedEntity: 'sprk_project',
                },
                {
                  ReferencingAttribute: 'sprk_invoice',
                  ReferencingEntityNavigationPropertyName: 'sprk_Invoice',
                  ReferencedEntity: 'sprk_invoice',
                },
              ],
            }),
        });
      }
      if (url.includes("EntityDefinitions(LogicalName='sprk_matter')")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ EntitySetName: 'sprk_matters' }) });
      }
      if (url.includes("EntityDefinitions(LogicalName='sprk_invoice')")) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ EntitySetName: 'sprk_invoices' }) });
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

  it('the AP export batch (ebilling 151c-2) offers NO "New" at all: neither the platform "+ New" nor the BFF command; no flag is read', async () => {
    const h = harness({ flag: false });
    await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf('sprk_billingexport')))).resolves.toBe(false);
    await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf('sprk_billingexport')))).resolves.toBe(false);
    expect(h.reads).toEqual([]);
  });

  // ebilling-r1 task 096b (EB-D-201): the invoice's platform "+ New" is closed on EVERY host; the Invoice wizard is the one "New" on a core-record host.
  it.each(['sprk_project', 'sprk_matter', 'sprk_workassignment'])(
    'a %s read as NOT secure offers NO platform "+ New" for an invoice and shows the Invoice wizard command (must fire); a to-do keeps the platform one (must not fire)',
    async root => {
      const h = harness({ flag: false });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf(root), 'sprk_invoice'))).resolves.toBe(false);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf(root), 'sprk_invoice'))).resolves.toBe(true);
      expect(h.reads).toEqual([]); // the answer does not depend on the flag

      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf(root), 'sprk_todo'))).resolves.toBe(true);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf(root), 'sprk_todo'))).resolves.toBe(false);
    }
  );

  it.each(['sprk_project', 'sprk_matter', 'sprk_workassignment'])(
    'the Invoice wizard shows on a %s host whether the flag reads secure, not secure or unreadable (must fire)',
    async root => {
      for (const flag of [true, false, 'throw'] as const) {
        const h = harness({ flag });
        expect(h.ribbon.nativeNewAllowed(formOf(root), 'sprk_invoice')).toBe(false);
        expect(h.ribbon.newChildAvailable(formOf(root), 'sprk_invoice')).toBe(true);
        expect(h.reads).toEqual([]);
      }
    }
  );

  it.each(['sprk_event', 'contact', 'sprk_organization', 'account'])(
    'an invoice subgrid on a %s host (not a core record the submission route accepts): no platform "+ New" and NO Invoice wizard command (must not fire)',
    async host => {
      const h = harness({ flag: 'throw' });
      expect(h.ribbon.nativeNewAllowed(formOf(host), 'sprk_invoice')).toBe(false);
      expect(h.ribbon.newChildAvailable(formOf(host), 'sprk_invoice')).toBe(false);
      expect(h.reads).toEqual([]);
    }
  );

  it('an invoice on an unsaved host or on the AP export batch still offers no "New" (the wizard needs a saved host; the batch offers none)', async () => {
    const h = harness({ flag: false });
    const unsaved = { data: { entity: { getId: () => '', getEntityName: () => 'sprk_matter' } } };
    expect(h.ribbon.newChildAvailable(unsaved, 'sprk_invoice')).toBe(false);
    expect(h.ribbon.nativeNewAllowed(unsaved, 'sprk_invoice')).toBe(false);
    expect(h.ribbon.newChildAvailable(formOf('sprk_billingexport'), 'sprk_invoice')).toBe(false);
    expect(h.ribbon.nativeNewAllowed(formOf('sprk_billingexport'), 'sprk_invoice')).toBe(false);
  });

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

  it.each(['sprk_budget', 'sprk_kpiassessment'])(
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

  // ebilling-r1 task 164a-1 (uac-r2 Q6): an invoice line's new rows have one server-side owner (extraction, a line decision,
  // Add line), and the BFF no longer creates one on the child-record route, so the ribbon has no "New Billing Event" create.
  it.each(['sprk_invoice', 'sprk_matter'])(
    'a billing event on a %s is not created: nothing is sent, nothing opens, the user is told',
    async host => {
      const h = harness({ flag: true });
      h.ribbon.newChild(formOf(host), { getRelationship: () => ({ attributeName: host }) }, 'sprk_billingevent');
      for (let i = 0; i < 10 && h.alerts.length === 0; i++) await flush();

      expect(h.posts).toEqual([]);
      expect(h.reads).toEqual([]); // not even the host row: there is no create to prepare
      expect(h.forms).toEqual([]);
      expect(h.pages).toEqual([]);
      expect(h.alerts).toHaveLength(1);
      expect(h.alerts[0].text).toContain('Nothing was created.');
    }
  );

  it('the billing event subgrid on an invoice still hides the platform "+ New" (the hide is kept; only the create is gone)', async () => {
    const h = harness({ flag: false });
    await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf('sprk_invoice')))).resolves.toBe(false);
  });

  // ebilling-r1 task 096b (EB-D-201): the billing event is no longer a served table; its platform "+ New" is hidden outright in the
  // sprk_BillingEvent RibbonDiff. The invoice entry stays and opens the Invoice wizard (a submission).
  it('does not serve sprk_billingevent, and still serves sprk_invoice through the Invoice wizard', () => {
    const h = harness({ flag: true });
    expect(h.ribbon.TABLES).not.toContain('sprk_billingevent');
    expect(h.ribbon.TABLES).toContain('sprk_invoice');
    expect(h.ribbon.WIZARDS.sprk_invoice.page).toBe('sprk_createinvoicewizard');
    expect(h.ribbon.CREATE_THEN_OPEN.sprk_billingevent).toBeUndefined();
    expect(h.ribbon.CREATE_THEN_OPEN.sprk_invoice).toBeUndefined();
  });

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
