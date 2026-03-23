/**
 * DocumentSelector — single-select document switcher for PlaybookLibraryShell.
 *
 * Rendered at the top of the PlaybookLibrary when two or more document IDs
 * are passed via the `documentIds` parameter. Fetches document names from
 * Dataverse and presents a RadioGroup so the user can switch the active
 * document before running an analysis.
 *
 * Single-select MVP: only one document can be active at a time.
 *
 * @see ADR-021 — Fluent v9 tokens only; dark mode via FluentProvider cascade.
 * @see ADR-012 — Shared component; IDataService for all Dataverse access.
 */

import React from 'react';
import {
  RadioGroup,
  Radio,
  Spinner,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { DocumentRegular } from '@fluentui/react-icons';
import type { IDataService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A resolved document item (id + display name). */
export interface IDocumentItem {
  id: string;
  name: string;
}

export interface IDocumentSelectorProps {
  /** Ordered list of document IDs to display. Must have length >= 2. */
  documentIds: string[];
  /** Currently selected document ID. */
  selectedDocumentId: string;
  /** Called when the user selects a different document. */
  onSelect: (documentId: string) => void;
  /** Data service for fetching document names. */
  dataService: IDataService;
  /** Optional extra class name for the root element. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  label: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalXS,
  },
  radioGroup: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalM,
  },
  radioItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  docIcon: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
    fontSize: '16px',
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// DocumentSelector component
// ---------------------------------------------------------------------------

/**
 * Fetches document display names for the given IDs and renders a horizontal
 * RadioGroup so the user can switch the active document.
 *
 * Hidden when documentIds.length < 2 (caller is responsible for the guard,
 * but the component also returns null defensively).
 */
export const DocumentSelector: React.FC<IDocumentSelectorProps> = ({
  documentIds,
  selectedDocumentId,
  onSelect,
  dataService,
  className,
}) => {
  const styles = useStyles();

  const [documents, setDocuments] = React.useState<IDocumentItem[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [fetchError, setFetchError] = React.useState<string | null>(null);

  // Fetch document names from Dataverse on mount / when IDs change.
  React.useEffect(() => {
    if (documentIds.length === 0) {
      setIsLoading(false);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setFetchError(null);

    (async () => {
      try {
        // Build an OData filter for the provided IDs.
        // Maximum supported by Dataverse in a single filter is well above the
        // expected handful of docs for this MVP scenario.
        const idList = documentIds
          .map(id => `sprk_documentid eq '${id}'`)
          .join(' or ');
        const options = `?$select=sprk_documentid,sprk_name&$filter=${idList}`;

        const result = await dataService.retrieveMultipleRecords('sprk_document', options);

        if (cancelled) return;

        // Preserve caller-supplied order so the UI is stable.
        const nameMap = new Map<string, string>();
        for (const entity of result.entities) {
          const id = entity['sprk_documentid'] as string | undefined;
          const name = entity['sprk_name'] as string | undefined;
          if (id) {
            nameMap.set(id, name ?? 'Untitled Document');
          }
        }

        const resolved: IDocumentItem[] = documentIds.map(id => ({
          id,
          name: nameMap.get(id) ?? 'Untitled Document',
        }));

        setDocuments(resolved);
      } catch (err) {
        if (cancelled) return;
        setFetchError(err instanceof Error ? err.message : 'Failed to load documents');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [documentIds, dataService]);

  // Guard: hide when fewer than 2 documents.
  if (documentIds.length < 2) return null;

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Text size={200} weight="semibold" className={styles.label}>
        Analyze document
      </Text>

      {isLoading ? (
        <div className={styles.loading}>
          <Spinner size="tiny" />
          <Text size={200}>Loading documents…</Text>
        </div>
      ) : fetchError ? (
        <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
          {fetchError}
        </Text>
      ) : (
        <RadioGroup
          value={selectedDocumentId}
          onChange={(_ev, data) => onSelect(data.value)}
          layout="horizontal"
          className={styles.radioGroup}
        >
          {documents.map(doc => (
            <Radio
              key={doc.id}
              value={doc.id}
              label={
                <span className={styles.radioItem}>
                  <DocumentRegular className={styles.docIcon} />
                  <span>{doc.name}</span>
                </span>
              }
            />
          ))}
        </RadioGroup>
      )}
    </div>
  );
};

export default DocumentSelector;
