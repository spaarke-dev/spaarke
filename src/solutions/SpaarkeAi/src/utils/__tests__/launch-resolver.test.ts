/**
 * launch-resolver.test.ts — spaarkeai-compose-r1 task 046 unit tests.
 *
 * Covers the POML §ui-tests contract for the Compose modal launch wiring at
 * the unit-test layer (the jsdom layer that is reliable on CI). The four POML
 * UI tests map to the categories below:
 *
 *   1. **Component Renders** — `openSpaarkeAiCompose` invokes
 *      `Xrm.Navigation.navigateTo` with the correct `webresourceName`,
 *      `target=2`, and `data` query string including `composeMode=editor`
 *      + `sprkDocumentId` + `speDriveItemId`. The actual modal-mount /
 *      `ComposeWorkspace` rendering is verified at the App boundary by
 *      the runtime when the URL params reach `main.tsx`.
 *
 *   2. **Dark Mode Compliance (ADR-021)** — non-applicable at this layer:
 *      `launch-resolver` is a navigation helper, not a render path. The
 *      modal's dark-mode chrome is owned by the Xrm dialog framework, and
 *      `ComposeWorkspace` itself is covered by ADR-021 via its own use of
 *      semantic tokens (verified in the ComposeWorkspace component test).
 *      Asserted in this file via documentation comment.
 *
 *   3. **Full-Screen Toggle** — the toggle is provided by the Xrm dialog
 *      chrome (platform-controlled Expand button on the modal header at
 *      target=2, 90%×90%). This file asserts the modal size/target contract
 *      (90% × 90%, target=2) on which the platform's Expand button operates.
 *      The visual toggle behaviour itself is a platform contract not under
 *      our control.
 *
 *   4. **Document Context Forwarding** — `buildLaunchUrl` emits exactly the
 *      expected URL parameters (sprkDocumentId, speDriveItemId, speDriveId,
 *      speFileName, composeMode) so `main.tsx` can read them and pass them
 *      to `App`, which forwards them to `ComposeWorkspace`.
 *
 * Test category per ADR-038: **Domain Logic** (KEEP path
 * `tests/unit/<solution>` analogue under `src/solutions/SpaarkeAi/src/utils/__tests__/`
 * per the SpaarkeAi convention). Tests assert PURE FUNCTION BEHAVIOUR
 * (URL parameter assembly) and a single sociable mock of `Xrm.Navigation`
 * (legitimate boundary — `Xrm` is the platform SDK, not under our control).
 *
 * Banned-pattern compliance (ADR-038):
 *   - No `Mock<HttpMessageHandler>` (no fetch in this file).
 *   - No DI-registration tests (no DI here).
 *   - No constructor null-check tests (this is a functions module).
 *
 * @see src/solutions/SpaarkeAi/src/utils/launch-resolver.ts
 * @see projects/spaarkeai-compose-r1/tasks/046-frontend-wire-modal-launch.poml
 */

import '@testing-library/jest-dom';

import {
  buildLaunchUrl,
  openSpaarkeAi,
  openSpaarkeAiCompose,
  type SpaarkeAiComposeLaunchParams,
} from '../launch-resolver';

// ---------------------------------------------------------------------------
// Xrm mock — minimal Navigation.navigateTo stand-in
// ---------------------------------------------------------------------------

interface MockNavigation {
  navigateTo: jest.Mock<Promise<void>, [unknown, unknown?]>;
}

interface MockXrm {
  Navigation: MockNavigation;
}

function installXrmMock(): MockNavigation {
  const nav: MockNavigation = {
    navigateTo: jest.fn().mockResolvedValue(undefined),
  };
  (globalThis as unknown as { Xrm: MockXrm }).Xrm = { Navigation: nav };
  return nav;
}

function uninstallXrmMock(): void {
  delete (globalThis as Partial<{ Xrm: unknown }>).Xrm;
}

// ---------------------------------------------------------------------------
// buildLaunchUrl — wire-format contract
// ---------------------------------------------------------------------------

describe('buildLaunchUrl — Compose params (task 046)', () => {
  test('omits Compose params when no composeMode is supplied (back-compat with non-Compose launches)', () => {
    const url = buildLaunchUrl({
      entityLogicalName: 'sprk_matter',
      entityId: '{abc-123}',
    });

    // Existing entity context params encoded; Compose params absent.
    expect(url).toContain('entityLogicalName=sprk_matter');
    expect(url).toContain('entityId=abc-123');
    expect(url).not.toContain('composeMode');
    expect(url).not.toContain('sprkDocumentId');
    expect(url).not.toContain('speDriveItemId');
  });

  test('emits composeMode + sprkDocumentId + speDriveItemId + speDriveId + speFileName when supplied', () => {
    const url = buildLaunchUrl({
      composeMode: 'editor',
      sprkDocumentId: '{f1a2b3c4-0000-1111-2222-333344445555}',
      speDriveItemId: '01ABCDEF0123456789',
      speDriveId: 'b!XYZ',
      speFileName: 'Acme MSA.docx',
    } satisfies SpaarkeAiComposeLaunchParams);

    expect(url).toContain('composeMode=editor');
    // Braces stripped on GUID (matches existing entityId handling).
    expect(url).toContain('sprkDocumentId=f1a2b3c4-0000-1111-2222-333344445555');
    expect(url).toContain('speDriveItemId=01ABCDEF0123456789');
    expect(url).toContain('speDriveId=b%21XYZ'); // URLSearchParams encodes '!'.
    expect(url).toContain('speFileName=Acme+MSA.docx');
  });

  test('allows Compose params alongside the existing entityLogicalName / entityId envelope (FR-19 ribbon path)', () => {
    const url = buildLaunchUrl({
      entityLogicalName: 'sprk_document',
      entityId: 'aaaa-bbbb-cccc',
      composeMode: 'editor',
      sprkDocumentId: 'aaaa-bbbb-cccc',
      speDriveItemId: '01ABCDEF',
    });

    // All five params present in the same URL.
    expect(url).toContain('entityLogicalName=sprk_document');
    expect(url).toContain('entityId=aaaa-bbbb-cccc');
    expect(url).toContain('composeMode=editor');
    expect(url).toContain('sprkDocumentId=aaaa-bbbb-cccc');
    expect(url).toContain('speDriveItemId=01ABCDEF');
  });
});

// ---------------------------------------------------------------------------
// openSpaarkeAi — back-compat regression
// ---------------------------------------------------------------------------

describe('openSpaarkeAi — back-compat (entity form launch unchanged)', () => {
  let nav: MockNavigation;

  beforeEach(() => {
    nav = installXrmMock();
  });
  afterEach(() => {
    uninstallXrmMock();
  });

  test('opens sprk_spaarkeai with default target=2 (modal) at 90% x 90%', () => {
    openSpaarkeAi({
      entityLogicalName: 'sprk_matter',
      entityId: 'abc-123',
    });

    expect(nav.navigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = nav.navigateTo.mock.calls[0];
    expect(pageInput).toMatchObject({
      pageType: 'webresource',
      webresourceName: 'sprk_spaarkeai',
    });
    expect(navOptions).toMatchObject({
      target: 2,
      width: { value: 90, unit: '%' },
      height: { value: 90, unit: '%' },
    });
  });

  test('still routes target=1 (full page) when explicitly requested', () => {
    openSpaarkeAi({}, 1);
    const [, navOptions] = nav.navigateTo.mock.calls[0];
    expect(navOptions).toMatchObject({ target: 1 });
  });
});

// ---------------------------------------------------------------------------
// openSpaarkeAiCompose — Path A entry (POML §ui-tests)
// ---------------------------------------------------------------------------

describe('openSpaarkeAiCompose — Path A entry (task 046 §ui-tests)', () => {
  let nav: MockNavigation;

  beforeEach(() => {
    nav = installXrmMock();
  });
  afterEach(() => {
    uninstallXrmMock();
  });

  /**
   * POML §ui-tests #1 (Component Renders).
   * Opening with `composeMode=editor` + speDriveItemId forwards the document
   * context through the URL so main.tsx → App → ComposeWorkspace can mount
   * the editor pre-loaded.
   */
  test('Component Renders: opens sprk_spaarkeai modal with composeMode=editor + document context in URL', () => {
    openSpaarkeAiCompose({
      entityLogicalName: 'sprk_document',
      entityId: 'doc-guid-1',
      sprkDocumentId: 'doc-guid-1',
      speDriveItemId: '01DRIVEITEM',
      speDriveId: 'b!DRIVE',
      speFileName: 'Test.docx',
    });

    expect(nav.navigateTo).toHaveBeenCalledTimes(1);
    const [pageInput] = nav.navigateTo.mock.calls[0];
    const pageInputTyped = pageInput as { pageType: string; webresourceName: string; data: string };
    expect(pageInputTyped.pageType).toBe('webresource');
    expect(pageInputTyped.webresourceName).toBe('sprk_spaarkeai');
    expect(pageInputTyped.data).toContain('composeMode=editor');
    expect(pageInputTyped.data).toContain('sprkDocumentId=doc-guid-1');
    expect(pageInputTyped.data).toContain('speDriveItemId=01DRIVEITEM');
    expect(pageInputTyped.data).toContain('speFileName=Test.docx');
  });

  /**
   * POML §ui-tests #3 (Full-Screen Toggle).
   * Asserts the modal contract on which the Xrm platform's Expand button
   * operates: target=2 + 90%×90%. The visual toggle behaviour itself is
   * provided by the Xrm dialog chrome and is not under our control.
   */
  test('Full-Screen Toggle: modal opens at target=2 (90% x 90%) for platform-provided expand affordance', () => {
    openSpaarkeAiCompose({
      sprkDocumentId: 'x',
      speDriveItemId: 'y',
    });

    expect(nav.navigateTo).toHaveBeenCalledTimes(1);
    const [, navOptions] = nav.navigateTo.mock.calls[0];
    expect(navOptions).toMatchObject({
      target: 2, // ALWAYS modal — never full-page for Compose Path A.
      width: { value: 90, unit: '%' },
      height: { value: 90, unit: '%' },
    });
  });

  /**
   * POML §ui-tests #4 (Document Context Forwarding).
   * Verifies the document pointer (sprkDocumentId + speDriveItemId) reaches
   * the URL `data` blob in a shape `main.tsx` can read directly.
   */
  test('Document Context Forwarding: sprkDocumentId + speDriveItemId reach the URL data blob', () => {
    openSpaarkeAiCompose({
      sprkDocumentId: '{D1A2B3C4-AAAA-BBBB-CCCC-DDDDEEEEFFFF}',
      speDriveItemId: '01ITEM',
    });

    const [pageInput] = nav.navigateTo.mock.calls[0];
    const data = (pageInput as { data: string }).data;
    const params = new URLSearchParams(data);

    // Braces stripped on the GUID (matches existing entityId handling).
    expect(params.get('sprkDocumentId')).toBe('D1A2B3C4-AAAA-BBBB-CCCC-DDDDEEEEFFFF');
    expect(params.get('speDriveItemId')).toBe('01ITEM');
    expect(params.get('composeMode')).toBe('editor');
  });

  /**
   * Empty-state launch — user clicks Open in Compose on a Document with no
   * SPE drive-item id yet. The ribbon handler defaults to omitting
   * speDriveItemId; ComposeWorkspace then renders its empty-state picker
   * per FR-19 + design.md §14 row 5.
   */
  test('Empty-state launch: composeMode=editor only, no document context', () => {
    openSpaarkeAiCompose({});

    const [pageInput] = nav.navigateTo.mock.calls[0];
    const params = new URLSearchParams((pageInput as { data: string }).data);
    expect(params.get('composeMode')).toBe('editor');
    expect(params.get('speDriveItemId')).toBeNull();
    expect(params.get('sprkDocumentId')).toBeNull();
  });

  /**
   * Defensive guard: when `Xrm` is unavailable (deep-link / non-Xrm context),
   * the function logs a warning and does NOT throw. The Xrm-less path is the
   * fallback contract documented on the function.
   */
  test('does not throw when Xrm global is unavailable', () => {
    uninstallXrmMock();
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() => openSpaarkeAiCompose({ speDriveItemId: 'x' })).not.toThrow();
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('[launch-resolver]'),
    );

    warnSpy.mockRestore();
  });
});
