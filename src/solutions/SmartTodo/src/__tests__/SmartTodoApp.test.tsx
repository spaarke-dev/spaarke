/**
 * SmartTodoApp — "+ New Task" unit tests (task 030, spec FR-10).
 *
 * Covers the closed acceptance set from
 * `projects/smart-todo-r5/tasks/030-new-task-oob-mainform-modal.poml` step 6:
 *   - `handleNewTask` calls `navigateToEntityRecordSurfaceAsync` with
 *     `entityName: 'sprk_todo'` and no `entityId` (CREATE mode).
 *   - On a resolved `savedEntityReference`, the refresh path is invoked.
 *   - On `cancelled` (no `savedEntityReference`), the refresh path is NOT invoked.
 *   - Regarding-context → `defaultValues` pre-seed mapping (openTodos +
 *     createTodo branches; unknown entity type; no context).
 *
 * Test harness note: `SmartTodoApp.tsx`'s `handleNewTask` (inside
 * `SmartTodoLayout`, not itself exported — mirrors the existing
 * `openSprkTodoAsLayout1` module-scope-helper precedent in the same file) is
 * a thin wrapper:
 *
 *   const handleNewTask = React.useCallback(() => {
 *     void launchNewTaskCreateForm(launchContext, handleRefresh);
 *   }, [launchContext, handleRefresh]);
 *
 * Task 030 extracted the actual create-form logic into
 * `services/newTaskLauncher.ts` (`buildNewTaskDefaultValues` +
 * `launchNewTaskCreateForm`) specifically so it's unit-testable WITHOUT
 * pulling `SmartTodoApp.tsx`'s full import graph (Header, SmartToDo,
 * SearchFilter, Toolbar, TodoContext, `@spaarke/auth`, ...) into the test
 * module graph — mirrors `Header.test.tsx` / `SearchFilter.test.tsx`'s
 * component-isolation precedent, applied one layer down (service isolation)
 * because `SmartTodoLayout` isn't itself exported for direct rendering.
 * These tests exercise `launchNewTaskCreateForm`/`buildNewTaskDefaultValues`
 * directly — the exact functions `handleNewTask` delegates to — which is
 * equivalent coverage to clicking the button in a full render.
 *
 * `@spaarke/ui-components` is mocked below for the same reason
 * `Header.test.tsx` mocks it: this worktree's shared-lib `dist/` has no
 * built `services/` or `index.js` (bare-barrel import would fail to resolve
 * regardless of the `@spaarke/sdap-client` issue that file's header
 * documents), so any test importing from the bare barrel must mock it.
 */

const mockNavigateToEntityRecordSurfaceAsync = jest.fn();

/**
 * Minimal mirror of two `TODO_REGARDING_CATALOG` entries (matter, project)
 * from `Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts` —
 * enough to exercise the mapping logic without importing the real (unbuilt)
 * package barrel. Attribute names copied verbatim from that file (task 030
 * constraint: mirror the existing convention, do not invent names).
 */
const MOCK_TODO_REGARDING_CATALOG = [
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
];

jest.mock('@spaarke/ui-components', () => ({
  navigateToEntityRecordSurfaceAsync: mockNavigateToEntityRecordSurfaceAsync,
  TODO_REGARDING_CATALOG: MOCK_TODO_REGARDING_CATALOG,
}));

import {
  buildNewTaskDefaultValues,
  launchNewTaskCreateForm,
  resolveHostRegardingRecord,
} from '../services/newTaskLauncher';
import type { ILaunchContext } from '../hooks/useLaunchContext';

/**
 * Stub the shell form context that `getXrm().Page.data.entity.getEntityReference()`
 * reads. `getXrm` resolves the bare global `Xrm` first, so we set it on
 * `window` (=== globalThis under jsdom). Cleared in `afterEach`.
 */
function stubHostRecord(ref: { id: string; entityType: string; name?: string } | null): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    WebApi: {},
    Page: {
      data: {
        entity: {
          getEntityReference: () => ref,
        },
      },
    },
  };
}

beforeEach(() => {
  mockNavigateToEntityRecordSurfaceAsync.mockReset();
});

afterEach(() => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
});

// ---------------------------------------------------------------------------
// buildNewTaskDefaultValues — regarding-context → defaultValues mapping
// ---------------------------------------------------------------------------

describe('buildNewTaskDefaultValues', () => {
  it('returns undefined when there is no launch context (plain create)', () => {
    expect(buildNewTaskDefaultValues(undefined)).toBeUndefined();
  });

  it('returns undefined for the openTodo (single-record open) branch — not a regarding source', () => {
    const launchContext: ILaunchContext = { action: 'openTodo', todoId: 'abc-123' };
    expect(buildNewTaskDefaultValues(launchContext)).toBeUndefined();
  });

  it('maps the openTodos regardingFilter branch to defaultValues using the catalog lookup attribute', () => {
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: {
        entityType: 'sprk_matter',
        recordId: 'guid-1',
        recordName: 'Matter A',
      },
    };

    const result = buildNewTaskDefaultValues(launchContext);

    expect(result).toBeDefined();
    // D-8 (2026-08-17): three-key `data` lookup convention (MS Learn) —
    // id / name / type — replaces the pre-D-8 (unsupported) JSON.stringify shape.
    expect(result!['sprk_regardingmatter']).toBe('guid-1');
    expect(result!['sprk_regardingmattername']).toBe('Matter A');
    expect(result!['sprk_regardingmattertype']).toBe('sprk_matter');
    expect(result!['sprk_regardingrecordid']).toBe('guid-1');
    expect(result!['sprk_regardingrecordname']).toBe('Matter A');
    // Documented gap (task 030 escalation trigger) — the type-lookup resolver
    // field is NOT pre-seeded (its GUID isn't the regarding record's GUID).
    expect(result!['sprk_regardingrecordtype']).toBeUndefined();
  });

  it('maps the createTodo initialRegarding branch (fallback when openTodos is absent)', () => {
    const launchContext: ILaunchContext = {
      action: 'createTodo',
      initialRegarding: {
        entityType: 'sprk_project',
        recordId: 'guid-2',
        recordName: 'Project B',
      },
    };

    const result = buildNewTaskDefaultValues(launchContext);

    expect(result).toBeDefined();
    expect(result!['sprk_regardingproject']).toBe('guid-2');
    expect(result!['sprk_regardingprojectname']).toBe('Project B');
    expect(result!['sprk_regardingprojecttype']).toBe('sprk_project');
    expect(result!['sprk_regardingrecordid']).toBe('guid-2');
    expect(result!['sprk_regardingrecordname']).toBe('Project B');
  });

  it('prefers the openTodos regardingFilter over createTodo initialRegarding when somehow both are present in a single context object', () => {
    // Not a real production shape (the union is exclusive), but exercises the
    // precedence logic directly against the openTodos branch, which is what
    // production code actually reaches for a mid-session "+ New Task" click.
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'guid-3', recordName: 'Matter C' },
    };
    const result = buildNewTaskDefaultValues(launchContext);
    expect(result!['sprk_regardingmatter']).toBe('guid-3');
  });

  it('returns undefined (plain create) when the regarding entityType is not one of the 12 canonical sprk_todo targets', () => {
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_unknown_entity', recordId: 'guid-4', recordName: 'Unknown' },
    };
    expect(buildNewTaskDefaultValues(launchContext)).toBeUndefined();
  });

  it('omits sprk_regardingrecordname when recordName is absent (VisualHost openTodos form)', () => {
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'guid-5' },
    };
    const result = buildNewTaskDefaultValues(launchContext);
    expect(result!['sprk_regardingmatter']).toBe('guid-5');
    expect(result!['sprk_regardingmattertype']).toBe('sprk_matter');
    expect(result!['sprk_regardingrecordid']).toBe('guid-5');
    // No name available → neither the lookup `name` key nor the text name field.
    expect('sprk_regardingmattername' in result!).toBe(false);
    expect('sprk_regardingrecordname' in result!).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// launchNewTaskCreateForm — the function `handleNewTask` delegates to
// ---------------------------------------------------------------------------

describe('launchNewTaskCreateForm (handleNewTask delegate)', () => {
  it('calls navigateToEntityRecordSurfaceAsync with entityName "sprk_todo" and no entityId (CREATE mode)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true, cancelled: true });
    const onSaved = jest.fn();

    await launchNewTaskCreateForm(undefined, onSaved);

    expect(mockNavigateToEntityRecordSurfaceAsync).toHaveBeenCalledTimes(1);
    const callArgs = mockNavigateToEntityRecordSurfaceAsync.mock.calls[0][0];
    expect(callArgs.entityName).toBe('sprk_todo');
    expect('entityId' in callArgs).toBe(false);
    expect(callArgs.title).toBe('New To Do');
  });

  it('invokes the refresh callback when the outcome carries a savedEntityReference (user saved)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({
      launched: true,
      savedEntityReference: { id: 'new-todo-guid', entityType: 'sprk_todo', name: 'New To Do' },
    });
    const onSaved = jest.fn();

    await launchNewTaskCreateForm(undefined, onSaved);

    expect(onSaved).toHaveBeenCalledTimes(1);
  });

  it('does NOT invoke the refresh callback when the outcome is cancelled (no savedEntityReference)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true, cancelled: true });
    const onSaved = jest.fn();

    await launchNewTaskCreateForm(undefined, onSaved);

    expect(onSaved).not.toHaveBeenCalled();
  });

  it('does NOT invoke the refresh callback when navigateTo could not launch (non-host environment)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: false });
    const onSaved = jest.fn();

    await launchNewTaskCreateForm(undefined, onSaved);

    expect(onSaved).not.toHaveBeenCalled();
  });

  it('passes the built defaultValues through to navigateToEntityRecordSurfaceAsync when regarding context is present', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true, cancelled: true });
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'guid-6', recordName: 'Matter F' },
    };

    await launchNewTaskCreateForm(launchContext, jest.fn());

    const callArgs = mockNavigateToEntityRecordSurfaceAsync.mock.calls[0][0];
    expect(callArgs.defaultValues['sprk_regardingrecordid']).toBe('guid-6');
  });

  it('passes createFromEntity (regarding pre-associate) alongside defaultValues when regarding context is present', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true, cancelled: true });
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'guid-7', recordName: 'Matter G' },
    };

    await launchNewTaskCreateForm(launchContext, jest.fn());

    const callArgs = mockNavigateToEntityRecordSurfaceAsync.mock.calls[0][0];
    expect(callArgs.createFromEntity).toEqual({
      entityType: 'sprk_matter',
      id: 'guid-7',
      name: 'Matter G',
    });
  });

  it('does not pass createFromEntity when there is no regarding source (plain create)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true, cancelled: true });

    await launchNewTaskCreateForm(undefined, jest.fn());

    const callArgs = mockNavigateToEntityRecordSurfaceAsync.mock.calls[0][0];
    expect(callArgs.createFromEntity).toBeUndefined();
    expect(callArgs.defaultValues).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// resolveHostRegardingRecord + host-preferred precedence (D-8, 2026-08-17)
// ---------------------------------------------------------------------------

describe('resolveHostRegardingRecord (D-8 host-record read)', () => {
  it('returns undefined when there is no reachable form context (standalone / jsdom)', () => {
    expect(resolveHostRegardingRecord()).toBeUndefined();
  });

  it('reads the host record from Xrm.Page.data.entity.getEntityReference()', () => {
    stubHostRecord({ id: '{guid-host}', entityType: 'sprk_matter', name: 'Host Matter' });
    expect(resolveHostRegardingRecord()).toEqual({
      entityType: 'sprk_matter',
      recordId: 'guid-host', // braces stripped
      recordName: 'Host Matter',
    });
  });

  it('returns undefined when the host record has no id yet (unsaved host form)', () => {
    stubHostRecord({ id: '', entityType: 'sprk_matter', name: 'Unsaved' });
    expect(resolveHostRegardingRecord()).toBeUndefined();
  });
});

describe('host record is preferred over launch context (D-8)', () => {
  it('buildNewTaskDefaultValues uses the host record when present, over launch context', () => {
    stubHostRecord({ id: 'host-matter-guid', entityType: 'sprk_matter', name: 'Host Matter' });
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_project', recordId: 'lc-project-guid', recordName: 'LC Project' },
    };

    const result = buildNewTaskDefaultValues(launchContext);

    // Host (matter) wins — project lookup keys must be absent.
    expect(result!['sprk_regardingmatter']).toBe('host-matter-guid');
    expect(result!['sprk_regardingrecordid']).toBe('host-matter-guid');
    expect('sprk_regardingproject' in result!).toBe(false);
  });

  it('falls back to launch context when the host record is not a supported regarding target', () => {
    stubHostRecord({ id: 'acct-guid', entityType: 'account', name: 'Some Account' });
    const launchContext: ILaunchContext = {
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'lc-matter-guid', recordName: 'LC Matter' },
    };

    const result = buildNewTaskDefaultValues(launchContext);

    // Host entity (account) isn't a catalog target → launch-context matter used.
    expect(result!['sprk_regardingmatter']).toBe('lc-matter-guid');
  });
});
