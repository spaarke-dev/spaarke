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

  /** task 041 (FR-13): activeWorkType is additive — encoded alongside the existing params. */
  test('emits activeWorkType when supplied (task 041, FR-13)', () => {
    const url = buildLaunchUrl({
      composeMode: 'editor',
      speDriveItemId: '01ABCDEF0123456789',
      activeWorkType: 'agreement-analysis',
    } satisfies SpaarkeAiComposeLaunchParams);

    expect(url).toContain('activeWorkType=agreement-analysis');
  });

  test('omits activeWorkType when not supplied (no regression on pre-existing launches)', () => {
    const url = buildLaunchUrl({
      composeMode: 'editor',
      speDriveItemId: '01ABCDEF0123456789',
    } satisfies SpaarkeAiComposeLaunchParams);

    expect(url).not.toContain('activeWorkType');
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
// buildLaunchUrl — Analysis entry-matrix params (ai-advanced-capabilities-
// analysis-hub-r1 task 052, spec §13.3 / FR-16)
// ---------------------------------------------------------------------------

describe('buildLaunchUrl — Analysis params (task 052)', () => {
  test('omits analysis params when none are supplied (back-compat with non-Analysis launches)', () => {
    const url = buildLaunchUrl({
      entityLogicalName: 'sprk_matter',
      entityId: '{abc-123}',
    });

    expect(url).toContain('entityLogicalName=sprk_matter');
    expect(url).toContain('entityId=abc-123');
    expect(url).not.toContain('analysisId');
    expect(url).not.toContain('worktype');
    expect(url).not.toContain('regarding');
  });

  test('emits worktype + regarding alongside entity context (entry case 2b: new-in-record)', () => {
    const url = buildLaunchUrl({
      entityLogicalName: 'sprk_matter',
      entityId: 'matter-guid-1',
      worktype: '100000000',
      regarding: 'matter-guid-1',
    });

    expect(url).toContain('entityLogicalName=sprk_matter');
    expect(url).toContain('entityId=matter-guid-1');
    expect(url).toContain('worktype=100000000');
    expect(url).toContain('regarding=matter-guid-1');
    expect(url).not.toContain('analysisId');
  });

  test('emits analysisId (entry case 2d: open existing) with braces stripped', () => {
    const url = buildLaunchUrl({
      analysisId: '{D1A2B3C4-AAAA-BBBB-CCCC-DDDDEEEEFFFF}',
    });

    expect(url).toContain('analysisId=D1A2B3C4-AAAA-BBBB-CCCC-DDDDEEEEFFFF');
    expect(url).not.toContain('worktype');
    expect(url).not.toContain('regarding');
  });

  test('regarding braces are stripped like entityId', () => {
    const url = buildLaunchUrl({
      regarding: '{abc-123}',
    });

    expect(url).toContain('regarding=abc-123');
  });
});

// ---------------------------------------------------------------------------
// buildLaunchUrl — subDomain deep-link param (ai-advanced-capabilities-
// agreements-r1 task 022, spec FR-09 — hub A3 deferred deep-threading leg)
// ---------------------------------------------------------------------------

describe('buildLaunchUrl — subDomain deep-link param (task 022)', () => {
  test('omits subDomain when not supplied (back-compat with every existing launch)', () => {
    const url = buildLaunchUrl({
      analysisId: 'analysis-guid-1',
    });

    expect(url).not.toContain('subDomain');
  });

  test('emits subDomain alongside analysisId (cold-load open-existing door)', () => {
    const url = buildLaunchUrl({
      analysisId: 'analysis-guid-1',
      subDomain: 'nda',
    });

    expect(url).toContain('analysisId=analysis-guid-1');
    expect(url).toContain('subDomain=nda');
  });

  test('emits subDomain alongside worktype (cold-load new-analysis-hub hint door)', () => {
    const url = buildLaunchUrl({
      worktype: '100000000',
      subDomain: 'employment',
    });

    expect(url).toContain('worktype=100000000');
    expect(url).toContain('subDomain=employment');
  });

  test('subDomain is a plain slug — no brace-stripping applied (not a GUID)', () => {
    const url = buildLaunchUrl({
      subDomain: 'asset-purchase',
    });

    expect(url).toContain('subDomain=asset-purchase');
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

  test('opens sprk_spaarkeai with default target=2 (modal) at 80% x 80%', () => {
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
    // Modal sizing is 80% x 80% per spaarkeai-compose-r1 task 101
    // (2026-07-01 supplement, commit bb109056a) — intentionally reduced
    // from the original 90% x 90% once the three-pane shell needed more
    // surrounding chrome margin. This assertion tracks the shipped value.
    expect(navOptions).toMatchObject({
      target: 2,
      width: { value: 80, unit: '%' },
      height: { value: 80, unit: '%' },
    });
  });

  test('still routes target=1 (full page) when explicitly requested', () => {
    openSpaarkeAi({}, 1);
    const [, navOptions] = nav.navigateTo.mock.calls[0];
    expect(navOptions).toMatchObject({ target: 1 });
  });
});

// ---------------------------------------------------------------------------
// openSpaarkeAi — Analysis entry-matrix params (task 052)
// ---------------------------------------------------------------------------

describe('openSpaarkeAi — Analysis params (task 052 §ui-tests)', () => {
  let nav: MockNavigation;

  beforeEach(() => {
    nav = installXrmMock();
  });
  afterEach(() => {
    uninstallXrmMock();
  });

  /** POML ui-test #1: ribbon new-in-record opens modal with regarding. */
  test('new-in-record: worktype + regarding=parent reach the URL data blob at target=2', () => {
    openSpaarkeAi({
      entityLogicalName: 'sprk_matter',
      entityId: 'matter-guid-1',
      worktype: '100000000',
      regarding: 'matter-guid-1',
    });

    expect(nav.navigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = nav.navigateTo.mock.calls[0];
    const data = (pageInput as { data: string }).data;
    const params = new URLSearchParams(data);

    expect(params.get('entityLogicalName')).toBe('sprk_matter');
    expect(params.get('entityId')).toBe('matter-guid-1');
    expect(params.get('worktype')).toBe('100000000');
    expect(params.get('regarding')).toBe('matter-guid-1');
    expect(params.get('analysisId')).toBeNull();
    expect(navOptions).toMatchObject({ target: 2 });
  });

  /** POML ui-test #2: open existing passes analysisId. */
  test('open existing: analysisId reaches the URL data blob at target=2', () => {
    openSpaarkeAi({ analysisId: 'analysis-guid-1' });

    const [pageInput, navOptions] = nav.navigateTo.mock.calls[0];
    const params = new URLSearchParams((pageInput as { data: string }).data);

    expect(params.get('analysisId')).toBe('analysis-guid-1');
    expect(params.get('worktype')).toBeNull();
    expect(navOptions).toMatchObject({ target: 2 });
  });

  /** task 022 (spec FR-09) ui-test: "Deep-link door" — subDomain=nda reaches the URL data blob. */
  test('Deep-link door (task 022): subDomain=nda reaches the URL data blob alongside analysisId', () => {
    openSpaarkeAi({ analysisId: 'analysis-guid-1', subDomain: 'nda' });

    const [pageInput] = nav.navigateTo.mock.calls[0];
    const params = new URLSearchParams((pageInput as { data: string }).data);

    expect(params.get('analysisId')).toBe('analysis-guid-1');
    expect(params.get('subDomain')).toBe('nda');
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
  test('Full-Screen Toggle: modal opens at target=2 (80% x 80%) for platform-provided expand affordance', () => {
    openSpaarkeAiCompose({
      sprkDocumentId: 'x',
      speDriveItemId: 'y',
    });

    expect(nav.navigateTo).toHaveBeenCalledTimes(1);
    const [, navOptions] = nav.navigateTo.mock.calls[0];
    // 80% x 80% per spaarkeai-compose-r1 task 101 (see back-compat test above
    // for the commit reference) — Compose reuses openSpaarkeAi's modal sizing.
    expect(navOptions).toMatchObject({
      target: 2, // ALWAYS modal — never full-page for Compose Path A.
      width: { value: 80, unit: '%' },
      height: { value: 80, unit: '%' },
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
  /**
   * ai-advanced-capabilities-analysis-hub-r1 task 041 (FR-13): activeWorkType (e.g. an
   * Agreement Review launch) reaches the URL data blob so main.tsx → App → ThreePaneShell →
   * ComposeLaunchContext → ComposeWorkspace → ComposeEditor can scope the AI toolbar via the
   * already-shipped getToolsForSurface(surface, activeWorkType).
   */
  test('Agreement Review scopes palette: activeWorkType="agreement-analysis" reaches the URL data blob', () => {
    openSpaarkeAiCompose({
      sprkDocumentId: 'doc-guid-2',
      speDriveItemId: '01DRIVEITEM2',
      activeWorkType: 'agreement-analysis',
    });

    const [pageInput] = nav.navigateTo.mock.calls[0];
    const params = new URLSearchParams((pageInput as { data: string }).data);
    expect(params.get('activeWorkType')).toBe('agreement-analysis');
    expect(params.get('composeMode')).toBe('editor');
  });

  /**
   * Default is unscoped (no regression): omitting activeWorkType keeps the URL free of the
   * param — main.tsx falls through to `undefined`, and ComposeEditor's own `'*'` default applies.
   */
  test("Default is unscoped: omitting activeWorkType omits the param (ComposeEditor's own '*' default applies)", () => {
    openSpaarkeAiCompose({
      sprkDocumentId: 'doc-guid-3',
      speDriveItemId: '01DRIVEITEM3',
    });

    const [pageInput] = nav.navigateTo.mock.calls[0];
    const params = new URLSearchParams((pageInput as { data: string }).data);
    expect(params.get('activeWorkType')).toBeNull();
  });

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
