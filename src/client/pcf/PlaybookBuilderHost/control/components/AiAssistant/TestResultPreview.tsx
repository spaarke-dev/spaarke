/**
 * Test Result Preview - Display and download test execution results
 *
 * Shows node-by-node results in expandable sections with:
 * - Accordion for each node's output
 * - JSON formatting for readability
 * - Download full results as JSON
 * - Copy individual values to clipboard
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useCallback, useMemo, useState } from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Text,
  Badge,
  Tooltip,
  makeStyles,
  tokens,
  shorthands,
  mergeClasses,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import {
  ArrowDownload20Regular,
  Copy20Regular,
  Checkmark16Regular,
  Dismiss16Regular,
  ArrowForward16Regular,
  Code20Regular,
  DocumentText20Regular,
} from '@fluentui/react-icons';
import {
  useAiAssistantStore,
  type TestNodeProgress,
} from '../../stores/aiAssistantStore';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.gap(tokens.spacingHorizontalM),
  },
  headerLeft: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  accordion: {
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  accordionPanel: {
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  nodeHeader: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    width: '100%',
  },
  nodeIcon: {
    flexShrink: 0,
  },
  nodeIconCompleted: {
    color: tokens.colorPaletteGreenForeground1,
  },
  nodeIconFailed: {
    color: tokens.colorPaletteRedForeground1,
  },
  nodeIconSkipped: {
    color: tokens.colorNeutralForeground3,
  },
  nodeLabel: {
    flex: 1,
    ...shorthands.overflow('hidden'),
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  nodeDuration: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    flexShrink: 0,
  },
  outputContainer: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  outputHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  codeBlock: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    ...shorthands.overflow('auto'),
    maxHeight: '300px',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  errorMessage: {
    color: tokens.colorPaletteRedForeground1,
    backgroundColor: tokens.colorPaletteRedBackground1,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
  },
  copyButton: {
    minWidth: 'auto',
  },
  actions: {
    display: 'flex',
    justifyContent: 'flex-end',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL),
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  emptyIcon: {
    fontSize: '48px',
    marginBottom: tokens.spacingVerticalM,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format duration in ms to human-readable string.
 */
const formatDuration = (ms: number): string => {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60000);
  const seconds = ((ms % 60000) / 1000).toFixed(0);
  return `${minutes}m ${seconds}s`;
};

/**
 * Format JSON for display with proper indentation.
 */
const formatJson = (obj: unknown): string => {
  try {
    return JSON.stringify(obj, null, 2);
  } catch {
    return String(obj);
  }
};

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface TestResultPreviewProps {
  /** Optional callback when download is clicked */
  onDownload?: (data: string, filename: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Node Result Component
// ─────────────────────────────────────────────────────────────────────────────

interface NodeResultProps {
  node: TestNodeProgress;
  onCopy: (text: string) => Promise<void>;
}

const NodeResult: React.FC<NodeResultProps> = ({ node, onCopy }) => {
  const styles = useStyles();
  const [copySuccess, setCopySuccess] = useState(false);

  // Get status icon
  const getStatusIcon = () => {
    switch (node.status) {
      case 'completed':
        return <Checkmark16Regular className={styles.nodeIconCompleted} />;
      case 'failed':
        return <Dismiss16Regular className={styles.nodeIconFailed} />;
      case 'skipped':
        return <ArrowForward16Regular className={styles.nodeIconSkipped} />;
      default:
        return null;
    }
  };

  // Handle copy
  const handleCopy = useCallback(async () => {
    if (node.output) {
      await onCopy(formatJson(node.output));
      setCopySuccess(true);
      setTimeout(() => setCopySuccess(false), 2000);
    }
  }, [node.output, onCopy]);

  const hasOutput = node.output && Object.keys(node.output).length > 0;

  return (
    <div className={styles.outputContainer}>
      {/* Error message if failed */}
      {node.error && (
        <div className={styles.errorMessage}>
          <Text weight="semibold">Error: </Text>
          <Text>{node.error}</Text>
        </div>
      )}

      {/* Output JSON */}
      {hasOutput && (
        <>
          <div className={styles.outputHeader}>
            <Text size={200} weight="semibold">
              Output
            </Text>
            <Tooltip
              content={copySuccess ? 'Copied!' : 'Copy to clipboard'}
              relationship="label"
            >
              <Button
                appearance="subtle"
                size="small"
                icon={copySuccess ? <Checkmark16Regular /> : <Copy20Regular />}
                className={styles.copyButton}
                onClick={handleCopy}
              />
            </Tooltip>
          </div>
          <pre className={styles.codeBlock}>{formatJson(node.output)}</pre>
        </>
      )}

      {/* No output and no error */}
      {!hasOutput && !node.error && node.status === 'skipped' && (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Node was skipped (condition not met)
        </Text>
      )}

      {!hasOutput && !node.error && node.status === 'completed' && (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          No output data
        </Text>
      )}
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const TestResultPreview: React.FC<TestResultPreviewProps> = ({
  onDownload,
}) => {
  const styles = useStyles();

  // Store state
  const { testExecution } = useAiAssistantStore();

  // Filter nodes with results (completed, failed, or skipped)
  const nodesWithResults = useMemo(() => {
    return testExecution.nodesProgress.filter(
      (n) => n.status === 'completed' || n.status === 'failed' || n.status === 'skipped'
    );
  }, [testExecution.nodesProgress]);

  // Build full results object for download
  const fullResults = useMemo(() => {
    return {
      testMode: testExecution.mode,
      analysisId: testExecution.analysisId,
      totalDurationMs: testExecution.totalDurationMs,
      reportUrl: testExecution.reportUrl,
      completedAt: new Date().toISOString(),
      nodes: testExecution.nodesProgress.map((n) => ({
        nodeId: n.nodeId,
        label: n.label,
        status: n.status,
        durationMs: n.durationMs,
        output: n.output,
        error: n.error,
      })),
    };
  }, [testExecution]);

  // Handle copy to clipboard
  const handleCopy = useCallback(async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
    } catch (err) {
      console.error('Failed to copy to clipboard:', err);
    }
  }, []);

  // Handle download
  const handleDownload = useCallback(() => {
    const json = formatJson(fullResults);
    const filename = `test-results-${testExecution.mode ?? 'test'}-${Date.now()}.json`;

    if (onDownload) {
      onDownload(json, filename);
    } else {
      // Default download behavior
      const blob = new Blob([json], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }
  }, [fullResults, testExecution.mode, onDownload]);

  // Don't render if no results
  if (nodesWithResults.length === 0) {
    return (
      <div className={styles.emptyState}>
        <DocumentText20Regular className={styles.emptyIcon} />
        <Text size={400} weight="semibold">
          No Results Yet
        </Text>
        <Text size={200}>Run a test to see results here</Text>
      </div>
    );
  }

  // Default open items (first node and any failed nodes)
  const defaultOpenItems = useMemo(() => {
    const items: string[] = [];
    if (nodesWithResults.length > 0) {
      items.push(nodesWithResults[0].nodeId);
    }
    nodesWithResults.forEach((n) => {
      if (n.status === 'failed' && !items.includes(n.nodeId)) {
        items.push(n.nodeId);
      }
    });
    return items;
  }, [nodesWithResults]);

  return (
    <div className={styles.container}>
      {/* Header */}
      <div className={styles.header}>
        <div className={styles.headerLeft}>
          <Code20Regular />
          <Text weight="semibold">Test Results</Text>
          <Badge appearance="outline" size="small">
            {nodesWithResults.length} nodes
          </Badge>
        </div>
        <Button
          appearance="secondary"
          size="small"
          icon={<ArrowDownload20Regular />}
          onClick={handleDownload}
        >
          Download JSON
        </Button>
      </div>

      {/* Error banner if test failed */}
      {testExecution.error && (
        <MessageBar intent="error">
          <MessageBarBody>{testExecution.error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Node results accordion */}
      <Accordion
        className={styles.accordion}
        multiple
        collapsible
        defaultOpenItems={defaultOpenItems}
      >
        {nodesWithResults.map((node) => (
          <AccordionItem key={node.nodeId} value={node.nodeId}>
            <AccordionHeader size="small">
              <div className={styles.nodeHeader}>
                <span className={styles.nodeIcon}>{getStatusIcon(node)}</span>
                <Text className={styles.nodeLabel} size={200}>
                  {node.label}
                </Text>
                {node.status === 'failed' && (
                  <Badge appearance="filled" color="danger" size="small">
                    Failed
                  </Badge>
                )}
                {node.status === 'skipped' && (
                  <Badge appearance="outline" size="small">
                    Skipped
                  </Badge>
                )}
                {node.durationMs !== undefined && node.status === 'completed' && (
                  <Text className={styles.nodeDuration}>{formatDuration(node.durationMs)}</Text>
                )}
              </div>
            </AccordionHeader>
            <AccordionPanel className={styles.accordionPanel}>
              <NodeResult node={node} onCopy={handleCopy} />
            </AccordionPanel>
          </AccordionItem>
        ))}
      </Accordion>

      {/* Summary */}
      {testExecution.analysisId && (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Analysis ID: {testExecution.analysisId}
        </Text>
      )}
    </div>
  );
};

// Helper function for status icon (used in accordion header)
const getStatusIcon = (node: TestNodeProgress) => {
  const iconStyle = {
    completed: { color: tokens.colorPaletteGreenForeground1 },
    failed: { color: tokens.colorPaletteRedForeground1 },
    skipped: { color: tokens.colorNeutralForeground3 },
  };

  switch (node.status) {
    case 'completed':
      return <Checkmark16Regular style={iconStyle.completed} />;
    case 'failed':
      return <Dismiss16Regular style={iconStyle.failed} />;
    case 'skipped':
      return <ArrowForward16Regular style={iconStyle.skipped} />;
    default:
      return null;
  }
};

export default TestResultPreview;
