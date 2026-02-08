/**
 * DueDateCardList Visual Component
 * Renders a list of EventDueDateCards driven by a Dataverse view.
 * Supports context filtering and "View List" navigation.
 */

import * as React from "react";
import { useState, useEffect, useCallback } from "react";
import {
  Spinner,
  makeStyles,
  tokens,
  Text,
  Link,
  MessageBar,
  MessageBarBody,
} from "@fluentui/react-components";
import { EventDueDateCard, type IEventDueDateCardProps } from "@spaarke/ui-components/dist/components/EventDueDateCard";
import { ChevronRight20Regular } from "@fluentui/react-icons";
import type { IChartDefinition } from "../types";
import type { IConfigWebApi } from "../services/ConfigurationLoader";
import {
  resolveQuery,
  type ISubstitutionParams,
} from "../services/ViewDataService";
import { logger } from "../utils/logger";

export interface IDueDateCardListVisualProps {
  chartDefinition: IChartDefinition;
  webApi: IConfigWebApi;
  contextRecordId?: string;
  onClickAction?: (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => void;
  onViewListClick?: () => void;
  fetchXmlOverride?: string;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    gap: tokens.spacingVerticalS,
  },
  cardList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  viewListLink: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalXS,
    padding: tokens.spacingVerticalXS,
  },
  loading: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "100px",
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    padding: tokens.spacingVerticalL,
  },
});

/**
 * Calculate days until due date from today
 */
function calculateDaysUntilDue(dueDate: Date): { daysUntilDue: number; isOverdue: boolean } {
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
  const dueDate = record.sprk_duedate
    ? new Date(record.sprk_duedate as string)
    : new Date();
  const { daysUntilDue, isOverdue } = calculateDaysUntilDue(dueDate);

  const eventTypeRef = record["sprk_eventtype_ref"] as Record<string, unknown> | undefined;
  const eventTypeColor = eventTypeRef?.sprk_eventtypecolor as string | undefined;
  const eventTypeName = (record["_sprk_eventtypeid_value@OData.Community.Display.V1.FormattedValue"] as string)
    || (eventTypeRef?.sprk_name as string)
    || "Event";

  return {
    eventId: (record.sprk_eventid as string) || "",
    eventName: (record.sprk_eventname as string) || (record.sprk_name as string) || "Untitled Event",
    eventTypeName,
    dueDate,
    daysUntilDue,
    isOverdue,
    eventTypeColor: eventTypeColor || undefined,
    description: record.sprk_description as string | undefined,
    assignedTo: (record["_sprk_assignedtoid_value@OData.Community.Display.V1.FormattedValue"] as string) || undefined,
  };
}

export const DueDateCardListVisual: React.FC<IDueDateCardListVisualProps> = ({
  chartDefinition,
  webApi,
  contextRecordId,
  onClickAction,
  onViewListClick,
  fetchXmlOverride,
}) => {
  const styles = useStyles();
  const [cards, setCards] = useState<IEventDueDateCardProps[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [navigatingId, setNavigatingId] = useState<string | null>(null);

  const maxItems = chartDefinition.sprk_maxdisplayitems || 10;
  const showViewListLink = !!chartDefinition.sprk_viewlisttabname;

  useEffect(() => {
    fetchEvents();
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

      if (resolved.source !== "directEntity" && resolved.fetchXml) {
        // Execute the resolved FetchXML (from override, custom, or view)
        const encodedFetchXml = encodeURIComponent(resolved.fetchXml);
        const result = await webApi.retrieveMultipleRecords(
          resolved.entityName,
          `?fetchXml=${encodedFetchXml}`
        );
        setCards(result.entities.map(mapEventToCardProps));
      } else {
        // Fallback: direct OData query (no FetchXML source available)
        const entityName = chartDefinition.sprk_entitylogicalname || "sprk_event";
        const select = "sprk_eventid,sprk_eventname,sprk_name,sprk_duedate,sprk_description,_sprk_assignedtoid_value,_sprk_eventtypeid_value";
        const expand = "sprk_eventtype_ref($select=sprk_name,sprk_eventtypecolor)";

        let queryOptions = `?$select=${select}&$expand=${expand}&$top=${maxItems}&$orderby=sprk_duedate asc`;

        // Add context filter if configured
        if (chartDefinition.sprk_contextfieldname && contextRecordId) {
          const filterField = chartDefinition.sprk_contextfieldname;
          const cleanId = contextRecordId.replace(/[{}]/g, "");
          queryOptions += `&$filter=${filterField} eq '${cleanId}'`;
        }

        const result = await webApi.retrieveMultipleRecords(entityName, queryOptions);
        setCards(result.entities.map(mapEventToCardProps));
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      logger.error("DueDateCardListVisual", "Failed to fetch events", err);
      setError(`Failed to load events: ${msg}`);
    } finally {
      setLoading(false);
    }
  };

  const handleCardClick = useCallback(async (eventId: string) => {
    if (onClickAction && !navigatingId) {
      setNavigatingId(eventId);
      try {
        const record = cards.find(c => c.eventId === eventId);
        await onClickAction(eventId, chartDefinition.sprk_entitylogicalname, record as unknown as Record<string, unknown>);
      } finally {
        setNavigatingId(null);
      }
    }
  }, [onClickAction, navigatingId, cards, chartDefinition]);

  if (loading) {
    return (
      <div className={styles.loading}>
        <Spinner size="small" label="Loading events..." />
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

  if (cards.length === 0) {
    return (
      <div className={styles.empty}>
        <Text size={200}>No upcoming events</Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.cardList}>
        {cards.map((card) => (
          <EventDueDateCard
            key={card.eventId}
            {...card}
            onClick={onClickAction ? handleCardClick : undefined}
            isNavigating={navigatingId === card.eventId}
          />
        ))}
      </div>

      {showViewListLink && (
        <div className={styles.viewListLink}>
          <Link onClick={onViewListClick}>
            View All <ChevronRight20Regular />
          </Link>
        </div>
      )}
    </div>
  );
};
