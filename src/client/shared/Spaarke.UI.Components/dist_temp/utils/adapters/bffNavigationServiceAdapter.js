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
export function createBffNavigationService(navigate, dialogRenderer, dialogCloser) {
    return {
        async openRecord(entityName, entityId) {
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
        async openDialog(webresourceName, data, options) {
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
                return new Promise((resolve) => {
                    const interval = setInterval(() => {
                        if (dialogWindow.closed) {
                            clearInterval(interval);
                            // Attempt to read result from the dialog window
                            try {
                                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                                const result = dialogWindow.__dialogResult;
                                if (result && typeof result === 'object') {
                                    resolve(result);
                                }
                                else {
                                    resolve({ confirmed: true });
                                }
                            }
                            catch {
                                // Cross-origin — cannot read result
                                resolve({ confirmed: true });
                            }
                        }
                    }, 250);
                });
            }
            return { confirmed: false };
        },
        closeDialog(result) {
            if (dialogCloser) {
                dialogCloser(result);
                return;
            }
            // Fallback: store result and close window
            if (typeof window !== 'undefined') {
                if (result !== undefined) {
                    // eslint-disable-next-line @typescript-eslint/no-explicit-any
                    window.__dialogResult = result;
                }
                window.close();
            }
        },
        // eslint-disable-next-line @typescript-eslint/no-unused-vars
        async openLookup(_options) {
            // Xrm.Utility.lookupObjects is not available in a Power Pages SPA context.
            // Return an empty array as a graceful no-op so that components that call
            // openLookup do not crash when running outside Dataverse.
            return [];
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
function normaliseDimensionToPx(dim, viewportSize) {
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
//# sourceMappingURL=bffNavigationServiceAdapter.js.map