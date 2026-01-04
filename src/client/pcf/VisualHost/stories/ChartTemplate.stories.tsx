/**
 * Chart Component Story Template
 * Use this as a starting point for new chart component stories
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import {
  Card,
  CardHeader,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { VisualType, AggregationType } from "../control/types";
import type { IChartDefinition, IDrillInteraction } from "../control/types";

// Sample chart definition for stories
const sampleChartDefinition: IChartDefinition = {
  sprk_chartdefinitionid: "sample-001",
  sprk_name: "Sample Chart",
  sprk_description: "A sample chart for Storybook development",
  sprk_visualtype: VisualType.MetricCard,
  sprk_aggregationtype: AggregationType.Count,
  sprk_sourceentity: "account",
  sprk_configurationjson: JSON.stringify({
    primaryColor: "#0078D4",
    showTrend: true,
  }),
};

// Placeholder component until real chart components are built
const useStyles = makeStyles({
  placeholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "200px",
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  visualType: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  drillInfo: {
    marginTop: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusSmall,
    fontFamily: "monospace",
    fontSize: tokens.fontSizeBase200,
  },
});

interface IChartPlaceholderProps {
  definition: IChartDefinition;
  onDrillInteraction?: (interaction: IDrillInteraction) => void;
}

/**
 * ChartPlaceholder - Temporary component for story development
 * This will be replaced by actual chart implementations in tasks 010-016
 */
const ChartPlaceholder: React.FC<IChartPlaceholderProps> = ({
  definition,
  onDrillInteraction,
}) => {
  const styles = useStyles();

  const handleClick = () => {
    if (onDrillInteraction) {
      const interaction: IDrillInteraction = {
        field: "sample_field",
        operator: "eq",
        value: "sample_value",
        label: "Sample Filter",
      };
      onDrillInteraction(interaction);
    }
  };

  const visualTypeName = VisualType[definition.sprk_visualtype];
  const aggregationTypeName = AggregationType[definition.sprk_aggregationtype || 0];

  return (
    <Card onClick={handleClick} style={{ cursor: onDrillInteraction ? "pointer" : "default" }}>
      <CardHeader
        header={<Text weight="semibold">{definition.sprk_name}</Text>}
        description={<Text size={200}>{definition.sprk_description}</Text>}
      />
      <div className={styles.placeholder}>
        <Text className={styles.visualType}>{visualTypeName}</Text>
        <Text size={200}>Coming in Task 010-016</Text>
        <Text size={100}>Aggregation: {aggregationTypeName}</Text>
        <Text size={100}>Source: {definition.sprk_sourceentity}</Text>
        {onDrillInteraction && (
          <div className={styles.drillInfo}>
            Click to trigger drill interaction
          </div>
        )}
      </div>
    </Card>
  );
};

// Story metadata
const meta: Meta<typeof ChartPlaceholder> = {
  title: "Charts/Template",
  component: ChartPlaceholder,
  parameters: {
    layout: "centered",
    docs: {
      description: {
        component:
          "Template for chart component stories. Use this as a starting point when implementing actual chart components.",
      },
    },
  },
  tags: ["autodocs"],
  argTypes: {
    definition: {
      description: "Chart definition from sprk_chartdefinition entity",
      control: "object",
    },
    onDrillInteraction: {
      description: "Callback when user clicks to drill through",
      action: "drillInteraction",
    },
  },
};

export default meta;
type Story = StoryObj<typeof ChartPlaceholder>;

// Default story
export const Default: Story = {
  args: {
    definition: sampleChartDefinition,
    onDrillInteraction: action("drillInteraction"),
  },
};

// MetricCard variant
export const MetricCard: Story = {
  args: {
    definition: {
      ...sampleChartDefinition,
      sprk_chartdefinitionid: "metric-001",
      sprk_name: "Total Accounts",
      sprk_description: "Count of active accounts",
      sprk_visualtype: VisualType.MetricCard,
      sprk_aggregationtype: AggregationType.Count,
    },
    onDrillInteraction: action("drillInteraction"),
  },
};

// BarChart variant
export const BarChart: Story = {
  args: {
    definition: {
      ...sampleChartDefinition,
      sprk_chartdefinitionid: "bar-001",
      sprk_name: "Revenue by Region",
      sprk_description: "Sum of revenue grouped by region",
      sprk_visualtype: VisualType.BarChart,
      sprk_aggregationtype: AggregationType.Sum,
    },
    onDrillInteraction: action("drillInteraction"),
  },
};

// Without drill interaction
export const NoDrill: Story = {
  args: {
    definition: {
      ...sampleChartDefinition,
      sprk_name: "Static Display",
      sprk_description: "Chart without drill-through capability",
    },
    onDrillInteraction: undefined,
  },
};

// All visual types showcase
export const AllVisualTypes: Story = {
  render: () => {
    const visualTypes = [
      VisualType.MetricCard,
      VisualType.BarChart,
      VisualType.LineChart,
      VisualType.DonutChart,
      VisualType.StatusBar,
      VisualType.Calendar,
      VisualType.MiniTable,
    ];

    return (
      <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: "1rem", padding: "1rem" }}>
        {visualTypes.map((visualType) => (
          <ChartPlaceholder
            key={visualType}
            definition={{
              ...sampleChartDefinition,
              sprk_chartdefinitionid: `type-${visualType}`,
              sprk_name: VisualType[visualType],
              sprk_visualtype: visualType,
            }}
            onDrillInteraction={action(`drill-${VisualType[visualType]}`)}
          />
        ))}
      </div>
    );
  },
};
