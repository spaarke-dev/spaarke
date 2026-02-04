/**
 * URL Parameter Parsing Utility
 *
 * Parses eventId and eventType from URL query parameters.
 * Dataverse side panes receive parameters via webResourceParams which are
 * encoded in the URL query string.
 *
 * Expected URL format:
 * - ?eventId={guid}&eventType={guid}
 * - ?data={base64EncodedJson}  (Dataverse format)
 *
 * @module utils/parseParams
 */

/**
 * Parameters expected by EventDetailSidePane
 */
export interface SidePaneParams {
  /** GUID of the Event record */
  eventId: string | null;
  /** GUID of the Event Type lookup */
  eventType: string | null;
}

/**
 * Parse parameters from URL query string.
 *
 * Handles two formats:
 * 1. Direct parameters: ?eventId={guid}&eventType={guid}
 * 2. Dataverse webResourceParams: ?data={base64Json}
 *
 * @returns Parsed parameters object
 */
export function parseSidePaneParams(): SidePaneParams {
  const params = new URLSearchParams(window.location.search);

  // Try direct parameters first
  let eventId = params.get("eventId");
  let eventType = params.get("eventType");

  // If not found, try Dataverse encoded data parameter
  if (!eventId && !eventType) {
    const dataParam = params.get("data");
    if (dataParam) {
      try {
        // Dataverse encodes parameters as base64 JSON
        const decoded = atob(dataParam);
        const parsed = JSON.parse(decoded) as Record<string, unknown>;
        eventId = typeof parsed.eventId === "string" ? parsed.eventId : null;
        eventType = typeof parsed.eventType === "string" ? parsed.eventType : null;
      } catch {
        // Invalid base64 or JSON - use null values
        console.warn("[EventDetailSidePane] Failed to parse data parameter");
      }
    }
  }

  // Also check for lowercase variants (PowerApps sometimes varies casing)
  if (!eventId) {
    eventId = params.get("eventid") || params.get("EventId") || params.get("EVENTID");
  }
  if (!eventType) {
    eventType = params.get("eventtype") || params.get("EventType") || params.get("EVENTTYPE");
  }

  return {
    eventId: eventId || null,
    eventType: eventType || null,
  };
}

/**
 * Build a URL with side pane parameters
 *
 * @param baseUrl - Base URL of the Custom Page
 * @param eventId - Event record GUID
 * @param eventType - Event Type lookup GUID
 * @returns URL with encoded parameters
 */
export function buildSidePaneUrl(
  baseUrl: string,
  eventId: string,
  eventType?: string
): string {
  const url = new URL(baseUrl, window.location.origin);
  url.searchParams.set("eventId", eventId);
  if (eventType) {
    url.searchParams.set("eventType", eventType);
  }
  return url.toString();
}
