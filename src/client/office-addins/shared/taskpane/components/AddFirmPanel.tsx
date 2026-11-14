import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  Field,
  Listbox,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Option,
  SearchBox,
  Spinner,
  Text,
  makeStyles,
  tokens,
  type GriffelStyle,
  type OptionOnSelectData,
  type SelectionEvents,
} from '@fluentui/react-components';
import type { AnnounceMode } from '../hooks/useAnnounce';
import type {
  AddFirmOutcome,
  AddFirmToRecord,
  InvoiceFirmOption,
  InvoiceSubmissionTarget,
  ListLawFirms,
} from '../services/invoiceSubmissionSeam';
import { FIRM_ASSIGN_SLOTS_FULL_CODE } from '../utils/invoiceSubmissionMessages';

/**
 * AddFirmPanel — adds a law firm to the invoice's record from inside the Invoice tab (task 134 slice C, ebilling 095g).
 *
 * Opens inline under the Firm field (not a modal). A search box (debounced) filters the active law firms
 * ({@link AddFirmPanelProps.listLawFirms}); the list loads more pages as the user scrolls (ADR-051, no pager and no
 * "Load more": an `IntersectionObserver` on a bottom sentinel, as `useFindRecordMatches` does). Choosing a firm shows who
 * gets access; "Add firm" sends the ONE add request ({@link AddFirmPanelProps.addFirmToRecord}), and nothing is sent
 * before that click. The pane only ever ADDS a firm: it never removes, replaces or clears one, so it can never take a
 * firm's access away.
 *
 * On a 200 the panel hands the result to {@link AddFirmPanelProps.onAdded} (the view refreshes its firm list, selects the
 * firm and shows the server's notice) and closes. A 200 whose access pass is still pending ("Saved. Spaarke is still
 * updating who can see this record.") stays open with a Retry that resends the SAME request. A refusal shows its sentence
 * inside the panel and keeps it open.
 */
export interface AddFirmPanelProps {
  /** The record the firm is added to. */
  target: InvoiceSubmissionTarget;
  listLawFirms: ListLawFirms;
  addFirmToRecord: AddFirmToRecord;
  /** Called with every 200 (also a repeat, `changed: false`, and a pending one) and the firm that was added. */
  onAdded: (result: Extract<AddFirmOutcome, { kind: 'added' }>, firm: InvoiceFirmOption) => void;
  /** Cancel, or done: the panel closes. */
  onClose: () => void;
  /**
   * True while an add request is in flight, false again when it ends (and when the panel goes away). The view uses it
   * to hold back whatever would unmount the panel mid-request (Submit, the New / Existing switch), which would drop the
   * answer silently.
   */
  onBusyChange?: (busy: boolean) => void;
  /** The view's own `announce` (this component renders no live region of its own). */
  announce: (message: string, mode?: AnnounceMode) => void;
  /** Rendered with the "two firms are already assigned" refusal: the view's "Open in Spaarke" link for the record. */
  openInSpaarke?: React.ReactNode;
}

/** The search input's debounce. */
export const ADD_FIRM_SEARCH_DEBOUNCE_MS = 300;
/** ADR-051: the next page is requested this far before the sentinel scrolls into view. */
const SENTINEL_ROOT_MARGIN = '200px';

/** Shown when a search has no results. */
export const NO_LAW_FIRMS_MESSAGE = 'No law firms match.';
/** The 200 with a pending access pass (the contract's own sentence). */
export const FIRM_ACCESS_PENDING_MESSAGE = 'Saved. Spaarke is still updating who can see this record.';

/**
 * The access notice shown once a firm is chosen, before anything is sent. The pane cannot know what the access pass
 * will do (an inactive record gets nothing until it is active again; the pass may decide not to grant), so it only says
 * what adding CAN do. The definite statement is the server's own `notice`, shown after the add.
 */
export function describeAddFirmNotice(firmName: string): string {
  return `Adding ${firmName} can give that firm's people access to this record. Spaarke will say what it did.`;
}

// Recreated locally per `.claude/patterns/ui/thin-scrollbar.md`, as `FindResultsList` does (the add-in does not take
// the shared theme module). Semantic tokens only (ADR-021).
const thinScrollbarStyle: GriffelStyle = {
  scrollbarWidth: 'thin',
  scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  '::-webkit-scrollbar': { width: '8px', height: '8px' },
  '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
  '::-webkit-scrollbar-thumb': {
    backgroundColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
  },
  '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
};

const useStyles = makeStyles({
  panel: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalS,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  scroller: {
    maxHeight: '240px',
    overflowY: 'auto',
    ...thinScrollbarStyle,
  },
  sentinel: { height: '1px' },
  status: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, padding: tokens.spacingVerticalXS },
  note: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: tokens.spacingHorizontalS },
});

type ListState = { status: 'loading' } | { status: 'ready' } | { status: 'error'; message: string; page: number };

interface Refusal {
  code: string | undefined;
  message: string;
}

export const AddFirmPanel: React.FC<AddFirmPanelProps> = ({
  target,
  listLawFirms,
  addFirmToRecord,
  onAdded,
  onClose,
  onBusyChange,
  announce,
  openInSpaarke,
}) => {
  const styles = useStyles();

  const [searchText, setSearchText] = useState('');
  /** The trimmed search the list is for (the input's text, once it has been still for the debounce). */
  const [query, setQuery] = useState('');
  const [items, setItems] = useState<InvoiceFirmOption[]>([]);
  const [list, setList] = useState<ListState>({ status: 'loading' });
  const [hasMore, setHasMore] = useState(false);
  const [nextPage, setNextPage] = useState(1);
  const [loadingMore, setLoadingMore] = useState(false);
  const [selected, setSelected] = useState<InvoiceFirmOption | null>(null);
  const [sending, setSending] = useState(false);
  const [refusal, setRefusal] = useState<Refusal | null>(null);
  /** Set once the server has the firm on the record but its access pass is unfinished: Retry resends this same firm. */
  const [pendingFirm, setPendingFirm] = useState<InvoiceFirmOption | null>(null);

  // A response is applied only if it belongs to the CURRENT search (and the panel is still mounted).
  const generationRef = useRef(0);
  const inFlightRef = useRef(false);
  const sendingRef = useRef(false);
  const mountedRef = useRef(true);
  // Focus (a11y): the panel opens on its search box (the entry button that opened it is gone), and the pending view
  // opens on Retry. Callback refs, as `RelatedToPicker` does (Fluent v9's ref typing here resolves to `Ref<never>`).
  const searchInputRef = useRef<HTMLInputElement | null>(null);
  const retryButtonRef = useRef<HTMLButtonElement | null>(null);
  const onBusyChangeRef = useRef(onBusyChange);
  onBusyChangeRef.current = onBusyChange;
  useEffect(() => {
    searchInputRef.current?.focus();
  }, []);
  useEffect(() => {
    onBusyChangeRef.current?.(sending);
  }, [sending]);
  useEffect(
    () => () => {
      onBusyChangeRef.current?.(false);
    },
    []
  );
  // The pending view appears (and again after a refused Retry, which disables the button while it runs): land on Retry.
  useEffect(() => {
    if (pendingFirm && !sending) retryButtonRef.current?.focus();
  }, [pendingFirm, sending]);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      generationRef.current += 1;
    };
  }, []);

  const fetchPage = useCallback(
    async (page: number, search: string, generation: number) => {
      inFlightRef.current = true;
      if (page > 1) setLoadingMore(true);
      else setList({ status: 'loading' });
      try {
        const result = await listLawFirms({ ...(search ? { search } : {}), page });
        if (generation !== generationRef.current) return;
        setItems(previous => {
          // A firm seen twice (the list shifts between requests) would render twice with the same key.
          const seen = new Set(previous.map(f => f.firmId));
          const fresh = result.items.filter(f => !seen.has(f.firmId));
          return page === 1 ? fresh : fresh.length > 0 ? [...previous, ...fresh] : previous;
        });
        setHasMore(result.hasMore);
        setNextPage(page + 1);
        setList({ status: 'ready' });
        if (page === 1) {
          announce(
            result.items.length === 0
              ? NO_LAW_FIRMS_MESSAGE
              : `${result.items.length}${result.hasMore ? '+' : ''} law firms found.`
          );
        }
      } catch (err) {
        if (generation !== generationRef.current) return;
        // Keep the rows already shown; the message says why, and Retry asks for the page that failed.
        setHasMore(false);
        const message = err instanceof Error && err.message ? err.message : "Couldn't list the law firms. Try again.";
        setList({ status: 'error', message, page });
        announce(message, 'assertive');
      } finally {
        if (generation === generationRef.current) {
          inFlightRef.current = false;
          setLoadingMore(false);
        }
      }
    },
    [listLawFirms, announce]
  );

  // A new search resets the list and loads its first page.
  useEffect(() => {
    generationRef.current += 1;
    const generation = generationRef.current;
    inFlightRef.current = false;
    setItems([]);
    setHasMore(false);
    setNextPage(1);
    setLoadingMore(false);
    void fetchPage(1, query, generation);
    return () => {
      generationRef.current += 1;
    };
  }, [query, fetchPage]);

  // The search text becomes the search once it has been still for the debounce.
  useEffect(() => {
    const trimmed = searchText.trim();
    if (trimmed === query) return;
    const timer = setTimeout(() => setQuery(trimmed), ADD_FIRM_SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchText, query]);

  // ⚠️ A CALLBACK ref held in state, as `useFindRecordMatches` does: the sentinel's ARRIVAL must re-run the observer
  // effect (an object ref would leave the list stuck on page 1 if the sentinel mounted after the effect ran).
  const [sentinelNode, setSentinelNode] = useState<HTMLDivElement | null>(null);
  const sentinelRef = useCallback((node: HTMLDivElement | null) => setSentinelNode(node), []);

  // ADR-051: load the next page ~200px before the sentinel scrolls into view. Re-created after every page (`nextPage`
  // in deps): an IntersectionObserver reports the CURRENT intersection when it starts observing, so a short list that
  // still shows the sentinel fetches again instead of waiting for a scroll that will never come.
  const scrollerRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (list.status !== 'ready' || !hasMore || !sentinelNode || typeof IntersectionObserver === 'undefined') return;
    const generation = generationRef.current;
    const observer = new IntersectionObserver(
      entries => {
        if (inFlightRef.current) return;
        if (entries.some(entry => entry.isIntersecting)) void fetchPage(nextPage, query, generation);
      },
      { root: scrollerRef.current, rootMargin: SENTINEL_ROOT_MARGIN, threshold: 0 }
    );
    observer.observe(sentinelNode);
    return () => observer.disconnect();
  }, [list.status, hasMore, nextPage, query, fetchPage, sentinelNode]);

  const retryList = (): void => {
    if (list.status !== 'error') return;
    void fetchPage(list.page, query, generationRef.current);
  };

  const handleSelect = (_event: SelectionEvents, data: OptionOnSelectData): void => {
    const firm = items.find(f => f.firmId === data.optionValue) ?? null;
    if (firm) {
      setSelected(firm);
      setRefusal(null);
    }
  };

  const send = async (firm: InvoiceFirmOption): Promise<void> => {
    if (sendingRef.current) return;
    sendingRef.current = true;
    setSending(true);
    setRefusal(null);
    let outcome: AddFirmOutcome;
    try {
      outcome = await addFirmToRecord(target, firm.firmId);
    } finally {
      sendingRef.current = false;
      if (mountedRef.current) setSending(false);
    }
    if (!mountedRef.current) return;
    if (outcome.kind === 'refused') {
      // Not `setPendingFirm(null)`: a refused Retry leaves the firm on the record with its access unfinished, so the
      // pending message and its Retry stay, with the refusal under them.
      setRefusal({ code: outcome.code, message: outcome.message });
      announce(outcome.message, 'assertive');
      return;
    }
    onAdded(outcome, firm);
    if (outcome.accessState === 'pending') {
      // The firm is on the record; the access pass is not finished. Stay open so Retry (the same request) can finish it.
      setPendingFirm(firm);
      announce(FIRM_ACCESS_PENDING_MESSAGE);
      return;
    }
    setPendingFirm(null);
    onClose();
  };

  if (pendingFirm) {
    return (
      <div className={styles.panel} role="group" aria-label="Add a firm to this record" data-testid="add-firm-panel">
        <MessageBar intent="warning" layout="multiline" data-testid="add-firm-pending">
          <MessageBarBody>{FIRM_ACCESS_PENDING_MESSAGE}</MessageBarBody>
          <MessageBarActions>
            <Button
              appearance="outline"
              size="small"
              ref={el => {
                retryButtonRef.current = el;
              }}
              disabled={sending}
              onClick={() => void send(pendingFirm)}
              data-testid="add-firm-pending-retry"
            >
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
        {refusal && (
          <MessageBar intent="error" layout="multiline" data-testid="add-firm-refusal">
            <MessageBarBody>{refusal.message}</MessageBarBody>
          </MessageBar>
        )}
        <div className={styles.actions}>
          {sending && <Spinner size="tiny" label="Updating access…" />}
          <Button appearance="secondary" disabled={sending} onClick={onClose} data-testid="add-firm-close">
            Close
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.panel} role="group" aria-label="Add a firm to this record" data-testid="add-firm-panel">
      <Field label="Search law firms">
        <SearchBox
          ref={el => {
            searchInputRef.current = el;
          }}
          value={searchText}
          onChange={(_, data) => setSearchText(data.value)}
          placeholder="Firm name"
          disabled={sending}
          data-testid="add-firm-search"
        />
      </Field>

      <div className={styles.scroller} ref={scrollerRef} data-testid="add-firm-scroll">
        {list.status === 'loading' && (
          <div className={styles.status}>
            <Spinner size="tiny" />
            <Text size={200}>Loading law firms…</Text>
          </div>
        )}

        {list.status === 'ready' && items.length === 0 && (
          <Text size={200} className={styles.note} data-testid="add-firm-empty">
            {NO_LAW_FIRMS_MESSAGE}
          </Text>
        )}

        {items.length > 0 && (
          <Listbox
            aria-label="Law firms"
            selectedOptions={selected ? [selected.firmId] : []}
            onOptionSelect={handleSelect}
            data-testid="add-firm-list"
          >
            {items.map(firm => (
              <Option key={firm.firmId} value={firm.firmId} text={firm.name} disabled={sending}>
                {firm.name}
              </Option>
            ))}
          </Listbox>
        )}

        {loadingMore && (
          <div className={styles.status}>
            <Spinner size="tiny" />
            <Text size={200}>Loading more law firms…</Text>
          </div>
        )}

        {list.status === 'error' && (
          <MessageBar intent="error" layout="multiline" data-testid="add-firm-list-error">
            <MessageBarBody>{list.message}</MessageBarBody>
            <MessageBarActions>
              <Button appearance="outline" size="small" onClick={retryList} data-testid="add-firm-list-retry">
                Retry
              </Button>
            </MessageBarActions>
          </MessageBar>
        )}

        {/* The progressive-fetch sentinel (ADR-051). Inside the scroll area, after the last row. */}
        {list.status === 'ready' && hasMore && (
          <div ref={sentinelRef} className={styles.sentinel} aria-hidden="true" data-testid="add-firm-sentinel" />
        )}
      </div>

      {selected && (
        <Text size={200} data-testid="add-firm-access-notice">
          {describeAddFirmNotice(selected.name)}
        </Text>
      )}

      {refusal && (
        <>
          <MessageBar intent="error" layout="multiline" data-testid="add-firm-refusal">
            <MessageBarBody>{refusal.message}</MessageBarBody>
          </MessageBar>
          {refusal.code === FIRM_ASSIGN_SLOTS_FULL_CODE && openInSpaarke}
        </>
      )}

      <div className={styles.actions}>
        {sending && <Spinner size="tiny" label="Adding the firm…" />}
        <Button
          appearance="primary"
          disabled={!selected || sending}
          onClick={() => selected && void send(selected)}
          data-testid="add-firm-confirm"
        >
          Add firm
        </Button>
        <Button appearance="secondary" disabled={sending} onClick={onClose} data-testid="add-firm-cancel">
          Cancel
        </Button>
      </div>
    </div>
  );
};
