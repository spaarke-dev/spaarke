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
import type { DialogOptions, DialogResult, INavigationService } from '../../types/serviceInterfaces';
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
export type DialogRenderer = (webresourceName: string, data?: string, options?: DialogOptions) => Promise<DialogResult>;
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
export declare function createBffNavigationService(navigate?: NavigateFunction, dialogRenderer?: DialogRenderer, dialogCloser?: DialogCloser): INavigationService;
//# sourceMappingURL=bffNavigationServiceAdapter.d.ts.map