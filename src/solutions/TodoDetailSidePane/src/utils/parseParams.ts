/**
 * parseParams — Parse eventId from URL query parameters.
 *
 * Dataverse web resources receive parameters via the `data` query param
 * or directly as query params.
 */

export interface ISidePaneParams {
  eventId: string | null;
}

export function parseSidePaneParams(): ISidePaneParams {
  const urlParams = new URLSearchParams(window.location.search);

  // Try direct param first
  let eventId = urlParams.get("eventId") ?? urlParams.get("eventid");

  // Try Dataverse encoded data param: ?data=eventId=xxx
  if (!eventId) {
    const dataParam = urlParams.get("data");
    if (dataParam) {
      const dataParams = new URLSearchParams(dataParam);
      eventId = dataParams.get("eventId") ?? dataParams.get("eventid");
    }
  }

  // Normalize GUID — remove braces if present
  if (eventId) {
    eventId = eventId.replace(/[{}]/g, "");
  }

  return { eventId };
}
