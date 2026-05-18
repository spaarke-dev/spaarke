/**
 * @spaarke/ai-widgets — GenericTextWidget
 *
 * Fallback workspace widget rendered by WorkspaceWidgetRegistry when a
 * widget type is unrecognised or its factory fails to load.
 *
 * Renders the raw data payload as formatted JSON inside a scrollable block
 * so developers and support staff can see exactly what the server sent.
 * Uses Fluent UI v9 tokens for all styling — no hard-coded colors, dark-mode
 * compatible.
 *
 * React 19, NOT PCF-safe.
 */

import React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Card,
  CardHeader,
  mergeClasses,
} from '@fluentui/react-components';
import { WarningRegular } from '@fluentui/react-icons';
import type { WorkspaceWidgetProps } from '../types/widget-types';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
  },
  card: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
  },
  headerIcon: {
    color: tokens.colorPaletteYellowForeground2,
    fontSize: tokens.fontSizeBase500,
    flexShrink: 0,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  headerSubtitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  preContainer: {
    flex: 1,
    overflow: 'auto',
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    padding: tokens.spacingHorizontalM,
    minHeight: 0,
  },
  pre: {
    margin: 0,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-all',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * GenericTextWidget — fallback for unregistered or failed workspace widget types.
 *
 * Displays:
 * - A yellow warning header: "Unknown widget type: <widgetType>"
 * - The raw data payload serialised as formatted JSON in a scrollable pre block.
 *
 * This widget is never registered by name — WorkspaceWidgetRegistry loads it
 * directly as a fallback and injects it into the resolved component cache.
 */
const GenericTextWidget: React.FC<WorkspaceWidgetProps> = ({
  data,
  widgetType,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();

  // Serialise data for display, falling back gracefully on circular refs.
  let jsonText: string;
  try {
    jsonText = JSON.stringify(data, null, 2);
  } catch {
    jsonText = String(data);
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Card className={styles.card}>
        <CardHeader
          image={<WarningRegular className={styles.headerIcon} />}
          header={
            <Text className={styles.headerTitle}>
              Unknown widget type
            </Text>
          }
          description={
            <Text className={styles.headerSubtitle}>
              {`"${widgetType}" is not registered. Showing raw payload.`}
            </Text>
          }
        />

        {/* Loading state */}
        {isLoading && (
          <Text style={{ padding: tokens.spacingHorizontalM, color: tokens.colorNeutralForeground3 }}>
            Loading…
          </Text>
        )}

        {/* Error state */}
        {error && (
          <Text
            style={{
              padding: tokens.spacingHorizontalM,
              color: tokens.colorPaletteRedForeground1,
            }}
          >
            {error}
          </Text>
        )}

        {/* Raw JSON payload */}
        {!isLoading && !error && (
          <div className={styles.preContainer}>
            <pre className={styles.pre}>{jsonText}</pre>
          </div>
        )}
      </Card>
    </div>
  );
};

export default GenericTextWidget;
