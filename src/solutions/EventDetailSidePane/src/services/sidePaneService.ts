/**
 * Side Pane Service - Control side pane behavior
 *
 * Provides utilities to close the side pane and navigate to parent records.
 * Uses the global Xrm.App.sidePanes API available in Custom Pages.
 *
 * @see design.md - Event Detail Side Pane specification
 */

/**
 * Xrm.App.sidePanes type definition (subset needed)
 */
interface IXrmSidePanes {
  getSelectedPane(): { close: () => void } | null;
}

/**
 * Xrm.Navigation type definition (subset needed)
 */
interface IXrmNavigation {
  openUrl(options: { url: string }): void;
}

/**
 * Get the Xrm.App.sidePanes object from window context
 */
function getXrmSidePanes(): IXrmSidePanes | null {
  try {
    // Try window.parent.Xrm first (Custom Page in iframe)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.App?.sidePanes) {
      return parentXrm.App.sidePanes as IXrmSidePanes;
    }

    // Try window.Xrm
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const windowXrm = (window as any)?.Xrm;
    if (windowXrm?.App?.sidePanes) {
      return windowXrm.App.sidePanes as IXrmSidePanes;
    }

    console.warn("[SidePaneService] Xrm.App.sidePanes not available");
    return null;
  } catch (error) {
    console.error("[SidePaneService] Error accessing Xrm.App.sidePanes:", error);
    return null;
  }
}

/**
 * Get the Xrm.Navigation object from window context
 */
function getXrmNavigation(): IXrmNavigation | null {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const parentXrm = (window.parent as any)?.Xrm;
    if (parentXrm?.Navigation) {
      return parentXrm.Navigation as IXrmNavigation;
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const windowXrm = (window as any)?.Xrm;
    if (windowXrm?.Navigation) {
      return windowXrm.Navigation as IXrmNavigation;
    }

    return null;
  } catch (error) {
    console.error("[SidePaneService] Error accessing Xrm.Navigation:", error);
    return null;
  }
}

/**
 * Close the current side pane
 *
 * Closes the side pane that hosts this Custom Page.
 *
 * @returns true if close was initiated, false if API not available
 */
export function closeSidePane(): boolean {
  const sidePanes = getXrmSidePanes();
  if (!sidePanes) {
    console.warn("[SidePaneService] Cannot close - sidePanes API not available");
    return false;
  }

  try {
    const currentPane = sidePanes.getSelectedPane();
    if (currentPane) {
      currentPane.close();
      return true;
    } else {
      console.warn("[SidePaneService] No selected pane to close");
      return false;
    }
  } catch (error) {
    console.error("[SidePaneService] Error closing side pane:", error);
    return false;
  }
}

/**
 * Navigate to a parent record URL
 *
 * Opens the parent record in the main app area (not the side pane).
 *
 * @param url - Full URL to the parent record
 * @returns true if navigation was initiated, false if URL invalid or API not available
 */
export function navigateToParentRecord(url: string): boolean {
  if (!url) {
    console.warn("[SidePaneService] Cannot navigate - URL is empty");
    return false;
  }

  try {
    // First try using Xrm.Navigation for proper routing
    const navigation = getXrmNavigation();
    if (navigation) {
      navigation.openUrl({ url });
      return true;
    }

    // Fallback: Open in parent window (main app area)
    if (window.parent && window.parent !== window) {
      window.parent.location.href = url;
      return true;
    }

    // Last fallback: Open in same window
    window.location.href = url;
    return true;
  } catch (error) {
    console.error("[SidePaneService] Error navigating to parent:", error);
    return false;
  }
}
