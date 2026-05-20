/**
 * AssignWorkWizardLauncher — unit tests (task 045, FR-20).
 *
 * Verifies the two acceptance-criteria paths:
 *   (a) window.Xrm present → navigateTo called with the EXACT UQ-05 verified args.
 *   (b) window.Xrm absent  → returns { launched: false, reason: 'no-xrm' }; no throw.
 *
 * Also covers:
 *   - invalid options (empty bffBaseUrl) → returns 'invalid-options'.
 *   - default dialog options + title override.
 *   - swallowed navigateTo rejections (user cancel / dialog error).
 *   - bffBaseUrl with special characters is encodeURIComponent-encoded.
 *
 * The launcher is a pure module — no React rendering, no jsdom needed beyond
 * `window`. We use the existing `jest-environment-jsdom` test environment
 * provided by jest.config.ts.
 */

import {
  launchAssignWorkWizard,
  ASSIGN_WORK_WEBRESOURCE_NAME,
} from '../AssignWorkWizardLauncher';

// Local mirror of the navigationOptions shape navigateTo receives — narrowed
// for test assertions (avoids depending on Dataverse's full typedef which is
// not shipped in the shared-lib package).
interface CapturedNavigationOptions {
  target?: number;
  width?: { value: number; unit?: string };
  height?: { value: number; unit?: string };
  title?: string;
}

interface CapturedPageInput {
  pageType: string;
  webresourceName: string;
  data?: string;
}

describe('launchAssignWorkWizard', () => {
  // -------------------------------------------------------------------------
  // window.Xrm mocking helpers
  // -------------------------------------------------------------------------

  const originalXrm: unknown = (window as unknown as { Xrm?: unknown }).Xrm;

  function installXrm(navigateTo: jest.Mock): void {
    (window as unknown as { Xrm: { Navigation: { navigateTo: jest.Mock } } }).Xrm = {
      Navigation: { navigateTo },
    };
  }

  function clearXrm(): void {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  }

  afterEach(() => {
    // Restore whatever Xrm-shaped object existed before this test (usually
    // nothing in jsdom, but the mock stays clean between tests either way).
    if (originalXrm === undefined) {
      clearXrm();
    } else {
      (window as unknown as { Xrm?: unknown }).Xrm = originalXrm;
    }
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  // Happy path: window.Xrm present → navigateTo called with verified args
  // -------------------------------------------------------------------------

  describe('when window.Xrm.Navigation.navigateTo is available', () => {
    it('calls navigateTo with the UQ-05 verified pageInput + data format', () => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      installXrm(navigateTo);

      const result = launchAssignWorkWizard({
        bffBaseUrl: 'https://bff.example.com',
      });

      expect(result).toEqual({ launched: true });
      expect(navigateTo).toHaveBeenCalledTimes(1);

      const [pageInput, navigationOptions] = navigateTo.mock.calls[0] as [
        CapturedPageInput,
        CapturedNavigationOptions,
      ];

      // UQ-05: the exact verified shape — pageType, webresourceName, data.
      expect(pageInput.pageType).toBe('webresource');
      expect(pageInput.webresourceName).toBe(ASSIGN_WORK_WEBRESOURCE_NAME);
      expect(pageInput.webresourceName).toBe('sprk_createworkassignmentwizard');
      expect(pageInput.data).toBe('bffBaseUrl=https%3A%2F%2Fbff.example.com');
    });

    it('encodes special characters in bffBaseUrl via encodeURIComponent', () => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      installXrm(navigateTo);

      launchAssignWorkWizard({
        bffBaseUrl: 'https://bff.example.com/api?x=1&y=2',
      });

      const [pageInput] = navigateTo.mock.calls[0] as [CapturedPageInput];
      // encodeURIComponent encodes `:`, `/`, `?`, `=`, `&`.
      expect(pageInput.data).toBe(
        'bffBaseUrl=https%3A%2F%2Fbff.example.com%2Fapi%3Fx%3D1%26y%3D2'
      );
    });

    it('uses the default dialog options matching WorkspaceGrid.tsx precedent', () => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      installXrm(navigateTo);

      launchAssignWorkWizard({ bffBaseUrl: 'https://bff.example.com' });

      const [, navigationOptions] = navigateTo.mock.calls[0] as [
        CapturedPageInput,
        CapturedNavigationOptions,
      ];

      expect(navigationOptions.target).toBe(2);
      expect(navigationOptions.width).toEqual({ value: 60, unit: '%' });
      expect(navigationOptions.height).toEqual({ value: 70, unit: '%' });
      expect(navigationOptions.title).toBe('Create Work Assignment');
    });

    it('honours a custom title override', () => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      installXrm(navigateTo);

      launchAssignWorkWizard({
        bffBaseUrl: 'https://bff.example.com',
        title: 'Assign Counsel',
      });

      const [, navigationOptions] = navigateTo.mock.calls[0] as [
        CapturedPageInput,
        CapturedNavigationOptions,
      ];
      expect(navigationOptions.title).toBe('Assign Counsel');
    });

    it('does not throw when navigateTo rejects (user cancel / dialog error)', () => {
      const navigateTo = jest
        .fn()
        .mockRejectedValue(new Error('User cancelled dialog'));
      installXrm(navigateTo);

      // The .catch() inside the launcher swallows rejections — the synchronous
      // return value should still be { launched: true }, and Jest must not
      // observe an unhandled rejection. We give the microtask queue a tick to
      // confirm no rejection leaks.
      expect(() =>
        launchAssignWorkWizard({ bffBaseUrl: 'https://bff.example.com' })
      ).not.toThrow();

      // Allow the rejected promise's .catch() to run.
      return Promise.resolve().then(() => {
        expect(navigateTo).toHaveBeenCalledTimes(1);
      });
    });
  });

  // -------------------------------------------------------------------------
  // Non-host fallback: window.Xrm absent → status return, no throw
  // -------------------------------------------------------------------------

  describe('when window.Xrm is unavailable (Vite dev, jsdom, etc.)', () => {
    it('returns { launched: false, reason: "no-xrm" } and does not throw', () => {
      clearXrm();

      const result = launchAssignWorkWizard({
        bffBaseUrl: 'https://bff.example.com',
      });

      expect(result).toEqual({ launched: false, reason: 'no-xrm' });
    });

    it('treats an Xrm object without Navigation as non-host', () => {
      (window as unknown as { Xrm: object }).Xrm = {}; // present but malformed

      const result = launchAssignWorkWizard({
        bffBaseUrl: 'https://bff.example.com',
      });

      expect(result).toEqual({ launched: false, reason: 'no-xrm' });
    });

    it('treats Xrm.Navigation without navigateTo as non-host', () => {
      (window as unknown as { Xrm: { Navigation: object } }).Xrm = {
        Navigation: {},
      };

      const result = launchAssignWorkWizard({
        bffBaseUrl: 'https://bff.example.com',
      });

      expect(result).toEqual({ launched: false, reason: 'no-xrm' });
    });
  });

  // -------------------------------------------------------------------------
  // Defensive: invalid options
  // -------------------------------------------------------------------------

  describe('when options are invalid', () => {
    it('returns { launched: false, reason: "invalid-options" } for empty bffBaseUrl', () => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      installXrm(navigateTo);

      const result = launchAssignWorkWizard({ bffBaseUrl: '' });

      expect(result).toEqual({ launched: false, reason: 'invalid-options' });
      expect(navigateTo).not.toHaveBeenCalled();
    });

    it('returns invalid-options when bffBaseUrl is not a string', () => {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      installXrm(navigateTo);

      // Cast through unknown — exercises the runtime defensive check rather
      // than the compile-time type contract.
      const result = launchAssignWorkWizard({
        bffBaseUrl: undefined as unknown as string,
      });

      expect(result).toEqual({ launched: false, reason: 'invalid-options' });
      expect(navigateTo).not.toHaveBeenCalled();
    });
  });
});
