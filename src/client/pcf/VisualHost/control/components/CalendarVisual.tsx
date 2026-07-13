/**
 * CalendarVisual container (PCF-side)
 * Owns the Dataverse fetch for the calendar day-detail popover, maps raw
 * records to the presentational `ICalendarEventRecord` shape, and renders the
 * pure @spaarke/visuals `CalendarVisual` with data passed in as props.
 *
 * VHVU-050 — data flow inverted: the presentational component (in
 * @spaarke/visuals) no longer touches webApi/FetchXML. This container is the
 * single seam where the calendar reaches Dataverse. ChartRenderer still imports
 * `CalendarVisual` + `ICalendarEvent` from here, unchanged.
 */

import * as React from 'react';
import { useState, useEffect } from 'react';
import type { IChartDefinition, DrillInteraction } from '../types';
import type { IConfigWebApi } from '../services/ConfigurationLoader';
import { logger } from '../utils/logger';
import {
  CalendarVisual as CalendarVisualView,
  type ICalendarEvent,
  type ICalendarEventRecord,
} from '../../../../shared/Spaarke.Visuals/src/components/CalendarVisual';

// Re-export the presentational event type so existing importers
// (ChartRenderer) keep their `from './CalendarVisual'` path.
export type { ICalendarEvent } from '../../../../shared/Spaarke.Visuals/src/components/CalendarVisual';

export interface ICalendarVisualProps {
  /** Aggregated events (badge counts) — used when no fetch is possible */
  events: ICalendarEvent[];
  /** Initial month to display */
  initialMonth?: Date;
  /** Title */
  title?: string;
  /** Callback when a day is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Whether to show navigation buttons */
  showNavigation?: boolean;
  /** Chart definition — required for the day-detail fetch (v1.4.24). */
  chartDefinition?: IChartDefinition;
  /** WebAPI for the day-detail fetch (v1.4.24). */
  webApi?: IConfigWebApi;
  /** Context record id passed by VisualHostRoot (optional filter). */
  contextRecordId?: string;
}

/**
 * v1.4.24 — Map a fetched Dataverse record to a calendar event for the popover.
 * Generic: tries chartDefinition.sprk_groupbyfield as the date attribute,
 * falls back to sprk_finalduedate / sprk_duedate (both common on sprk_event).
 * Event type name + color resolve via any `<alias>.sprk_name` /
 * `<alias>.sprk_eventtypecolor` key so the FetchXML's link-entity alias
 * (e.g. `evtype`, `eventtype`) doesn't have to be standardized.
 */
function mapRecordToEvent(
  record: Record<string, unknown>,
  entityName: string,
  dateField: string | undefined
): ICalendarEventRecord | null {
  const primaryIdAttr = `${entityName}id`;
  const id = (record[primaryIdAttr] as string) || (record.sprk_eventid as string) || '';
  const name = (record.sprk_eventname as string) || (record[`${entityName}name`] as string) || 'Untitled';

  // Resolve the bucketing date: configured field → finalduedate → duedate.
  const candidates = [dateField, 'sprk_finalduedate', 'sprk_duedate'].filter((f): f is string => !!f);
  let dateStr: string | undefined;
  for (const f of candidates) {
    const v = record[f] as string | undefined;
    if (v) {
      dateStr = v;
      break;
    }
  }
  if (!dateStr) return null;
  const date = new Date(dateStr);
  if (isNaN(date.getTime())) return null;

  // Find alias-keyed event-type attrs without hard-coding the link-entity alias.
  let typeName: string | undefined;
  let typeColor: string | undefined;
  for (const key of Object.keys(record)) {
    if (!typeName && key.endsWith('.sprk_name')) typeName = record[key] as string;
    if (!typeColor && key.endsWith('.sprk_eventtypecolor')) typeColor = record[key] as string;
  }
  if (!typeName) {
    typeName = record['_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue'] as string | undefined;
  }

  return {
    id,
    name,
    date,
    typeName,
    typeColor,
    description: record.sprk_description as string | undefined,
    assignedTo: (record['_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue'] as string) || undefined,
    entityName,
  };
}

/**
 * CalendarVisual container — fetches detailed events (when configured) and
 * renders the presentational calendar.
 */
export const CalendarVisual: React.FC<ICalendarVisualProps> = ({
  events,
  initialMonth,
  title,
  onDrillInteraction,
  drillField,
  showNavigation = true,
  chartDefinition,
  webApi,
  contextRecordId,
}) => {
  // v1.4.24 — fetched detailed events used by the day-detail popover AND
  // (when available) as the source for badge counts. `null` = no fetch made
  // → the presentational component falls back to the aggregated `events`.
  const [detailedEvents, setDetailedEvents] = useState<ICalendarEventRecord[] | null>(null);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const canFetch = !!webApi && !!chartDefinition?.sprk_fetchxmlquery;
  const entityName = chartDefinition?.sprk_entitylogicalname || chartDefinition?.sprk_sourceentity || 'sprk_event';
  const dateField = chartDefinition?.sprk_groupbyfield;

  useEffect(() => {
    if (!canFetch) {
      setDetailedEvents(null);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        setFetchError(null);
        const fetchXml = chartDefinition!.sprk_fetchxmlquery!;
        const encoded = encodeURIComponent(fetchXml);
        const result = await webApi!.retrieveMultipleRecords(entityName, `?fetchXml=${encoded}`);
        if (cancelled) return;
        const mapped = result.entities
          .map(r => mapRecordToEvent(r as Record<string, unknown>, entityName, dateField))
          .filter((e): e is ICalendarEventRecord => e !== null);
        setDetailedEvents(mapped);
      } catch (err) {
        if (cancelled) return;
        const msg = err instanceof Error ? err.message : String(err);
        logger.error('CalendarVisual', 'Failed to fetch events for modal', err);
        setFetchError(msg);
        setDetailedEvents([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [canFetch, entityName, dateField, contextRecordId, chartDefinition]);

  return (
    <CalendarVisualView
      events={events}
      initialMonth={initialMonth}
      title={title}
      onDrillInteraction={onDrillInteraction}
      drillField={drillField}
      showNavigation={showNavigation}
      detailedEvents={detailedEvents}
      fetchError={fetchError}
    />
  );
};
