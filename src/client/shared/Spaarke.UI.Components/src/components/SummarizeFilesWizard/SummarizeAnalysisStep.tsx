/**
 * SummarizeAnalysisStep.tsx
 * Follow-on step for "Work on Analysis" in the Summarize Files wizard.
 *
 * When the user clicks a playbook card this step:
 *   1. Queries the current user's business unit to obtain sprk_containerid.
 *   2. Creates sprk_document records for each uploaded file via BFF
 *      POST /api/v1/documents (ADR-013: AI features use BFF, not Xrm.WebApi).
 *   3. Collects the created document IDs.
 *   4. Opens the PlaybookLibrary Code Page via navigationService.openDialog,
 *      passing documentIds as a comma-separated query parameter.
 *
 * NFR-05: document creation must complete within 3 seconds for ≤10 files.
 * Partial failures are handled gracefully — successfully-created documents
 * are still passed to PlaybookLibrary; failed documents are reported to the
 * user via a warning MessageBar.
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
import {
  PlaybookCardGrid,
  loadPlaybooks,
} from '../Playbook';
import type { IPlaybook, AuthenticatedFetchFn } from '../Playbook';
import type { IDataService } from '../../types/serviceInterfaces';
import type { INavigationService } from '../../types/serviceInterfaces';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const LOG_PREFIX = '[SummarizeAnalysisStep]';

/**
 * Web resource name for the PlaybookLibrary Code Page.
 * Must match the logical name registered in Dataverse.
 */
const PLAYBOOK_LIBRARY_WEBRESOURCE = 'sprk_playbooklibrary';

// ---------------------------------------------------------------------------
// Document creation helpers
// ---------------------------------------------------------------------------

/**
 * Result of creating a single sprk_document record.
 */
export interface ICreateDocumentResult {
  fileName: string;
  documentId?: string;
  error?: string;
}

/**
 * Retrieves the sprk_containerid from the current user's business unit.
 *
 * Pattern: systemuser → _businessunitid_value → businessunit → sprk_containerid.
 * Matches the SemanticSearchControl NavigationService pattern.
 */
async function getBusinessUnitContainerId(dataService: IDataService): Promise<string> {
  // Retrieve the current user record — Xrm.Utility.getUserId() is not available
  // here so we retrieve 'WhoAmI' equivalent by querying systemuser with no filter.
  // Dataverse returns the caller's own record when the id is omitted in
  // retrieveMultipleRecords if we use the /UserInfo endpoint — but IDataService
  // doesn't expose that. Instead we use the whoami pattern via a known placeholder.
  //
  // Preferred approach: ask Dataverse who the current user is via WhoAmI-style
  // retrieveRecord on the logged-in user.  The xrmDataServiceAdapter wraps
  // Xrm.WebApi which uses the caller identity automatically, so retrieving the
  // systemuser record with a dummy whoami approach works by querying
  // ?$select=_businessunitid_value&$top=1 without a filter — but that could
  // return any user in a large org.  The safest approach is to use
  // window.Xrm.Utility.getUserId() via a try/catch and fall back gracefully.
  //
  // Since this code runs inside a Dataverse Code Page iframe, Xrm is available.
  let userId: string | null = null;
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm;
    if (xrm?.Utility?.getUserId) {
      userId = xrm.Utility.getUserId() as string;
      // Remove surrounding braces if present
      userId = userId.replace(/^\{|\}$/g, '');
    }
  } catch {
    // Xrm not available (test environment or non-Dataverse context)
  }

  if (!userId) {
    throw new Error(`${LOG_PREFIX} Cannot determine current user — Xrm.Utility.getUserId() is unavailable.`);
  }

  // Step 1: Get the user's business unit ID
  const userRecord = await dataService.retrieveRecord(
    'systemuser',
    userId,
    '?$select=_businessunitid_value'
  );

  const buId = userRecord['_businessunitid_value'] as string | undefined;
  if (!buId) {
    throw new Error(`${LOG_PREFIX} Current user has no associated business unit.`);
  }

  // Step 2: Get the business unit's sprk_containerid
  const buRecord = await dataService.retrieveRecord(
    'businessunit',
    buId,
    '?$select=sprk_containerid'
  );

  const containerId = buRecord['sprk_containerid'] as string | undefined;
  if (!containerId) {
    throw new Error(
      `${LOG_PREFIX} Business unit ${buId} does not have a container configured (sprk_containerid is empty). ` +
      'Ask your administrator to configure the SPE container for your business unit.'
    );
  }

  return containerId;
}

/**
 * Creates a single sprk_document record via the BFF API.
 *
 * POST /api/v1/documents
 * Body: { Name, ContainerId }
 *
 * Returns the created document GUID extracted from the 201 Created response.
 */
async function createDocumentRecord(
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string,
  fileName: string,
  containerId: string,
): Promise<string> {
  const url = `${bffBaseUrl}/api/v1/documents`;

  const response = await authenticatedFetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: fileName,
      containerId,
    }),
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
    throw new Error(`BFF returned ${response.status} for "${fileName}": ${errorText}`);
  }

  // Response shape: { data: { sprk_documentid: "...", ... }, metadata: { ... } }
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const json: any = await response.json();

  // Try common ID field names in the response
  const documentId: string | undefined =
    json?.data?.sprk_documentid ??
    json?.data?.id ??
    json?.id;

  if (!documentId) {
    throw new Error(`${LOG_PREFIX} BFF response did not include a document ID for "${fileName}".`);
  }

  return documentId;
}

/**
 * Creates sprk_document records in parallel for all uploaded files.
 *
 * NFR-05: ≤3 s for ≤10 files — all creation calls run concurrently via
 * Promise.allSettled so a single failure doesn't block the others.
 *
 * @returns Array of per-file results (each has documentId on success, error on failure).
 */
async function createDocumentRecords(
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string,
  files: IUploadedFile[],
  containerId: string,
): Promise<ICreateDocumentResult[]> {
  const settled = await Promise.allSettled(
    files.map((f) =>
      createDocumentRecord(authenticatedFetch, bffBaseUrl, f.name, containerId)
        .then((id) => ({ fileName: f.name, documentId: id }))
        .catch((err) => ({
          fileName: f.name,
          error: err instanceof Error ? err.message : String(err),
        }))
    )
  );

  return settled.map((result) =>
    result.status === 'fulfilled'
      ? result.value
      : { fileName: 'unknown', error: (result.reason as Error)?.message ?? 'Unknown error' }
  );
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeAnalysisStepProps {
  /** IDataService reference for Dataverse operations. */
  dataService: IDataService;
  /** Navigation service for opening entity records and Code Page dialogs. */
  navigationService?: INavigationService;
  /** Files uploaded in the wizard (used to create sprk_document records). */
  uploadedFiles?: IUploadedFile[];
  /** Authenticated fetch function for BFF API calls (required for document creation). */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** Base URL of the BFF API (e.g. "https://spe-api-dev.azurewebsites.net"). */
  bffBaseUrl?: string;
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
  dataService,
  navigationService,
  uploadedFiles = [],
  authenticatedFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();

  // ── Playbook loading state ─────────────────────────────────────────────
  const [playbooks, setPlaybooks] = React.useState<IPlaybook[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [selectedId, setSelectedId] = React.useState<string | undefined>();
  const [launchStatus, setLaunchStatus] = React.useState<'idle' | 'launching' | 'error'>('idle');
  const [launchStatusLabel, setLaunchStatusLabel] = React.useState('Creating documents...');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [warningMessages, setWarningMessages] = React.useState<string[]>([]);

  // ── Load playbooks on mount ────────────────────────────────────────────
  React.useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const result = await loadPlaybooks(dataService);
        if (!cancelled) {
          setPlaybooks(result);
          setIsLoading(false);
        }
      } catch (err) {
        if (!cancelled) {
          console.error(`${LOG_PREFIX} Failed to load playbooks:`, err);
          setIsLoading(false);
        }
      }
    };

    void load();
    return () => { cancelled = true; };
  }, [dataService]);

  // ── Handle playbook card click ─────────────────────────────────────────
  const handleSelect = React.useCallback(
    async (playbook: IPlaybook) => {
      setSelectedId(playbook.id);
      setLaunchStatus('launching');
      setErrorMessage(null);
      setWarningMessages([]);

      try {
        // ── Guard: need auth + BFF ───────────────────────────────────────
        if (!authenticatedFetch || !bffBaseUrl) {
          setErrorMessage(
            'Authentication is not available. Please reload the page and try again.'
          );
          setLaunchStatus('error');
          return;
        }

        // ── Guard: need files ────────────────────────────────────────────
        if (uploadedFiles.length === 0) {
          setErrorMessage(
            'No files have been uploaded. Please go back and upload at least one file.'
          );
          setLaunchStatus('error');
          return;
        }

        // ── Step 1: Get business unit container ID ───────────────────────
        setLaunchStatusLabel('Retrieving container…');
        const containerId = await getBusinessUnitContainerId(dataService);

        // ── Step 2: Create sprk_document records (parallel, ≤3 s for ≤10) ─
        setLaunchStatusLabel(`Creating ${uploadedFiles.length} document record${uploadedFiles.length > 1 ? 's' : ''}…`);

        const createResults = await createDocumentRecords(
          authenticatedFetch,
          bffBaseUrl,
          uploadedFiles,
          containerId,
        );

        // ── Step 3: Collect IDs and gather warnings ──────────────────────
        const successfulIds: string[] = [];
        const failures: string[] = [];

        for (const r of createResults) {
          if (r.documentId) {
            successfulIds.push(r.documentId);
          } else {
            failures.push(`"${r.fileName}": ${r.error ?? 'Unknown error'}`);
          }
        }

        if (failures.length > 0) {
          setWarningMessages(failures);
        }

        if (successfulIds.length === 0) {
          setErrorMessage(
            'All document records failed to create. ' +
            'Check your connection and try again, or contact your administrator.'
          );
          setLaunchStatus('error');
          return;
        }

        // ── Step 4: Open PlaybookLibrary with documentIds ────────────────
        setLaunchStatusLabel('Opening Playbook Library…');
        const documentIdsParam = encodeURIComponent(successfulIds.join(','));
        const bffUrlParam = encodeURIComponent(bffBaseUrl);
        const data = `documentIds=${documentIdsParam}&bffBaseUrl=${bffUrlParam}`;

        if (!navigationService) {
          console.warn(`${LOG_PREFIX} navigationService is not available — cannot open PlaybookLibrary dialog.`);
          setLaunchStatus('error');
          setErrorMessage('Navigation service is not available. Please reload the page and try again.');
          return;
        }

        await navigationService.openDialog(
          PLAYBOOK_LIBRARY_WEBRESOURCE,
          data,
          { width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' } }
        );

        setLaunchStatus('idle');
        setSelectedId(undefined);
      } catch (err) {
        console.error(`${LOG_PREFIX} Failed to launch analysis:`, err);
        setErrorMessage(
          err instanceof Error ? err.message : 'Failed to create document records. Please try again.'
        );
        setLaunchStatus('error');
      }
    },
    [dataService, navigationService, uploadedFiles, authenticatedFetch, bffBaseUrl]
  );

  return (
    <div className={styles.root}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Work on Analysis
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Choose a playbook to run analysis on the uploaded files.
          Document records will be created automatically, then PlaybookLibrary will open.
        </Text>
      </div>

      {/* Partial failure warnings */}
      {warningMessages.length > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <div>Some files could not be saved as documents:</div>
            {warningMessages.map((msg, i) => (
              <div key={i}>{msg}</div>
            ))}
          </MessageBarBody>
        </MessageBar>
      )}

      {/* Hard error */}
      {errorMessage && (
        <MessageBar intent="warning">
          <MessageBarBody>{errorMessage}</MessageBarBody>
        </MessageBar>
      )}

      {launchStatus === 'launching' ? (
        <div className={styles.statusContainer}>
          <Spinner size="large" label={launchStatusLabel} labelPosition="below" />
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
