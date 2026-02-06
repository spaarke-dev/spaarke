/**
 * PostMessage Communication Utility for CalendarSidePane
 *
 * Enables bidirectional communication between the CalendarSidePane web resource
 * and the parent EventsPage via window.postMessage.
 *
 * Message Types:
 * - CALENDAR_FILTER_CHANGED: Sent when user changes date selection
 * - CALENDAR_EVENTS_UPDATE: Received from parent with event date indicators
 * - CALENDAR_CLOSE: Sent when user clicks close (mutual exclusivity)
 *
 * @module utils/postMessage
 */

import { CalendarFilterOutput } from "./parseParams";

// ─────────────────────────────────────────────────────────────────────────────
// Message Types
// ─────────────────────────────────────────────────────────────────────────────

export const MESSAGE_TYPES = {
  /** Calendar filter changed - sent from CalendarSidePane to parent */
  CALENDAR_FILTER_CHANGED: "CALENDAR_FILTER_CHANGED",
  /** Event dates update - received from parent to update indicators */
  CALENDAR_EVENTS_UPDATE: "CALENDAR_EVENTS_UPDATE",
  /** Calendar close request - sent when switching to another pane */
  CALENDAR_CLOSE: "CALENDAR_CLOSE",
  /** Calendar ready - sent when side pane is initialized */
  CALENDAR_READY: "CALENDAR_READY",
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Message Interfaces
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Event date info for calendar indicators
 */
export interface IEventDateInfo {
  date: string;
  count: number;
}

/**
 * Message sent when calendar filter changes
 */
export interface CalendarFilterChangedMessage {
  type: typeof MESSAGE_TYPES.CALENDAR_FILTER_CHANGED;
  payload: {
    filter: CalendarFilterOutput | null;
  };
}

/**
 * Message received from parent with event date indicators
 */
export interface CalendarEventsUpdateMessage {
  type: typeof MESSAGE_TYPES.CALENDAR_EVENTS_UPDATE;
  payload: {
    eventDates: IEventDateInfo[];
  };
}

/**
 * Message sent when calendar should close
 */
export interface CalendarCloseMessage {
  type: typeof MESSAGE_TYPES.CALENDAR_CLOSE;
}

/**
 * Message sent when calendar side pane is ready
 */
export interface CalendarReadyMessage {
  type: typeof MESSAGE_TYPES.CALENDAR_READY;
  payload: {
    currentFilter: CalendarFilterOutput | null;
  };
}

export type CalendarMessage =
  | CalendarFilterChangedMessage
  | CalendarEventsUpdateMessage
  | CalendarCloseMessage
  | CalendarReadyMessage;

// ─────────────────────────────────────────────────────────────────────────────
// BroadcastChannel for Cross-Iframe Communication
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BroadcastChannel name for Events page communication.
 * Used for communication between sibling iframes (CalendarSidePane and EventsPage).
 */
const EVENTS_CHANNEL_NAME = "spaarke-events-page-channel";

/**
 * Get or create the BroadcastChannel for cross-iframe communication.
 * Returns null if BroadcastChannel is not supported.
 */
let eventsChannel: BroadcastChannel | null = null;
function getEventsChannel(): BroadcastChannel | null {
  if (typeof BroadcastChannel === "undefined") {
    return null;
  }
  if (!eventsChannel) {
    eventsChannel = new BroadcastChannel(EVENTS_CHANNEL_NAME);
  }
  return eventsChannel;
}

// ─────────────────────────────────────────────────────────────────────────────
// Message Sending
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Detect parent window origin
 * Falls back to same origin if detection fails
 */
function getTargetOrigin(): string {
  try {
    // If we're in an iframe, try to get parent origin
    if (window.parent !== window) {
      // In Dataverse context, parent is usually same origin
      return window.location.origin;
    }
    // Direct window, likely same origin
    return window.location.origin;
  } catch {
    // Cross-origin access blocked, use "*" as fallback
    // This is less secure but necessary in some Dataverse configurations
    return "*";
  }
}

/**
 * Send filter changed message to parent and sibling iframes
 *
 * Uses BroadcastChannel for reliable cross-iframe communication
 * (CalendarSidePane and EventsPage are sibling iframes under Dataverse shell).
 *
 * @param filter - New filter state (null means cleared)
 */
export function sendFilterChanged(filter: CalendarFilterOutput | null): void {
  const message: CalendarFilterChangedMessage = {
    type: MESSAGE_TYPES.CALENDAR_FILTER_CHANGED,
    payload: { filter },
  };

  const targetOrigin = getTargetOrigin();

  try {
    // PRIMARY: Use BroadcastChannel for cross-iframe communication
    // This works across sibling iframes (CalendarSidePane and EventsPage)
    const channel = getEventsChannel();
    if (channel) {
      channel.postMessage(message);
      console.log("[CalendarSidePane] Filter sent via BroadcastChannel:", filter);
    }

    // FALLBACK: Try sending to parent window (iframe context)
    if (window.parent !== window) {
      window.parent.postMessage(message, targetOrigin);
    }

    // Also dispatch as custom event for same-window scenarios
    window.dispatchEvent(
      new CustomEvent("calendar-filter-changed", { detail: { filter } })
    );

    console.log("[CalendarSidePane] Filter changed message sent:", filter);
  } catch (error) {
    console.error("[CalendarSidePane] Failed to send filter message:", error);
  }
}

/**
 * Send calendar ready message to parent and sibling iframes
 *
 * @param currentFilter - Initial filter state
 */
export function sendCalendarReady(currentFilter: CalendarFilterOutput | null): void {
  const message: CalendarReadyMessage = {
    type: MESSAGE_TYPES.CALENDAR_READY,
    payload: { currentFilter },
  };

  const targetOrigin = getTargetOrigin();

  try {
    // PRIMARY: Use BroadcastChannel for cross-iframe communication
    const channel = getEventsChannel();
    if (channel) {
      channel.postMessage(message);
    }

    // FALLBACK: postMessage to parent
    if (window.parent !== window) {
      window.parent.postMessage(message, targetOrigin);
    }

    window.dispatchEvent(
      new CustomEvent("calendar-ready", { detail: { currentFilter } })
    );

    console.log("[CalendarSidePane] Ready message sent");
  } catch (error) {
    console.error("[CalendarSidePane] Failed to send ready message:", error);
  }
}

/**
 * Send close request message to parent
 */
export function sendCloseRequest(): void {
  const message: CalendarCloseMessage = {
    type: MESSAGE_TYPES.CALENDAR_CLOSE,
  };

  const targetOrigin = getTargetOrigin();

  try {
    if (window.parent !== window) {
      window.parent.postMessage(message, targetOrigin);
    }

    window.dispatchEvent(new CustomEvent("calendar-close"));

    console.log("[CalendarSidePane] Close request sent");
  } catch (error) {
    console.error("[CalendarSidePane] Failed to send close message:", error);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Message Receiving
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Callback type for event dates update
 */
export type EventDatesUpdateCallback = (eventDates: IEventDateInfo[]) => void;

/**
 * Set up listener for messages from parent window
 *
 * @param onEventDatesUpdate - Callback when event dates are received
 * @returns Cleanup function to remove listener
 */
export function setupMessageListener(
  onEventDatesUpdate: EventDatesUpdateCallback
): () => void {
  const handleMessage = (event: MessageEvent) => {
    // Basic validation - check if it's a valid message
    if (!event.data || typeof event.data !== "object") {
      return;
    }

    const data = event.data as { type?: string; payload?: unknown };

    switch (data.type) {
      case MESSAGE_TYPES.CALENDAR_EVENTS_UPDATE: {
        const payload = data.payload as { eventDates?: IEventDateInfo[] };
        if (Array.isArray(payload?.eventDates)) {
          console.log("[CalendarSidePane] Received event dates update:", payload.eventDates.length, "dates");
          onEventDatesUpdate(payload.eventDates);
        }
        break;
      }
      // Add more message handlers here as needed
    }
  };

  // Listen for postMessage
  window.addEventListener("message", handleMessage);

  // Also listen for custom events (same-window scenarios)
  const handleCustomEvent = (event: CustomEvent<{ eventDates?: IEventDateInfo[] }>) => {
    if (Array.isArray(event.detail?.eventDates)) {
      onEventDatesUpdate(event.detail.eventDates);
    }
  };

  window.addEventListener(
    "calendar-events-update" as keyof WindowEventMap,
    handleCustomEvent as EventListener
  );

  // Return cleanup function
  return () => {
    window.removeEventListener("message", handleMessage);
    window.removeEventListener(
      "calendar-events-update" as keyof WindowEventMap,
      handleCustomEvent as EventListener
    );
  };
}
