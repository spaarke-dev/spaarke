/**
 * @spaarke/ai-widgets — PinnedMemoryListWidget
 *
 * Context-pane widget delivering the Pinned Memory CRUD + visualization UX
 * promised by R6 task 070 (Q7 scope expansion). Shows the caller's pinned
 * memory items grouped by `pinType` (user-preference / system-rule /
 * matter-fact) with filter, search, and per-item edit / delete actions.
 *
 * Companion components owned by this widget:
 *   - {@link PinnedMemoryEditDialog} — create + edit form.
 *   - {@link PinnedMemoryDeleteConfirmation} — delete confirm (cross-session warning).
 *   - {@link PinnedMemoryProvenanceBadge} — UI vs chat source attribution (stub).
 *
 * BFF integration (per PART A handoff):
 *   - Base path: `/api/memory/pins`
 *   - List:   `GET /api/memory/pins[?matterId={id}]`
 *   - Create: `POST /api/memory/pins`
 *   - Update: `PUT  /api/memory/pins/{pinId}`
 *   - Delete: `DELETE /api/memory/pins/{pinId}`  →  204 No Content
 *   - Tenant + user scope: BFF derives tenant from `tid`, user from `oid`.
 *     The client does NOT send either — bug class (cross-tenant) cannot exist.
 *
 * Standards:
 *   - ADR-008 / ADR-016: BFF surface is authenticated + rate-limited.
 *   - ADR-012: lives in `@spaarke/ai-widgets`; Fluent v9 components.
 *   - ADR-013: consumes BFF via `authenticatedFetch` from `@spaarke/auth`.
 *   - ADR-015: never logs title / content text in any telemetry; counts only.
 *   - ADR-021: zero hardcoded colors; Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component + hooks.
 *   - ADR-028: function-based auth surface; token never crosses a prop.
 *   - ADR-030: subscribes to no PaneEventBus channel (display-only widget).
 *
 * Task: R6-070 (D-C-24 / D-C-25, Pillar 7, Q7 scope expansion) — PART B.
 */

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Badge,
  Button,
  Divider,
  Dropdown,
  Input,
  makeStyles,
  mergeClasses,
  Option,
  Spinner,
  Text,
  Tooltip,
  tokens,
} from '@fluentui/react-components';
import {
  AddRegular,
  ArrowClockwiseRegular,
  DeleteRegular,
  EditRegular,
  PinRegular,
  SearchRegular,
} from '@fluentui/react-icons';
import { buildBffApiUrl } from '@spaarke/auth';

import type { ContextWidgetProps } from '../../types/widget-types';
import { useAiSession } from '../../providers/useAiSession';
import {
  buildListPath,
  buildPinPath,
  PIN_TYPE_VALUES,
  type PinDto,
  type PinListResponse,
  type PinType,
  type PinUpsertRequest,
  type PinUpsertResponse,
  type ProblemDetailsLike,
} from '../../components/memory/pinned-memory-contracts';
import PinnedMemoryEditDialog, {
  type PinnedMemoryEditDialogMode,
} from '../../components/memory/PinnedMemoryEditDialog';
import PinnedMemoryDeleteConfirmation from '../../components/memory/PinnedMemoryDeleteConfirmation';
import PinnedMemoryProvenanceBadge from '../../components/memory/PinnedMemoryProvenanceBadge';

// ---------------------------------------------------------------------------
// Public widget contract
// ---------------------------------------------------------------------------

/** Widget type ID under which `PinnedMemoryListWidget` is registered. */
export const PINNED_MEMORY_LIST_WIDGET_TYPE = 'pinned-memory-list' as const;

/**
 * Data payload delivered to the widget via `ContextWidgetProps.data`. All
 * fields are optional — the widget loads its data from the BFF on mount and
 * does not require any server-pushed seed.
 */
export interface PinnedMemoryListData {
  /** Optional matter scoping — when set, narrows the GET list call. */
  matterId?: string;
}

export type PinnedMemoryListWidgetProps = ContextWidgetProps<PinnedMemoryListData>;

// ---------------------------------------------------------------------------
// Internal state types
// ---------------------------------------------------------------------------

type PinTypeFilter = 'all' | PinType;

type DialogState = { kind: 'closed' } | { kind: 'create' } | { kind: 'edit'; pin: PinDto };

type DeleteState = { kind: 'idle' } | { kind: 'confirm'; pin: PinDto } | { kind: 'deleting'; pin: PinDto };

interface LoadState {
  isLoading: boolean;
  error: string | null;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  headerTitleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
    flex: 1,
  },
  headerSubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  controlsRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
    alignItems: 'center',
  },
  searchInput: {
    flex: 1,
    minWidth: '160px',
  },
  filterDropdown: {
    minWidth: '160px',
  },
  scrollContainer: {
    flex: 1,
    minHeight: 0,
    overflowY: 'auto',
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalM,
  },
  groupBlock: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingTop: tokens.spacingVerticalM,
  },
  groupHeaderRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  groupHeaderTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.5px',
  },
  groupHeaderBadge: {
    flexShrink: 0,
  },
  itemList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  item: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderTopLeftRadius: tokens.borderRadiusMedium,
    borderTopRightRadius: tokens.borderRadiusMedium,
    borderBottomLeftRadius: tokens.borderRadiusMedium,
    borderBottomRightRadius: tokens.borderRadiusMedium,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
  },
  itemTopRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  itemTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    flex: 1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  itemActions: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  itemContent: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    display: '-webkit-box',
    WebkitLineClamp: 3,
    WebkitBoxOrient: 'vertical',
    overflow: 'hidden',
  },
  itemFooterRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  itemMatterTag: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontFamily: tokens.fontFamilyMonospace,
  },
  centerState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flex: 1,
    minHeight: 0,
    textAlign: 'center',
  },
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: '48px',
  },
  emptyTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground2,
  },
  emptyBody: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    maxWidth: '320px',
  },
  errorText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorPaletteRedForeground1,
    lineHeight: tokens.lineHeightBase200,
  },
});

// ---------------------------------------------------------------------------
// Pin-type display metadata (group headings)
// ---------------------------------------------------------------------------

const PIN_TYPE_GROUP_ORDER: readonly PinType[] = ['user-preference', 'system-rule', 'matter-fact'];

const PIN_TYPE_GROUP_LABELS: Record<PinType, string> = {
  'user-preference': 'User preferences',
  'system-rule': 'System rules',
  'matter-fact': 'Matter facts',
};

const PIN_TYPE_FILTER_LABELS: Record<PinTypeFilter, string> = {
  all: 'All pin types',
  'user-preference': 'User preferences',
  'system-rule': 'System rules',
  'matter-fact': 'Matter facts',
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function caseInsensitiveIncludes(haystack: string, needle: string): boolean {
  if (needle.length === 0) return true;
  return haystack.toLowerCase().includes(needle.toLowerCase());
}

/**
 * Best-effort error extractor — surfaces `detail` from ProblemDetails JSON if
 * available; otherwise falls back to a status-based label. Never throws.
 */
async function extractError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as ProblemDetailsLike;
    if (body && typeof body.detail === 'string' && body.detail.length > 0) {
      return body.detail;
    }
    if (body && typeof body.title === 'string' && body.title.length > 0) {
      return body.title;
    }
  } catch {
    // Body wasn't JSON — fall through.
  }
  return `Request failed (${response.status}).`;
}

// ---------------------------------------------------------------------------
// PinnedMemoryListWidget
// ---------------------------------------------------------------------------

/**
 * PinnedMemoryListWidget — primary Context-pane widget for R6 Q7 Pinned
 * Memory UI. Loads the caller's pinned items from the BFF on mount, groups
 * them by pinType, and surfaces create / edit / delete CRUD.
 */
const PinnedMemoryListWidget: React.FC<PinnedMemoryListWidgetProps> = ({
  data,
  isLoading: externalLoading,
  error: externalError,
  className,
}) => {
  const styles = useStyles();
  const { bffBaseUrl, authenticatedFetch } = useAiSession();
  const matterScope = data?.matterId;

  // ── Server-state ────────────────────────────────────────────────────────
  const [pins, setPins] = useState<PinDto[]>([]);
  const [load, setLoad] = useState<LoadState>({ isLoading: true, error: null });

  // ── UI-state ────────────────────────────────────────────────────────────
  const [search, setSearch] = useState<string>('');
  const [filter, setFilter] = useState<PinTypeFilter>('all');
  const [dialog, setDialog] = useState<DialogState>({ kind: 'closed' });
  const [deleteState, setDeleteState] = useState<DeleteState>({ kind: 'idle' });
  const [dialogIsSubmitting, setDialogIsSubmitting] = useState<boolean>(false);
  const [dialogServerError, setDialogServerError] = useState<string | null>(null);

  // Track latest in-flight request to discard out-of-order responses.
  const requestSeqRef = useRef<number>(0);

  // ── Load ────────────────────────────────────────────────────────────────
  const loadPins = useCallback(async () => {
    const seq = ++requestSeqRef.current;
    setLoad({ isLoading: true, error: null });
    try {
      const url = buildBffApiUrl(bffBaseUrl, buildListPath(matterScope));
      const response = await authenticatedFetch(url, { method: 'GET' });
      if (seq !== requestSeqRef.current) return; // superseded — ignore.
      if (!response.ok) {
        const detail = await extractError(response);
        setLoad({ isLoading: false, error: detail });
        return;
      }
      const body = (await response.json()) as PinListResponse;
      setPins(Array.isArray(body.items) ? body.items : []);
      setLoad({ isLoading: false, error: null });
    } catch (err) {
      if (seq !== requestSeqRef.current) return;
      // ADR-015: never log title / content text. Log the error class only.
      console.warn('[PinnedMemoryListWidget] Load failed:', (err as Error)?.name ?? 'Error');
      setLoad({
        isLoading: false,
        error: 'Could not load pinned memory. Please try again.',
      });
    }
  }, [authenticatedFetch, bffBaseUrl, matterScope]);

  useEffect(() => {
    void loadPins();
  }, [loadPins]);

  // ── Dialog open / close ─────────────────────────────────────────────────
  const handleOpenCreate = useCallback(() => {
    setDialogServerError(null);
    setDialog({ kind: 'create' });
  }, []);

  const handleOpenEdit = useCallback((pin: PinDto) => {
    setDialogServerError(null);
    setDialog({ kind: 'edit', pin });
  }, []);

  const handleCloseDialog = useCallback(() => {
    setDialog({ kind: 'closed' });
    setDialogIsSubmitting(false);
    setDialogServerError(null);
  }, []);

  // ── Submit (Create + Edit) ──────────────────────────────────────────────
  const handleSubmitDialog = useCallback(
    async (req: PinUpsertRequest) => {
      if (dialog.kind === 'closed') return;
      setDialogIsSubmitting(true);
      setDialogServerError(null);
      try {
        const isCreate = dialog.kind === 'create';
        const url = buildBffApiUrl(bffBaseUrl, isCreate ? buildListPath() : buildPinPath(dialog.pin.pinId));
        const response = await authenticatedFetch(url, {
          method: isCreate ? 'POST' : 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(req),
        });
        if (!response.ok) {
          const detail = await extractError(response);
          setDialogServerError(detail);
          setDialogIsSubmitting(false);
          return;
        }
        const body = (await response.json()) as PinUpsertResponse;
        const saved = body.item;
        setPins(prev => {
          if (isCreate) {
            return [saved, ...prev];
          }
          return prev.map(p => (p.pinId === saved.pinId ? saved : p));
        });
        setDialog({ kind: 'closed' });
        setDialogIsSubmitting(false);
        setDialogServerError(null);
      } catch (err) {
        console.warn('[PinnedMemoryListWidget] Save failed:', (err as Error)?.name ?? 'Error');
        setDialogServerError('Could not save the pin. Please try again.');
        setDialogIsSubmitting(false);
      }
    },
    [authenticatedFetch, bffBaseUrl, dialog]
  );

  // ── Delete flow ─────────────────────────────────────────────────────────
  const handleOpenDelete = useCallback((pin: PinDto) => {
    setDeleteState({ kind: 'confirm', pin });
  }, []);

  const handleCancelDelete = useCallback(() => {
    setDeleteState({ kind: 'idle' });
  }, []);

  const handleConfirmDelete = useCallback(async () => {
    if (deleteState.kind !== 'confirm') return;
    const pinId = deleteState.pin.pinId;
    setDeleteState({ kind: 'deleting', pin: deleteState.pin });
    try {
      const url = buildBffApiUrl(bffBaseUrl, buildPinPath(pinId));
      const response = await authenticatedFetch(url, { method: 'DELETE' });
      // 204 No Content is success; anything else is an error.
      if (!response.ok && response.status !== 204) {
        const detail = await extractError(response);
        // ADR-015: log status code only, never the pin content.
        console.warn('[PinnedMemoryListWidget] Delete failed:', response.status);
        setDeleteState({ kind: 'idle' });
        setLoad(prev => ({ ...prev, error: detail }));
        return;
      }
      setPins(prev => prev.filter(p => p.pinId !== pinId));
      setDeleteState({ kind: 'idle' });
    } catch (err) {
      console.warn('[PinnedMemoryListWidget] Delete failed:', (err as Error)?.name ?? 'Error');
      setDeleteState({ kind: 'idle' });
      setLoad(prev => ({ ...prev, error: 'Could not delete the pin. Please try again.' }));
    }
  }, [authenticatedFetch, bffBaseUrl, deleteState]);

  // ── Derived: filtered + grouped pins ────────────────────────────────────
  const filteredPins = useMemo(() => {
    const trimmedSearch = search.trim();
    return pins.filter(p => {
      if (filter !== 'all' && p.pinType !== filter) return false;
      if (trimmedSearch.length > 0 && !caseInsensitiveIncludes(p.title, trimmedSearch)) {
        return false;
      }
      return true;
    });
  }, [filter, pins, search]);

  const groupedPins = useMemo<Record<PinType, PinDto[]>>(() => {
    const groups: Record<PinType, PinDto[]> = {
      'user-preference': [],
      'system-rule': [],
      'matter-fact': [],
    };
    for (const p of filteredPins) {
      // Defensive: tolerate unexpected pinType values from the server by
      // skipping (never throw at render time).
      if ((PIN_TYPE_VALUES as readonly string[]).includes(p.pinType)) {
        groups[p.pinType].push(p);
      }
    }
    return groups;
  }, [filteredPins]);

  const totalShown = filteredPins.length;

  // ── Sub-render: pin row ─────────────────────────────────────────────────
  const renderPinRow = (pin: PinDto) => (
    <div
      key={pin.pinId}
      className={styles.item}
      data-testid="pinned-memory-item"
      data-pin-id={pin.pinId}
      data-pin-type={pin.pinType}
    >
      <div className={styles.itemTopRow}>
        <Text className={styles.itemTitle} title={pin.title}>
          {pin.title}
        </Text>
        <div className={styles.itemActions}>
          <Tooltip content="Edit this pin" relationship="label" positioning="above">
            <Button
              appearance="subtle"
              size="small"
              icon={<EditRegular />}
              onClick={() => handleOpenEdit(pin)}
              aria-label={`Edit pin ${pin.title}`}
              data-testid="pinned-memory-edit-button"
            />
          </Tooltip>
          <Tooltip content="Delete this pin" relationship="label" positioning="above">
            <Button
              appearance="subtle"
              size="small"
              icon={<DeleteRegular />}
              onClick={() => handleOpenDelete(pin)}
              aria-label={`Delete pin ${pin.title}`}
              data-testid="pinned-memory-delete-button"
            />
          </Tooltip>
        </div>
      </div>
      <Text className={styles.itemContent} title={pin.content}>
        {pin.content}
      </Text>
      <div className={styles.itemFooterRow}>
        {/* Provenance badge is a STUB until the data layer carries source.
            See PinnedMemoryProvenanceBadge file-header. */}
        <PinnedMemoryProvenanceBadge />
        {pin.matterId && (
          <Text className={styles.itemMatterTag} title={pin.matterId}>
            Matter: {pin.matterId}
          </Text>
        )}
      </div>
    </div>
  );

  // ── Sub-render: empty state ─────────────────────────────────────────────
  const renderEmpty = (): React.ReactNode => {
    const hasUserFilter = search.trim().length > 0 || filter !== 'all';
    return (
      <div className={styles.centerState} role="status">
        <PinRegular className={styles.emptyIcon} aria-hidden="true" />
        <Text className={styles.emptyTitle}>{hasUserFilter ? 'No matching pins' : 'No pinned memory yet'}</Text>
        <Text className={styles.emptyBody}>
          {hasUserFilter
            ? 'Try clearing the search or changing the pin-type filter.'
            : 'Create a pin to give the assistant a long-lived preference, rule, or fact to remember across sessions.'}
        </Text>
        {!hasUserFilter && (
          <Button appearance="primary" icon={<AddRegular />} onClick={handleOpenCreate}>
            Create your first pin
          </Button>
        )}
      </div>
    );
  };

  // ── Top-level branches ──────────────────────────────────────────────────
  const showLoading = externalLoading || load.isLoading;
  const displayError = externalError ?? load.error;

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="region"
      aria-label="Pinned memory"
      data-testid="pinned-memory-list-widget"
    >
      <div className={styles.header}>
        <div className={styles.headerTitleRow}>
          <PinRegular aria-hidden="true" />
          <Text className={styles.headerTitle}>Pinned Memory</Text>
          <Tooltip content="Refresh" relationship="label" positioning="above">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              onClick={() => void loadPins()}
              disabled={showLoading}
              aria-label="Refresh pinned memory"
              data-testid="pinned-memory-refresh"
            />
          </Tooltip>
          <Button
            appearance="primary"
            size="small"
            icon={<AddRegular />}
            onClick={handleOpenCreate}
            data-testid="pinned-memory-create-button"
          >
            New pin
          </Button>
        </div>
        <Text className={styles.headerSubtitle}>{totalShown === 1 ? '1 pin shown' : `${totalShown} pins shown`}</Text>
        <div className={styles.controlsRow}>
          <Input
            className={styles.searchInput}
            value={search}
            onChange={(_e, d) => setSearch(d.value)}
            contentBefore={<SearchRegular />}
            placeholder="Search by title…"
            aria-label="Search pinned memory by title"
            data-testid="pinned-memory-search"
          />
          <Dropdown
            className={styles.filterDropdown}
            value={PIN_TYPE_FILTER_LABELS[filter]}
            selectedOptions={[filter]}
            onOptionSelect={(_e, d) => {
              const next = (d.optionValue ?? 'all') as PinTypeFilter;
              setFilter(next);
            }}
            aria-label="Filter by pin type"
            data-testid="pinned-memory-filter"
          >
            <Option value="all" text={PIN_TYPE_FILTER_LABELS.all}>
              {PIN_TYPE_FILTER_LABELS.all}
            </Option>
            {PIN_TYPE_VALUES.map(pt => (
              <Option key={pt} value={pt} text={PIN_TYPE_FILTER_LABELS[pt]}>
                {PIN_TYPE_FILTER_LABELS[pt]}
              </Option>
            ))}
          </Dropdown>
        </div>
      </div>
      <Divider appearance="subtle" />

      <div className={styles.scrollContainer} data-testid="pinned-memory-scroll">
        {/* Loading branch */}
        {showLoading && (
          <div className={styles.centerState} role="status" aria-busy="true" aria-label="Loading">
            <Spinner size="small" />
            <Text className={styles.emptyBody}>Loading pinned memory…</Text>
          </div>
        )}

        {/* Error branch (load-level) — surfaced inline; user can still create */}
        {!showLoading && displayError && (
          <div className={styles.centerState} role="alert" data-testid="pinned-memory-error">
            <Text className={styles.errorText}>{displayError}</Text>
            <Button appearance="primary" onClick={() => void loadPins()}>
              Retry
            </Button>
          </div>
        )}

        {/* Data branches */}
        {!showLoading && !displayError && totalShown === 0 && renderEmpty()}

        {!showLoading && !displayError && totalShown > 0 && (
          <div data-testid="pinned-memory-groups">
            {PIN_TYPE_GROUP_ORDER.map(pt => {
              const group = groupedPins[pt];
              if (group.length === 0) return null;
              return (
                <div key={pt} className={styles.groupBlock} data-testid="pinned-memory-group" data-group-type={pt}>
                  <div className={styles.groupHeaderRow}>
                    <Text className={styles.groupHeaderTitle}>{PIN_TYPE_GROUP_LABELS[pt]}</Text>
                    <Badge className={styles.groupHeaderBadge} appearance="outline" color="informative" size="small">
                      {group.length}
                    </Badge>
                  </div>
                  <div className={styles.itemList} role="list" aria-label={PIN_TYPE_GROUP_LABELS[pt]}>
                    {group.map(renderPinRow)}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Edit / create dialog */}
      <PinnedMemoryEditDialog
        open={dialog.kind !== 'closed'}
        mode={(dialog.kind === 'closed' ? 'create' : dialog.kind) as PinnedMemoryEditDialogMode}
        initial={dialog.kind === 'edit' ? dialog.pin : undefined}
        isSubmitting={dialogIsSubmitting}
        serverError={dialogServerError}
        onSubmit={handleSubmitDialog}
        onCancel={handleCloseDialog}
      />

      {/* Delete confirmation */}
      <PinnedMemoryDeleteConfirmation
        open={deleteState.kind !== 'idle'}
        pinTitle={deleteState.kind === 'idle' ? '' : deleteState.pin.title}
        isDeleting={deleteState.kind === 'deleting'}
        onConfirm={handleConfirmDelete}
        onCancel={handleCancelDelete}
      />
    </div>
  );
};

PinnedMemoryListWidget.displayName = 'PinnedMemoryListWidget';

export default PinnedMemoryListWidget;
