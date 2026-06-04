/**
 * calendarPaneOrchestrator — Calendar side-pane lifecycle for the EventsPage.
 *
 * Lifted from the legacy 1868-line App.tsx (L375-681 + cleanup hooks). The
 * Calendar pane is registered with `Xrm.App.sidePanes` on page load (skipped
 * in dialog mode) and communicates with the EventsPage via BroadcastChannel
 * + iframe postMessage fallback.
 *
 * Mutual-exclusivity rule (preserved verbatim): when the Event detail pane
 * opens, the Calendar pane is re-registered if the user previously closed
 * it with the 'x' button (otherwise the Calendar icon vanishes from the side
 * pane menu). This is the "load-bearing" calendar behavior the rewrite
 * must NOT lose — see legacy App.tsx L392-413.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/tasks/031-eventspage-app-tsx-rewrite.poml
 * **Design ref**: design.md §11.5.3 ("the EventsPageContext + Calendar pane stay unchanged")
 */

import {
  CALENDAR_PANE_ID,
  CALENDAR_PANE_WIDTH,
  CALENDAR_WEB_RESOURCE_NAME,
  EVENT_DETAIL_PANE_ID,
  EVENT_DETAIL_WEB_RESOURCE_NAME,
  PANE_WIDTH,
} from './config';
import { getXrm } from './xrmHelpers';

/* eslint-disable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Inter-iframe messaging contract (preserved verbatim from legacy App)
// ─────────────────────────────────────────────────────────────────────────────

const CALENDAR_MESSAGE_TYPES = {
  CALENDAR_FILTER_CHANGED: 'CALENDAR_FILTER_CHANGED',
  CALENDAR_EVENTS_UPDATE: 'CALENDAR_EVENTS_UPDATE',
  CALENDAR_CLOSE: 'CALENDAR_CLOSE',
  CALENDAR_READY: 'CALENDAR_READY',
} as const;

const EVENTS_CHANNEL_NAME = 'spaarke-events-page-channel';

const CALENDAR_FILTER_STATE_KEY = 'sprk_calendar_filter_state';
const EVENT_DETAIL_STATE_KEY = 'sprk_eventdetail_state';

// ─────────────────────────────────────────────────────────────────────────────
// Calendar pane lifecycle
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Register the Calendar side pane with `Xrm.App.sidePanes`. Idempotent —
 * if the pane already exists it is simply re-selected.
 */
export async function registerCalendarPane(): Promise<void> {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) return;

  try {
    const sidePanes = xrm.App.sidePanes;
    const existing = sidePanes.getPane(CALENDAR_PANE_ID);
    if (existing) {
      existing.select();
      return;
    }
    const pane = await sidePanes.createPane({
      title: 'Date Filter: Event',
      paneId: CALENDAR_PANE_ID,
      canClose: true,
      width: CALENDAR_PANE_WIDTH,
      isSelected: true,
      imageSrc: 'WebResources/sprk_calendarline_24',
    });
    await pane.navigate({ pageType: 'webresource', webresourceName: CALENDAR_WEB_RESOURCE_NAME });
  } catch (error) {
    console.error('[EventsPage] Failed to register Calendar pane:', error);
  }
}

/**
 * Open the Event detail pane for a specific record, navigating an existing
 * pane if one is open. Preserves mutual exclusivity by re-registering the
 * Calendar pane if it has been closed with the 'x' button so its icon stays
 * available in the side-pane menu.
 *
 * @param eventId Record GUID (curly braces tolerated).
 * @param eventTypeId Optional Event Type GUID forwarded to the detail pane
 *                    via `data=eventId=...&eventType=...`.
 */
export async function openEventDetailPane(eventId: string, eventTypeId?: string): Promise<void> {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) return;

  try {
    const sidePanes = xrm.App.sidePanes;

    // Mutual exclusivity: re-register the Calendar pane if the user closed it
    // with 'x' so the icon stays visible in the menu while Event pane is open.
    const calendarPane = sidePanes.getPane(CALENDAR_PANE_ID);
    if (!calendarPane) {
      await sidePanes.createPane({
        title: 'Date Filter: Event',
        paneId: CALENDAR_PANE_ID,
        canClose: true,
        width: CALENDAR_PANE_WIDTH,
        isSelected: false,
        imageSrc: 'WebResources/sprk_calendarline_24',
      });
      const recreated = sidePanes.getPane(CALENDAR_PANE_ID);
      if (recreated) {
        await recreated.navigate({ pageType: 'webresource', webresourceName: CALENDAR_WEB_RESOURCE_NAME });
      }
    }

    const cleanId = eventId.replace(/[{}]/g, '');
    const navigationOptions = {
      pageType: 'webresource',
      webresourceName: EVENT_DETAIL_WEB_RESOURCE_NAME,
      data: `eventId=${cleanId}&eventType=${eventTypeId ?? ''}`,
    };

    const existing = sidePanes.getPane(EVENT_DETAIL_PANE_ID);
    if (existing) {
      try {
        sessionStorage.removeItem(EVENT_DETAIL_STATE_KEY);
      } catch {
        /* ignore */
      }
      await existing.navigate(navigationOptions);
      existing.select();
      return;
    }

    const newPane = await sidePanes.createPane({
      title: 'Event',
      paneId: EVENT_DETAIL_PANE_ID,
      canClose: true,
      width: PANE_WIDTH,
      isSelected: true,
      imageSrc: 'WebResources/sprk_tabaddline_24',
    });
    await newPane.navigate(navigationOptions);
  } catch (error) {
    console.error('[EventsPage] Failed to open Event detail pane:', error);
  }
}

/**
 * Close both side panes — invoked on `beforeunload`, `pagehide`, and
 * `visibilitychange` to keep the side pane menu clean when the user navigates
 * away from the EventsPage. Also clears persisted session-storage state so
 * the next mount starts clean.
 */
export function closeAllEventsPanes(): void {
  const xrm = getXrm();
  if (!xrm?.App?.sidePanes) return;
  try {
    const sidePanes = xrm.App.sidePanes;
    sidePanes.getPane(EVENT_DETAIL_PANE_ID)?.close?.();
    sidePanes.getPane(CALENDAR_PANE_ID)?.close?.();
    try {
      sessionStorage.removeItem(EVENT_DETAIL_STATE_KEY);
    } catch {
      /* ignore */
    }
    try {
      sessionStorage.removeItem(CALENDAR_FILTER_STATE_KEY);
    } catch {
      /* ignore */
    }
  } catch {
    /* best-effort cleanup */
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Calendar messaging — BroadcastChannel + iframe postMessage fallback
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Broadcast the current Event date histogram to the Calendar pane so it can
 * render indicator dots on dates with events. Uses BroadcastChannel as the
 * primary channel and falls back to iframe `postMessage` for sandboxed hosts.
 */
export function sendEventDatesToCalendar(eventDates: ReadonlyArray<{ date: string; count: number }>): void {
  const message = {
    type: CALENDAR_MESSAGE_TYPES.CALENDAR_EVENTS_UPDATE,
    payload: { eventDates },
  };

  try {
    if (typeof BroadcastChannel !== 'undefined') {
      const channel = new BroadcastChannel(EVENTS_CHANNEL_NAME);
      channel.postMessage(message);
      channel.close();
    }
  } catch {
    /* BroadcastChannel unavailable */
  }

  try {
    const iframes = document.querySelectorAll('iframe');
    iframes.forEach(iframe => {
      try {
        iframe.contentWindow?.postMessage(message, '*');
      } catch {
        /* cross-origin */
      }
    });
  } catch {
    /* DOM not available */
  }

  try {
    window.dispatchEvent(new CustomEvent('calendar-events-update', { detail: { eventDates } }));
  } catch {
    /* CustomEvent unsupported */
  }
}

/**
 * Subscribe to Calendar filter change messages emitted by the Calendar pane.
 *
 * @param onFilterChange Callback invoked with the new filter payload. The
 *                       shape is opaque here — the legacy `CalendarFilterOutput`
 *                       type lives in `@spaarke/events-components`. Hosts that
 *                       care about the typed shape can re-import it.
 * @returns A cleanup function that tears down both BroadcastChannel and
 *          window message listeners.
 */
export function subscribeToCalendarFilter(onFilterChange: (payload: unknown) => void): () => void {
  let channel: BroadcastChannel | null = null;
  let messageHandler: ((event: MessageEvent) => void) | null = null;

  try {
    if (typeof BroadcastChannel !== 'undefined') {
      channel = new BroadcastChannel(EVENTS_CHANNEL_NAME);
      channel.onmessage = e => {
        if (e?.data?.type === CALENDAR_MESSAGE_TYPES.CALENDAR_FILTER_CHANGED) {
          onFilterChange(e.data.payload);
        }
      };
    }
  } catch {
    /* BroadcastChannel unavailable */
  }

  messageHandler = (event: MessageEvent) => {
    if (event?.data?.type === CALENDAR_MESSAGE_TYPES.CALENDAR_FILTER_CHANGED) {
      onFilterChange(event.data.payload);
    }
  };
  window.addEventListener('message', messageHandler);

  return () => {
    if (channel) {
      try {
        channel.close();
      } catch {
        /* ignore */
      }
    }
    if (messageHandler) {
      window.removeEventListener('message', messageHandler);
    }
  };
}

/* eslint-enable @typescript-eslint/no-explicit-any */
