/**
 * ConnectionsWriteHandler tests — the FR-21 / ADR-024 delegation contract.
 *
 * The PCF MUST NOT reimplement regarding write logic: `applyRegardingSelection`
 * must delegate the SET path to the shared `applyResolverFields`. We mock the
 * shared service and assert it is called (with the catalog-derived nav args) and
 * that the host record is persisted via `webApi.updateRecord`. The two
 * host-field writers (status advance + override-reason persist) are asserted to
 * write the right columns.
 */

// Mock the shared library BEFORE importing the handler.
jest.mock('@spaarke/ui-components', () => {
  const CATALOG = [
    {
      entityType: 'sprk_matter',
      entitySet: 'sprk_matters',
      lookupAttribute: 'sprk_regardingmatter',
      navPropHint: 'matter',
    },
    { entityType: 'contact', entitySet: 'contacts', lookupAttribute: 'sprk_regardingcontact', navPropHint: 'contact' },
  ];
  return {
    __esModule: true,
    TODO_REGARDING_CATALOG: CATALOG,
    applyResolverFields: jest.fn().mockResolvedValue({ recordNumber: 'MTR-2025-0001', displayName: 'Acme v. Beta' }),
  };
});

import { applyResolverFields } from '@spaarke/ui-components';
import {
  applyRegardingSelection,
  advanceAssociationStatus,
  persistOverrideReason,
  unlinkRegarding,
  _resetNavPropCacheForTests,
  type IResolverWriteContext,
} from '../CommunicationConnections/handlers/ConnectionsWriteHandler';

const NAV_PROPS_RESPONSE = {
  ok: true,
  json: async () => ({
    value: [
      {
        ReferencingAttribute: 'sprk_regardingmatter',
        ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
        ReferencedEntity: 'sprk_matter',
      },
      {
        ReferencingAttribute: 'sprk_regardingcontact',
        ReferencingEntityNavigationPropertyName: 'sprk_RegardingContact',
        ReferencedEntity: 'contact',
      },
    ],
  }),
};

function mkCtx(overrides?: Partial<IResolverWriteContext>): IResolverWriteContext {
  return {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    webApi: { updateRecord: jest.fn().mockResolvedValue({}) } as any,
    hostEntity: 'sprk_communication',
    hostRecordId: '22222222-2222-2222-2222-222222222222',
    ...overrides,
  };
}

beforeEach(() => {
  _resetNavPropCacheForTests();
  (applyResolverFields as jest.Mock).mockClear();
});

describe('applyRegardingSelection', () => {
  it('delegates the SET path to the shared applyResolverFields and persists via updateRecord', async () => {
    const ctx = mkCtx();
    const fetchMock = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE);

    const result = await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_matter', recordId: 'mtr-1', recordName: 'Acme v. Beta' },
      undefined,
      fetchMock as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    // The shared service is the SOLE regarding write logic (FR-21 / ADR-024).
    expect(applyResolverFields).toHaveBeenCalledTimes(1);
    const args = (applyResolverFields as jest.Mock).mock.calls[0];
    expect(args[3]).toBe('sprk_matter'); // entityType
    expect(args[4]).toBe('sprk_matters'); // entitySet
    expect(args[5]).toBe('mtr-1'); // recordId
    expect(args[7]).toBe('matter'); // navPropHint
    // Host record persisted.
    expect(ctx.webApi.updateRecord).toHaveBeenCalledWith(
      'sprk_communication',
      '22222222-2222-2222-2222-222222222222',
      expect.any(Object)
    );
    // ADDITIVE (multi-association): sibling typed lookups are NOT nulled — the
    // handler must never emit a `...@odata.bind: null` (that would discard other
    // associations; code-review C1).
    const payload = (ctx.webApi.updateRecord as jest.Mock).mock.calls[0][2];
    const nulledBinds = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
    expect(nulledBinds).toHaveLength(0);
  });

  it('files two selections additively — the second write does NOT clear the first (C1 regression guard)', async () => {
    const ctx = mkCtx();
    const fetchMock = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE);

    await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_matter', recordId: 'mtr-1', recordName: 'Acme v. Beta' },
      undefined,
      fetchMock as unknown as typeof fetch
    );
    await applyRegardingSelection(
      ctx,
      { entityType: 'contact', recordId: 'con-1', recordName: 'Jane Doe' },
      undefined,
      fetchMock as unknown as typeof fetch
    );

    // Both slots delegated to the shared SET path — neither call nulls the other.
    expect(applyResolverFields).toHaveBeenCalledTimes(2);
    const allPayloads = (ctx.webApi.updateRecord as jest.Mock).mock.calls.map(c => c[2]);
    for (const payload of allPayloads) {
      const nulledBinds = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
      expect(nulledBinds).toHaveLength(0);
    }
    // The two writes targeted the two different entity types (matter, contact).
    const targetedTypes = (applyResolverFields as jest.Mock).mock.calls.map(c => c[3]);
    expect(targetedTypes).toEqual(['sprk_matter', 'contact']);
  });

  it('rejects an unknown entity type without calling the shared service', async () => {
    const ctx = mkCtx();
    const result = await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_unknown', recordId: 'x', recordName: 'X' },
      undefined,
      jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE) as unknown as typeof fetch
    );
    expect(result.success).toBe(false);
    expect(applyResolverFields).not.toHaveBeenCalled();
  });
});

describe('advanceAssociationStatus', () => {
  it('writes sprk_associationstatus = Resolved (100000000)', async () => {
    const ctx = mkCtx();
    const res = await advanceAssociationStatus(ctx);
    expect(res.success).toBe(true);
    expect(ctx.webApi.updateRecord).toHaveBeenCalledWith('sprk_communication', expect.any(String), {
      sprk_associationstatus: 100000000,
    });
  });

  it('is a no-op (success) without a persisted host record', async () => {
    const ctx = mkCtx({ hostRecordId: undefined });
    const res = await advanceAssociationStatus(ctx);
    expect(res.success).toBe(true);
    expect(ctx.webApi.updateRecord).not.toHaveBeenCalled();
  });
});

describe('unlinkRegarding', () => {
  it('nulls exactly ONE typed lookup (the unlinked entity) and touches no sibling', async () => {
    const ctx = mkCtx();
    const fetchMock = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE);

    const res = await unlinkRegarding(ctx, 'sprk_matter', fetchMock as unknown as typeof fetch);

    expect(res.success).toBe(true);
    // Single updateRecord that nulls ONLY the matter nav-prop — additive-safe removal.
    const payload = (ctx.webApi.updateRecord as jest.Mock).mock.calls[0][2];
    expect(payload).toEqual({ 'sprk_RegardingMatter@odata.bind': null });
    // Exactly one nulled bind — never a bulk clear-and-set.
    const nulledBinds = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
    expect(nulledBinds).toHaveLength(1);
  });

  it('errors when the host entity has no regarding lookup for that type', async () => {
    const ctx = mkCtx();
    const fetchMock = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE);
    const res = await unlinkRegarding(ctx, 'sprk_project', fetchMock as unknown as typeof fetch);
    expect(res.success).toBe(false);
    expect(ctx.webApi.updateRecord).not.toHaveBeenCalled();
  });

  it('is a no-op (success) without a persisted host record', async () => {
    const ctx = mkCtx({ hostRecordId: undefined });
    const res = await unlinkRegarding(ctx, 'sprk_matter', jest.fn() as unknown as typeof fetch);
    expect(res.success).toBe(true);
    expect(ctx.webApi.updateRecord).not.toHaveBeenCalled();
  });
});

describe('persistOverrideReason', () => {
  it('mutates the decision block and rewrites sprk_associationprovenance (feedback signal only)', async () => {
    const ctx = mkCtx();
    const provenance = JSON.stringify({ decision: { status: 'Suggested' }, candidates: [] });

    const res = await persistOverrideReason(
      ctx,
      provenance,
      'sprk_regardingmatter',
      'wrong matter',
      '2026-07-15T00:00:00.000Z'
    );

    expect(res.success).toBe(true);
    const call = (ctx.webApi.updateRecord as jest.Mock).mock.calls[0];
    const written = JSON.parse(call[2].sprk_associationprovenance);
    expect(written.decision.overrideReason).toBe('wrong matter');
    expect(written.decision.overriddenField).toBe('sprk_regardingmatter');
    expect(written.decision.overriddenAt).toBe('2026-07-15T00:00:00.000Z');
    // No status write here — override capture must not advance the filing.
    expect(call[2].sprk_associationstatus).toBeUndefined();
  });

  it('fails gracefully when the provenance JSON is unparseable', async () => {
    const ctx = mkCtx();
    const res = await persistOverrideReason(ctx, '{bad', 'sprk_regardingmatter', 'x', '2026-07-15T00:00:00.000Z');
    expect(res.success).toBe(false);
    expect(ctx.webApi.updateRecord).not.toHaveBeenCalled();
  });
});
