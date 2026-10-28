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
 * missing form context is refused before any dialog opens; unsaved form edits are saved first and a failed save opens
 * nothing; the enable rule `hasSourceProfile` behaves as before (cache miss queries and writes the cache, cache hit
 * answers synchronously, no profile hides the button); and the data string the ribbon builds is accepted by the code
 * page's own parameter reader (a contract test, so renaming a key on either side fails).
 */

import * as fs from 'fs';
import * as path from 'path';
import * as vm from 'vm';
import { parseDataParams } from '../utils/parseDataParams';
import { readFamilyParams } from '../../../../../solutions/RecordFamily/src/familyParams';

const SCRIPT = fs.readFileSync(
  path.join(__dirname, '..', '..', '..', '..', 'webresources', 'js', 'sprk_fieldmapping_push.js'),
  'utf8'
);

const RECORD_ID = 'a1750000-0000-4000-8000-000000000001';
const BRACED_ID = `{${RECORD_ID.toUpperCase()}}`;
const SOURCE_RT_ID = 'c0de0000-0000-4000-8000-0000000000aa';

interface IPush {
  pushUpdates(primaryControl: unknown): void;
  hasSourceProfile(primaryControl: unknown): unknown;
}

interface IHarnessOptions {
  navigateResult?: 'resolve' | 'reject' | 'throw';
  /** Rows the fake `sprk_recordtype_ref` lookup returns. */
  recordTypeRefs?: Array<{ sprk_recordtype_refid: string }>;
  /** Rows the fake `sprk_fieldmappingprofile` query returns, or 'throw'. */
  profiles?: Array<Record<string, unknown>> | 'throw';
  /** Pre-seeded sessionStorage entries. */
  session?: Record<string, string>;
}

interface IHarness {
  push: IPush;
  pages: Array<{ page: Record<string, unknown>; options: Record<string, unknown> }>;
  alerts: Array<{ text: string }>;
  errorDialogs: Array<{ message?: string }>;
  confirms: unknown[];
  fetches: string[];
  progress: string[];
  webApiCalls: Array<{ entity: string; query: string }>;
  session: Map<string, string>;
  /** Ordered log of platform calls, for sequencing assertions. */
  events: string[];
}

function harness(options: IHarnessOptions = {}): IHarness {
  const navigateResult = options.navigateResult ?? 'resolve';
  const session = new Map<string, string>(Object.entries(options.session ?? {}));
  const h: IHarness = {
    push: undefined as unknown as IPush,
    pages: [],
    alerts: [],
    errorDialogs: [],
    confirms: [],
    fetches: [],
    progress: [],
    webApiCalls: [],
    session,
    events: [],
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
    sessionStorage: {
      getItem: (k: string) => (session.has(k) ? (session.get(k) as string) : null),
      setItem: (k: string, v: string) => void session.set(k, v),
    },
    Xrm: {
      Navigation: {
        openAlertDialog: (a: { text: string }) => {
          h.alerts.push(a);
          h.events.push('alert');
          return Promise.resolve();
        },
        openErrorDialog: (e: { message?: string }) => {
          h.errorDialogs.push(e);
          h.events.push('errorDialog');
          return Promise.resolve();
        },
        openConfirmDialog: (c: unknown) => {
          h.confirms.push(c);
          return Promise.resolve({ confirmed: true });
        },
        navigateTo: (page: Record<string, unknown>, opts: Record<string, unknown>) => {
          h.pages.push({ page, options: opts });
          h.events.push('navigateTo');
          if (navigateResult === 'throw') throw new Error('navigateTo unavailable');
          return navigateResult === 'reject' ? Promise.reject(new Error('closed')) : Promise.resolve();
        },
      },
      Utility: {
        showProgressIndicator: (m: string) => h.progress.push(m),
        closeProgressIndicator: () => undefined,
        getGlobalContext: () => ({ getClientUrl: () => 'https://org.crm.dynamics.com' }),
      },
      WebApi: {
        retrieveMultipleRecords: (entity: string, query: string) => {
          h.webApiCalls.push({ entity, query });
          if (entity === 'sprk_recordtype_ref') {
            return Promise.resolve({ entities: options.recordTypeRefs ?? [] });
          }
          if (entity === 'sprk_fieldmappingprofile') {
            if (options.profiles === 'throw') return Promise.reject(new Error('query failed'));
            return Promise.resolve({ entities: options.profiles ?? [] });
          }
          return Promise.resolve({ entities: [] });
        },
      },
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

interface IFormOptions {
  id?: string | null;
  /** `undefined` = the form API has no `getIsDirty` at all. */
  dirty?: boolean;
  save?: 'resolve' | 'reject' | 'rejectNoMessage';
}

function form(entity: string, h?: IHarness, opts: IFormOptions = {}) {
  const id = opts.id === undefined ? BRACED_ID : opts.id;
  const refreshes = { count: 0 };
  const saves = { count: 0 };
  const entityApi: Record<string, unknown> = { getEntityName: () => entity, getId: () => id };
  if (opts.dirty !== undefined) entityApi.getIsDirty = () => opts.dirty;
  return {
    refreshes,
    saves,
    primaryControl: {
      data: {
        refresh: () => {
          refreshes.count += 1;
          h?.events.push('refresh');
          return Promise.resolve();
        },
        save: () => {
          saves.count += 1;
          h?.events.push('save');
          if (opts.save === 'reject') return Promise.reject({ errorCode: 1, message: 'Required field missing' });
          if (opts.save === 'rejectNoMessage') return Promise.reject({});
          return Promise.resolve();
        },
        entity: entityApi,
      },
    },
  };
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));

const cacheKey = (entity: string, id = BRACED_ID) => `sprk_fieldmapping_hasSource_${entity}_${id}`;

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
    const h = harness({ navigateResult: 'resolve' });
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    expect(f.refreshes.count).toBe(0); // not before the dialog closes
    await flush();
    expect(f.refreshes.count).toBe(1);
  });

  it('still refreshes the form when the dialog promise rejects (cancelled or errored)', async () => {
    const h = harness({ navigateResult: 'reject' });
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    await flush();
    expect(f.refreshes.count).toBe(1);
    expect(h.alerts).toHaveLength(0);
  });

  it('tells the person when the dialog cannot be launched, and refreshes nothing', async () => {
    const h = harness({ navigateResult: 'throw' });
    const f = form('sprk_matter');
    h.push.pushUpdates(f.primaryControl);
    await flush();
    expect(h.alerts).toHaveLength(1);
    expect(f.refreshes.count).toBe(0);
  });

  it('refuses an unsaved record before opening anything', async () => {
    const h = harness();
    const f = form('sprk_matter', h, { id: null });
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

describe('sprk_fieldmapping_push.js — unsaved form edits (task 075 fix round 1, K1)', () => {
  it('saves a dirty form first, and opens the Family view only after the save completes', async () => {
    const h = harness();
    const f = form('sprk_matter', h, { dirty: true, save: 'resolve' });
    h.push.pushUpdates(f.primaryControl);
    await flush();

    expect(f.saves.count).toBe(1);
    expect(h.pages).toHaveLength(1);
    expect(h.events.slice(0, 3)).toEqual(['save', 'navigateTo', 'refresh']);
  });

  it('does not save a clean form', async () => {
    const h = harness();
    const f = form('sprk_matter', h, { dirty: false });
    h.push.pushUpdates(f.primaryControl);
    await flush();

    expect(f.saves.count).toBe(0);
    expect(h.pages).toHaveLength(1);
  });

  it('opens straight away when the form API has no getIsDirty', async () => {
    const h = harness();
    const f = form('sprk_matter', h);
    h.push.pushUpdates(f.primaryControl);
    await flush();

    expect(f.saves.count).toBe(0);
    expect(h.pages).toHaveLength(1);
  });

  it('shows the platform error and opens nothing when the save fails', async () => {
    const h = harness();
    const f = form('sprk_matter', h, { dirty: true, save: 'reject' });
    h.push.pushUpdates(f.primaryControl);
    await flush();

    expect(f.saves.count).toBe(1);
    expect(h.pages).toHaveLength(0);
    expect(h.errorDialogs).toEqual([{ message: 'Required field missing' }]);
    expect(f.refreshes.count).toBe(0);
  });

  it('shows a fixed message when the failed save carries none', async () => {
    const h = harness();
    const f = form('sprk_matter', h, { dirty: true, save: 'rejectNoMessage' });
    h.push.pushUpdates(f.primaryControl);
    await flush();

    expect(h.pages).toHaveLength(0);
    expect(h.errorDialogs).toHaveLength(1);
    expect(h.errorDialogs[0].message).toMatch(/could not be saved/i);
  });
});

describe('sprk_fieldmapping_push.js — the enable rule hasSourceProfile is unchanged (task 075, FR-35)', () => {
  it('cache miss with a profile: resolves true after the two-step lookup, and writes the cache', async () => {
    const h = harness({
      recordTypeRefs: [{ sprk_recordtype_refid: SOURCE_RT_ID }],
      profiles: [{ sprk_fieldmappingprofileid: 'p1', _sprk_targetrecordtype_value: 'rt-target' }],
    });
    const f = form('sprk_matter');

    const result = h.push.hasSourceProfile(f.primaryControl);

    expect(result).toBeInstanceOf(Promise); // an async EnableRule: a pending promise on a miss
    await expect(result).resolves.toBe(true);
    expect(h.session.get(cacheKey('sprk_matter'))).toBe('true');
    // Step 1 resolves the entity to its recordtype-ref; step 2 asks for ACTIVE profiles with it as SOURCE.
    expect(h.webApiCalls.map(c => c.entity)).toEqual(['sprk_recordtype_ref', 'sprk_fieldmappingprofile']);
    expect(h.webApiCalls[0].query).toContain("sprk_recordlogicalname eq 'sprk_matter'");
    expect(h.webApiCalls[1].query).toContain(`_sprk_sourcerecordtype_value eq ${SOURCE_RT_ID}`);
    expect(h.webApiCalls[1].query).toContain('statecode eq 0');
  });

  it('cache miss with no profile: resolves false and caches the false', async () => {
    const h = harness({ recordTypeRefs: [{ sprk_recordtype_refid: SOURCE_RT_ID }], profiles: [] });
    const result = h.push.hasSourceProfile(form('sprk_project').primaryControl);

    await expect(result).resolves.toBe(false);
    expect(h.session.get(cacheKey('sprk_project'))).toBe('false');
  });

  it('cache miss for an entity with no recordtype-ref: resolves false without querying profiles', async () => {
    const h = harness({ recordTypeRefs: [] });
    const result = h.push.hasSourceProfile(form('sprk_matter').primaryControl);

    await expect(result).resolves.toBe(false);
    expect(h.webApiCalls.map(c => c.entity)).toEqual(['sprk_recordtype_ref']);
    expect(h.session.get(cacheKey('sprk_matter'))).toBe('false');
  });

  it('a failed profile query resolves false and is NOT cached (the next call retries)', async () => {
    const h = harness({ recordTypeRefs: [{ sprk_recordtype_refid: SOURCE_RT_ID }], profiles: 'throw' });
    const result = h.push.hasSourceProfile(form('sprk_matter').primaryControl);

    await expect(result).resolves.toBe(false);
    expect(h.session.has(cacheKey('sprk_matter'))).toBe(false);
  });

  it('cache hit: returns the cached value synchronously (a boolean, not a promise) and queries nothing', () => {
    const h = harness({
      session: { [cacheKey('sprk_matter')]: 'true', [cacheKey('sprk_project')]: 'false' },
    });

    expect(h.push.hasSourceProfile(form('sprk_matter').primaryControl)).toBe(true);
    expect(h.push.hasSourceProfile(form('sprk_project').primaryControl)).toBe(false);
    expect(h.webApiCalls).toHaveLength(0);
  });

  it('is keyed per entity and form: a hit for one record does not answer for another', async () => {
    const h = harness({
      session: { [cacheKey('sprk_matter')]: 'true' },
      recordTypeRefs: [],
    });
    const other = form('sprk_matter', h, { id: '{B1750000-0000-4000-8000-000000000002}' });

    expect(h.push.hasSourceProfile(other.primaryControl)).toBeInstanceOf(Promise);
  });

  it('hides the button (false) without a form context or an entity name', () => {
    const h = harness();
    expect(h.push.hasSourceProfile(undefined)).toBe(false);
    expect(h.push.hasSourceProfile(form('').primaryControl)).toBe(false);
  });
});

describe('sprk_fieldmapping_push.js ↔ the RecordFamily page: launch data contract (task 075 fix round 1, K3)', () => {
  async function launchedData(entity: string): Promise<string> {
    const h = harness();
    h.push.pushUpdates(form(entity).primaryControl);
    await flush();
    return h.pages[0].page.data as string;
  }

  it.each(['sprk_matter', 'sprk_project'])(
    'the data string pushUpdates builds for %s is read back by the page, as the Xrm data envelope',
    async entity => {
      const data = await launchedData(entity);
      // Dataverse delivers `data` as one encoded query parameter on the web resource URL.
      const params = parseDataParams(`?data=${encodeURIComponent(data)}`);

      expect(readFamilyParams(params)).toEqual({ entityName: entity, id: RECORD_ID.toUpperCase() });
    }
  );

  it('also reads the same string as raw query parameters (local dev)', async () => {
    const data = await launchedData('sprk_matter');
    expect(readFamilyParams(parseDataParams(`?${data}`))).toEqual({
      entityName: 'sprk_matter',
      id: RECORD_ID.toUpperCase(),
    });
  });

  it('the page reader rejects what the ribbon would never send', () => {
    expect(readFamilyParams({})).toBeNull();
    expect(readFamilyParams({ entityName: 'sprk_matter' })).toBeNull();
    expect(readFamilyParams({ id: RECORD_ID })).toBeNull();
    // The old keys / a renamed key must fail loudly, not silently load nothing.
    expect(readFamilyParams({ entityType: 'sprk_matter', entityId: RECORD_ID })).toBeNull();
  });
});

describe('RecordFamily page parameter validation (task 075 fix round 1, K2)', () => {
  const GOOD_ID = RECORD_ID;

  it.each([GOOD_ID, GOOD_ID.toUpperCase(), `{${GOOD_ID}}`, `{${GOOD_ID.toUpperCase()}}`])(
    'accepts the GUID %s (braces and case allowed)',
    id => {
      expect(readFamilyParams({ entityName: 'sprk_matter', id })).toEqual({ entityName: 'sprk_matter', id });
    }
  );

  it.each(['sprk_matter', 'sprk_project', 'account', '_x1'])('accepts the table name %s', entityName => {
    expect(readFamilyParams({ entityName, id: GOOD_ID })).not.toBeNull();
  });

  it.each([
    '',
    'Sprk_Matter',
    '1sprk',
    'sprk-matter',
    'sprk matter',
    '../sprk_matter',
    'sprk_matter/x',
    "sprk_matter'",
    'sprk_matter\n',
  ])('rejects the table name %j', entityName => {
    expect(readFamilyParams({ entityName, id: GOOD_ID })).toBeNull();
  });

  it.each([
    '',
    'not-a-guid',
    GOOD_ID.slice(1),
    `${GOOD_ID}0`,
    `{${GOOD_ID}`,
    `${GOOD_ID}}`,
    `${GOOD_ID}/family`,
    `${GOOD_ID}?x=1`,
    'g1750000-0000-4000-8000-000000000001',
  ])('rejects the id %j', id => {
    expect(readFamilyParams({ entityName: 'sprk_matter', id })).toBeNull();
  });
});
