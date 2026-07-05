/**
 * Unit tests for useSprkMemoRepository.
 *
 * Approach
 * ────────
 * Notepad's devDeps do NOT include `@testing-library/react`, so we render the
 * hook using a minimalist harness built from `react-dom` + `react-dom/test-utils.act`.
 * This mirrors the pattern used elsewhere in Spaarke for hook testing when a
 * full RTL setup is intentionally out-of-scope.
 *
 * Coverage: spec FR-14 / FR-15 / FR-17 + PolymorphicResolverService integration
 * per notes/design-alignment-corrections.md.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

// React 18 concurrent-mode test environment flag — must be set BEFORE React
// is imported so its internal `isConcurrentActEnvironment` check picks it up.
// Without this, valid state updates inside `act` emit noisy warnings.
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import * as React from 'react';
import { createRoot, type Root } from 'react-dom/client';
// React 18.3+: `act` moved from `react-dom/test-utils` to the `react` package.
// Using the new location silences the deprecation warning.
import { act } from 'react';

// -- Mocks ---------------------------------------------------------------------

// Mock the resolver service so we can assert createMemo calls it correctly.
const mockApplyResolverFields = jest.fn(
  async (
    _webApi: any,
    entity: Record<string, unknown>,
    _navProps: any[],
    _parentEntity: string,
    _parentSet: string,
    parentRecordId: string,
    parentRecordName: string,
    _hint?: string
  ) => {
    // Simulate the real service: mutate `entity` in place with the
    // ADR-024 lookup + resolver fields. Tests inspect this on the create
    // payload argument.
    entity[`sprk_regardingmatter@odata.bind`] = `/sprk_matters(${parentRecordId})`;
    entity['sprk_regardingrecordid'] = parentRecordId;
    entity['sprk_regardingrecordname'] = parentRecordName;
    entity['sprk_regardingrecordurl'] =
      `/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${parentRecordId}`;
  }
);

// DEF-10 (2026-07-04) — mock targets updated to the deep-path modules used by
// the source (see useSprkMemoRepository.ts imports).
jest.mock(
  '@spaarke/ui-components/services/PolymorphicResolverService',
  () => ({
    applyResolverFields: (...args: any[]) =>
      (mockApplyResolverFields as any)(...args),
  }),
  { virtual: true }
);

// Mock the toolbarLaunchDefaults module that provides SUPPORTED_MEMO_PARENTS.
jest.mock(
  '@spaarke/ui-components/hooks/toolbarLaunchDefaults',
  () => ({
    SUPPORTED_MEMO_PARENTS: {
      sprk_matter: 'sprk_regardingmatter',
      sprk_project: 'sprk_regardingproject',
      sprk_event: 'sprk_regardingevent',
      sprk_invoice: 'sprk_regardinginvoice',
      sprk_budget: 'sprk_regardingbudget',
      sprk_workassignment: 'sprk_regardingworkassignment',
    },
  }),
  { virtual: true }
);

// Mock nav-prop discovery to return a plausible list without hitting fetch.
jest.mock('../discoverMemoNavProps', () => ({
  discoverMemoNavProps: jest.fn(async () => [
    {
      columnName: 'sprk_regardingmatter',
      navPropName: 'sprk_regardingmatter',
      referencedEntity: 'sprk_matter',
    },
    {
      columnName: 'sprk_regardingrecordtype',
      navPropName: 'sprk_regardingrecordtype',
      referencedEntity: 'sprk_recordtype_ref',
    },
  ]),
  _resetMemoNavPropCacheForTests: jest.fn(),
}));

// -- Fresh imports AFTER mocks are set up -------------------------------------

import {
  useSprkMemoRepository,
  type UseSprkMemoRepositoryResult,
  MEMO_BODY_DEBOUNCE_MS,
} from '../useSprkMemoRepository';
import { discoverMemoNavProps } from '../discoverMemoNavProps';

// -- Test helpers -------------------------------------------------------------

const CANONICAL_MATTER_ID = '11112222-3333-4444-5555-666677778888';

interface HarnessHandle {
  latest: UseSprkMemoRepositoryResult;
  unmount: () => void;
  rerender: (entity: string | null, id: string | null) => void;
}

/**
 * Render `useSprkMemoRepository` and expose the latest returned object.
 * Uses `act` to flush all queued state updates + effects.
 */
async function renderHook(
  initialEntity: string | null,
  initialId: string | null
): Promise<HarnessHandle> {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root: Root = createRoot(container);

  const handleRef: {
    latest: UseSprkMemoRepositoryResult | undefined;
    setProps?: (entity: string | null, id: string | null) => void;
  } = { latest: undefined };

  function TestComponent({
    entity,
    id,
  }: {
    entity: string | null;
    id: string | null;
  }): null {
    handleRef.latest = useSprkMemoRepository(entity, id);
    return null;
  }

  function Wrapper(): React.ReactElement {
    const [state, setState] = React.useState<{
      entity: string | null;
      id: string | null;
    }>({ entity: initialEntity, id: initialId });
    handleRef.setProps = (entity, id) => setState({ entity, id });
    return React.createElement(TestComponent, {
      entity: state.entity,
      id: state.id,
    });
  }

  await act(async () => {
    root.render(React.createElement(Wrapper));
  });
  // Flush any queued microtasks from the useEffect list-fetch chain.
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });

  return {
    get latest(): UseSprkMemoRepositoryResult {
      if (!handleRef.latest) throw new Error('Hook harness: latest is undefined');
      return handleRef.latest;
    },
    unmount: () => {
      act(() => {
        root.unmount();
      });
      document.body.removeChild(container);
    },
    rerender: (entity, id) => {
      act(() => {
        handleRef.setProps?.(entity, id);
      });
    },
  };
}

interface XrmWebApiStub {
  retrieveMultipleRecords: jest.Mock;
  retrieveRecord: jest.Mock;
  createRecord: jest.Mock;
  updateRecord: jest.Mock;
}

function installXrmStub(): XrmWebApiStub {
  const stub: XrmWebApiStub = {
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    retrieveRecord: jest.fn(async () => ({ sprk_mattername: 'Smith v Jones' })),
    createRecord: jest.fn(async () => ({ id: 'new-memo-id' })),
    updateRecord: jest.fn(async () => ({ id: 'existing-id' })),
  };
  (window as any).Xrm = { WebApi: stub };
  return stub;
}

function uninstallXrmStub(): void {
  delete (window as any).Xrm;
}

function makeMemoRaw(
  id: string,
  overrides: Partial<Record<string, unknown>> = {}
): Record<string, unknown> {
  return {
    sprk_memoid: id,
    sprk_name: `Memo ${id}`,
    sprk_memobody: 'body-' + id,
    sprk_regardingrecordid: CANONICAL_MATTER_ID,
    createdon: '2026-01-01T00:00:00Z',
    modifiedon: '2026-01-02T00:00:00Z',
    createdby: { fullname: 'Alice', systemuserid: 'user-1' },
    ...overrides,
  };
}

// -- Tests --------------------------------------------------------------------

describe('useSprkMemoRepository', () => {
  beforeEach(() => {
    jest.useRealTimers();
    mockApplyResolverFields.mockClear();
    (discoverMemoNavProps as jest.Mock).mockClear();
  });

  afterEach(() => {
    uninstallXrmStub();
  });

  // ─── Unsupported entity ──────────────────────────────────────────────────

  it('sets unsupported=true when regardingEntity is not in SUPPORTED_MEMO_PARENTS', async () => {
    const stub = installXrmStub();
    const harness = await renderHook('sprk_notinschema', CANONICAL_MATTER_ID);
    expect(harness.latest.unsupported).toBe(true);
    expect(harness.latest.memos).toEqual([]);
    expect(harness.latest.loading).toBe(false);
    // No queries should have been issued.
    expect(stub.retrieveMultipleRecords).not.toHaveBeenCalled();
    harness.unmount();
  });

  // ─── List query shape ────────────────────────────────────────────────────

  it('list query uses the correct entity-specific lookup filter for Matter', async () => {
    const stub = installXrmStub();
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    expect(stub.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    const [entityName, query] = stub.retrieveMultipleRecords.mock.calls[0];
    expect(entityName).toBe('sprk_memo');
    expect(query).toContain(
      `_sprk_regardingmatter_value eq ${CANONICAL_MATTER_ID}`
    );
    harness.unmount();
  });

  it('list query orders by createdon desc', async () => {
    const stub = installXrmStub();
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    const [, query] = stub.retrieveMultipleRecords.mock.calls[0];
    expect(query).toContain('$orderby=createdon desc');
    harness.unmount();
  });

  it('list $select includes memo fields + $expand createdby', async () => {
    const stub = installXrmStub();
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    const [, query] = stub.retrieveMultipleRecords.mock.calls[0];
    expect(query).toContain('sprk_memoid');
    expect(query).toContain('sprk_name');
    expect(query).toContain('sprk_memobody');
    expect(query).toContain('sprk_regardingrecordid');
    expect(query).toContain('createdon');
    expect(query).toContain('modifiedon');
    expect(query).toContain('$expand=createdby');
    harness.unmount();
  });

  it('flattens MemoRaw → Memo (createdby.fullname/systemuserid → {id,name})', async () => {
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    expect(harness.latest.memos).toHaveLength(1);
    expect(harness.latest.memos[0].createdby).toEqual({
      id: 'user-1',
      name: 'Alice',
    });
    expect(harness.latest.currentMemo?.sprk_memoid).toBe('m1');
    harness.unmount();
  });

  // ─── Create ──────────────────────────────────────────────────────────────

  it('createMemo calls applyResolverFields BEFORE createRecord', async () => {
    const stub = installXrmStub();
    const callOrder: string[] = [];
    mockApplyResolverFields.mockImplementationOnce(async () => {
      callOrder.push('applyResolverFields');
    });
    stub.createRecord.mockImplementationOnce(async () => {
      callOrder.push('createRecord');
      return { id: 'new-memo-id' };
    });

    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    await act(async () => {
      await harness.latest.createMemo();
    });

    expect(callOrder).toEqual(['applyResolverFields', 'createRecord']);
    harness.unmount();
  });

  it('createMemo payload includes sprk_name="Untitled" and empty sprk_memobody', async () => {
    const stub = installXrmStub();
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    await act(async () => {
      await harness.latest.createMemo();
    });
    expect(stub.createRecord).toHaveBeenCalledTimes(1);
    const [entityName, entity] = stub.createRecord.mock.calls[0];
    expect(entityName).toBe('sprk_memo');
    expect(entity.sprk_name).toBe('Untitled');
    expect(entity.sprk_memobody).toBe('');
    // applyResolverFields (mocked) should have populated the entity-specific bind + resolver id.
    expect(entity['sprk_regardingmatter@odata.bind']).toBe(
      `/sprk_matters(${CANONICAL_MATTER_ID})`
    );
    expect(entity.sprk_regardingrecordid).toBe(CANONICAL_MATTER_ID);
    harness.unmount();
  });

  it('createMemo passes parent name resolved via retrieveRecord to applyResolverFields', async () => {
    const stub = installXrmStub();
    stub.retrieveRecord.mockResolvedValueOnce({
      sprk_mattername: 'Smith v Jones',
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    await act(async () => {
      await harness.latest.createMemo();
    });
    expect(mockApplyResolverFields).toHaveBeenCalled();
    const args = mockApplyResolverFields.mock.calls[0];
    // signature: webApi, entity, navProps, parentEntity, parentSet, parentId, parentName, hint
    expect(args[3]).toBe('sprk_matter');
    expect(args[4]).toBe('sprk_matters');
    expect(args[5]).toBe(CANONICAL_MATTER_ID);
    expect(args[6]).toBe('Smith v Jones');
    harness.unmount();
  });

  it('createMemo refetches the list after create', async () => {
    const stub = installXrmStub();
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    expect(stub.retrieveMultipleRecords).toHaveBeenCalledTimes(1); // initial load
    await act(async () => {
      await harness.latest.createMemo();
    });
    expect(stub.retrieveMultipleRecords).toHaveBeenCalledTimes(2); // + refetch
    harness.unmount();
  });

  // ─── updateBody debounce ─────────────────────────────────────────────────

  it('updateBody debounces: 999ms → no write; ≥1000ms → single write', async () => {
    jest.useFakeTimers();
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);

    act(() => {
      harness.latest.updateBody('first');
      harness.latest.updateBody('second');
      harness.latest.updateBody('final');
    });

    // Not yet fired.
    act(() => {
      jest.advanceTimersByTime(999);
    });
    expect(stub.updateRecord).not.toHaveBeenCalled();

    // Cross the threshold — single write with the LATEST value.
    await act(async () => {
      jest.advanceTimersByTime(MEMO_BODY_DEBOUNCE_MS - 999);
      await Promise.resolve();
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);
    expect(stub.updateRecord).toHaveBeenCalledWith('sprk_memo', 'm1', {
      sprk_memobody: 'final',
    });

    harness.unmount();
    jest.useRealTimers();
  });

  it('updateBody({immediate:true}) writes immediately, cancelling any pending timer', async () => {
    jest.useFakeTimers();
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);

    act(() => {
      harness.latest.updateBody('typed');
    });
    // Immediate call BEFORE the debounce elapses.
    await act(async () => {
      harness.latest.updateBody('final', { immediate: true });
      await Promise.resolve();
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);
    expect(stub.updateRecord).toHaveBeenCalledWith('sprk_memo', 'm1', {
      sprk_memobody: 'final',
    });

    // Advance past the previously-set debounce; should not fire a second write.
    act(() => {
      jest.advanceTimersByTime(MEMO_BODY_DEBOUNCE_MS + 100);
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);

    harness.unmount();
    jest.useRealTimers();
  });

  // ─── updateName immediate ────────────────────────────────────────────────

  it('updateName writes sprk_name immediately (no debounce)', async () => {
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);

    await act(async () => {
      await harness.latest.updateName('New Title');
    });
    expect(stub.updateRecord).toHaveBeenCalledTimes(1);
    expect(stub.updateRecord).toHaveBeenCalledWith('sprk_memo', 'm1', {
      sprk_name: 'New Title',
    });
    harness.unmount();
  });

  // ─── Error handling ──────────────────────────────────────────────────────

  it('Xrm.WebApi.updateRecord rejection sets error state', async () => {
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1')],
    });
    stub.updateRecord.mockRejectedValueOnce(new Error('403 Forbidden'));

    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    await act(async () => {
      await harness.latest.updateName('Fails');
    });
    expect(harness.latest.error).not.toBeNull();
    expect(harness.latest.error?.message).toContain('403 Forbidden');
    harness.unmount();
  });

  it('Xrm undefined → error state + safe defaults, no crash', async () => {
    uninstallXrmStub(); // ensure Xrm is not present
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    expect(harness.latest.memos).toEqual([]);
    expect(harness.latest.error).not.toBeNull();
    expect(harness.latest.error?.message).toContain('Xrm.WebApi');
    harness.unmount();
  });

  // ─── Cleanup: unmount cancels pending debounce ───────────────────────────

  it('unmount cancels a pending debounced write', async () => {
    jest.useFakeTimers();
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);

    act(() => {
      harness.latest.updateBody('typing');
    });
    harness.unmount();

    // Advance far past the debounce; no write should occur.
    await act(async () => {
      jest.advanceTimersByTime(MEMO_BODY_DEBOUNCE_MS * 5);
      await Promise.resolve();
    });
    expect(stub.updateRecord).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  // ─── refetch preserves currentMemo id ────────────────────────────────────

  it('refetch preserves currentMemo id if still present in new list', async () => {
    const stub = installXrmStub();
    // First load: two memos.
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1'), makeMemoRaw('m2')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    // Focus m2.
    act(() => {
      harness.latest.setCurrentMemo('m2');
    });
    expect(harness.latest.currentMemo?.sprk_memoid).toBe('m2');

    // Refetch: m2 still present.
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1'), makeMemoRaw('m2'), makeMemoRaw('m3')],
    });
    await act(async () => {
      await harness.latest.refetch();
    });
    expect(harness.latest.currentMemo?.sprk_memoid).toBe('m2');
    harness.unmount();
  });

  it('refetch falls back to memos[0] if prior currentMemo is gone', async () => {
    const stub = installXrmStub();
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m1'), makeMemoRaw('m2')],
    });
    const harness = await renderHook('sprk_matter', CANONICAL_MATTER_ID);
    act(() => {
      harness.latest.setCurrentMemo('m2');
    });
    // Refetch: only m3 remains.
    stub.retrieveMultipleRecords.mockResolvedValueOnce({
      entities: [makeMemoRaw('m3')],
    });
    await act(async () => {
      await harness.latest.refetch();
    });
    expect(harness.latest.currentMemo?.sprk_memoid).toBe('m3');
    harness.unmount();
  });
});
