/**
 * BFF Navigation Service Adapter for INavigationService
 *
 * Provides navigation and dialog capabilities in a Power Pages SPA context
 * where no Xrm.Navigation API is available. Uses SPA router navigation for
 * record opening and programmatic dialog rendering for dialogs.
 *
 * @see INavigationService in ../../types/serviceInterfaces
 * @see ADR-006 - Anti-Legacy JS (Code Pages for standalone dialogs)
 * @see ADR-012 - Shared Component Library
 *
 * @example
 * ```typescript
 * import { createBffNavigationService } from "@spaarke/ui-components";
 *
 * // With SPA router navigate function
 * const navService = createBffNavigationService((path) => router.push(path));
 *
 * // Open a record (navigates within SPA)
 * await navService.openRecord("sprk_matter", matterId);
 *
 * // Open a dialog
 * const result = await navService.openDialog(
 *   "sprk_uploadwizard",
 *   `matterId=${matterId}`,
 *   { width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" }, title: "Upload Files" }
 * );
 * if (result.confirmed) {
 *   console.log("Dialog completed:", result.data);
 * }
 * ```
 */

import type {
  DialogOptions,
  DialogResult,
  INavigationService,
} from '../../types/serviceInterfaces';

/**
 * SPA router navigation function type.
 *
 * Accepts a path string and navigates to it within the SPA.
 * Compatible with React Router's `useNavigate()`, Next.js `router.push()`,
 * or any similar routing abstraction.
 */
export type NavigateFunction = (path: string) => void;

/**
 * Callback that renders a dialog and resolves with the dialog result.
 *
 * In an SPA context, dialogs are rendered programmatically (e.g. via a
 * Fluent UI Dialog or a portal). The host application provides this callback
 * to handle dialog lifecycle.
 *
 * @param webresourceName - Identifier for the dialog content to render
 * @param data - Optional query-string data to pass to the dialog
 * @param options - Optional dialog dimensions and title
 * @returns Promise resolving to the dialog result when the dialog is closed
 */
export type DialogRenderer = (
  webresourceName: string,
  data?: string,
  options?: DialogOptions
) => Promise<DialogResult>;

/**
 * Callback invoked when `closeDialog` is called.
 *
 * The host application provides this to dismiss whatever dialog is
 * currently open, optionally returning result data to the opener.
 */
export type DialogCloser = (result?: unknown) => void;

/**
 * Creates an INavigationService implementation for Power Pages SPA contexts.
 *
 * Navigation behaviour adapts based on the callbacks provided:
 *
 * - **openRecord**: Uses the `navigate` function to route within the SPA.
 *   Falls back to `window.open` if no navigate function is provided.
 * - **openDialog**: Uses the `dialogRenderer` callback to show a dialog.
 *   Falls back to opening a new browser window if no renderer is provided.
 * - **closeDialog**: Uses the `dialogCloser` callback to dismiss a dialog.
 *   Falls back to `window.close()` if no closer is provided.
 *
 * @param navigate - Optional SPA router navigation function
 * @param dialogRenderer - Optional callback to render dialogs within the SPA
 * @param dialogCloser - Optional callback to close the current dialog
 * @returns An INavigationService for SPA contexts
 *
 * @example
 * ```typescript
 * // Minimal setup — uses window.open fallbacks
 * const navService = createBffNavigationService();
 *
 * // With SPA router
 * const navService = createBffNavigationService(
 *   (path) => router.push(path)
 * );
 *
 * // Full setup with dialog support
 * const navService = createBffNavigationService(
 *   (path) => router.push(path),
 *   async (name, data, options) => {
 *     // Show a Fluent UI Dialog and return result when closed
 *     return showDialog(name, data, options);
 *   },
 *   (result) => {
 *     // Dismiss the current dialog
 *     dismissDialog(result);
 *   }
 * );
 * ```
 */
export function createBffNavigationService(
  navigate?: NavigateFunction,
  dialogRenderer?: DialogRenderer,
  dialogCloser?: DialogCloser
): INavigationService {
  return {
    async openRecord(entityName: string, entityId: string): Promise<void> {
      const path = `/records/${encodeURIComponent(entityName)}/${encodeURIComponent(entityId)}`;

      if (navigate) {
        navigate(path);
        return;
      }

      // Fallback: open in a new browser tab
      if (typeof window !== 'undefined') {
        window.open(path, '_blank');
      }
    },

    async openDialog(
      webresourceName: string,
      data?: string,
      options?: DialogOptions
    ): Promise<DialogResult> {
      // Delegate to the provided dialog renderer
      if (dialogRenderer) {
        return dialogRenderer(webresourceName, data, options);
      }

      // Fallback: open as a new browser window with approximate dimensions
      if (typeof window !== 'undefined') {
        const width = normaliseDimensionToPx(options?.width, window.innerWidth) ?? 800;
        const height = normaliseDimensionToPx(options?.height, window.innerHeight) ?? 600;
        const left = Math.round((window.innerWidth - width) / 2);
        const top = Math.round((window.innerHeight - height) / 2);

        const queryString = data ? `?${data}` : '';
        const dialogUrl = `/webresource/${encodeURIComponent(webresourceName)}${queryString}`;

        const features = `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`;
        const dialogWindow = window.open(dialogUrl, webresourceName, features);

        if (!dialogWindow) {
          // Popup was blocked
          return { confirmed: false };
        }

        // Wait for the dialog window to close
        return new Promise<DialogResult>((resolve) => {
          const interval = setInterval(() => {
            if (dialogWindow.closed) {
              clearInterval(interval);
              // Attempt to read result from the dialog window
              try {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const result = (dialogWindow as any).__dialogResult;
                if (result && typeof result === 'object') {
                  resolve(result as DialogResult);
                } else {
                  resolve({ confirmed: true });
                }
              } catch {
                // Cross-origin — cannot read result
                resolve({ confirmed: true });
              }
            }
          }, 250);
        });
      }

      return { confirmed: false };
    },

    closeDialog(result?: unknown): void {
      if (dialogCloser) {
        dialogCloser(result);
        return;
      }

      // Fallback: store result and close window
      if (typeof window !== 'undefined') {
        if (result !== undefined) {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          (window as any).__dialogResult = result;
        }
        window.close();
      }
    },
  };
}

/**
 * Converts a DialogOptions dimension to pixels.
 *
 * @param dim - Dimension from DialogOptions (number | { value, unit })
 * @param viewportSize - The viewport dimension (width or height) for percentage calculation
 * @returns Pixel value, or undefined if dim is undefined
 */
function normaliseDimensionToPx(
  dim: number | { value: number; unit: '%' | 'px' } | undefined,
  viewportSize: number
): number | undefined {
  if (dim === undefined) {
    return undefined;
  }
  if (typeof dim === 'number') {
    return dim;
  }
  if (dim.unit === '%') {
    return Math.round((dim.value / 100) * viewportSize);
  }
  return dim.value;
}
