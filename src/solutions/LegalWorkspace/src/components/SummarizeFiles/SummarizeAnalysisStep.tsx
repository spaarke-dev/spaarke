/**
 * SummarizeAnalysisStep.tsx
 * Follow-on step for "Work on Analysis" in the Summarize Files wizard.
 *
 * Shows a playbook card grid. When the user clicks a playbook card,
 * the analysis is created using the uploaded files and opens in a new tab.
 *
 * Note: For MVP, analysis requires files to be saved as sprk_document records.
 * This step creates the analysis record and associates scopes, then opens it.
 */
import * as React from 'react';
import {
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PlaybookCardGrid } from '../Playbook/PlaybookCardGrid';
import { loadPlaybooks, loadPlaybookScopes } from '../Playbook/playbookService';
import { createAndAssociate } from '../Playbook/analysisService';
import { navigateToEntity } from '../../utils/navigation';
import type { IPlaybook } from '../Playbook/types';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeAnalysisStepProps {
  /** Xrm.WebApi reference for Dataverse operations. */
  webApi: IWebApi;
  /** Document ID (sprk_document GUID) — needed for analysis creation. */
  documentId?: string;
  /** Document name for the analysis record title. */
  documentName?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
  statusContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
  },
});

// ---------------------------------------------------------------------------
// SummarizeAnalysisStep (exported)
// ---------------------------------------------------------------------------

export const SummarizeAnalysisStep: React.FC<ISummarizeAnalysisStepProps> = ({
  webApi,
  documentId,
  documentName,
}) => {
  const styles = useStyles();

  // ── Playbook loading state ─────────────────────────────────────────────
  const [playbooks, setPlaybooks] = React.useState<IPlaybook[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [selectedId, setSelectedId] = React.useState<string | undefined>();
  const [launchStatus, setLaunchStatus] = React.useState<'idle' | 'launching' | 'error'>('idle');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

  // ── Load playbooks on mount ────────────────────────────────────────────
  React.useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const result = await loadPlaybooks(webApi);
        if (!cancelled) {
          setPlaybooks(result);
          setIsLoading(false);
        }
      } catch (err) {
        if (!cancelled) {
          console.error('[SummarizeAnalysis] Failed to load playbooks:', err);
          setIsLoading(false);
        }
      }
    };

    void load();
    return () => { cancelled = true; };
  }, [webApi]);

  // ── Handle playbook card click ─────────────────────────────────────────
  const handleSelect = React.useCallback(
    async (playbook: IPlaybook) => {
      setSelectedId(playbook.id);
      setLaunchStatus('launching');
      setErrorMessage(null);

      try {
        // Load playbook scopes (skills, knowledge, tools, actions)
        const scopes = await loadPlaybookScopes(webApi, playbook.id);

        if (!documentId) {
          // No document ID available — files haven't been saved as sprk_document yet
          setErrorMessage(
            'Analysis requires files to be saved as document records first. ' +
            'Please create a project or matter to save the files, then run analysis from there.'
          );
          setLaunchStatus('error');
          return;
        }

        if (scopes.actionIds.length === 0) {
          setErrorMessage(
            `Playbook "${playbook.name}" has no actions configured. Please select a different playbook.`
          );
          setLaunchStatus('error');
          return;
        }

        // Create analysis record with associated scopes
        const analysisId = await createAndAssociate(webApi, {
          documentId,
          documentName: documentName || 'Document Summary',
          playbookId: playbook.id,
          actionId: scopes.actionIds[0],
          skillIds: scopes.skillIds,
          knowledgeIds: scopes.knowledgeIds,
          toolIds: scopes.toolIds,
        });

        // Open the analysis record in a new tab
        navigateToEntity({
          action: 'openRecord',
          entityName: 'sprk_analysis',
          entityId: analysisId,
        });

        setLaunchStatus('idle');
      } catch (err) {
        console.error('[SummarizeAnalysis] Failed to launch analysis:', err);
        setErrorMessage(
          err instanceof Error ? err.message : 'Failed to create analysis. Please try again.'
        );
        setLaunchStatus('error');
      }
    },
    [webApi, documentId, documentName]
  );

  return (
    <div className={styles.root}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Work on Analysis
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Choose a playbook to run analysis on the uploaded files. The analysis will open in a new tab.
        </Text>
      </div>

      {errorMessage && (
        <MessageBar intent="warning">
          <MessageBarBody>{errorMessage}</MessageBarBody>
        </MessageBar>
      )}

      {launchStatus === 'launching' ? (
        <div className={styles.statusContainer}>
          <Spinner size="large" label="Creating analysis..." labelPosition="below" />
        </div>
      ) : (
        <PlaybookCardGrid
          playbooks={playbooks}
          selectedId={selectedId}
          onSelect={handleSelect}
          isLoading={isLoading}
        />
      )}
    </div>
  );
};
