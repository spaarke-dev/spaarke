/**
 * unified-access-control-r2 task 075 — the TypeScript half of the SHARED decision table, plus the
 * client resolver's behaviour.
 *
 * ## Why the fixture
 *
 * The record-aware container decision exists twice: here and in C#
 * (`Infrastructure/Dataverse/SecureContainerDecision.cs`). Two implementations exist because INV-7
 * keeps business-unit resolution client-side while server-side email ingest has no client at all.
 * Two implementations of an ISOLATION rule is a known failure mode, so the rule is pinned in one
 * machine-readable place — `tests/fixtures/secure-container-decision-table.json` — and BOTH halves'
 * suites drive their own pure decision function against that same file.
 *
 * Change this half's behaviour and this test fails. Edit the fixture to suit this half and the C#
 * suite (`SecureContainerDecisionTableTests`) fails. Add a case and both halves must implement it.
 *
 * The vacuous-pass guards matter as much as the cases: a fixture-driven suite that silently stops
 * finding its fixture, or iterates zero rows, passes green while verifying nothing.
 */

import * as fs from 'fs';
import * as path from 'path';

import {
  decideContainer,
  isSecurableEntity,
  resolveContainerForRecord,
  SecureContainerUnresolvedError,
  __resetSecurableEntityCache,
  type IEntityMetadataProbe,
} from '../RecordContainerResolver';
import type { IWebApiLike } from '../../types/WebApiLike';

// ---------------------------------------------------------------------------------------------
// Fixture loading — repo-root walk, mirroring the C# half's ResolveRepoRoot
// ---------------------------------------------------------------------------------------------

interface DecisionCase {
  name: string;
  isSecure: boolean;
  ownContainerId?: string | null;
  fallbackContainerId?: string | null;
  expect: { outcome: string; containerId?: string | null };
  why: string;
}

interface DecisionTable {
  caseCount: number;
  cases: DecisionCase[];
}

function resolveRepoRoot(): string {
  let dir = __dirname;

  // Throws rather than falling back: a wrong root would make the fixture load fail in a way that
  // could be mistaken for an absent file.
  for (let i = 0; i < 15; i++) {
    if (fs.existsSync(path.join(dir, 'src')) && fs.existsSync(path.join(dir, 'tests'))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }

  throw new Error(`Could not locate the repo root walking up from '${__dirname}'.`);
}

function loadTable(): DecisionTable {
  const fixture = path.join(resolveRepoRoot(), 'tests', 'fixtures', 'secure-container-decision-table.json');

  if (!fs.existsSync(fixture)) {
    throw new Error(
      `The SHARED decision table must be reachable from this suite, otherwise it verifies nothing ` +
        `and the C# half is unpinned. Looked for '${fixture}'.`
    );
  }

  const table = JSON.parse(fs.readFileSync(fixture, 'utf8')) as DecisionTable;

  if (!table.cases?.length) {
    throw new Error('An empty decision table pins nothing.');
  }

  return table;
}

// ---------------------------------------------------------------------------------------------

const OWN_CONTAINER = 'b!secure-own-container-0000000000';
const SHARED_BU_CONTAINER = 'b!shared-bu-container-000000000000';
const SECURE_ENTITY = 'sprk_project';
const NON_SECURABLE_ENTITY = 'sprk_invoice';
const RECORD_ID = '11111111-1111-1111-1111-111111111111';

function probe(securable: string[]): IEntityMetadataProbe {
  return {
    retrieveEntityMetadata: jest.fn(async (entityName: string) => ({
      attributes: securable.includes(entityName.toLowerCase())
        ? { sprk_issecure: {}, sprk_containerid: {} }
        : { sprk_name: {} },
    })),
  };
}

function webApiReturning(record: Record<string, unknown> | null): IWebApiLike {
  return {
    retrieveRecord: jest.fn(async () => record as Record<string, unknown>),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
  };
}

beforeEach(() => {
  __resetSecurableEntityCache();
});

// =============================================================================================
// THE SHARED DECISION TABLE
// =============================================================================================

describe('decideContainer — driven by the shared decision table', () => {
  const table = loadTable();
  const exercised: string[] = [];

  it.each(table.cases.map(c => [c.name, c] as const))('%s', (_name, testCase) => {
    const actual = decideContainer({
      isSecure: testCase.isSecure,
      ownContainerId: testCase.ownContainerId,
      fallbackContainerId: testCase.fallbackContainerId,
    });

    expect(actual.outcome).toBe(testCase.expect.outcome);

    if (actual.outcome === 'resolved-secure' || actual.outcome === 'resolved-fallback') {
      expect(actual.containerId).toBe(testCase.expect.containerId);
    } else {
      // A non-resolved decision must not carry a container id, or a caller that reads it without
      // checking the outcome would use it.
      expect((actual as { containerId?: string }).containerId).toBeUndefined();
    }

    exercised.push(testCase.name);
  });

  it('exercised every case the fixture declares (vacuous-pass guard)', () => {
    expect(exercised).toHaveLength(table.caseCount);
    expect(new Set(exercised).size).toBe(exercised.length);
  });

  it('the table covers fail-closed WITH a usable fallback available', () => {
    // The distinguishing detail. "Fail closed when there is nothing to fall back to" is a much
    // weaker claim and easy to satisfy by accident; the actual requirement is that an AVAILABLE
    // shared container is deliberately not used.
    const withFallback = table.cases.filter(
      c =>
        c.isSecure &&
        c.expect.outcome === 'fail-closed' &&
        typeof c.fallbackContainerId === 'string' &&
        c.fallbackContainerId.trim() !== ''
    );

    expect(withFallback.length).toBeGreaterThan(0);
  });

  it("'unresolved' is unreachable for a secure record", () => {
    // The load-bearing invariant: 'unresolved' is the benign config-absence outcome that callers
    // may skip quietly on. A secure record must never reach it.
    for (const c of table.cases.filter(x => x.isSecure)) {
      expect(c.expect.outcome).not.toBe('unresolved');
    }

    for (const own of [null, undefined, '', '   ', '\t']) {
      for (const fallback of [null, undefined, '', '   ', SHARED_BU_CONTAINER]) {
        expect(decideContainer({ isSecure: true, ownContainerId: own, fallbackContainerId: fallback }).outcome).toBe(
          'fail-closed'
        );
      }
    }
  });
});

// =============================================================================================
// THE CLIENT RESOLVER
// =============================================================================================

describe('resolveContainerForRecord', () => {
  it('resolves a secure record to its OWN container, not the shared fallback', async () => {
    const result = await resolveContainerForRecord({
      webApi: webApiReturning({ sprk_issecure: true, sprk_containerid: OWN_CONTAINER }),
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    expect(result.containerId).toBe(OWN_CONTAINER);
    expect(result.containerId).not.toBe(SHARED_BU_CONTAINER);
    expect(result.source).toBe('secure-record-own-container');
  });

  it('FAILS CLOSED for a secure record with no container, even though a fallback is available', async () => {
    // The most important assertion. A usable fallback is deliberately supplied: the failure mode
    // being prevented is not "no container available" but "a shared container WAS available and got
    // used silently, and the upload succeeded".
    const act = resolveContainerForRecord({
      webApi: webApiReturning({ sprk_issecure: true, sprk_containerid: null }),
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    await expect(act).rejects.toThrow(SecureContainerUnresolvedError);
    await expect(act).rejects.toMatchObject({
      code: 'secure_record_container_missing',
      fallbackWasAvailable: true,
    });
  });

  it.each([null, undefined, '', '   ', '\t'])(
    'fails closed for a secure record whose container is %p',
    async containerId => {
      await expect(
        resolveContainerForRecord({
          webApi: webApiReturning({ sprk_issecure: true, sprk_containerid: containerId }),
          metadataProbe: probe([SECURE_ENTITY]),
          entityLogicalName: SECURE_ENTITY,
          recordId: RECORD_ID,
          fallbackContainerId: SHARED_BU_CONTAINER,
        })
      ).rejects.toThrow(SecureContainerUnresolvedError);
    }
  );

  it('leaves NON-secure resolution on the business-unit cascade, unchanged', async () => {
    const result = await resolveContainerForRecord({
      webApi: webApiReturning({ sprk_issecure: false, sprk_containerid: null }),
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    expect(result.containerId).toBe(SHARED_BU_CONTAINER);
    expect(result.source).toBe('non-secure-fallback');
  });

  it("ignores a non-secure record's own stamped container", async () => {
    // Three live projects carry the ROOT business unit's container id because the creation wizard's
    // BU cascade writes this column (task 076 removes that). Reading a non-secure record's stamp
    // would silently redirect content for any record carrying a stale one.
    const result = await resolveContainerForRecord({
      webApi: webApiReturning({
        sprk_issecure: false,
        sprk_containerid: 'b!some-stale-stamp-00000000000000',
      }),
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    expect(result.containerId).toBe(SHARED_BU_CONTAINER);
  });

  it('never reads the record for a non-securable entity', async () => {
    // Correctness and cost: an entity that cannot carry sprk_issecure cannot be secure, and no
    // Dataverse round trip should be spent proving it.
    const webApi = webApiReturning(null);

    const result = await resolveContainerForRecord({
      webApi,
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: NON_SECURABLE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    expect(result.source).toBe('non-secure-fallback');
    expect(webApi.retrieveRecord).not.toHaveBeenCalled();
  });

  it("reports 'unresolved' only for a non-secure record with no fallback", async () => {
    const result = await resolveContainerForRecord({
      webApi: webApiReturning({ sprk_issecure: false }),
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: null,
    });

    expect(result.source).toBe('unresolved');
    expect(result.containerId).toBeUndefined();

    // Same inputs on a secure record must throw rather than reach the quiet-skip path.
    await expect(
      resolveContainerForRecord({
        webApi: webApiReturning({ sprk_issecure: true }),
        metadataProbe: probe([SECURE_ENTITY]),
        entityLogicalName: SECURE_ENTITY,
        recordId: RECORD_ID,
        fallbackContainerId: null,
      })
    ).rejects.toThrow(SecureContainerUnresolvedError);
  });

  it.each([null, undefined])(
    'FAILS CLOSED when the record read resolves to %p (C-3: parity with the C# half)',
    async empty => {
      // C-3 regression. `IWebApiLike.retrieveRecord` is typed non-nullable and satisfied STRUCTURALLY, so
      // TypeScript warns at no call site if an implementation resolves null/undefined — and the shipped
      // adapters are not the only implementations (PCF context.webAPI, host shims, mocks). Without the
      // guard, `record?.[flag] === true` is false for both, routing content to the BU fallback container
      // while the C# resolver throws container_record_not_found on the identical condition. A fail-OPEN
      // client next to a fail-CLOSED server is the worst of the two.
      await expect(
        resolveContainerForRecord({
          webApi: webApiReturning(empty as unknown as Record<string, unknown>),
          metadataProbe: probe([SECURE_ENTITY]),
          entityLogicalName: SECURE_ENTITY,
          recordId: RECORD_ID,
          fallbackContainerId: SHARED_BU_CONTAINER,
        })
      ).rejects.toThrow(SecureContainerUnresolvedError);
    }
  );

  it('treats an EMPTY record object as not-secure but records no container (no silent secure bypass)', async () => {
    // An empty object is a real read that returned no columns — distinct from null. It cannot assert
    // security, so it must not resolve to a secure container; the BU fallback is the honest answer and is
    // what the pre-existing non-secure path already does.
    const result = await resolveContainerForRecord({
      webApi: webApiReturning({}),
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: RECORD_ID,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    expect(result.source).toBe('non-secure-fallback');
    expect(result.containerId).toBe(SHARED_BU_CONTAINER);
  });

  it('propagates a metadata failure rather than defaulting to not-secure', async () => {
    // The subtle version of the same bug: an unavailable metadata probe read as "not securable"
    // would resolve every record to the shared fallback — the identical isolation failure, with an
    // extra step and no log line saying so.
    const failing: IEntityMetadataProbe = {
      retrieveEntityMetadata: jest.fn(async () => {
        throw new Error('metadata unavailable');
      }),
    };

    await expect(
      resolveContainerForRecord({
        webApi: webApiReturning({ sprk_issecure: false }),
        metadataProbe: failing,
        entityLogicalName: SECURE_ENTITY,
        recordId: RECORD_ID,
        fallbackContainerId: SHARED_BU_CONTAINER,
      })
    ).rejects.toThrow('metadata unavailable');
  });

  it('propagates a record-read failure rather than defaulting to not-secure', async () => {
    const webApi: IWebApiLike = {
      retrieveRecord: jest.fn(async () => {
        throw new Error('Dataverse timed out');
      }),
      retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    };

    await expect(
      resolveContainerForRecord({
        webApi,
        metadataProbe: probe([SECURE_ENTITY]),
        entityLogicalName: SECURE_ENTITY,
        recordId: RECORD_ID,
        fallbackContainerId: SHARED_BU_CONTAINER,
      })
    ).rejects.toThrow('Dataverse timed out');
  });

  it('refuses a securable entity with a blank record id rather than falling back', async () => {
    await expect(
      resolveContainerForRecord({
        webApi: webApiReturning(null),
        metadataProbe: probe([SECURE_ENTITY]),
        entityLogicalName: SECURE_ENTITY,
        recordId: '  ',
        fallbackContainerId: SHARED_BU_CONTAINER,
      })
    ).rejects.toThrow(SecureContainerUnresolvedError);
  });

  it('strips braces from a record id before reading', async () => {
    const webApi = webApiReturning({ sprk_issecure: true, sprk_containerid: OWN_CONTAINER });

    await resolveContainerForRecord({
      webApi,
      metadataProbe: probe([SECURE_ENTITY]),
      entityLogicalName: SECURE_ENTITY,
      recordId: `{${RECORD_ID}}`,
      fallbackContainerId: SHARED_BU_CONTAINER,
    });

    expect(webApi.retrieveRecord).toHaveBeenCalledWith(
      SECURE_ENTITY,
      RECORD_ID,
      expect.stringContaining('sprk_issecure')
    );
  });
});

describe('isSecurableEntity', () => {
  it('derives securability from live metadata rather than a hard-coded list', async () => {
    const p = probe([SECURE_ENTITY, 'sprk_matter', 'sprk_workassignment']);

    await expect(isSecurableEntity(p, SECURE_ENTITY)).resolves.toBe(true);
    await expect(isSecurableEntity(p, 'sprk_matter')).resolves.toBe(true);
    await expect(isSecurableEntity(p, NON_SECURABLE_ENTITY)).resolves.toBe(false);
  });

  it('picks up a FOURTH securable entity with no code change', async () => {
    // The reason the list is not a constant: a new securable entity must not silently resolve
    // through the shared fallback, which SPE's additive-only permissions would make irreversible.
    await expect(isSecurableEntity(probe(['sprk_somethingnew']), 'sprk_somethingnew')).resolves.toBe(true);
  });

  it('memoises answers but never memoises a failure', async () => {
    const failing: IEntityMetadataProbe = {
      retrieveEntityMetadata: jest.fn(async () => {
        throw new Error('transient');
      }),
    };

    await expect(isSecurableEntity(failing, SECURE_ENTITY)).rejects.toThrow('transient');
    await expect(isSecurableEntity(failing, SECURE_ENTITY)).rejects.toThrow('transient');

    // Two calls, not one: a cached failure would become a permanent silent "not securable".
    expect(failing.retrieveEntityMetadata).toHaveBeenCalledTimes(2);
  });
});
