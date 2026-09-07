/**
 * Core-ancestor derivation tests — FR-26 (unified-access-control-r2 task 050).
 *
 * These tests guard an ACCESS CONTROL invariant, not a formatting one. The
 * stamp this code writes is the only thing that lets the evaluator answer
 * "can this principal see this child record?" in one hop; get the derivation
 * wrong and records are either silently hidden (under-grant) or silently
 * shared (over-grant).
 *
 * Two rules are pinned here specifically because they are easy to invert:
 *
 *   - Matter does NOT inherit from Project. Both are CORE. Selecting a core
 *     target stamps only that target.
 *   - Derivation is exactly ONE hop (ADR-034). It reads the target's own
 *     core-ancestor lookups and stops.
 *
 * Test doctrine: no `fetch`/XHR mocking of the Dataverse data plane — all data
 * I/O goes through the injected `IPolymorphicWebApi` shim (ADR-038). The one
 * `fetch` stub here is for the METADATA endpoint, which the shared
 * `discoverNavProps` reaches directly by design and which has no shim.
 *
 * @see projects/unified-access-control-r2/spec.md FR-26
 * @see projects/unified-access-control-r2/design.md §4.3
 * @see projects/unified-access-control-r2/notes/phase3-derivation-rules.md
 */

import {
  CORE_RECORD_ENTITIES,
  CHILD_RECORD_ENTITIES,
  CORE_ANCESTOR_LOOKUPS,
  isCoreRecordEntity,
  isChildRecordEntity,
  deriveCoreAncestorStamps,
  buildRegardingSelectionPayload,
  _resetNavPropCacheForTests,
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../PolymorphicResolverService';
import type { INavPropEntry, IPolymorphicWebApi, IRegardingTargetDescriptor } from '../PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const MATTER_ID_2 = '22222222-2222-2222-2222-222222222222';
const COMM_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const COMM_ID_2 = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const PROJECT_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const SR_ID = 'dddddddd-dddd-dddd-dddd-dddddddddddd';

/** Nav-props for the HOST being written (a `sprk_todo`-shaped child). */
function hostTodoNavProps(): INavPropEntry[] {
  return [
    { columnName: 'sprk_regardingmatter', navPropName: 'sprk_RegardingMatter', referencedEntity: 'sprk_matter' },
    { columnName: 'sprk_regardingproject', navPropName: 'sprk_RegardingProject', referencedEntity: 'sprk_project' },
    {
      columnName: 'sprk_regardingworkassignment',
      navPropName: 'sprk_RegardingWorkAssignment',
      referencedEntity: 'sprk_workassignment',
    },
    {
      columnName: 'sprk_regardingcommunication',
      navPropName: 'sprk_RegardingCommunication',
      referencedEntity: 'sprk_communication',
    },
    { columnName: 'sprk_regardingevent', navPropName: 'sprk_RegardingEvent', referencedEntity: 'sprk_event' },
    {
      columnName: 'sprk_regardingrecordtype',
      navPropName: 'sprk_RegardingRecordType',
      referencedEntity: 'sprk_recordtype_ref',
    },
  ];
}

/** The minimal catalog a host offers as regarding targets. */
const CATALOG: ReadonlyArray<IRegardingTargetDescriptor> = [
  {
    entityType: 'sprk_matter',
    entitySet: 'sprk_matters',
    lookupAttribute: 'sprk_regardingmatter',
    navPropHint: 'matter',
  },
  {
    entityType: 'sprk_project',
    entitySet: 'sprk_projects',
    lookupAttribute: 'sprk_regardingproject',
    navPropHint: 'project',
  },
  {
    entityType: 'sprk_communication',
    entitySet: 'sprk_communications',
    lookupAttribute: 'sprk_regardingcommunication',
    navPropHint: 'communication',
  },
  { entityType: 'sprk_event', entitySet: 'sprk_events', lookupAttribute: 'sprk_regardingevent', navPropHint: 'event' },
];

/**
 * Metadata (`EntityDefinitions/ManyToOneRelationships`) stub. Declares which
 * core-ancestor lookup columns each TARGET entity carries — the presence oracle
 * derivation uses so it never `$select`s a column that does not exist.
 */
function metadataFetchStub(columnsByEntity: Record<string, string[]>, opts: { failFor?: string[] } = {}): typeof fetch {
  return (async (url: string) => {
    const match = /LogicalName='([^']+)'/.exec(String(url));
    const entity = match?.[1] ?? '';
    if (opts.failFor?.includes(entity)) {
      return { ok: false, status: 500, json: async () => ({}) } as unknown as Response;
    }
    const cols = columnsByEntity[entity] ?? [];
    return {
      ok: true,
      status: 200,
      json: async () => ({
        value: cols.map(c => ({
          ReferencingAttribute: c,
          ReferencingEntityNavigationPropertyName: c.replace(/^sprk_regarding/, 'sprk_Regarding'),
          ReferencedEntity: 'unused-for-column-matching',
        })),
      }),
    } as unknown as Response;
  }) as unknown as typeof fetch;
}

/** Every core-ancestor lookup, as `sprk_communication` actually carries them. */
const COMMUNICATION_CORE_COLUMNS = [
  'sprk_regardingmatter',
  'sprk_regardingproject',
  'sprk_regardingworkassignment',
  'sprk_regardingservicerequest',
];

/**
 * WebApi shim. Routes by entity + `$select` shape:
 *   - `sprk_recordtype_ref` → a catalog row with NO record-number / display-name
 *     mapping, so `applyResolverFields` performs no extra target read.
 *   - anything else with `_..._value` in `$select` → the ancestor row.
 */
function buildWebApi(ancestorRow: Record<string, unknown> | null, opts: { throwOnAncestorRead?: boolean } = {}) {
  const calls: Array<{ entity: string; query: string }> = [];
  const webApi: IPolymorphicWebApi = {
    retrieveMultipleRecords: jest.fn(async (entity: string, query: string) => {
      calls.push({ entity, query });
      if (entity === 'sprk_recordtype_ref') {
        return {
          entities: [
            {
              sprk_recordtype_refid: 'rt-0001',
              sprk_recorddisplayname: 'Test Record Type',
              sprk_regardingrecordnumberfield: null,
              sprk_recorddisplaynamefield: null,
            },
          ],
        };
      }
      if (query.includes('_value')) {
        if (opts.throwOnAncestorRead) throw new Error('Dataverse 503');
        return { entities: ancestorRow ? [ancestorRow] : [] };
      }
      return { entities: [] };
    }),
  };
  return { webApi, calls };
}

beforeEach(() => {
  _resetNavPropCacheForTests();
  _resetRecordNumberFieldCacheForTests();
  _resetDisplayNameFieldCacheForTests();
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Taxonomy — pinned literally (POML step 5, final criterion)
// ---------------------------------------------------------------------------

describe('FR-26 taxonomy', () => {
  it('pinsTheCoreRecordSetLiterally', () => {
    // Changing this set changes who can see what. It must fail a test, loudly.
    expect([...CORE_RECORD_ENTITIES]).toEqual([
      'sprk_project',
      'sprk_matter',
      'sprk_workassignment',
      'sprk_servicerequest',
    ]);
  });

  it('pinsTheChildRecordSetLiterally', () => {
    expect([...CHILD_RECORD_ENTITIES]).toEqual([
      'sprk_invoice',
      'sprk_communication',
      'sprk_document',
      'sprk_event',
      'sprk_todo',
      'sprk_analysis',
    ]);
  });

  it('keepsCoreAndChildSetsDisjoint', () => {
    const overlap = CORE_RECORD_ENTITIES.filter(e => CHILD_RECORD_ENTITIES.includes(e));
    expect(overlap).toEqual([]);
  });

  it('hasOneAncestorLookupPerCoreEntity', () => {
    // The derivation table and the taxonomy must not drift apart — the
    // 'core-target' branch fails closed if they do.
    expect(CORE_ANCESTOR_LOOKUPS.map(c => c.entityType).sort()).toEqual([...CORE_RECORD_ENTITIES].sort());
  });

  it('classifiesMatterAsCoreNotChild', () => {
    // Matter is CORE. If this ever flips, every Project holder silently gains
    // every Matter beneath it.
    expect(isCoreRecordEntity('sprk_matter')).toBe(true);
    expect(isChildRecordEntity('sprk_matter')).toBe(false);
  });

  it('leavesNonAccessConferringTargetsUnclassified', () => {
    for (const e of ['sprk_budget', 'sprk_organization', 'contact', 'sprk_reportcard']) {
      expect(isCoreRecordEntity(e)).toBe(false);
      expect(isChildRecordEntity(e)).toBe(false);
    }
  });
});

// ---------------------------------------------------------------------------
// deriveCoreAncestorStamps
// ---------------------------------------------------------------------------

describe('deriveCoreAncestorStamps', () => {
  it('returnsTheTargetItselfForACoreTargetWithoutAnyRead', async () => {
    const { webApi, calls } = buildWebApi(null);
    const result = await deriveCoreAncestorStamps(webApi, 'sprk_matter', MATTER_ID, metadataFetchStub({}));

    expect(result.status).toBe('core-target');
    expect(result.stamps).toEqual([
      {
        entityType: 'sprk_matter',
        entitySet: 'sprk_matters',
        lookupAttribute: 'sprk_regardingmatter',
        recordId: MATTER_ID,
      },
    ]);
    // A core target is terminal — no hop is taken at all.
    expect(calls).toHaveLength(0);
  });

  it('doesNotStampAMattersOwnProject', async () => {
    // Matter does NOT inherit from Project (design.md §4.3). Even if the matter
    // row carried a project association, derivation must not read it.
    const { webApi } = buildWebApi({ _sprk_regardingproject_value: PROJECT_ID });
    const result = await deriveCoreAncestorStamps(webApi, 'sprk_matter', MATTER_ID, metadataFetchStub({}));

    expect(result.stamps.map(s => s.entityType)).toEqual(['sprk_matter']);
    expect(result.stamps.some(s => s.entityType === 'sprk_project')).toBe(false);
  });

  it('derivesTheMatterAncestorFromAChildTarget', async () => {
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    const result = await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      COMM_ID,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.status).toBe('derived');
    expect(result.stamps).toEqual([
      {
        entityType: 'sprk_matter',
        entitySet: 'sprk_matters',
        lookupAttribute: 'sprk_regardingmatter',
        recordId: MATTER_ID,
      },
    ]);
  });

  it('selectsOnlyTheAncestorColumnsThatExistOnTheTarget', async () => {
    // sprk_event carries no service-request lookup. Selecting it would 400 and
    // turn a schema gap into a blocked save.
    const { webApi, calls } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    await deriveCoreAncestorStamps(
      webApi,
      'sprk_event',
      COMM_ID,
      metadataFetchStub({ sprk_event: ['sprk_regardingmatter', 'sprk_regardingproject'] })
    );

    const ancestorCall = calls.find(c => c.query.includes('_value'));
    expect(ancestorCall?.query).toContain('_sprk_regardingmatter_value');
    expect(ancestorCall?.query).toContain('_sprk_regardingproject_value');
    expect(ancestorCall?.query).not.toContain('servicerequest');
  });

  it('takesExactlyOneHopAndNeverWalksTheGrandparentChain', async () => {
    // The target communication carries a matter stamp. Derivation must read the
    // communication ONCE and stop — never follow the matter (ADR-034 1-hop).
    const { webApi, calls } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      COMM_ID,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    const ancestorReads = calls.filter(c => c.query.includes('_value'));
    expect(ancestorReads).toHaveLength(1);
    expect(ancestorReads[0].entity).toBe('sprk_communication');
  });

  it('reportsNoAncestorAsADistinctStateWhenEveryCoreLookupIsNull', async () => {
    const { webApi } = buildWebApi({
      _sprk_regardingmatter_value: null,
      _sprk_regardingproject_value: null,
    });
    const result = await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      COMM_ID,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    // 'no-ancestor' must NOT collapse into 'error' — an orphan communication is
    // a legitimate record; it simply confers nothing.
    expect(result.status).toBe('no-ancestor');
    expect(result.stamps).toEqual([]);
    expect(result.error).toBeUndefined();
  });

  it('reportsUnclassifiedForTargetsThatAreNeitherCoreNorChild', async () => {
    const { webApi, calls } = buildWebApi(null);
    const result = await deriveCoreAncestorStamps(webApi, 'sprk_organization', PROJECT_ID, metadataFetchStub({}));

    expect(result.status).toBe('unclassified');
    expect(result.stamps).toEqual([]);
    expect(calls).toHaveLength(0);
  });

  it('failsClosedWhenTheAncestorReadThrows', async () => {
    const { webApi } = buildWebApi(null, { throwOnAncestorRead: true });
    const result = await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      COMM_ID,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.status).toBe('error');
    expect(result.stamps).toEqual([]);
    expect(result.error).toContain('Dataverse 503');
  });

  it('failsClosedWhenTheTargetRowIsUnreadable', async () => {
    const { webApi } = buildWebApi(null);
    const result = await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      COMM_ID,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.status).toBe('error');
  });

  it('failsClosedWhenTargetMetadataCannotBeDiscovered', async () => {
    // "no core-ancestor columns" and "could not read the metadata" are
    // indistinguishable from an empty nav-prop list, so we must not guess the
    // optimistic branch.
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    const result = await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      COMM_ID,
      metadataFetchStub({}, { failFor: ['sprk_communication'] })
    );

    expect(result.status).toBe('error');
  });

  it('normalizesBracedAncestorGuids', async () => {
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: `{${MATTER_ID.toUpperCase()}}` });
    const result = await deriveCoreAncestorStamps(
      webApi,
      'sprk_communication',
      `{${COMM_ID.toUpperCase()}}`,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.stamps[0].recordId).toBe(MATTER_ID);
  });
});

// ---------------------------------------------------------------------------
// buildRegardingSelectionPayload — the ordering-safe assembly
// ---------------------------------------------------------------------------

describe('buildRegardingSelectionPayload', () => {
  const target = (entityType: string): IRegardingTargetDescriptor => CATALOG.find(c => c.entityType === entityType)!;

  it('stampsTheDerivedMatterOnAChildOfAChild', async () => {
    // FR-26 acceptance, verbatim: a To Do regarding a Communication regarding a
    // Matter must carry sprk_regardingmatter in the SAME write.
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_communication'),
      COMM_ID,
      'Thread with client',
      undefined,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.success).toBe(true);
    expect(result.ancestor.status).toBe('derived');
    expect(result.payload!['sprk_RegardingCommunication@odata.bind']).toBe(`/sprk_communications(${COMM_ID})`);
    expect(result.payload!['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID})`);
  });

  it('appliesTheStampAfterThePreClearSoItIsNotNulledByTheSamePayload', async () => {
    // The ordering bug this function exists to make impossible: pre-clear nulls
    // sprk_regardingmatter, then the stamp must overwrite that null.
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_communication'),
      COMM_ID,
      'Thread',
      undefined,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.payload!['sprk_RegardingMatter@odata.bind']).not.toBeNull();
  });

  it('stampsOnlyTheTargetItselfForACoreTarget', async () => {
    const { webApi } = buildWebApi(null);
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_matter'),
      MATTER_ID,
      'Acme v. Widget Co.',
      undefined,
      metadataFetchStub({})
    );

    expect(result.ancestor.status).toBe('core-target');
    expect(result.payload!['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID})`);
    // Matter does not inherit from Project — the project lookup stays cleared.
    expect(result.payload!['sprk_RegardingProject@odata.bind']).toBeNull();
  });

  it('leavesExactlyTheNewAncestorAfterAReparent', async () => {
    // A → B reparent. A's ancestor (M1) must be gone, B's (M2) must be present.
    const fetchStub = metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS });

    const first = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    const payloadA = (
      await buildRegardingSelectionPayload(
        first.webApi,
        hostTodoNavProps(),
        CATALOG,
        target('sprk_communication'),
        COMM_ID,
        'Thread A',
        undefined,
        fetchStub
      )
    ).payload!;
    expect(payloadA['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID})`);

    const second = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID_2 });
    const payloadB = (
      await buildRegardingSelectionPayload(
        second.webApi,
        hostTodoNavProps(),
        CATALOG,
        target('sprk_communication'),
        COMM_ID_2,
        'Thread B',
        undefined,
        fetchStub
      )
    ).payload!;

    expect(payloadB['sprk_RegardingCommunication@odata.bind']).toBe(`/sprk_communications(${COMM_ID_2})`);
    expect(payloadB['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_2})`);
    expect(payloadB['sprk_RegardingMatter@odata.bind']).not.toBe(`/sprk_matters(${MATTER_ID})`);
  });

  it('clearsAStaleAncestorWhenTheNewTargetHasNone', async () => {
    // Reparenting onto an orphan must not leave the OLD ancestor behind — that
    // would keep the child visible to principals who no longer have a path.
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: null });
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_communication'),
      COMM_ID,
      'Orphan thread',
      undefined,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.ancestor.status).toBe('no-ancestor');
    expect(result.payload!['sprk_RegardingMatter@odata.bind']).toBeNull();
  });

  it('refusesToProduceAPayloadWhenDerivationFails', async () => {
    const { webApi } = buildWebApi(null, { throwOnAncestorRead: true });
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_communication'),
      COMM_ID,
      'Thread',
      undefined,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.success).toBe(false);
    expect(result.payload).toBeUndefined();
    expect(result.error).toBeTruthy();
  });

  it('surfacesAnAncestorTheHostCannotStampInsteadOfSwallowingIt', async () => {
    // The host (a sprk_todo) has no sprk_regardingservicerequest column, so a
    // service-request ancestor cannot be stamped. That is a real hole in child
    // inheritance and must be reported, not silently dropped.
    const { webApi } = buildWebApi({ _sprk_regardingservicerequest_value: SR_ID });
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_communication'),
      COMM_ID,
      'Thread',
      undefined,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    expect(result.success).toBe(true);
    expect(result.ancestor.status).toBe('derived');
    expect(result.unstampable).toEqual(['sprk_regardingservicerequest']);
    expect(result.payload!['sprk_RegardingServiceRequest@odata.bind']).toBeUndefined();
  });

  it('neverWritesALookupTheHostDoesNotHave', async () => {
    // Writing an absent property makes Dataverse reject the whole update.
    const { webApi } = buildWebApi({ _sprk_regardingmatter_value: MATTER_ID });
    const result = await buildRegardingSelectionPayload(
      webApi,
      hostTodoNavProps(),
      CATALOG,
      target('sprk_communication'),
      COMM_ID,
      'Thread',
      undefined,
      metadataFetchStub({ sprk_communication: COMMUNICATION_CORE_COLUMNS })
    );

    const hostNavPropNames = new Set(hostTodoNavProps().map(n => n.navPropName));
    for (const key of Object.keys(result.payload!)) {
      if (!key.endsWith('@odata.bind')) continue;
      expect(hostNavPropNames.has(key.replace('@odata.bind', ''))).toBe(true);
    }
  });
});
