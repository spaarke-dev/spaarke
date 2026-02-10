/**
 * URL Parameter Parsing Utility for CalendarSidePane
 *
 * Parses filter state from URL query parameters.
 * Dataverse side panes receive parameters via webResourceParams which are
 * encoded in the URL query string.
 *
 * Expected URL formats:
 * - ?selectedDate={YYYY-MM-DD}
 * - ?rangeStart={YYYY-MM-DD}&rangeEnd={YYYY-MM-DD}
 * - ?data={base64EncodedJson} (Dataverse format)
 *
 * @module utils/parseParams
 */

/**
 * Calendar filter output format
 */
export type CalendarFilterType = "single" | "range" | "clear";

export interface CalendarFilterSingle {
  type: "single";
  date: string;
}

export interface CalendarFilterRange {
  type: "range";
  start: string;
  end: string;
}

export interface CalendarFilterClear {
  type: "clear";
}

export type CalendarFilterOutput =
  | CalendarFilterSingle
  | CalendarFilterRange
  | CalendarFilterClear;

/**
 * Parameters expected by CalendarSidePane
 */
export interface CalendarSidePaneParams {
  /** Initial selected date (ISO date string YYYY-MM-DD) */
  selectedDate: string | null;
  /** Range start date (ISO date string) */
  rangeStart: string | null;
  /** Range end date (ISO date string) */
  rangeEnd: string | null;
  /** Parent window origin for postMessage (for security) */
  parentOrigin: string | null;
}

/**
 * Parse parameters from URL query string.
 *
 * Handles multiple formats:
 * 1. Direct parameters: ?selectedDate={date}&rangeStart={date}&rangeEnd={date}
 * 2. Dataverse webResourceParams: ?data={base64Json}
 *
 * @returns Parsed parameters object
 */
export function parseCalendarParams(): CalendarSidePaneParams {
  const params = new URLSearchParams(window.location.search);

  // Try direct parameters first
  let selectedDate = params.get("selectedDate");
  let rangeStart = params.get("rangeStart");
  let rangeEnd = params.get("rangeEnd");
  let parentOrigin = params.get("parentOrigin");

  // If not found, try Dataverse encoded data parameter
  if (!selectedDate && !rangeStart && !rangeEnd) {
    const dataParam = params.get("data");
    if (dataParam) {
      // First try: URL-encoded query string (webresource navigation)
      // Format: selectedDate=xxx&rangeStart=yyy&rangeEnd=zzz
      if (dataParam.includes("=")) {
        try {
          const dataParams = new URLSearchParams(dataParam);
          selectedDate = dataParams.get("selectedDate") || dataParams.get("selecteddate");
          rangeStart = dataParams.get("rangeStart") || dataParams.get("rangestart");
          rangeEnd = dataParams.get("rangeEnd") || dataParams.get("rangeend");
          parentOrigin = dataParams.get("parentOrigin") || dataParams.get("parentorigin");
        } catch {
          // Not a valid query string - try base64
        }
      }

      // Second try: base64 JSON (Dataverse pageInput format)
      if (!selectedDate && !rangeStart && !rangeEnd) {
        try {
          const decoded = atob(dataParam);
          const parsed = JSON.parse(decoded) as Record<string, unknown>;
          selectedDate = typeof parsed.selectedDate === "string" ? parsed.selectedDate : null;
          rangeStart = typeof parsed.rangeStart === "string" ? parsed.rangeStart : null;
          rangeEnd = typeof parsed.rangeEnd === "string" ? parsed.rangeEnd : null;
          parentOrigin = typeof parsed.parentOrigin === "string" ? parsed.parentOrigin : null;
        } catch {
          // Invalid base64 or JSON - use null values
          console.warn("[CalendarSidePane] Failed to parse data parameter");
        }
      }
    }
  }

  // Also check for lowercase variants (PowerApps sometimes varies casing)
  if (!selectedDate) {
    selectedDate = params.get("selecteddate") || params.get("SelectedDate") || params.get("SELECTEDDATE");
  }
  if (!rangeStart) {
    rangeStart = params.get("rangestart") || params.get("RangeStart") || params.get("RANGESTART");
  }
  if (!rangeEnd) {
    rangeEnd = params.get("rangeend") || params.get("RangeEnd") || params.get("RANGEEND");
  }
  if (!parentOrigin) {
    parentOrigin = params.get("parentorigin") || params.get("ParentOrigin") || params.get("PARENTORIGIN");
  }

  return {
    selectedDate: selectedDate || null,
    rangeStart: rangeStart || null,
    rangeEnd: rangeEnd || null,
    parentOrigin: parentOrigin || null,
  };
}

/**
 * Convert params to initial filter state
 *
 * @param params - Parsed URL parameters
 * @returns Initial filter state or null if no filter
 */
export function getInitialFilterState(params: CalendarSidePaneParams): CalendarFilterOutput | null {
  if (params.rangeStart && params.rangeEnd) {
    return {
      type: "range",
      start: params.rangeStart,
      end: params.rangeEnd,
    };
  }

  if (params.selectedDate) {
    return {
      type: "single",
      date: params.selectedDate,
    };
  }

  return null;
}

/**
 * Build a URL with calendar parameters
 *
 * @param baseUrl - Base URL of the web resource
 * @param filter - Current filter state
 * @param parentOrigin - Parent window origin for postMessage
 * @returns URL with encoded parameters
 */
export function buildCalendarUrl(
  baseUrl: string,
  filter: CalendarFilterOutput | null,
  parentOrigin?: string
): string {
  const url = new URL(baseUrl, window.location.origin);

  if (filter) {
    if (filter.type === "single") {
      url.searchParams.set("selectedDate", filter.date);
    } else if (filter.type === "range") {
      url.searchParams.set("rangeStart", filter.start);
      url.searchParams.set("rangeEnd", filter.end);
    }
    // type === "clear" means no params
  }

  if (parentOrigin) {
    url.searchParams.set("parentOrigin", parentOrigin);
  }

  return url.toString();
}
