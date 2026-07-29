/**
 * EmailConnectionsReview.tsx — the reading-pane ASSOCIATION RESOLVER (email-
 * communication-solution-r5, reading-pane MAIN-AREA redesign, section #6).
 *
 * Redesign goal: answer ONE plain question with an obvious action. The section
 * that hosts this view is COLLAPSED by default; its header dot signals state, so
 * this body does NOT auto-expand anything. States (locked owner design):
 *   - CLEAR MATCH  — a single strong primary suggestion: "This email looks like
 *     it's about {Matter · X} — {rationale} · {97%}" → [✓ Confirm] · "Not this".
 *   - AMBIGUOUS    — 2+ strong conflicting candidates: "Which is this about?" →
 *     a ranked radio list (best pre-selected), each showing {label} · {real %} ·
 *     {plain-English rationale}. Enter confirms.
 *   - FILED        — green, SILENT: "✓ Filed to {X}" + Change / Remove only.
 *   - SUGGESTED    — a low/med-confidence single: "Possible match: {X} · {%} ·
 *     {why}" → Confirm · Not related.
 *   - UNMATCHED    — "Not filed yet." → Find a record · (Create new) · Dismiss.
 * Every % is REAL (from `sprk_associationprovenance`) and is ALWAYS paired with
 * its *why* — never a bare, unexplained percentage.
 *
 * WRITE PATH (binding MUST): confirm / change / link-another all persist via the
 * task-020 ADDITIVE `applyRegardingSelection` (starts from an EMPTY payload,
 * never nulls a sibling typed lookup). "Remove" on a FILED row calls
 * `unlinkRegarding`, which nulls EXACTLY the one targeted lookup and leaves every
 * other regarding untouched (the additive model's inverse-of-add, never a bulk
 * clear). Dismissing a SUGGESTED (never-filed) row is an in-session hide — nothing
 * was written, so hiding needs no write. The connection derivation is the shared
 * `buildEmailConnections` (no client-side recompute of engine decisions; ADR-045).
 *
 * "Change" / "Link another" / "Find a record" reuse the shared `PolymorphicPicker`
 * (`@spaarke/ui-components`) — §11 reuse, no new picker UI. Fluent v9 tokens only
 * (ADR-021, dark-mode correct). No `as React.ComponentType` cast (NFR-05).
 */
import * as React from 'react';
import { Text, Button, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { Search20Regular, Link20Regular } from '@fluentui/react-icons';
import { PolymorphicPicker } from '@spaarke/ui-components';
import {
  buildEmailConnections,
  groupConnectionsByAction,
  applyRegardingSelection,
  unlinkRegarding,
  type Connection,
  type IRegardingSelection,
} from '../../logic/connections';
import type { EmailConnectionsReviewProps } from './EmailAssociationsAndTracking.types';
import { useConnectionsReviewStyles } from './EmailConnectionsReview.styles';
import { DEFAULT_LINK_CATALOG, fieldFor } from './EmailConnectionsReview.helpers';
import { DecisionBlock, FiledRow, SuggestedMatch } from './EmailConnectionsReviewRows';

export function EmailConnectionsReview(props: EmailConnectionsReviewProps): React.ReactElement {
  const {
    communicationId,
    associationStatus,
    associationProvenanceJson,
    filedAssociations = [],
    writeContext,
    pickerWebApi = {},
    linkAnotherCatalog,
    resolveDisplayName,
    readOnly = false,
    onAssociationsChanged,
    onCreateNewRecord,
  } = props;
  const s = useConnectionsReviewStyles();

  const [confirmedFields, setConfirmedFields] = React.useState<Set<string>>(new Set());
  const [dismissed, setDismissed] = React.useState<Set<string>>(new Set());
  const [dismissedUnmatched, setDismissedUnmatched] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [changingEntity, setChangingEntity] = React.useState<string | null>(null);
  const [linking, setLinking] = React.useState(false);

  // Session-local review state is per-selected-email — reset on selection change.
  React.useEffect(() => {
    setConfirmedFields(new Set());
    setDismissed(new Set());
    setDismissedUnmatched(false);
    setError(null);
    setChangingEntity(null);
    setLinking(false);
  }, [communicationId]);

  // SAME data path as the production PCF (no client recompute; ADR-045).
  const connections = React.useMemo(
    () => buildEmailConnections(associationProvenanceJson, associationStatus, filedAssociations),
    [associationProvenanceJson, associationStatus, filedAssociations]
  );

  const { needsDecision, filed, suggested } = React.useMemo(
    () => groupConnectionsByAction(connections, [], confirmedFields, dismissed),
    [connections, confirmedFields, dismissed]
  );

  const persist = React.useCallback(
    async (field: string, selection: IRegardingSelection): Promise<void> => {
      setBusy(true);
      setError(null);
      try {
        const res = await applyRegardingSelection(writeContext, selection);
        if (!res.success) {
          setError(res.error ?? 'Could not file this connection.');
          return;
        }
        setConfirmedFields(prev => new Set(prev).add(field));
        onAssociationsChanged?.();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unexpected error while filing.');
      } finally {
        setBusy(false);
      }
    },
    [writeContext, onAssociationsChanged]
  );

  const handleConfirm = React.useCallback(
    (conn: Connection, chosen?: { targetEntity: string; targetId: string; targetName?: string }): void => {
      const selection: IRegardingSelection = chosen
        ? { entityType: chosen.targetEntity, recordId: chosen.targetId, recordName: chosen.targetName ?? chosen.targetId }
        : { entityType: conn.entity, recordId: conn.targetId, recordName: conn.targetName };
      void persist(conn.field, selection);
    },
    [persist]
  );

  // "Remove" on a FILED row — removes ONLY this one association (unlinkRegarding
  // nulls exactly the one typed lookup); every sibling regarding stays filed.
  const handleRemoveFiled = React.useCallback(
    (conn: Connection): void => {
      void (async () => {
        setBusy(true);
        setError(null);
        try {
          const res = await unlinkRegarding(writeContext, conn.entity);
          if (!res.success) {
            setError(res.error ?? 'Could not remove this connection.');
            return;
          }
          setConfirmedFields(prev => {
            const next = new Set(prev);
            next.delete(conn.field);
            return next;
          });
          onAssociationsChanged?.();
        } catch (err) {
          setError(err instanceof Error ? err.message : 'Unexpected error while removing.');
        } finally {
          setBusy(false);
        }
      })();
    },
    [writeContext, onAssociationsChanged]
  );

  // "Not this" / "Not related" on a SUGGESTED row — in-session hide only (never
  // filed, so hiding it needs no write — matches the production PCF's model).
  const handleDismissSuggested = React.useCallback((key: string): void => {
    setDismissed(prev => new Set(prev).add(key));
  }, []);

  const handleChangeSelected = React.useCallback(
    (entityType: string, recordId: string, recordName: string): void => {
      setChangingEntity(null);
      void persist(fieldFor(entityType), { entityType, recordId, recordName });
    },
    [persist]
  );

  const handleLinkAnotherSelected = React.useCallback(
    (entityType: string, recordId: string, recordName: string): void => {
      setLinking(false);
      void persist(fieldFor(entityType), { entityType, recordId, recordName });
    },
    [persist]
  );

  const catalog = linkAnotherCatalog ?? DEFAULT_LINK_CATALOG;
  const hasAnyRows = needsDecision.length > 0 || filed.length > 0 || suggested.length > 0;
  const isUnmatched = !hasAnyRows && !dismissedUnmatched;

  return (
    <div className={s.root} data-testid="email-connections-review">
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* AMBIGUOUS — "Which is this about?" */}
      {needsDecision.map(conn => (
        <DecisionBlock
          key={conn.field}
          conn={conn}
          busy={busy}
          readOnly={readOnly}
          resolveDisplayName={resolveDisplayName}
          onConfirm={handleConfirm}
          s={s}
        />
      ))}

      {/* SUGGESTED — clear match (high) or possible match (low/med) */}
      {suggested.map(conn => (
        <SuggestedMatch
          key={conn.field}
          conn={conn}
          busy={busy}
          readOnly={readOnly}
          resolveDisplayName={resolveDisplayName}
          onConfirm={handleConfirm}
          onDismiss={handleDismissSuggested}
          s={s}
        />
      ))}

      {/* FILED — green, silent (Change / Remove) */}
      {filed.length > 0 && (
        <div className={s.block}>
          <Text className={s.groupLabel}>Filed</Text>
          {filed.map(conn => (
            <FiledRow
              key={conn.field}
              conn={conn}
              busy={busy}
              readOnly={readOnly}
              resolveDisplayName={resolveDisplayName}
              isChanging={changingEntity === conn.entity}
              onRequestChange={() => setChangingEntity(conn.entity)}
              onCancelChange={() => setChangingEntity(null)}
              onChangeSelected={handleChangeSelected}
              onRemove={() => handleRemoveFiled(conn)}
              pickerWebApi={pickerWebApi}
              catalog={catalog}
              s={s}
            />
          ))}
        </div>
      )}

      {/* UNMATCHED (interactive) — "Not filed yet." + Find a record · (Create new) · Dismiss */}
      {isUnmatched && !readOnly && (
        <div className={s.unmatched}>
          <Text className={s.unmatchedText}>Not filed yet.</Text>
          {linking ? (
            <div className={s.linkRow}>
              <PolymorphicPicker
                title="Find a record"
                catalog={catalog}
                webApi={pickerWebApi}
                onSelect={handleLinkAnotherSelected}
                onError={m => setError(m)}
                disabled={busy}
              />
            </div>
          ) : (
            <div className={s.actionsRow}>
              <Button size="small" appearance="primary" icon={<Search20Regular />} disabled={busy} onClick={() => setLinking(true)}>
                Find a record
              </Button>
              {onCreateNewRecord && (
                <Button size="small" appearance="secondary" disabled={busy} onClick={() => onCreateNewRecord()}>
                  Create new
                </Button>
              )}
              <Button size="small" appearance="transparent" className={s.linkBtn} disabled={busy} onClick={() => setDismissedUnmatched(true)}>
                Dismiss
              </Button>
            </div>
          )}
        </div>
      )}

      {/* UNMATCHED (review-only) — nothing to review */}
      {isUnmatched && readOnly && <Text className={s.empty}>No connections yet.</Text>}

      {/* LINK ANOTHER — available once there IS at least one row to add alongside */}
      {!readOnly &&
        hasAnyRows &&
        (linking ? (
          <div className={s.linkRow}>
            <PolymorphicPicker
              title="Link another record"
              catalog={catalog}
              webApi={pickerWebApi}
              onSelect={handleLinkAnotherSelected}
              onError={m => setError(m)}
              disabled={busy}
            />
          </div>
        ) : (
          <div className={s.linkRow}>
            <Button size="small" appearance="subtle" icon={<Link20Regular />} disabled={busy} onClick={() => setLinking(true)}>
              Link another record…
            </Button>
          </div>
        ))}
    </div>
  );
}
