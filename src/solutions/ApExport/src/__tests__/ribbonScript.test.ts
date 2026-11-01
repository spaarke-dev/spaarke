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
