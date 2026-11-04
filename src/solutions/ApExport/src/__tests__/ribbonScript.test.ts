/**
 * The AP export ribbon script (`src/client/webresources/js/sprk_apexport_ribbon.js`, mirrored to SpaarkeMaster as
 * `sprk_/scripts/apexport_ribbon.js`). There is no web-resource test runner in the repository, so the script itself runs
 * here in a sandbox with a fake `Xrm`, `fetch` and sign-in helper (ADR-038: the real code, only the platform seams faked),
 * as `Spaarke.UI.Components/src/utils/adapters/__tests__/secureChildRibbonScript.test.ts` does for its script.
 *
 * Pinned: the enable rules (display only) for the form status, the selection status and a Completed batch; the dialog
 * launch (page, mode, ids, size) and the refresh after it; the download URL, bearer token, file name and the server's
 * words on a refusal.
 */

import * as fs from 'fs';
import * as path from 'path';
import * as vm from 'vm';

const SCRIPT = fs.readFileSync(
  path.join(__dirname, '..', '..', '..', '..', 'client', 'webresources', 'js', 'sprk_apexport_ribbon.js'),
  'utf8'
);

const MIRROR = fs.readFileSync(
  path.join(__dirname, '..', '..', '..', '..', 'dataverse', 'solutions', 'SpaarkeMaster', 'WebResources', 'sprk_', 'scripts', 'apexport_ribbon.js'),
  'utf8'
);

const INV_A = 'aaaaaaaa-0000-4000-8000-000000000001';
const INV_B = 'bbbbbbbb-0000-4000-8000-000000000002';
const BATCH = 'eeeeeeee-0000-4000-8000-000000000005';
const BASE = 'https://bff.example.test';

interface IRibbon {
  formStatusIs(primaryControl: unknown, statusName: string): boolean | Promise<boolean>;
  selectionHasStatus(selectedIds: string[] | null, statusName: string): boolean | Promise<boolean>;
  batchIsCompleted(primaryControl: unknown): boolean | Promise<boolean>;
  openFromGrid(selectedIds: string[] | null, selectedControl: unknown, mode: string): void;
  openFromForm(primaryControl: unknown, mode: string): void;
  openFromBatchForm(primaryControl: unknown, mode: string): Promise<void>;
  batchLaunchData(mode: string, batchId: string): string;
  downloadBatchFile(primaryControl: unknown, format: string): Promise<void>;
  launchData(mode: string, ids: string[]): string;
  fileUrl(baseUrl: string, batchId: string, format: string): string;
  fileNameOf(contentDisposition: string | null, batchId: string, format: string): string;
  saveBlob(blob: unknown, name: string): void;
}

interface IHarness {
  ribbon: IRibbon;
  alerts: Array<{ title: string; text: string }>;
  pages: Array<{ page: Record<string, unknown>; options: Record<string, unknown> }>;
  reads: Array<{ entity: string; id?: string; options: string }>;
  fetches: Array<{ url: string; init: Record<string, unknown> }>;
  saved: Array<{ blob: unknown; name: string }>;
}

function harness(options: {
  rows?: Array<Record<string, unknown>> | 'throw';
  record?: Record<string, unknown> | 'throw';
  token?: string | null;
  helpers?: boolean;
  response?: { ok: boolean; status: number; body?: string; disposition?: string | null };
}): IHarness {
  const h: IHarness = { ribbon: undefined as unknown as IRibbon, alerts: [], pages: [], reads: [], fetches: [], saved: [] };
  const spaarke: Record<string, unknown> = {};
  if (options.helpers !== false) {
    spaarke.BffAuth = { getToken: () => Promise.resolve(options.token === undefined ? 'tok' : options.token) };
    spaarke.AssignedAccess = { getApiBaseUrl: () => Promise.resolve(BASE) };
  }
  const response = options.response ?? { ok: true, status: 200, body: 'a,b', disposition: null };
  const context: Record<string, unknown> = {
    console: { warn: () => undefined, error: () => undefined, log: () => undefined },
    setTimeout: () => 0,
    Spaarke: spaarke,
    Xrm: {
      Navigation: {
        openAlertDialog: (a: { title: string; text: string }) => {
          h.alerts.push(a);
          return Promise.resolve();
        },
        navigateTo: (page: Record<string, unknown>, opts: Record<string, unknown>) => {
          h.pages.push({ page, options: opts });
          return Promise.resolve();
        },
      },
      WebApi: {
        retrieveMultipleRecords: (entity: string, query: string) => {
          h.reads.push({ entity, options: query });
          return options.rows === 'throw' ? Promise.reject(new Error('read failed')) : Promise.resolve({ entities: options.rows ?? [] });
        },
        retrieveRecord: (entity: string, id: string, query: string) => {
          h.reads.push({ entity, id, options: query });
          return options.record === 'throw' ? Promise.reject(new Error('read failed')) : Promise.resolve(options.record ?? {});
        },
      },
    },
    fetch: (url: string, init: Record<string, unknown>) => {
      h.fetches.push({ url, init });
      return Promise.resolve({
        ok: response.ok,
        status: response.status,
        headers: { get: (name: string) => (name === 'Content-Disposition' ? response.disposition ?? null : null) },
        text: () => Promise.resolve(response.body ?? ''),
        blob: () => Promise.resolve({ blob: response.body }),
      });
    },
  };
  vm.createContext(context);
  vm.runInContext(SCRIPT, context);
  const ns = (context.Spaarke as { ApExport: { Ribbon: IRibbon } }).ApExport.Ribbon;
  ns.saveBlob = (blob: unknown, name: string) => h.saved.push({ blob, name });
  h.ribbon = ns;
  return h;
}

/** A form context: `id` null is a new form; `attributes` the columns the form carries. */
function form(id: string | null, attributes: Record<string, unknown> = {}) {
  const refreshed: string[] = [];
  return {
    refreshed,
    data: {
      entity: { getId: () => (id ? `{${id.toUpperCase()}}` : '') },
      refresh: () => {
        refreshed.push('data');
        return Promise.resolve();
      },
    },
    ui: { refreshRibbon: () => refreshed.push('ribbon') },
    getAttribute: (name: string) => (name in attributes ? { getValue: () => attributes[name] } : null),
  };
}

const flush = () => new Promise(resolve => setImmediate(resolve));

it('the SpaarkeMaster mirror is the same script (WebResourceMirrorParityGuardTests pairs them)', () => {
  const norm = (s: string) => s.replace(/^﻿/, '').replace(/\r\n/g, '\n');
  expect(norm(MIRROR)).toBe(norm(SCRIPT));
});

describe('helpers', () => {
  it('launchData carries the mode and the ids', () => {
    expect(harness({}).ribbon.launchData('export', [INV_A, INV_B])).toBe(`mode=export&ids=${INV_A}%2C${INV_B}`);
  });

  it('fileUrl builds 151a route from the BFF host', () => {
    expect(harness({}).ribbon.fileUrl(`${BASE}/`, `{${BATCH.toUpperCase()}}`, 'csv')).toBe(
      `${BASE}/api/finance/export-batches/${BATCH}/files/csv`
    );
  });

  it("fileNameOf takes the server's name, else ap-export-{batchId}.{format}", () => {
    const r = harness({}).ribbon;
    expect(r.fileNameOf('attachment; filename=x.json', BATCH, 'json')).toBe('x.json');
    expect(r.fileNameOf(null, BATCH, 'csv')).toBe(`ap-export-${BATCH}.csv`);
  });
});

describe('enable rules (display only)', () => {
  it('formStatusIs reads the status on the form', async () => {
    const r = harness({}).ribbon;
    expect(await r.formStatusIs(form(INV_A, { statuscode: 659490000 }), 'approved')).toBe(true);
    expect(await r.formStatusIs(form(INV_A, { statuscode: 659490000 }), 'sentToAp')).toBe(false);
    expect(await r.formStatusIs(form(INV_A, { statuscode: 659490001 }), 'sentToAp')).toBe(true);
  });

  it('formStatusIs reads the record when the form does not carry the status', async () => {
    const h = harness({ record: { statuscode: 659490001 } });
    expect(await h.ribbon.formStatusIs(form(INV_A), 'sentToAp')).toBe(true);
    expect(h.reads).toEqual([{ entity: 'sprk_invoice', id: INV_A, options: '?$select=statuscode' }]);
  });

  it('formStatusIs is false on a new form, an unknown name and a failed read', async () => {
    expect(await harness({}).ribbon.formStatusIs(form(null, { statuscode: 659490000 }), 'approved')).toBe(false);
    expect(await harness({}).ribbon.formStatusIs(form(INV_A, { statuscode: 659490000 }), 'paidd')).toBe(false);
    expect(await harness({ record: 'throw' }).ribbon.formStatusIs(form(INV_A), 'approved')).toBe(false);
  });

  it('selectionHasStatus is true only when every selected invoice has the status', async () => {
    const all = harness({ rows: [{ statuscode: 659490001 }, { statuscode: 659490001 }] });
    expect(await all.ribbon.selectionHasStatus([`{${INV_A.toUpperCase()}}`, INV_B], 'sentToAp')).toBe(true);
    expect(all.reads[0].options).toContain(`PropertyValues=['${INV_A}','${INV_B}']`);

    const mixed = harness({ rows: [{ statuscode: 659490001 }, { statuscode: 659490000 }] });
    expect(await mixed.ribbon.selectionHasStatus([INV_A, INV_B], 'sentToAp')).toBe(false);

    const unreadable = harness({ rows: [{ statuscode: 659490001 }] });
    expect(await unreadable.ribbon.selectionHasStatus([INV_A, INV_B], 'sentToAp')).toBe(false);
  });

  it('selectionHasStatus is false on an empty or too large selection (no read) and on a failed read', async () => {
    const h = harness({ rows: [] });
    expect(await h.ribbon.selectionHasStatus([], 'sentToAp')).toBe(false);
    expect(await h.ribbon.selectionHasStatus(null, 'sentToAp')).toBe(false);
    const many = Array.from({ length: 201 }, (_, i) => `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`);
    expect(await h.ribbon.selectionHasStatus(many, 'sentToAp')).toBe(false);
    expect(h.reads).toEqual([]);
    expect(await harness({ rows: 'throw' }).ribbon.selectionHasStatus([INV_A], 'sentToAp')).toBe(false);
  });

  it('batchIsCompleted is true only for a Completed batch', async () => {
    const r = harness({}).ribbon;
    expect(await r.batchIsCompleted(form(BATCH, { sprk_exportstatus: 100000001 }))).toBe(true);
    expect(await r.batchIsCompleted(form(BATCH, { sprk_exportstatus: 100000000 }))).toBe(false);
    expect(await r.batchIsCompleted(form(BATCH, { sprk_exportstatus: 100000002 }))).toBe(false);
    expect(await r.batchIsCompleted(form(null, { sprk_exportstatus: 100000001 }))).toBe(false);
  });
});

/**
 * The two rules the coordinator added (151b review): the grid Export to AP is enabled only when every selected invoice is
 * Approved (the enable rule runs selectionHasStatus "approved"), and Mark Paid is shown only to a user with Write on
 * sprk_invoice (the decide right is Write), on the grid and the form. The script half runs in the sandbox; the wiring
 * half reads the committed RibbonDiff, since an EntityPrivilegeRule is evaluated by the platform, not by the script.
 */
const RIBBON = fs
  .readFileSync(
    path.join(__dirname, '..', '..', '..', '..', 'dataverse', 'solutions', 'SpaarkeMaster', 'Entities', 'sprk_Invoice', 'RibbonDiff.xml'),
    'utf8'
  )
  .replace(/^﻿/, '');

/** The inner XML of the element `<tag Id="id">...</tag>` in the RibbonDiff ('' when absent). */
function block(tag: string, id: string): string {
  const match = new RegExp(`<${tag} Id="${id.replace(/\./g, '\\.')}">([\\s\\S]*?)</${tag}>`).exec(RIBBON);
  return match ? match[1] : '';
}

/** The rule ids a command lists under EnableRules or DisplayRules. */
function rulesOf(commandId: string, kind: 'EnableRule' | 'DisplayRule'): string[] {
  const command = block('CommandDefinition', commandId);
  return Array.from(command.matchAll(new RegExp(`<${kind} Id="([^"]+)" />`, 'g')), m => m[1]);
}

const GRID_EXPORT = 'sprk.ApExport.Invoice.Grid.Export.Command';
const FORM_EXPORT = 'sprk.ApExport.Invoice.Form.Export.Command';
const GRID_MARKPAID = 'sprk.ApExport.Invoice.Grid.MarkPaid.Command';
const FORM_MARKPAID = 'sprk.ApExport.Invoice.Form.MarkPaid.Command';
const SELECTION_APPROVED = 'sprk.ApExport.Invoice.SelectionApproved.EnableRule';
const WRITE_PRIVILEGE = 'sprk.ApExport.InvoiceWritePrivilege.DisplayRule';
const CREATE_BATCH_PRIVILEGE = 'sprk.ApExport.CreateBatchPrivilege.DisplayRule';

describe('grid Export to AP: every selected invoice is Approved', () => {
  it('fires when every selected invoice is Approved', async () => {
    const h = harness({ rows: [{ statuscode: 659490000 }, { statuscode: 659490000 }] });
    expect(await h.ribbon.selectionHasStatus([INV_A, INV_B], 'approved')).toBe(true);
  });

  it('does not fire when one is Sent to AP, all are Paid, or one is not returned', async () => {
    expect(
      await harness({ rows: [{ statuscode: 659490000 }, { statuscode: 659490001 }] }).ribbon.selectionHasStatus([INV_A, INV_B], 'approved')
    ).toBe(false);
    expect(
      await harness({ rows: [{ statuscode: 659490002 }, { statuscode: 659490002 }] }).ribbon.selectionHasStatus([INV_A, INV_B], 'approved')
    ).toBe(false);
    expect(await harness({ rows: [{ statuscode: 659490000 }] }).ribbon.selectionHasStatus([INV_A, INV_B], 'approved')).toBe(false);
  });

  it('the grid command carries the rule, which asks for "approved" on the selected ids', () => {
    expect(rulesOf(GRID_EXPORT, 'EnableRule')).toEqual(['sprk.ApExport.Invoice.SelectionInRange.EnableRule', SELECTION_APPROVED]);
    const rule = block('EnableRule', SELECTION_APPROVED);
    expect(rule).toContain('FunctionName="Spaarke.ApExport.Ribbon.selectionHasStatus"');
    expect(rule).toContain('<CrmParameter Value="SelectedControlSelectedItemIds" />');
    expect(rule).toContain('<StringParameter Value="approved" />');
    expect(rule).toContain('Default="false"');
  });

  it('keeps the create-privilege display rule; the form and Mark Paid commands do not carry the selection rule', () => {
    expect(rulesOf(GRID_EXPORT, 'DisplayRule')).toEqual([CREATE_BATCH_PRIVILEGE]);
    expect(rulesOf(FORM_EXPORT, 'EnableRule')).not.toContain(SELECTION_APPROVED);
    expect(rulesOf(GRID_MARKPAID, 'EnableRule')).not.toContain(SELECTION_APPROVED);
  });
});

describe('Mark Paid: shown only with Write on sprk_invoice', () => {
  it('the rule is the Write privilege on sprk_invoice', () => {
    expect(block('DisplayRule', WRITE_PRIVILEGE).trim()).toBe(
      '<EntityPrivilegeRule EntityName="sprk_invoice" PrivilegeType="Write" PrivilegeDepth="Basic" />'
    );
  });

  it('fires on the grid and the form Mark Paid commands, which keep their status enable rules', () => {
    expect(rulesOf(GRID_MARKPAID, 'DisplayRule')).toEqual([WRITE_PRIVILEGE]);
    expect(rulesOf(FORM_MARKPAID, 'DisplayRule')).toEqual(['sprk.ApExport.Invoice.FormExisting.DisplayRule', WRITE_PRIVILEGE]);
    expect(rulesOf(GRID_MARKPAID, 'EnableRule')).toContain('sprk.ApExport.Invoice.SelectionSentToAp.EnableRule');
    expect(rulesOf(FORM_MARKPAID, 'EnableRule')).toEqual(['sprk.ApExport.Invoice.FormSentToAp.EnableRule']);
  });

  it('does not fire on the Export commands', () => {
    expect(rulesOf(GRID_EXPORT, 'DisplayRule')).not.toContain(WRITE_PRIVILEGE);
    expect(rulesOf(FORM_EXPORT, 'DisplayRule')).not.toContain(WRITE_PRIVILEGE);
  });
});

describe('dialog commands', () => {
  it('openFromGrid opens the page with the mode and the ids, then refreshes the grid', async () => {
    const h = harness({});
    const refreshed: string[] = [];
    h.ribbon.openFromGrid([`{${INV_A.toUpperCase()}}`, INV_B], { refresh: () => refreshed.push('grid') }, 'export');
    await flush();
    expect(h.pages).toHaveLength(1);
    expect(h.pages[0].page).toEqual({ pageType: 'webresource', webresourceName: 'sprk_apexport', data: `mode=export&ids=${INV_A}%2C${INV_B}` });
    expect(h.pages[0].options).toMatchObject({ target: 2, title: 'Export to AP', width: { value: 60, unit: '%' } });
    expect(refreshed).toEqual(['grid']);
  });

  it('openFromGrid refuses an empty or too large selection without opening the page', () => {
    const h = harness({});
    h.ribbon.openFromGrid([], null, 'markpaid');
    const many = Array.from({ length: 201 }, (_, i) => `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`);
    h.ribbon.openFromGrid(many, null, 'markpaid');
    h.ribbon.openFromGrid([INV_A], null, 'unknown');
    expect(h.pages).toEqual([]);
    expect(h.alerts.map(a => a.text)).toEqual(['Select one or more invoices first.', 'Select at most 200 invoices at a time.']);
  });

  it('openFromForm opens Mark Paid for the open invoice, then refreshes the form and its ribbon', async () => {
    const h = harness({});
    const f = form(INV_A, { statuscode: 659490001 });
    h.ribbon.openFromForm(f, 'markpaid');
    await flush();
    expect(h.pages[0].page).toMatchObject({ data: `mode=markpaid&ids=${INV_A}` });
    expect(h.pages[0].options).toMatchObject({ title: 'Mark Paid' });
    expect(f.refreshed).toEqual(['data', 'ribbon']);
  });

  it('openFromForm on a new form asks to save first', () => {
    const h = harness({});
    h.ribbon.openFromForm(form(null), 'export');
    expect(h.pages).toEqual([]);
    expect(h.alerts[0].text).toBe('Save this invoice first.');
  });
});

describe('downloadBatchFile (Billing Export form)', () => {
  it('fetches the file with the bearer token and saves it under the server name', async () => {
    const h = harness({ response: { ok: true, status: 200, body: 'a,b', disposition: 'attachment; filename=ap-export-x.csv' } });
    await h.ribbon.downloadBatchFile(form(BATCH), 'csv');
    expect(h.fetches).toEqual([
      { url: `${BASE}/api/finance/export-batches/${BATCH}/files/csv`, init: { method: 'GET', headers: { Authorization: 'Bearer tok' } } },
    ]);
    expect(h.saved).toEqual([{ blob: { blob: 'a,b' }, name: 'ap-export-x.csv' }]);
    expect(h.alerts).toEqual([]);
  });

  it("falls back to ap-export-{batchId}.{format} when the header is not exposed", async () => {
    const h = harness({});
    await h.ribbon.downloadBatchFile(form(BATCH), 'json');
    expect(h.saved[0].name).toBe(`ap-export-${BATCH}.json`);
  });

  it("shows the server's words and code on a refusal, and saves nothing", async () => {
    const body = JSON.stringify({ detail: 'The file is waiting for its malware scan.', errorCode: 'file_scan_pending' });
    const h = harness({ response: { ok: false, status: 409, body } });
    await h.ribbon.downloadBatchFile(form(BATCH), 'csv');
    expect(h.saved).toEqual([]);
    expect(h.alerts).toEqual([{ title: 'Download CSV', text: 'The file is waiting for its malware scan. (file_scan_pending)' }]);
  });

  it('no token: asks to sign in again and sends nothing', async () => {
    const h = harness({ token: null });
    await h.ribbon.downloadBatchFile(form(BATCH), 'csv');
    expect(h.fetches).toEqual([]);
    expect(h.alerts[0].text).toBe('Sign-in needed - reload the page and retry.');
  });

  it('helpers not loaded: says so and sends nothing', async () => {
    const h = harness({ helpers: false });
    await h.ribbon.downloadBatchFile(form(BATCH), 'json');
    expect(h.fetches).toEqual([]);
    expect(h.alerts[0].text).toContain('sign-in helper');
  });

  it('an unknown format sends nothing', async () => {
    const h = harness({});
    await h.ribbon.downloadBatchFile(form(BATCH), 'xml');
    expect(h.fetches).toEqual([]);
  });
});

/**
 * Task 151c-1b: Email to AP and Mark Sent to AP on the Billing Export form. The script half runs in the sandbox; the
 * wiring half reads the committed RibbonDiff (an EntityPrivilegeRule is evaluated by the platform, not by the script).
 */
describe('Billing Export form: Email to AP and Mark Sent to AP (script)', () => {
  it('batchLaunchData carries the mode and the batch id, without braces', () => {
    expect(harness({}).ribbon.batchLaunchData('email', `{${BATCH.toUpperCase()}}`)).toBe(`mode=email&batch=${BATCH}`);
  });

  it.each([
    ['email', 'Email to AP'],
    ['marksent', 'Mark Sent to AP'],
  ])('openFromBatchForm opens the page on the open batch in %s mode, then refreshes the form and its ribbon', async (mode, title) => {
    const h = harness({});
    const f = form(BATCH, { sprk_exportstatus: 100000001 });
    await h.ribbon.openFromBatchForm(f, mode);
    await flush();
    expect(h.pages).toHaveLength(1);
    expect(h.pages[0].page).toEqual({ pageType: 'webresource', webresourceName: 'sprk_apexport', data: `mode=${mode}&batch=${BATCH}` });
    expect(h.pages[0].options).toMatchObject({ target: 2, title, width: { value: 60, unit: '%' } });
    expect(f.refreshed).toEqual(['data', 'ribbon']);
  });

  it('openFromBatchForm on a new form asks to save first, and an unknown mode opens nothing', async () => {
    const h = harness({});
    await h.ribbon.openFromBatchForm(form(null), 'email');
    await h.ribbon.openFromBatchForm(form(BATCH), 'delete');
    expect(h.pages).toEqual([]);
    expect(h.alerts.map(a => a.text)).toEqual(['Save this export batch first.']);
  });

  it('the invoice commands do not open the batch modes', () => {
    const h = harness({});
    h.ribbon.openFromGrid([INV_A], null, 'email');
    h.ribbon.openFromForm(form(INV_A), 'marksent');
    expect(h.pages).toEqual([]);
  });

  it('the script version is bumped for the new commands', () => {
    const version = /ns\.VERSION = "([^"]+)"/.exec(SCRIPT)?.[1];
    expect(version).toBe('1.1.0');
  });
});

const BATCH_RIBBON = fs
  .readFileSync(
    path.join(__dirname, '..', '..', '..', '..', 'dataverse', 'solutions', 'SpaarkeMaster', 'Entities', 'sprk_billingexport', 'RibbonDiff.xml'),
    'utf8'
  )
  .replace(/^﻿/, '');

function batchBlock(tag: string, id: string): string {
  const match = new RegExp(`<${tag} Id="${id.replace(/\./g, '\\.')}">([\\s\\S]*?)</${tag}>`).exec(BATCH_RIBBON);
  return match ? match[1] : '';
}

function batchRules(commandId: string, kind: 'EnableRule' | 'DisplayRule'): string[] {
  return Array.from(batchBlock('CommandDefinition', commandId).matchAll(new RegExp(`<${kind} Id="([^"]+)" />`, 'g')), m => m[1]);
}

const EMAIL_COMMAND = 'sprk.ApExport.BillingExport.Form.EmailToAp.Command';
const MARKSENT_COMMAND = 'sprk.ApExport.BillingExport.Form.MarkSentToAp.Command';
const DOWNLOAD_CSV_COMMAND = 'sprk.ApExport.BillingExport.Form.DownloadCsv.Command';
const BATCH_WRITE = 'sprk.ApExport.BatchWritePrivilege.DisplayRule';

/**
 * A batch is created only by Export to AP (POST /api/finance/export-batches). The platform New and Delete commands are hidden on the table's
 * home grid, form and sub-grids. Every Location below is a control id read from the PUBLISHED ribbon of sprk_billingexport on dev
 * (RetrieveEntityRibbon, read-only). Create stays granted to Core User (the BFF's OBO create needs it): this is the UI half; the server refuses
 * a hand-made batch on its own (ApExportDeliveryService).
 */
describe('Billing Export: platform New and Delete are hidden (RibbonDiff)', () => {
  const HIDDEN = [
    'Mscrm.HomepageGrid.sprk_billingexport.NewRecord',
    'Mscrm.HomepageGrid.sprk_billingexport.DeleteMenu',
    'Mscrm.Form.sprk_billingexport.NewRecord',
    'Mscrm.Form.sprk_billingexport.Delete',
    'Mscrm.Form.sprk_billingexport.SaveAndNew',
    'Mscrm.SubGrid.sprk_billingexport.AddNewStandard',
    'Mscrm.SubGrid.sprk_billingexport.NewRecord',
  ];

  it('each platform command is hidden once, under the sprk.<Location>.Hide id', () => {
    const hides = Array.from(BATCH_RIBBON.matchAll(/<HideCustomAction HideActionId="([^"]+)" Location="([^"]+)" \/>/g), m => [m[1], m[2]]);
    expect(hides.map(h => h[1]).sort()).toEqual([...HIDDEN].sort());
    for (const [id, location] of hides) {
      expect(id).toBe(`sprk.${location}.Hide`);
    }
  });

  it('the four Billing Export form buttons are still there and are not hidden', () => {
    for (const id of ['DownloadCsv', 'DownloadJson', 'EmailToAp', 'MarkSentToAp']) {
      expect(BATCH_RIBBON).toContain(`<CustomAction Id="sprk.ApExport.BillingExport.Form.${id}.CustomAction"`);
      expect(BATCH_RIBBON).not.toMatch(new RegExp(`<HideCustomAction[^>]*${id}`));
    }
  });
});

describe('Billing Export main form: the legacy CSV / JSON Document lookups are off the form', () => {
  const formDir = path.join(__dirname, '..', '..', '..', '..', 'dataverse', 'solutions', 'SpaarkeMaster', 'Entities', 'sprk_billingexport', 'FormXml', 'main');
  const forms = ['{bd0020ec-1912-4f2d-b3e6-a6315f8b9e48}.xml', '{bd0020ec-1912-4f2d-b3e6-a6315f8b9e48}_managed.xml'];

  it.each(forms)('%s has neither lookup, and keeps the Sent to AP Date and the Owner', file => {
    const xml = fs.readFileSync(path.join(formDir, file), 'utf8');
    expect(xml).not.toContain('sprk_csvdocument');
    expect(xml).not.toContain('sprk_jsondocument');
    expect(xml).toContain('datafieldname="sprk_senttoapdate"');
    expect(xml).toContain('datafieldname="ownerid"');
    expect(xml).toContain('id="subgrid_apemails"');
  });
});

describe('Billing Export form: Email to AP and Mark Sent to AP (RibbonDiff wiring)', () => {
  it('both buttons exist on the form, with their labels', () => {
    for (const id of ['EmailToAp', 'MarkSentToAp']) {
      expect(BATCH_RIBBON).toContain(`Id="sprk.ApExport.BillingExport.Form.${id}.Button"`);
      expect(BATCH_RIBBON).toContain(`Location="Mscrm.Form.sprk_billingexport.MainTab.Actions.Controls._children"`);
    }
    expect(BATCH_RIBBON).toContain('<Title description="Email to AP" languagecode="1033" />');
    expect(BATCH_RIBBON).toContain('<Title description="Mark Sent to AP" languagecode="1033" />');
  });

  it('the display rule is the Write privilege on sprk_billingexport, the pattern of InvoiceWritePrivilege', () => {
    expect(batchBlock('DisplayRule', BATCH_WRITE).trim()).toBe(
      '<EntityPrivilegeRule EntityName="sprk_billingexport" PrivilegeType="Write" PrivilegeDepth="Basic" />'
    );
  });

  it('both commands are shown only for an existing record with Write, and enabled only on a Completed batch', () => {
    for (const command of [EMAIL_COMMAND, MARKSENT_COMMAND]) {
      expect(batchRules(command, 'DisplayRule')).toEqual(['sprk.ApExport.BillingExport.FormExisting.DisplayRule', BATCH_WRITE]);
      expect(batchRules(command, 'EnableRule')).toEqual(['sprk.ApExport.BillingExport.Completed.EnableRule']);
    }
    const rule = batchBlock('EnableRule', 'sprk.ApExport.BillingExport.Completed.EnableRule');
    expect(rule).toContain('FunctionName="Spaarke.ApExport.Ribbon.batchIsCompleted"');
    expect(rule).toContain('Default="false"');
  });

  it('each command calls openFromBatchForm with the open batch and its mode', () => {
    for (const [command, mode] of [
      [EMAIL_COMMAND, 'email'],
      [MARKSENT_COMMAND, 'marksent'],
    ]) {
      const body = batchBlock('CommandDefinition', command);
      expect(body).toContain('FunctionName="Spaarke.ApExport.Ribbon.openFromBatchForm"');
      expect(body).toContain('<CrmParameter Value="PrimaryControl" />');
      expect(body).toContain(`<StringParameter Value="${mode}" />`);
    }
  });

  it('Download CSV and JSON do not carry the Write rule (a reader may still download)', () => {
    expect(batchRules(DOWNLOAD_CSV_COMMAND, 'DisplayRule')).not.toContain(BATCH_WRITE);
    expect(batchRules('sprk.ApExport.BillingExport.Form.DownloadJson.Command', 'DisplayRule')).not.toContain(BATCH_WRITE);
  });
});

/** The view source files this task edits (SpaarkeMaster). */
function view(entityFolder: string, guid: string): string {
  return fs
    .readFileSync(
      path.join(__dirname, '..', '..', '..', '..', 'dataverse', 'solutions', 'SpaarkeMaster', 'Entities', entityFolder, 'SavedQueries', `{${guid}}.xml`),
      'utf8'
    )
    .replace(/^﻿/, '');
}

describe('Sent to AP Date on the views', () => {
  it('the Billing Export Active view lists the date, in the layout and the fetch', () => {
    const xml = view('sprk_billingexport', 'c19542df-a537-49c5-9021-b4a6cbb3fc36');
    expect(xml).toContain('<cell name="sprk_senttoapdate"');
    expect(xml).toContain('<attribute name="sprk_senttoapdate" />');
  });

  it.each([
    ['Sent to AP', 'cd153641-74d9-4a9f-9318-3cb7caf4fccf'],
    ['Paid', 'b0feb580-dbb5-464f-bb07-3ce64fb5e21d'],
  ])('the invoice view "%s" shows the batch date through the batch lookup, with an outer join and no per-invoice copy', (_name, guid) => {
    const xml = view('sprk_Invoice', guid);
    expect(xml).toContain(
      '<link-entity name="sprk_billingexport" from="sprk_billingexportid" to="sprk_billingexport" alias="export_batch" link-type="outer">'
    );
    expect(xml).toContain('<attribute name="sprk_senttoapdate" />');
    // The layout cell is addressed as <alias>.<attribute>, and the alias is the one the fetch declares.
    expect(xml).toContain('<cell name="export_batch.sprk_senttoapdate"');
    expect(xml).toContain('<cell name="sprk_billingexport"');
    // No column of the invoice itself carries the date.
    expect(xml).not.toMatch(/<entity name="sprk_invoice">[\s\S]*?<attribute name="sprk_senttoapdate" \/>[\s\S]*?<link-entity/);
  });

  it('the Approved view has no batch, so it gets no linked column', () => {
    const xml = view('sprk_Invoice', '467cbd98-eb63-4f05-b7fb-3bc71f14bac9');
    expect(xml).not.toContain('sprk_senttoapdate');
  });
});
