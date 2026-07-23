/**
 * DocumentLookupDialog.tsx
 *
 * A modal that overlays the email composer (owner UAT round 3/4, 2026-07-22) so the
 * user can search existing `sprk_document` records and Attach and/or Link them to the
 * message. Context-agnostic (ADR-012): the host binds the Dataverse query via
 * `onSearch`; this component only renders the search + results + add affordances.
 *
 * Adds are immediate (per row) so the user can pick several without closing; the dialog
 * stays open until Done. Fluent v9 only, semantic tokens (ADR-021).
 */
import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Input,
  Text,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DocumentRegular, SearchRegular } from '@fluentui/react-icons';
import type { IDocumentSearchResult } from '../EmailComposer.types';

export interface IDocumentLookupDialogProps {
  open: boolean;
  onClose: () => void;
  /** Host-bound Dataverse `sprk_document` search. */
  onSearch: (query: string) => Promise<IDocumentSearchResult[]>;
  /** Add a picked document to the composer's attachments (Attach/Link then toggle in the row). */
  onAdd: (doc: IDocumentSearchResult) => void;
}

const useStyles = makeStyles({
  surface: { maxWidth: '640px', width: '92vw' },
  searchRow: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'center' },
  searchInput: { flexGrow: 1 },
  results: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalM,
    maxHeight: '360px',
    overflowY: 'auto',
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  rowName: { flexGrow: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  rowActions: { display: 'flex', gap: tokens.spacingHorizontalXS, flexShrink: 0 },
  hint: { color: tokens.colorNeutralForeground3 },
});

const MIN_QUERY = 2;

export function DocumentLookupDialog({ open, onClose, onSearch, onAdd }: IDocumentLookupDialogProps) {
  const styles = useStyles();
  const [query, setQuery] = React.useState('');
  const [results, setResults] = React.useState<IDocumentSearchResult[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [searched, setSearched] = React.useState(false);
  const [added, setAdded] = React.useState<Set<string>>(new Set());
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Reset transient state each time the dialog opens.
  React.useEffect(() => {
    if (open) {
      setQuery('');
      setResults([]);
      setSearched(false);
      setAdded(new Set());
    }
  }, [open]);

  React.useEffect(() => {
    if (!open) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (query.trim().length < MIN_QUERY) {
      setResults([]);
      setSearched(false);
      return;
    }
    debounceRef.current = setTimeout(async () => {
      setLoading(true);
      try {
        const items = await onSearch(query.trim());
        setResults(items);
      } catch (err) {
        console.error('[DocumentLookupDialog] search failed:', err);
        setResults([]);
      } finally {
        setLoading(false);
        setSearched(true);
      }
    }, 300);
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [query, open, onSearch]);

  const handleAdd = (doc: IDocumentSearchResult) => {
    onAdd(doc);
    setAdded(prev => new Set(prev).add(doc.documentId));
  };

  return (
    <Dialog open={open} onOpenChange={(_e, data) => !data.open && onClose()}>
      <DialogSurface className={styles.surface}>
        <DialogBody>
          <DialogTitle>Look up a document</DialogTitle>
          <DialogContent>
            <div className={styles.searchRow}>
              <SearchRegular aria-hidden="true" />
              <Input
                className={styles.searchInput}
                value={query}
                onChange={(_e, data) => setQuery(data.value)}
                placeholder="Search documents by name…"
                aria-label="Search documents"
                autoFocus
              />
            </div>

            <div className={styles.results} role="listbox" aria-label="Document search results">
              {loading && <Spinner size="tiny" label="Searching…" />}
              {!loading && searched && results.length === 0 && (
                <Text size={200} className={styles.hint}>
                  No documents match “{query.trim()}”.
                </Text>
              )}
              {!loading && !searched && query.trim().length < MIN_QUERY && (
                <Text size={200} className={styles.hint}>
                  Type at least {MIN_QUERY} characters to search.
                </Text>
              )}
              {results.map(doc => (
                <div key={doc.documentId} className={styles.row} role="option" aria-selected={false}>
                  <DocumentRegular aria-hidden="true" />
                  <Text size={200} className={styles.rowName} title={doc.fileName}>
                    {doc.fileName}
                  </Text>
                  <div className={styles.rowActions}>
                    <Button
                      size="small"
                      appearance={added.has(doc.documentId) ? 'primary' : 'secondary'}
                      disabled={added.has(doc.documentId)}
                      onClick={() => handleAdd(doc)}
                    >
                      {added.has(doc.documentId) ? 'Added' : 'Add'}
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" onClick={onClose}>
              Done
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

DocumentLookupDialog.displayName = 'DocumentLookupDialog';
