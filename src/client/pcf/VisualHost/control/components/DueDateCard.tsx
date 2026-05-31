/**
 * DueDateCard Visual Component
 * Renders a single EventDueDateCard for an event bound via lookup field.
 * Fetches event data from Dataverse and maps to shared component props.
 */

import * as React from 'react';
import { useState, useEffect } from 'react';
import { Spinner, makeStyles, tokens, Text, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { EventDueDateCard, type IEventDueDateCardProps } from './EventDueDateCard';
import type { IChartDefinition } from '../types';
import type { IConfigWebApi } from '../services/ConfigurationLoader';
import { substituteParameters } from '../services/ViewDataService';
import { logger } from '../utils/logger';

export interface IDueDateCardVisualProps {
  chartDefinition: IChartDefinition;
  webApi: IConfigWebApi;
  contextRecordId?: string;
  onClickAction?: (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => void;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
    minHeight: '80px',
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    padding: tokens.spacingVerticalM,
  },
});

/**
 * Calculate days until due date from today
 */
function calculateDaysUntilDue(dueDate: Date): {
  daysUntilDue: number;
  isOverdue: boolean;
} {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(dueDate);
  due.setHours(0, 0, 0, 0);
  const diffMs = due.getTime() - today.getTime();
  const daysUntilDue = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  return { daysUntilDue, isOverdue: daysUntilDue < 0 };
}

/**
 * Map a Dataverse event record to EventDueDateCard props
 */
function mapEventToCardProps(record: Record<string, unknown>): IEventDueDateCardProps {
  // v1.4.5 — prefer sprk_finalduedate (the canonical date after any extensions
  // or reassignments) when present; fall back to sprk_duedate (original
  // planned date) only when finalduedate is null. This matches how dates are
  // surfaced in other Spaarke event-list contexts.
  const dueDateRaw =
    (record.sprk_finalduedate as string | undefined) ||
    (record.sprk_duedate as string | undefined);
  const dueDate = dueDateRaw ? new Date(dueDateRaw) : new Date();
  const { daysUntilDue, isOverdue } = calculateDaysUntilDue(dueDate);

  // Event type from FetchXML link-entity alias or formatted value
  const eventTypeColor = (record['eventtype.sprk_eventtypecolor'] as string) || undefined;
  const eventTypeName =
    (record['_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue'] as string) ||
    (record['eventtype.sprk_name'] as string) ||
    'Event';

  return {
    eventId: (record.sprk_eventid as string) || '',
    eventName: (record.sprk_eventname as string) || 'Untitled Event',
    eventTypeName,
    dueDate,
    daysUntilDue,
    isOverdue,
    eventTypeColor: eventTypeColor || undefined,
    description: record.sprk_description as string | undefined,
    assignedTo: (record['_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue'] as string) || undefined,
  };
}

export const DueDateCardVisual: React.FC<IDueDateCardVisualProps> = ({
  chartDefinition,
  webApi,
  contextRecordId,
  onClickAction,
}) => {
  const styles = useStyles();
  const [cardProps, setCardProps] = useState<IEventDueDateCardProps | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isNavigating, setIsNavigating] = useState(false);

  useEffect(() => {
    fetchEventData();
  }, [chartDefinition, contextRecordId]);

  const fetchEventData = async () => {
    try {
      setLoading(true);
      setError(null);

      const entityName = chartDefinition.sprk_entitylogicalname || 'sprk_event';
      const recordId = contextRecordId;
      if (!recordId) {
        setLoading(false);
        setCardProps(null);
        return;
      }

      // v1.4.5 — Prefer the chart definition's FetchXML when set, with token
      // substitution. Falls back to the hardcoded `sprk_eventid = recordId`
      // lookup only when no FetchXML is configured (preserves the historical
      // single-event-lookup behavior for any chart def that depends on it).
      //
      // The configured FetchXML is the right path for "Matter Next Date" and
      // similar parent-context cards where contextRecordId is the parent
      // (e.g. Matter) and the query filters event records related to it via
      // sprk_regardingmatter / sprk_regardingproject / etc.
      let fetchXml: string;
      if (chartDefinition.sprk_fetchxmlquery && chartDefinition.sprk_fetchxmlquery.trim().length > 0) {
        fetchXml = substituteParameters(
          chartDefinition.sprk_fetchxmlquery,
          { contextRecordId: recordId },
          chartDefinition.sprk_fetchxmlparams || undefined
        );
      } else {
        // Hardcoded single-event-lookup fallback (pre-v1.4.5 behavior).
        const cleanRecordId = recordId.replace(/[{}]/g, '');
        fetchXml = [
          `<fetch top="1">`,
          `  <entity name="${entityName}">`,
          `    <attribute name="sprk_eventid" />`,
          `    <attribute name="sprk_eventname" />`,
          `    <attribute name="sprk_duedate" />`,
          `    <attribute name="sprk_description" />`,
          `    <attribute name="sprk_assignedto" />`,
          `    <attribute name="sprk_eventtype_ref" />`,
          `    <link-entity name="sprk_eventtype_ref" from="sprk_eventtype_refid" to="sprk_eventtype_ref" link-type="outer" alias="eventtype">`,
          `      <attribute name="sprk_name" />`,
          `      <attribute name="sprk_eventtypecolor" />`,
          `    </link-entity>`,
          `    <filter type="and">`,
          `      <condition attribute="sprk_eventid" operator="eq" value="${cleanRecordId}" />`,
          `    </filter>`,
          `  </entity>`,
          `</fetch>`,
        ].join('');
      }

      const encodedFetchXml = encodeURIComponent(fetchXml);
      const result = await webApi.retrieveMultipleRecords(entityName, `?fetchXml=${encodedFetchXml}`);
      const record = result.entities[0];
      if (!record) {
        setLoading(false);
        setCardProps(null);
        return;
      }

      setCardProps(mapEventToCardProps(record));
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      logger.error('DueDateCardVisual', 'Failed to fetch event data', err);
      setError(`Failed to load event: ${msg}`);
    } finally {
      setLoading(false);
    }
  };

  const handleClick = async (eventId: string) => {
    if (onClickAction && !isNavigating) {
      setIsNavigating(true);
      try {
        await onClickAction(
          eventId,
          chartDefinition.sprk_entitylogicalname,
          cardProps as unknown as Record<string, unknown>
        );
      } finally {
        setIsNavigating(false);
      }
    }
  };

  if (loading) {
    return (
      <div className={styles.container}>
        <Spinner size="small" label="Loading event..." />
      </div>
    );
  }

  if (error) {
    return (
      <MessageBar intent="error">
        <MessageBarBody>{error}</MessageBarBody>
      </MessageBar>
    );
  }

  if (!cardProps) {
    return (
      <div className={styles.empty}>
        <Text size={200}>No event data available</Text>
      </div>
    );
  }

  return (
    <EventDueDateCard {...cardProps} onClick={onClickAction ? handleClick : undefined} isNavigating={isNavigating} />
  );
};
