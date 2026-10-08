/**
 * DueDateCardList container (PCF-side)
 * Fetches a Dataverse view of events, maps to EventDueDateCard props, owns
 * record navigation (shared getXrm), and renders the pure @spaarke/visuals
 * `DueDateCardList`.
 *
 * VHVU-050 — data flow inverted: the presentational component (in
 * @spaarke/visuals) no longer touches webApi / window.Xrm / FetchXML.
 * ChartRenderer still imports `DueDateCardListVisual` from here, unchanged.
 */

import * as React from 'react';
import { useState, useEffect, useCallback } from 'react';
import { DueDateCardList, type IEventDueDateCardProps } from '@spaarke/visuals';
import { OOB_MODAL_SIZES } from '../../../../shared/Spaarke.UI.Components/src/utils/adapters/oobModalSizes';
import { cleanGuid } from '@spaarke/ui-components';
import type { IChartDefinition } from '../types';
import type { IConfigWebApi } from '../services/ConfigurationLoader';
import { resolveQuery, injectContextFilter, type ISubstitutionParams } from '../services/ViewDataService';
import { logger } from '../utils/logger';
import { mapEventToCardProps } from '../utils/eventDueDate';
import { getXrm } from '../../../../shared/Spaarke.UI.Components/src/utils/xrmContext';

export interface IDueDateCardListVisualProps {
  chartDefinition: IChartDefinition;
  webApi: IConfigWebApi;
  contextRecordId?: string;
  onClickAction?: (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => void;
  onViewListClick?: () => void;
  fetchXmlOverride?: string;
}

export const DueDateCardListVisual: React.FC<IDueDateCardListVisualProps> = ({
  chartDefinition,
  webApi,
  contextRecordId,
  onClickAction,
  onViewListClick,
  fetchXmlOverride,
}) => {
  const [cards, setCards] = useState<IEventDueDateCardProps[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [navigatingId, setNavigatingId] = useState<string | null>(null);

  const maxItems = chartDefinition.sprk_maxdisplayitems || 10;
  const showViewListLink = !!chartDefinition.sprk_viewlisttabname;

  useEffect(() => {
    fetchEvents();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chartDefinition, contextRecordId]);

  const fetchEvents = async () => {
    try {
      setLoading(true);
      setError(null);

      // Build substitution params from runtime context
      const substitutionParams: ISubstitutionParams = {
        contextRecordId: contextRecordId || undefined,
      };

      // Use query priority resolution:
      // Priority: PCF override → Custom FetchXML → View → Direct entity query
      const resolved = await resolveQuery({
        chartDefinition,
        fetchXmlOverride: fetchXmlOverride || undefined,
        substitutionParams,
        webApi,
      });

      if (resolved.source !== 'directEntity' && resolved.fetchXml) {
        // Inject context filter if configured (filters to current record's related events)
        let fetchXml = resolved.fetchXml;
        if (chartDefinition.sprk_contextfieldname && contextRecordId) {
          const filterField = chartDefinition.sprk_contextfieldname.replace(/^_/, '').replace(/_value$/, '');
          const cleanId = cleanGuid(contextRecordId);
          fetchXml = injectContextFilter(fetchXml, filterField, cleanId);
        }

        // Execute the resolved FetchXML (from override, custom, or view)
        const encodedFetchXml = encodeURIComponent(fetchXml);
        const result = await webApi.retrieveMultipleRecords(resolved.entityName, `?fetchXml=${encodedFetchXml}`);
        setCards(
          result.entities
            .map(record => mapEventToCardProps(record))
            .filter((c): c is IEventDueDateCardProps => c !== null)
        );
      } else {
        // Fallback: FetchXML query with link-entity for event type
        // Uses attribute names (not navigation property names) for reliable cross-environment support
        const entityName = chartDefinition.sprk_entitylogicalname || 'sprk_event';
        let contextCondition = '';
        if (chartDefinition.sprk_contextfieldname && contextRecordId) {
          const filterField = chartDefinition.sprk_contextfieldname.replace(/^_/, '').replace(/_value$/, '');
          const cleanId = cleanGuid(contextRecordId);
          contextCondition = `<condition attribute="${filterField}" operator="eq" value="${cleanId}" />`;
        }

        const fallbackFetchXml = [
          `<fetch top="${maxItems}">`,
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
          `    <order attribute="sprk_duedate" />`,
          contextCondition ? `    <filter type="and">${contextCondition}</filter>` : '',
          `  </entity>`,
          `</fetch>`,
        ]
          .filter(Boolean)
          .join('');

        const encodedFallback = encodeURIComponent(fallbackFetchXml);
        const result = await webApi.retrieveMultipleRecords(entityName, `?fetchXml=${encodedFallback}`);
        setCards(
          result.entities
            .map(record => mapEventToCardProps(record))
            .filter((c): c is IEventDueDateCardProps => c !== null)
        );
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      logger.error('DueDateCardListVisual', 'Failed to fetch events', err);
      setError(`Failed to load events: ${msg}`);
    } finally {
      setLoading(false);
    }
  };

  const handleCardClick = useCallback(
    async (eventId: string) => {
      if (navigatingId) return;
      setNavigatingId(eventId);
      try {
        const entityName = chartDefinition.sprk_entitylogicalname || 'sprk_event';
        // Shared walker (task 081 round 4, review F2) — this read window.Xrm only.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm: any = getXrm('navigation');

        if (xrm?.Navigation?.navigateTo) {
          // Open event record form as a modal dialog at the `record` OOB size
          // (85%×85% — record-modal-selection.md invariant; was 80%×80%).
          await xrm.Navigation.navigateTo(
            { pageType: 'entityrecord', entityName, entityId: eventId },
            {
              target: 2,
              position: 1,
              width: OOB_MODAL_SIZES.record.width,
              height: OOB_MODAL_SIZES.record.height,
            }
          );
        } else if (onClickAction) {
          // Fallback to generic click action if Xrm not available
          const record = cards.find(c => c.eventId === eventId);
          await onClickAction(eventId, entityName, record as unknown as Record<string, unknown>);
        }
      } catch (err) {
        logger.error('DueDateCardListVisual', 'Failed to open event form dialog', err);
      } finally {
        setNavigatingId(null);
      }
    },
    [navigatingId, chartDefinition, onClickAction, cards]
  );

  return (
    <DueDateCardList
      cards={cards}
      loading={loading}
      error={error}
      navigatingId={navigatingId}
      onCardClick={handleCardClick}
      showViewListLink={showViewListLink}
      onViewListClick={onViewListClick}
    />
  );
};
