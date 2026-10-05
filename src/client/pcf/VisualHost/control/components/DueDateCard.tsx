/**
 * DueDateCard container (PCF-side)
 * Fetches a single event from Dataverse, maps it to EventDueDateCard props,
 * and renders the pure @spaarke/visuals `DueDateCard`. Owns record navigation.
 *
 * VHVU-050 — data flow inverted: the presentational component (in
 * @spaarke/visuals) no longer touches webApi/FetchXML. ChartRenderer still
 * imports `DueDateCardVisual` from here, unchanged.
 */

import * as React from 'react';
import { useState, useEffect } from 'react';
import { DueDateCard, type IEventDueDateCardProps } from '@spaarke/visuals';
import { cleanGuid } from '@spaarke/ui-components';
import type { IChartDefinition } from '../types';
import type { IConfigWebApi } from '../services/ConfigurationLoader';
import { substituteParameters } from '../services/ViewDataService';
import { logger } from '../utils/logger';
import { mapEventToCardProps } from '../utils/eventDueDate';

export interface IDueDateCardVisualProps {
  chartDefinition: IChartDefinition;
  webApi: IConfigWebApi;
  contextRecordId?: string;
  onClickAction?: (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => void;
}

export const DueDateCardVisual: React.FC<IDueDateCardVisualProps> = ({
  chartDefinition,
  webApi,
  contextRecordId,
  onClickAction,
}) => {
  const [cardProps, setCardProps] = useState<IEventDueDateCardProps | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isNavigating, setIsNavigating] = useState(false);

  useEffect(() => {
    fetchEventData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
        const cleanRecordId = cleanGuid(recordId);
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

  return (
    <DueDateCard
      cardProps={cardProps}
      loading={loading}
      error={error}
      onCardClick={onClickAction ? handleClick : undefined}
      isNavigating={isNavigating}
    />
  );
};
