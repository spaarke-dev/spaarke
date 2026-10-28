/**
 * The Matter / Project "Push Updates" ribbon command (`src/client/webresources/js/sprk_fieldmapping_push.js`,
 * `Spaarke.FieldMapping.Push.pushUpdates`) — field-mapping-server-write-path-r1 task 075 (spec FR-35).
 *
 * The button now opens the Family view (the `sprk_recordfamily` code-page dialog hosting RecordFamilyModal) and pushes
 * nothing itself. There is no web-resource test runner in the repository, so the script runs in a `vm` sandbox with a
 * fake `Xrm` and a `fetch` that records every call (ADR-038: the real code, only the platform seams faked).
 *
 * Pinned: the dialog is a `navigateTo` webresource page with `{ entityName, id }` data; the form refreshes when the
 * dialog closes (resolved or rejected); NO network call and NO confirm dialog happens on click; an unsaved record or a
 * missing form context is refused before any dialog opens; `hasSourceProfile` stays exported (the enable rule).
 */

import * as fs from 'fs';
import * as path from 'path';
import * as vm from 'vm';

const SCRIPT = fs.readFileSync(
  path.join(__dirname, '..', '..', '..', '..', 'webresources', 'js', 'sprk_fieldmapping_push.js'),
  'utf8'
);

const RECORD_ID = 'a1750000-0000-4000-8000-000000000001';

interface IPush {
  pushUpdates(primaryControl: unknown): void;
  hasSourceProfile(primaryControl: unknown): unknown;
}

interface IHarness {
  push: IPush;
  pages: Array<{ page: Record<string, unknown>; options: Record<string, unknown> }>;
  alerts: Array<{ text: string }>;
  confirms: unknown[];
  fetches: string[];
  progress: string[];
}

function harness(navigateResult: 'resolve' | 'reject' | 'throw' = 'resolve'): IHarness {
  const h: IHarness = {
    push: undefined as unknown as IPush,
    pages: [],
    alerts: [],
    confirms: [],
    fetches: [],
    progress: [],
  };
  const context: Record<string, unknown> = {
    console: { warn: () => undefined, error: () => undefined, log: () => undefined },
    Promise,
    Object,
    JSON,
    String,
    Array,
    encodeURIComponent,
    document: {},
    Xrm: {
      Navigation: {
        openAlertDialog: (a: { text: string }) => {
          h.alerts.push(a);
          return Promise.resolve();
        },
        openConfirmDialog: (c: unknown) => {
          h.confirms.push(c);
          return Promise.resolve({ confirmed: true });
        },
        navigateTo: (page: Record<string, unknown>, options: Record<string, unknown>) => {
          h.pages.push({ page, options });
          if (navigateResult === 'throw') throw new Error('navigateTo unavailable');
          return navigateResult === 'reject' ? Promise.reject(new Error('closed')) : Promise.resolve();
        },
      },
      Utility: {
        showProgressIndicator: (m: string) => h.progress.push(m),
        closeProgressIndicator: () => undefined,
        getGlobalContext: () => ({ getClientUrl: () => 'https://org.crm.dynamics.com' }),
      },
      WebApi: { retrieveMultipleRecords: () => Promise.resolve({ entities: [] }) },
    },
    fetch: (url: string) => {
      h.fetches.push(url);
      return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({}) });
    },
  };
  context.window = context;
  vm.createContext(context);
  vm.runInContext(SCRIPT, context);
  h.push = (context.Spaarke as { FieldMapping: { Push: IPush } }).FieldMapping.Push;
  return h;
}

function form(entity: string, id: string | null = `{${RECORD_ID.toUpperCase()}}`) {
  const refreshes = { count: 0 };
  return {
    refreshes,
    primaryControl: {
      data: {
        refresh: () => {
          refreshes.count += 1;
          return Promise.resolve();
        },
        entity: { getEntityName: () => entity, getId: () => id },
      },
    },
  };
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));

describe('sprk_fieldmapping_push.js — Push Updates opens the Family view (task 075, FR-35)', () => {
  it.each(['sprk_matter', 'sprk_project'])(
    'opens the sprk_recordfamily dialog for a %s, with its entity name and bare id',
    async entity => {
      const h = harness();
      const f = form(entity);
      h.push.pushUpdates(f.primaryControl);
      await flush();

      expect(h.pages).toHaveLength(1);
      const { page, options } = h.pages[0];
      expect(page.pageType).toBe('webresource');
      expect(page.webresourceName).toBe('sprk_recordfamily');
      expect(page.data).toBe(`entityName=${entity}&id=${RECORD_ID.toUpperCase()}`);
      expect(options.target).toBe(2);
    }
  );

  it('pushes nothing: no confirm dialog, no progress indicator and no network call on click', async () => {
    const h = harness();
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    await flush();

    expect(h.confirms).toHaveLength(0);
    expect(h.progress).toHaveLength(0);
    expect(h.fetches).toHaveLength(0);
    expect(h.alerts).toHaveLength(0);
  });

  it('refreshes the form when the dialog closes', async () => {
    const h = harness('resolve');
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    expect(f.refreshes.count).toBe(0); // not before the dialog closes
    await flush();
    expect(f.refreshes.count).toBe(1);
  });

  it('still refreshes the form when the dialog promise rejects (cancelled or errored)', async () => {
    const h = harness('reject');
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    await flush();
    expect(f.refreshes.count).toBe(1);
    expect(h.alerts).toHaveLength(0);
  });

  it('tells the person when the dialog cannot be launched, and refreshes nothing', async () => {
    const h = harness('throw');
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    await flush();
    expect(h.alerts).toHaveLength(1);
    expect(f.refreshes.count).toBe(0);
  });

  it('refuses an unsaved record before opening anything', async () => {
    const h = harness();
    const f = form('sprk_matter', null);
    h.push.pushUpdates(f.primaryControl);
    await flush();
    expect(h.pages).toHaveLength(0);
    expect(h.alerts).toHaveLength(1);
  });

  it('refuses a missing form context before opening anything', async () => {
    const h = harness();
    h.push.pushUpdates(undefined);
    await flush();
    expect(h.pages).toHaveLength(0);
    expect(h.alerts).toHaveLength(1);
  });

  it('keeps the enable rule: hasSourceProfile is still exported', () => {
    const h = harness();
    expect(typeof h.push.hasSourceProfile).toBe('function');
  });

  it('pushUpdates no longer references the push endpoint (source scan)', () => {
    const start = SCRIPT.indexOf('ns.pushUpdates = function');
    expect(start).toBeGreaterThan(-1);
    const end = SCRIPT.indexOf('// LEGACY direct-push helpers', start);
    expect(end).toBeGreaterThan(start);
    const body = SCRIPT.slice(start, end);
    expect(body).not.toMatch(
      /field-mappings\/push|PUSH_ENDPOINT_PATH|fetch\(|openConfirmDialog|executePushForAllTargets/
    );
  });
});
