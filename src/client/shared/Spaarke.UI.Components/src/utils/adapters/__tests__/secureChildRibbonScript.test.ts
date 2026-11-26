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
import { parseComposeRegarding } from '../../../components/EmailComposer/openEmailCompose';

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
  NATIVE_NEW_REDIRECTED: Record<string, boolean>;
  hostOf(primaryControl: unknown): { entity: string; id: string } | null;
  createChild(
    table: string,
    host: { entity: string; id: string },
    selectedControl: unknown
  ): Promise<{ id: string } | null>;
}

interface IHarness {
  ribbon: IRibbon;
  reads: string[];
  alerts: Array<{ title: string; text: string }>;
  pages: Array<{ name: string; data: string }>;
  forms: Array<{ entityName: string; entityId: string }>;
  posts: Array<{ url: string; body: Record<string, unknown> }>;
  metadataUrls: string[];
  warnings: string[];
  /** The sandbox the script ran in (a second web resource can be run in the same one, as the ribbon loads libraries in order). */
  context: Record<string, unknown>;
}

function harness(options: {
  flag?: boolean | null | 'throw';
  bffStatus?: number;
  bffBody?: unknown;
  /** The ManyToOneRelationships read answers 200 with an empty list (what a wrong-case logical name gets). */
  noRelationships?: boolean;
}): IHarness {
  const h: IHarness = {
    ribbon: undefined as unknown as IRibbon,
    reads: [],
    alerts: [],
    pages: [],
    forms: [],
    posts: [],
    metadataUrls: [],
    warnings: [],
    context: {},
  };
  const flag = options.flag;
  const context: Record<string, unknown> = {
    console: {
      warn: (...args: unknown[]) => {
        h.warnings.push(args.map(String).join(' '));
      },
      error: () => undefined,
      log: () => undefined,
    },
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
      if (url.includes('EntityDefinitions(')) h.metadataUrls.push(url);
      if (url.includes('/ManyToOneRelationships')) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              value: options.noRelationships
                ? []
                : [
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
  h.context = context;
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
    'a %s read as NOT secure offers NO platform "+ New" for an invoice and shows the Invoice wizard command (must fire); a table outside the redirected set keeps the platform one (must not fire)',
    async root => {
      const h = harness({ flag: false });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf(root), 'sprk_invoice'))).resolves.toBe(false);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf(root), 'sprk_invoice'))).resolves.toBe(true);
      expect(h.reads).toEqual([]); // the answer does not depend on the flag

      // Task 051 (FR-24): a to-do is now redirected too (see the redirected-tables describe below); sprk_analysis is neither closed
      // nor redirected (its platform "+ New" is hidden by AnalysisRibbons), so the flag still decides for it.
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf(root), 'sprk_analysis'))).resolves.toBe(true);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf(root), 'sprk_analysis'))).resolves.toBe(false);
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

describe('sprk_secure_child_ribbon.js — "New Email" on a communications subgrid (#2908)', () => {
  it('opens the email composer (sprk_emailpage) "Related to" the host, never the retired Communication page', async () => {
    const h = harness({ flag: true });
    h.ribbon.newChild(formOf('sprk_matter', HOST_ID, 'Acme v Beta & Co'), null, 'sprk_communication');
    for (let i = 0; i < 10 && h.pages.length === 0; i++) await flush();

    expect(h.pages).toHaveLength(1);
    expect(h.pages[0].name).toBe('sprk_emailpage');
    const data = new URLSearchParams(h.pages[0].data);
    expect(data.get('compose')).toBe('new');
    // The composer reads exactly what the shared launcher writes (parseComposeRegarding / composeRegardingParams).
    expect(parseComposeRegarding(data)).toEqual({ entityType: 'sprk_matter', id: HOST_ID, name: 'Acme v Beta & Co' });
    expect(h.posts).toEqual([]);
    expect(h.forms).toEqual([]);
  });

  it('an unsaved host opens nothing and asks to save first', async () => {
    const h = harness({ flag: true });
    h.ribbon.newChild(
      { data: { entity: { getId: () => '', getEntityName: () => 'sprk_matter' } } },
      null,
      'sprk_communication'
    );
    await flush();

    expect(h.pages).toEqual([]);
    expect(h.alerts[0]?.text).toContain('Save this record first');
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

// #2909: the script keeps its own nav-prop discovery (a ribbon web resource has no module system), aligned with the
// shared discoverNavProps: lowercased logical names (SRFR-050) and a warning on an empty relationship list.
describe('sprk_secure_child_ribbon.js — nav-prop discovery follows the shared discoverNavProps (#2909)', () => {
  it('lowercases a mixed-case host name for the metadata reads and the lookup match, and still files the row under the host', async () => {
    const h = harness({ flag: true });
    h.ribbon.newChild(
      formOf('sprk_Matter'),
      { getRelationship: () => ({ attributeName: 'sprk_Matter' }) },
      'sprk_budget'
    );
    for (let i = 0; i < 10 && h.forms.length === 0 && h.alerts.length === 0; i++) await flush();

    expect(h.metadataUrls.length).toBeGreaterThan(0);
    for (const url of h.metadataUrls) {
      const name = /LogicalName='([^']*)'/.exec(url)?.[1] ?? '';
      expect(name).toBe(name.toLowerCase());
    }
    expect(h.alerts).toEqual([]);
    expect(h.posts).toHaveLength(1);
    expect(h.posts[0].body['sprk_Matter@odata.bind']).toBe(`/sprk_matters(${HOST_ID})`);
  });

  it('warns, naming the table, when the relationship list is empty, and creates nothing (must fire)', async () => {
    const h = harness({ flag: true, noRelationships: true });
    h.ribbon.newChild(
      formOf('sprk_matter'),
      { getRelationship: () => ({ attributeName: 'sprk_matter' }) },
      'sprk_budget'
    );
    for (let i = 0; i < 10 && h.alerts.length === 0; i++) await flush();

    expect(h.warnings.some(w => w.includes('zero entries') && w.includes('sprk_budget'))).toBe(true);
    expect(h.posts).toEqual([]);
    expect(h.alerts).toHaveLength(1);
    expect(h.alerts[0].text).toContain('Nothing was created.');
  });

  it('does not warn when relationships are found (must not fire)', async () => {
    const h = harness({ flag: true });
    h.ribbon.newChild(
      formOf('sprk_matter'),
      { getRelationship: () => ({ attributeName: 'sprk_matter' }) },
      'sprk_budget'
    );
    for (let i = 0; i < 10 && h.forms.length === 0; i++) await flush();

    expect(h.forms).toHaveLength(1);
    expect(h.warnings.some(w => w.includes('zero entries'))).toBe(false);
  });
});

// field-mapping-server-write-path-r1 task 051 (FR-24): the platform subgrid "+ New" of a served table is redirected on every host
// that can own it - secure or not - so no native create (no mapping, no owner rule, no stamp) is offered from the subgrid.
describe('sprk_secure_child_ribbon.js — the platform "+ New" is redirected for the FR-24 tables (task 051)', () => {
  const REDIRECTED = [
    'sprk_todo',
    'sprk_event',
    'sprk_reportcard',
    'sprk_document',
    'sprk_communication',
    'sprk_budget',
    'sprk_kpiassessment',
  ];

  it('lists exactly the served tables other than the invoice (which is closed outright)', () => {
    const h = harness({ flag: false });
    expect(Object.keys(h.ribbon.NATIVE_NEW_REDIRECTED).sort()).toEqual([...REDIRECTED].sort());
    expect(h.ribbon.TABLES.filter(t => t !== 'sprk_invoice').sort()).toEqual([...REDIRECTED].sort());
  });

  it.each(REDIRECTED)(
    'a %s subgrid on a root read as NOT secure hides the platform "+ New" and shows the BFF command (must fire)',
    async table => {
      const h = harness({ flag: false });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf('sprk_matter'), table))).resolves.toBe(false);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf('sprk_matter'), table))).resolves.toBe(true);
      expect(h.reads).toEqual([]); // the answer does not depend on the flag: no read
    }
  );

  it.each(REDIRECTED)(
    'a %s subgrid on a child host shows the BFF command on every saved host, never the platform one',
    async table => {
      const h = harness({ flag: false });
      await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf('sprk_event'), table))).resolves.toBe(false);
      await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf('sprk_event'), table))).resolves.toBe(true);
    }
  );

  it.each(REDIRECTED)(
    'a %s subgrid on a party host keeps the platform "+ New" (never an ownership parent; must not fire)',
    async table => {
      const h = harness({ flag: 'throw' });
      expect(h.ribbon.nativeNewAllowed(formOf('contact'), table)).toBe(true);
      expect(h.ribbon.newChildAvailable(formOf('contact'), table)).toBe(false);
    }
  );

  it('a call that names no table behaves as before: a root read as not secure keeps the platform "+ New" (must not fire)', async () => {
    const h = harness({ flag: false });
    await expect(Promise.resolve(h.ribbon.nativeNewAllowed(formOf('sprk_matter')))).resolves.toBe(true);
    await expect(Promise.resolve(h.ribbon.newChildAvailable(formOf('sprk_matter')))).resolves.toBe(false);
  });

  it('the AP export batch still offers no "New" for a redirected table', () => {
    const h = harness({ flag: false });
    expect(h.ribbon.nativeNewAllowed(formOf('sprk_billingexport'), 'sprk_communication')).toBe(false);
    expect(h.ribbon.newChildAvailable(formOf('sprk_billingexport'), 'sprk_communication')).toBe(false);
  });
});

describe('sprk_secure_child_ribbon.js — createChild creates through the BFF only (task 051: Add Memo, Add KPI)', () => {
  it('creates a memo through POST /api/v1/child-records/sprk_memo, filed under the host, and resolves its id (no form, no Xrm create)', async () => {
    const h = harness({ flag: true });
    const host = h.ribbon.hostOf(formOf('sprk_matter'))!;
    const created = await h.ribbon.createChild('sprk_memo', host, null);

    expect(created).toEqual({ id: NEW_ID });
    expect(h.posts).toHaveLength(1);
    expect(h.posts[0].url).toBe('https://bff.example/api/v1/child-records/sprk_memo');
    expect(h.posts[0].body['sprk_Matter@odata.bind']).toBe(`/sprk_matters(${HOST_ID})`);
    expect(Object.keys(h.posts[0].body).some(k => /^ownerid/i.test(k))).toBe(false);
    expect(h.forms).toEqual([]); // createChild opens nothing: the caller decides
  });

  it('resolves null and tells the user when the BFF refuses (nothing created)', async () => {
    const h = harness({ flag: true, bffStatus: 403, bffBody: { detail: 'You cannot add to this record.' } });
    const created = await h.ribbon.createChild('sprk_memo', h.ribbon.hostOf(formOf('sprk_matter'))!, null);

    expect(created).toBeNull();
    expect(h.alerts).toHaveLength(1);
    expect(h.alerts[0].text).toContain('You cannot add to this record.');
    expect(h.alerts[0].text).toContain('Nothing was created.');
  });

  it('resolves null, sends nothing and says so when the sign-in helper is not loaded (never an unauthenticated call)', async () => {
    const h = harness({ flag: true });
    (h.context.Spaarke as { BffAuth?: unknown }).BffAuth = undefined;
    const created = await h.ribbon.createChild('sprk_memo', h.ribbon.hostOf(formOf('sprk_matter'))!, null);

    expect(created).toBeNull();
    expect(h.posts).toEqual([]);
    expect(h.alerts[0].text).toContain('Nothing was created.');
  });
});

describe('sprk_kpi_ribbon_actions.js — Add KPI creates through the BFF, never a quick create (task 051)', () => {
  const KPI_SCRIPT = fs.readFileSync(
    path.join(
      __dirname,
      '..',
      '..',
      '..',
      '..',
      '..',
      '..',
      '..',
      'solutions',
      'webresources',
      'sprk_kpi_ribbon_actions.js'
    ),
    'utf8'
  );

  interface IKpi {
    openQuickCreate(p: unknown): void;
    openProjectQuickCreate(p: unknown): void;
  }

  function withKpiScript(h: IHarness): IKpi {
    vm.runInContext(KPI_SCRIPT, h.context as unknown as vm.Context);
    return (h.context.Spaarke as { KpiRibbon: IKpi }).KpiRibbon;
  }

  const formWithSubgrid = (entity: string) => ({
    ...formOf(entity),
    getControl: () => ({ getRelationship: () => ({ attributeName: entity }), refresh: () => undefined }),
  });

  it.each([
    ['openQuickCreate', 'sprk_matter'],
    ['openProjectQuickCreate', 'sprk_project'],
  ] as const)(
    '%s on a %s form posts the KPI assessment to the BFF and opens the new row (not a quick create)',
    async (fn, entity) => {
      const h = harness({ flag: false }); // an unsecured record: the old script opened the quick create here
      const kpi = withKpiScript(h);
      kpi[fn](formWithSubgrid(entity));
      for (let i = 0; i < 10 && h.forms.length === 0; i++) await flush();

      expect(h.posts).toHaveLength(1);
      expect(h.posts[0].url).toBe('https://bff.example/api/v1/child-records/sprk_kpiassessment');
      expect(h.forms).toEqual([{ entityName: 'sprk_kpiassessment', entityId: NEW_ID }]);
      expect(h.forms.some(f => 'useQuickCreateForm' in f)).toBe(false);
    }
  );

  it('with the shared create helper missing it creates nothing and says so (no quick create fallback)', async () => {
    const h = harness({ flag: false });
    const kpi = withKpiScript(h);
    (h.context.Spaarke as { SecureChild?: unknown }).SecureChild = undefined;
    kpi.openQuickCreate(formWithSubgrid('sprk_matter'));
    await flush();

    expect(h.posts).toEqual([]);
    expect(h.forms).toEqual([]);
    expect(h.alerts).toHaveLength(1);
    expect(h.alerts[0].text).toContain('Nothing was created.');
  });

  it('the script holds no quick create launch or direct Xrm create (static guard)', () => {
    expect(KPI_SCRIPT).not.toContain('useQuickCreateForm');
    expect(KPI_SCRIPT).not.toMatch(/createRecord/);
  });
});
