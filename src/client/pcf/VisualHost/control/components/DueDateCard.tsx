/**
 * DueDateCard Visual Component
 * Renders a single EventDueDateCard for an event bound via lookup field.
 * Fetches event data from Dataverse and maps to shared component props.
 */

import * as React from "react";
import { useState, useEffect } from "react";
import { Spinner, makeStyles, tokens, Text, MessageBar, MessageBarBody } from "@fluentui/react-components";
import { EventDueDateCard, type IEventDueDateCardProps } from "@spaarke/ui-components/dist/components/EventDueDateCard";
import type { IChartDefinition } from "../types";
import type { IConfigWebApi } from "../services/ConfigurationLoader";
import { logger } from "../utils/logger";

export interface IDueDateCardVisualProps {
  chartDefinition: IChartDefinition;
  webApi: IConfigWebApi;
  contextRecordId?: string;
  onClickAction?: (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => void;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "100%",
    minHeight: "80px",
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    padding: tokens.spacingVerticalM,
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

  // Event type color from expanded lookup
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

      // For single card, we need a record ID - either from context or from the chart definition
      const recordId = contextRecordId;
      if (!recordId) {
        setLoading(false);
        setCardProps(null);
        return;
      }

      const entityName = chartDefinition.sprk_entitylogicalname || "sprk_event";
      const select = "sprk_eventid,sprk_eventname,sprk_name,sprk_duedate,sprk_description,_sprk_assignedtoid_value,_sprk_eventtypeid_value";
      const expand = "sprk_eventtype_ref($select=sprk_name,sprk_eventtypecolor)";

      const record = await webApi.retrieveRecord(
        entityName,
        recordId.replace(/[{}]/g, ""),
        `?$select=${select}&$expand=${expand}`
      );

      setCardProps(mapEventToCardProps(record));
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      logger.error("DueDateCardVisual", "Failed to fetch event data", err);
      setError(`Failed to load event: ${msg}`);
    } finally {
      setLoading(false);
    }
  };

  const handleClick = async (eventId: string) => {
    if (onClickAction && !isNavigating) {
      setIsNavigating(true);
      try {
        await onClickAction(eventId, chartDefinition.sprk_entitylogicalname, cardProps as unknown as Record<string, unknown>);
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
    <EventDueDateCard
      {...cardProps}
      onClick={onClickAction ? handleClick : undefined}
      isNavigating={isNavigating}
    />
  );
};
