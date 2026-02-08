/**
 * ChartRenderer Component
 * Dynamically renders the appropriate chart component based on visual type
 * Central switching logic for the Visual Host PCF control
 */

import * as React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type {
  IChartDefinition,
  IAggregatedDataPoint,
  DrillInteraction,
  VisualType,
  IChartData,
} from "../types";
import { VisualType as VT } from "../types";
import { MetricCard } from "./MetricCard";
import { BarChart } from "./BarChart";
import { LineChart } from "./LineChart";
import { DonutChart } from "./DonutChart";
import { StatusDistributionBar } from "./StatusDistributionBar";
import { CalendarVisual, type ICalendarEvent } from "./CalendarVisual";
import { MiniTable, type IMiniTableItem, type IMiniTableColumn } from "./MiniTable";
import { DueDateCardVisual } from "./DueDateCard";
import { DueDateCardListVisual } from "./DueDateCardList";
import type { IConfigWebApi } from "../services/ConfigurationLoader";

export interface IChartRendererProps {
  /** Chart definition from Dataverse */
  chartDefinition: IChartDefinition;
  /** Aggregated data for chart rendering */
  chartData?: IChartData;
  /** Callback when user interacts with chart for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Height override for the chart */
  height?: number;
  /** WebAPI for data fetching (needed by DueDateCard visuals) */
  webApi?: IConfigWebApi;
  /** Current record ID for context filtering */
  contextRecordId?: string;
  /** Callback for configured click actions */
  onClickAction?: (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => void;
  /** Callback for "View List" navigation */
  onViewListClick?: () => void;
  /** FetchXML override from PCF property (highest query priority) */
  fetchXmlOverride?: string;
}

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "100%",
    height: "100%",
    minHeight: "150px",
  },
  placeholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    padding: tokens.spacingVerticalL,
  },
  unknownType: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
  },
});

/**
 * Parse configuration JSON from chart definition
 * Returns empty object if parsing fails or input is empty
 */
const parseConfig = (jsonString?: string): Record<string, unknown> => {
  if (!jsonString) return {};
  try {
    return JSON.parse(jsonString) as Record<string, unknown>;
  } catch {
    return {};
  }
};

/**
 * Get visual type name for display
 */
const getVisualTypeName = (visualType: VisualType): string => {
  switch (visualType) {
    case VT.MetricCard:
      return "Metric Card";
    case VT.BarChart:
      return "Bar Chart";
    case VT.LineChart:
      return "Line Chart";
    case VT.AreaChart:
      return "Area Chart";
    case VT.DonutChart:
      return "Donut Chart";
    case VT.StatusBar:
      return "Status Distribution Bar";
    case VT.Calendar:
      return "Calendar";
    case VT.MiniTable:
      return "Mini Table";
    case VT.DueDateCard:
      return "Due Date Card";
    case VT.DueDateCardList:
      return "Due Date Card List";
    default:
      return `Unknown (${visualType})`;
  }
};

/**
 * ChartRenderer - Renders the appropriate chart based on visual type
 */
export const ChartRenderer: React.FC<IChartRendererProps> = ({
  chartDefinition,
  chartData,
  onDrillInteraction,
  height = 300,
  webApi,
  contextRecordId,
  onClickAction,
  onViewListClick,
  fetchXmlOverride,
}) => {
  const styles = useStyles();
  const { sprk_visualtype, sprk_name, sprk_configurationjson, sprk_groupbyfield } =
    chartDefinition;

  // Parse configuration options
  const config = parseConfig(sprk_configurationjson);

  // No data available
  if (!chartData || !chartData.dataPoints || chartData.dataPoints.length === 0) {
    // Some chart types don't need data (MetricCard, DueDateCard types fetch their own)
    if (sprk_visualtype !== VT.MetricCard
      && sprk_visualtype !== VT.DueDateCard
      && sprk_visualtype !== VT.DueDateCardList) {
      return (
        <div className={styles.placeholder}>
          <Text size={400}>No data available</Text>
          <Text size={200}>
            {getVisualTypeName(sprk_visualtype)} requires data to display
          </Text>
        </div>
      );
    }
  }

  const dataPoints = chartData?.dataPoints || [];
  const drillField = sprk_groupbyfield || "";

  switch (sprk_visualtype) {
    case VT.MetricCard: {
      // MetricCard shows a single value (first data point or total)
      const metricValue =
        dataPoints.length > 0
          ? dataPoints[0].value
          : chartData?.totalRecords || 0;
      const metricLabel = dataPoints.length > 0 ? dataPoints[0].label : sprk_name;

      return (
        <div className={styles.container}>
          <MetricCard
            value={metricValue}
            label={metricLabel}
            description={chartDefinition.sprk_description}
            trend={config.trend as "up" | "down" | "neutral" | undefined}
            trendValue={config.trendValue as number | undefined}
            onDrillInteraction={onDrillInteraction}
            drillField={drillField}
            drillValue={dataPoints.length > 0 ? dataPoints[0].fieldValue : null}
            interactive={!!onDrillInteraction}
            compact={config.compact as boolean | undefined}
          />
        </div>
      );
    }

    case VT.BarChart: {
      return (
        <BarChart
          data={dataPoints}
          title={config.showTitle !== false ? sprk_name : undefined}
          orientation={config.orientation as "vertical" | "horizontal" | undefined}
          showLabels={config.showLabels as boolean | undefined}
          showLegend={config.showLegend as boolean | undefined}
          onDrillInteraction={onDrillInteraction}
          drillField={drillField}
          height={height}
          responsive
        />
      );
    }

    case VT.LineChart:
    case VT.AreaChart: {
      return (
        <LineChart
          data={dataPoints}
          title={config.showTitle !== false ? sprk_name : undefined}
          variant={sprk_visualtype === VT.AreaChart ? "area" : "line"}
          showLegend={config.showLegend as boolean | undefined}
          onDrillInteraction={onDrillInteraction}
          drillField={drillField}
          height={height}
          lineColor={config.lineColor as string | undefined}
        />
      );
    }

    case VT.DonutChart: {
      return (
        <DonutChart
          data={dataPoints}
          title={config.showTitle !== false ? sprk_name : undefined}
          innerRadius={config.innerRadius as number | undefined}
          showCenterValue={config.showCenterValue as boolean | undefined}
          centerLabel={config.centerLabel as string | undefined}
          showLegend={config.showLegend as boolean | undefined}
          onDrillInteraction={onDrillInteraction}
          drillField={drillField}
          height={height}
        />
      );
    }

    case VT.StatusBar: {
      // StatusDistributionBar expects data with label, value, color
      const statusSegments = dataPoints.map((dp) => ({
        label: dp.label,
        value: dp.value,
        color: dp.color,
        fieldValue: dp.fieldValue,
      }));

      return (
        <StatusDistributionBar
          segments={statusSegments}
          title={config.showTitle !== false ? sprk_name : undefined}
          showLabels={config.showLabels as boolean | undefined}
          showCounts={config.showCounts as boolean | undefined}
          onDrillInteraction={onDrillInteraction}
          drillField={drillField}
          height={config.barHeight as number | undefined}
        />
      );
    }

    case VT.Calendar: {
      // Calendar expects events with date, count, and optional label/fieldValue
      // Transform dataPoints to calendar events
      const events: ICalendarEvent[] = dataPoints.map((dp) => ({
        date: dp.fieldValue instanceof Date
          ? dp.fieldValue
          : typeof dp.fieldValue === 'string'
            ? new Date(dp.fieldValue)
            : new Date(),
        count: dp.value,
        label: dp.label,
        fieldValue: dp.fieldValue,
      }));

      return (
        <CalendarVisual
          events={events}
          title={config.showTitle !== false ? sprk_name : undefined}
          onDrillInteraction={onDrillInteraction}
          drillField={drillField}
          showNavigation={config.showNavigation !== false}
        />
      );
    }

    case VT.MiniTable: {
      // MiniTable expects items with values: Record<string, string | number>
      // Transform dataPoints to table items
      const tableItems: IMiniTableItem[] = dataPoints.map((dp, index) => ({
        id: `row-${index}`,
        values: {
          label: dp.label,
          value: dp.value,
        },
        fieldValue: dp.fieldValue,
      }));

      const tableColumns: IMiniTableColumn[] = [
        { key: "label", header: "Name", width: "60%" },
        { key: "value", header: "Value", width: "40%", isValue: true },
      ];

      // If config has custom columns, use those
      const customColumns = config.columns as IMiniTableColumn[] | undefined;

      return (
        <MiniTable
          items={tableItems}
          columns={customColumns || tableColumns}
          title={config.showTitle !== false ? sprk_name : undefined}
          onDrillInteraction={onDrillInteraction}
          drillField={drillField}
          topN={config.topN as number | undefined}
          showRank={config.showRank !== false}
        />
      );
    }

    case VT.DueDateCard: {
      if (!webApi) {
        return (
          <div className={styles.placeholder}>
            <Text size={200}>DueDateCard requires WebAPI context</Text>
          </div>
        );
      }
      return (
        <DueDateCardVisual
          chartDefinition={chartDefinition}
          webApi={webApi}
          contextRecordId={contextRecordId}
          onClickAction={onClickAction}
        />
      );
    }

    case VT.DueDateCardList: {
      if (!webApi) {
        return (
          <div className={styles.placeholder}>
            <Text size={200}>DueDateCardList requires WebAPI context</Text>
          </div>
        );
      }
      return (
        <DueDateCardListVisual
          chartDefinition={chartDefinition}
          webApi={webApi}
          contextRecordId={contextRecordId}
          onClickAction={onClickAction}
          onViewListClick={onViewListClick}
          fetchXmlOverride={fetchXmlOverride}
        />
      );
    }

    default: {
      return (
        <div className={styles.unknownType}>
          <Text size={400} weight="semibold">
            Unsupported Visual Type
          </Text>
          <Text size={200}>
            Visual type {sprk_visualtype} is not yet supported.
          </Text>
          <Text size={200}>
            Supported types: MetricCard, BarChart, LineChart, AreaChart,
            DonutChart, StatusBar, Calendar, MiniTable, DueDateCard, DueDateCardList
          </Text>
        </div>
      );
    }
  }
};
