/**
 * nextStepLauncher.ts
 * Utility for launching post-wizard "next step" actions from the success screen.
 *
 * Two launcher functions:
 *   1. openAnalysisBuilder — opens the Analysis Builder Code Page via
 *      Xrm.Navigation.navigateTo({ pageType: "webresource" })
 *   2. (Find Similar is rendered inline via React state toggle in the wizard
 *      dialog — no navigation needed here.)
 *
 * @see ADR-006  - Code Pages for standalone dialogs (navigateTo webresource)
 * @see ADR-008  - Independent auth per Code Page (no tokens in URL params)
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Web resource name for the Analysis Builder Code Page. */
const ANALYSIS_BUILDER_WEB_RESOURCE = "sprk_analysisbuilder";

/** Log prefix for console output. */
const LOG_PREFIX = "[nextStepLauncher]";

// ---------------------------------------------------------------------------
// Xrm resolution helper
// ---------------------------------------------------------------------------

/**
 * Resolve the Xrm global in a Code Page context.
 *
 * Code Pages run as HTML web resources inside Dataverse, so Xrm is typically
 * available on `window.parent`. We check multiple locations for robustness.
 */
function getXrm(): typeof Xrm | undefined {
    // Direct global (classic web resource context)
    if (typeof Xrm !== "undefined") {
        return Xrm;
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;

    // window.Xrm (same-origin frame)
    if (w.Xrm) {
        return w.Xrm as typeof Xrm;
    }

    // window.parent.Xrm (Code Page inside Dataverse dialog iframe)
    try {
        if (w.parent?.Xrm) {
            return w.parent.Xrm as typeof Xrm;
        }
    } catch {
        // Cross-origin parent access blocked — swallow silently
    }

    return undefined;
}

// ---------------------------------------------------------------------------
// openAnalysisBuilder
// ---------------------------------------------------------------------------

/**
 * Open the Analysis Builder Code Page in a Dataverse dialog.
 *
 * Uses `Xrm.Navigation.navigateTo` with `pageType: "webresource"` to open
 * the Analysis Builder as a near-full-screen modal dialog. Passes document
 * context (documentId, containerId) via URL data parameters so the Code Page
 * can load the correct document.
 *
 * Security: Only record GUIDs are passed in URL parameters. The Code Page
 * acquires its own Bearer tokens via Xrm.Utility.getGlobalContext() (ADR-008).
 *
 * @param documentId  - Dataverse sprk_document record GUID
 * @param containerId - SPE container ID for file operations
 */
export async function openAnalysisBuilder(
    documentId: string,
    containerId: string,
): Promise<void> {
    const xrm = getXrm();

    if (!xrm?.Navigation?.navigateTo) {
        console.warn(
            LOG_PREFIX,
            "Xrm.Navigation.navigateTo is not available. Cannot open Analysis Builder.",
            "This typically means the wizard is running outside of a Dataverse context.",
        );
        return;
    }

    // Build data parameters (only non-empty values)
    const params = new URLSearchParams();
    if (documentId) {
        params.set("documentId", documentId);
    }
    if (containerId) {
        params.set("containerId", containerId);
    }

    const pageInput = {
        pageType: "webresource" as const,
        webresourceName: ANALYSIS_BUILDER_WEB_RESOURCE,
        data: params.toString(),
    };

    const navigationOptions = {
        target: 2 as const, // Dialog
        width: { value: 85, unit: "%" as const },
        height: { value: 85, unit: "%" as const },
    };

    try {
        console.log(LOG_PREFIX, "Opening Analysis Builder:", pageInput);
        await xrm.Navigation.navigateTo(pageInput, navigationOptions);
        console.log(LOG_PREFIX, "Analysis Builder dialog closed.");
    } catch (err) {
        // Xrm throws errorCode 2 when user cancels the dialog — not a real error
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        if ((err as any)?.errorCode === 2) {
            console.log(LOG_PREFIX, "Analysis Builder dialog was cancelled by user.");
            return;
        }
        console.error(LOG_PREFIX, "Failed to open Analysis Builder:", err);
    }
}
