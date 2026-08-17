/**
 * wizardLaunchers.test — unit tests for `navigateToEntityRecordSurfaceAsync`'s
 * task 031 consolidation (spec FR-11 — "one code path for create + open").
 *
 * Covers smart-todo-r5 task 031 acceptance criteria:
 *   - `entityId` present → OPEN branch: `record` OOB size (85%×85%),
 *     `position: 1`, `pageInput.entityId` set with braces stripped.
 *   - `entityId` absent → CREATE branch: `createForm` OOB size (70%×80%), no
 *     `position` key (unchanged pre-031 behavior).
 *   - Outcome-shape parity per the task's Microsoft Learn research finding:
 *     OPEN never returns `savedEntityReference` (the API only populates it
 *     for CREATE); a clean OPEN resolve returns a plain `{ launched: true }`
 *     with no `cancelled` flag.
 *   - `resolveXrmNavigation()`'s frame-walk remains the sole resolver (no
 *     host reachable → `{ launched: false }`, verified via `window.Xrm`
 *     absence rather than re-implementing a second resolver in the test).
 *
 * Classification (ADR-038 §7): MAINTAIN-class framework-contract tests for a
 * shared-lib launcher consumed by two host surfaces (SmartTodo Code Page +
 * LegalWorkspace widget). No `Mock<HttpMessageHandler>`, no DI-registration
 * checks, no ctor null-checks, no coverage-as-gate.
 */

import { navigateToEntityRecordSurfaceAsync } from '../wizardLaunchers';

// ---------------------------------------------------------------------------
// Test fixtures — a minimal `window.Xrm.Navigation.navigateTo` stub. Assigned
// directly on the jsdom `window` (not `window.parent`/`window.top`) so the
// `resolveXrmNavigation()` frame-walk's FIRST frame (`window` itself) resolves
// it — exercising the real resolver rather than mocking it away.
// ---------------------------------------------------------------------------

interface StubXrm {
  Navigation: {
    navigateTo: jest.Mock;
  };
}

function installXrmStub(resolveValue: unknown): StubXrm['Navigation']['navigateTo'] {
  const navigateTo = jest.fn().mockResolvedValue(resolveValue);
  (window as unknown as { Xrm?: StubXrm }).Xrm = { Navigation: { navigateTo } };
  return navigateTo;
}

afterEach(() => {
  delete (window as unknown as { Xrm?: StubXrm }).Xrm;
  jest.clearAllMocks();
});

describe('navigateToEntityRecordSurfaceAsync', () => {
  describe('OPEN branch (entityId present)', () => {
    it('uses the record OOB size (85%x85%) and centered position', async () => {
      const navigateTo = installXrmStub(undefined);

      await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        entityId: '11111111-1111-1111-1111-111111111111',
      });

      expect(navigateTo).toHaveBeenCalledTimes(1);
      const [, navOptions] = navigateTo.mock.calls[0];
      expect(navOptions).toMatchObject({
        target: 2,
        position: 1,
        width: { value: 85, unit: '%' },
        height: { value: 85, unit: '%' },
      });
    });

    it('strips braces from a registry-format entityId', async () => {
      const navigateTo = installXrmStub(undefined);

      await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        entityId: '{22222222-2222-2222-2222-222222222222}',
      });

      const [pageInput] = navigateTo.mock.calls[0];
      expect(pageInput).toMatchObject({
        pageType: 'entityrecord',
        entityName: 'sprk_todo',
        entityId: '22222222-2222-2222-2222-222222222222',
      });
    });

    it('returns a plain launched outcome with no savedEntityReference or cancelled flag on a clean resolve', async () => {
      // Per MS Learn (Return value): savedEntityReference is populated ONLY
      // for entityrecord CREATE-mode navigations — an existing-record open
      // always resolves with no result object.
      installXrmStub(undefined);

      const outcome = await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        entityId: '33333333-3333-3333-3333-333333333333',
      });

      expect(outcome).toEqual({ launched: true });
    });

    it('returns launched+cancelled when navigateTo rejects (dialog error)', async () => {
      const navigateTo = jest.fn().mockRejectedValue(new Error('dialog error'));
      (window as unknown as { Xrm?: StubXrm }).Xrm = { Navigation: { navigateTo } };

      const outcome = await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        entityId: '44444444-4444-4444-4444-444444444444',
      });

      expect(outcome).toEqual({ launched: true, cancelled: true });
    });

    it('omits the title navOption when no title is supplied', async () => {
      const navigateTo = installXrmStub(undefined);

      await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        entityId: '55555555-5555-5555-5555-555555555555',
      });

      const [, navOptions] = navigateTo.mock.calls[0];
      expect(navOptions).not.toHaveProperty('title');
    });
  });

  describe('CREATE branch (entityId absent)', () => {
    it('uses the createForm OOB size (70%x80%) with no position key', async () => {
      const navigateTo = installXrmStub({});

      await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        title: 'New To Do',
      });

      const [pageInput, navOptions] = navigateTo.mock.calls[0];
      expect(pageInput).not.toHaveProperty('entityId');
      expect(navOptions).toMatchObject({
        target: 2,
        width: { value: 70, unit: '%' },
        height: { value: 80, unit: '%' },
        title: 'New To Do',
      });
      expect(navOptions).not.toHaveProperty('position');
    });

    it('returns the saved entity reference (braces stripped) when the record is saved', async () => {
      installXrmStub({
        savedEntityReference: [
          { id: '{66666666-6666-6666-6666-666666666666}', entityType: 'sprk_todo', name: 'A new task' },
        ],
      });

      const outcome = await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        title: 'New To Do',
      });

      expect(outcome).toEqual({
        launched: true,
        savedEntityReference: {
          id: '66666666-6666-6666-6666-666666666666',
          entityType: 'sprk_todo',
          name: 'A new task',
        },
      });
    });

    it('returns launched+cancelled when the resolve carries no savedEntityReference', async () => {
      installXrmStub(undefined);

      const outcome = await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        title: 'New To Do',
      });

      expect(outcome).toEqual({ launched: true, cancelled: true });
    });
  });

  describe('no reachable Xrm host', () => {
    it('returns launched:false without throwing (Vite dev / jsdom without window.Xrm)', async () => {
      const outcome = await navigateToEntityRecordSurfaceAsync({
        entityName: 'sprk_todo',
        entityId: '77777777-7777-7777-7777-777777777777',
      });

      expect(outcome).toEqual({ launched: false });
    });
  });
});
