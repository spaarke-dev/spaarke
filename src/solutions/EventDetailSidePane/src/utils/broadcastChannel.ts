/**
 * BroadcastChannel Communication for EventDetailSidePane
 *
 * Enables cross-iframe communication between EventDetailSidePane
 * and EventsPage via BroadcastChannel API.
 *
 * Uses the same channel as CalendarSidePane (spaarke-events-page-channel)
 * so that EventsPage has a single listener for all side pane messages.
 *
 * @module utils/broadcastChannel
 * @see src/solutions/CalendarSidePane/src/utils/postMessage.ts
 * @see Task 103 - BroadcastChannel integration
 */

import { IEventRecord } from "../types/EventRecord";

// ─────────────────────────────────────────────────────────────────────────────
// Channel Configuration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Shared channel name - same as CalendarSidePane and EventsPage.
 * All side panes on the Events page share this channel.
 */
const EVENTS_CHANNEL_NAME = "spaarke-events-page-channel";

// ─────────────────────────────────────────────────────────────────────────────
// Message Types
// ─────────────────────────────────────────────────────────────────────────────

export const EVENT_DETAIL_MESSAGE_TYPES = {
  /** Event was saved successfully — triggers grid refresh in EventsPage */
  EVENT_SAVED: "EVENT_DETAIL_SAVED",
  /** Event detail pane loaded a record */
  EVENT_OPENED: "EVENT_DETAIL_OPENED",
  /** Event detail pane is closing */
  EVENT_CLOSED: "EVENT_DETAIL_CLOSED",
  /** Dirty state changed — informs parent of unsaved changes */
  EVENT_DIRTY_STATE: "EVENT_DETAIL_DIRTY",
  /** Parent requests navigation to a different event */
  NAVIGATE_TO_EVENT: "EVENT_DETAIL_NAVIGATE",
} as const;

// ─────────────────────────────────────────────────────────────────────────────
// Message Interfaces
// ─────────────────────────────────────────────────────────────────────────────

export interface EventSavedMessage {
  type: typeof EVENT_DETAIL_MESSAGE_TYPES.EVENT_SAVED;
  payload: {
    eventId: string;
    updatedFields: Partial<IEventRecord>;
  };
}

export interface EventOpenedMessage {
  type: typeof EVENT_DETAIL_MESSAGE_TYPES.EVENT_OPENED;
  payload: {
    eventId: string;
    eventTypeId: string;
  };
}

export interface EventClosedMessage {
  type: typeof EVENT_DETAIL_MESSAGE_TYPES.EVENT_CLOSED;
  payload: {
    eventId: string;
  };
}

export interface EventDirtyStateMessage {
  type: typeof EVENT_DETAIL_MESSAGE_TYPES.EVENT_DIRTY_STATE;
  payload: {
    eventId: string;
    isDirty: boolean;
  };
}

export type EventDetailMessage =
  | EventSavedMessage
  | EventOpenedMessage
  | EventClosedMessage
  | EventDirtyStateMessage;

// ─────────────────────────────────────────────────────────────────────────────
// Channel Management
// ─────────────────────────────────────────────────────────────────────────────

let channelInstance: BroadcastChannel | null = null;

function getChannel(): BroadcastChannel | null {
  if (typeof BroadcastChannel === "undefined") {
    return null;
  }
  if (!channelInstance) {
    channelInstance = new BroadcastChannel(EVENTS_CHANNEL_NAME);
  }
  return channelInstance;
}

/**
 * Close the BroadcastChannel. Call on component unmount.
 */
export function closeChannel(): void {
  if (channelInstance) {
    channelInstance.close();
    channelInstance = null;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Outbound Messages (EventDetailSidePane → EventsPage)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Notify parent that an event was saved successfully.
 * EventsPage uses this to refresh the grid row.
 */
export function sendEventSaved(
  eventId: string,
  updatedFields: Partial<IEventRecord>
): void {
  const message: EventSavedMessage = {
    type: EVENT_DETAIL_MESSAGE_TYPES.EVENT_SAVED,
    payload: { eventId, updatedFields },
  };

  try {
    const channel = getChannel();
    if (channel) {
      channel.postMessage(message);
      console.log("[EventDetailSidePane] Sent EVENT_SAVED via BroadcastChannel:", eventId);
    }
  } catch (error) {
    console.error("[EventDetailSidePane] Failed to send EVENT_SAVED:", error);
  }
}

/**
 * Notify parent that the side pane loaded an event.
 */
export function sendEventOpened(eventId: string, eventTypeId: string): void {
  const message: EventOpenedMessage = {
    type: EVENT_DETAIL_MESSAGE_TYPES.EVENT_OPENED,
    payload: { eventId, eventTypeId },
  };

  try {
    const channel = getChannel();
    if (channel) {
      channel.postMessage(message);
      console.log("[EventDetailSidePane] Sent EVENT_OPENED:", eventId);
    }
  } catch (error) {
    console.error("[EventDetailSidePane] Failed to send EVENT_OPENED:", error);
  }
}

/**
 * Notify parent that the side pane is closing.
 */
export function sendEventClosed(eventId: string): void {
  const message: EventClosedMessage = {
    type: EVENT_DETAIL_MESSAGE_TYPES.EVENT_CLOSED,
    payload: { eventId },
  };

  try {
    const channel = getChannel();
    if (channel) {
      channel.postMessage(message);
      console.log("[EventDetailSidePane] Sent EVENT_CLOSED:", eventId);
    }
  } catch (error) {
    console.error("[EventDetailSidePane] Failed to send EVENT_CLOSED:", error);
  }
}

/**
 * Notify parent of dirty state change.
 * Parent can use this to warn before navigating to a different event.
 */
export function sendDirtyStateChanged(eventId: string, isDirty: boolean): void {
  const message: EventDirtyStateMessage = {
    type: EVENT_DETAIL_MESSAGE_TYPES.EVENT_DIRTY_STATE,
    payload: { eventId, isDirty },
  };

  try {
    const channel = getChannel();
    if (channel) {
      channel.postMessage(message);
    }
  } catch (error) {
    console.error("[EventDetailSidePane] Failed to send dirty state:", error);
  }
}
