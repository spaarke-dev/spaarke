/**
 * SummaryResultsStep.tsx
 * Step 2 of the Summarize New File(s) wizard — displays AI-generated summary results.
 *
 * Sections rendered (conditionally):
 *   - TL;DR (always)
 *   - Summary (always)
 *   - File-by-File Highlights (multi-file only)
 *   - Related Practice Areas (if detected)
 *   - Who's Mentioned (if parties found)
 *   - Call to Action (if actionable items found)
 */
import * as React from 'react';
import {
  Badge,
  Button,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  tokens,
} from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import type { ISummarizeResult } from './summarizeTypes';
import type { SummarizeStatus } from './summarizeTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummaryResultsStepProps {
  status: SummarizeStatus;
  result: ISummarizeResult | null;
  errorMessage: string | null;
  onRetry: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    overflowY: 'auto',
    maxHeight: '100%',
    paddingRight: tokens.spacingHorizontalS,
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalL,
    minHeight: '300px',
  },
  sectionHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalXS,
  },
  paragraph: {
    lineHeight: '1.6',
    whiteSpace: 'pre-wrap',
    color: tokens.colorNeutralForeground1,
  },
  fileCard: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  fileHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  bulletList: {
    margin: 0,
    paddingLeft: '20px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  tagsContainer: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  partiesTable: {
    width: '100%',
    borderCollapse: 'collapse',
    '& th': {
      textAlign: 'left',
      padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
      borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
      color: tokens.colorNeutralForeground3,
      fontWeight: 600,
      fontSize: tokens.fontSizeBase200,
    },
    '& td': {
      padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
      borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
      color: tokens.colorNeutralForeground1,
      fontSize: tokens.fontSizeBase300,
    },
  },
  callToActionBox: {
    backgroundColor: tokens.colorNeutralBackground4,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
  },
  confidenceBadge: {
    marginLeft: 'auto',
  },
  stepTitle: {
    display: 'block',
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    display: 'block',
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SummaryResultsStep: React.FC<ISummaryResultsStepProps> = ({
  status,
  result,
  errorMessage,
  onRetry,
}) => {
  const styles = useStyles();

  // Loading state
  if (status === 'loading') {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="large" label="Analyzing files..." labelPosition="below" />
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          The AI is reading and summarizing your documents. This may take a moment.
        </Text>
      </div>
    );
  }

  // Error state
  if (status === 'error') {
    return (
      <div className={styles.container}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Analysis Results
        </Text>
        <MessageBar intent="error">
          <MessageBarBody>
            {errorMessage || 'An error occurred while analyzing the files.'}
          </MessageBarBody>
        </MessageBar>
        <Button appearance="primary" onClick={onRetry}>
          Retry Analysis
        </Button>
      </div>
    );
  }

  // Idle state (should not normally be shown)
  if (status === 'idle' || !result) {
    return (
      <div className={styles.container}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Analysis Results
        </Text>
        <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
          Click "Run Analysis" to start.
        </Text>
      </div>
    );
  }

  // Success state — render all sections
  return (
    <div className={styles.container}>
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
        <Text as="h2" size={500} weight="semibold">
          Analysis Results
        </Text>
        <Badge appearance="tint" icon={<SparkleRegular />} color="brand">
          AI Generated
        </Badge>
        {result.confidence != null && (
          <Badge
            appearance="outline"
            className={styles.confidenceBadge}
          >
            {Math.round(result.confidence * 100)}% confidence
          </Badge>
        )}
      </div>

      {/* TL;DR — always shown */}
      <section>
        <div className={styles.sectionHeader}>
          <Text size={400} weight="semibold">TL;DR</Text>
        </div>
        <Text size={300} className={styles.paragraph}>
          {result.tldr}
        </Text>
      </section>

      {/* Summary — always shown */}
      <section>
        <div className={styles.sectionHeader}>
          <Text size={400} weight="semibold">Summary</Text>
        </div>
        <Text size={300} className={styles.paragraph}>
          {result.summary}
        </Text>
      </section>

      {/* File-by-File Highlights — multi-file only */}
      {result.fileHighlights && result.fileHighlights.length > 0 && (
        <section>
          <div className={styles.sectionHeader}>
            <Text size={400} weight="semibold">File-by-File Highlights</Text>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM }}>
            {result.fileHighlights.map((file, idx) => (
              <div key={idx} className={styles.fileCard}>
                <div className={styles.fileHeader}>
                  <Text size={300} weight="semibold">{file.fileName}</Text>
                  <Badge appearance="outline" size="small">{file.documentType}</Badge>
                </div>
                <ul className={styles.bulletList}>
                  {file.highlights.map((h, hIdx) => (
                    <li key={hIdx}>
                      <Text size={200}>{h}</Text>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </section>
      )}

      {/* Related Practice Areas */}
      {result.practiceAreas && result.practiceAreas.length > 0 && (
        <section>
          <div className={styles.sectionHeader}>
            <Text size={400} weight="semibold">Related Practice Areas</Text>
          </div>
          <div className={styles.tagsContainer}>
            {result.practiceAreas.map((area) => (
              <Badge key={area} appearance="tint" color="informative" size="medium">
                {area}
              </Badge>
            ))}
          </div>
        </section>
      )}

      {/* Who's Mentioned */}
      {result.mentionedParties && result.mentionedParties.length > 0 && (
        <section>
          <div className={styles.sectionHeader}>
            <Text size={400} weight="semibold">Who&apos;s Mentioned</Text>
          </div>
          <table className={styles.partiesTable}>
            <thead>
              <tr>
                <th>Name</th>
                <th>Role</th>
              </tr>
            </thead>
            <tbody>
              {result.mentionedParties.map((party, idx) => (
                <tr key={idx}>
                  <td>{party.name}</td>
                  <td>{party.role}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {/* Call to Action */}
      {result.callToAction && (
        <section>
          <div className={styles.sectionHeader}>
            <Text size={400} weight="semibold">Call to Action</Text>
          </div>
          <div className={styles.callToActionBox}>
            <Text size={300} className={styles.paragraph}>
              {result.callToAction}
            </Text>
          </div>
        </section>
      )}
    </div>
  );
};
