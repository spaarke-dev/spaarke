/**
 * EmailConnectionsReview.tsx
 *
 * Reading-pane ASSOCIATIONS sub-view (email-communication-solution-r5 task 035,
 * FR-13 / FR-19). React 19 review view over the PRODUCTION `ConnectionsEditor`
 * Layer-1 logic extracted in task 020
 * (`@spaarke/communication-components/logic/connections` — deep-imported here
 * via the package-relative `../../logic/connections` path, per the established
 * in-package convention in `AttachmentList`/`EmailComposeActions`). This view
 * is NOT built on the stale `CommunicationPage/.../ConnectionsEditor.tsx` stub
 * (design Lens 4 / ADR-045) — every grouping/derive/write function below comes
 * from the task-020 shared module.
 *
 * Groups the email's record associations into three action sections (mirrors
 * the production `CommunicationConnections` PCF's rebuilt grouped body, task
 * 105 / UAT R2 C1 — same `groupConnectionsByAction` partition, a trimmed
 * presentation for the reading-pane context, no AI-suggested-"Create X" rows
 * — that affordance belongs to the toolbar's Create action, task 036, not the
 * association review):
 *   - "Needs your decision" — ambiguous conflicts (FR-19 uncertainty state).
 *   - "Filed automatically" — confirmed / auto-filed associations, INCLUDING
 *     reply-chain auto-association (`ThreadContinuityRung`, applied
 *     server-side at ingestion) — this section requires NO special-casing:
 *     the task-020 `deriveConnections`/`mergeFiledConnections` already mark a
 *     `written: true` candidate as `status: 'confirmed'` regardless of which
 *     rung produced it, so a reply's inherited regarding renders here as
 *     display-only, never recomputed client-side (ADR-045).
 *   - "Suggested" — soft matches (Confirm / Dismiss) + "Link another record…".
 *
 * WRITE PATH (FR-13 MUST): confirm / change / link-another all persist via
 * `applyRegardingSelection` (task-020 additive write — starts from an EMPTY
 * payload, never nulls a sibling typed lookup). The "Dismiss" affordance on a
 * FILED row calls `unlinkRegarding`, which nulls EXACTLY the one targeted
 * entity's lookup and leaves every other regarding association untouched —
 * this is the additive model's inverse-of-add, never a bulk clear (verified
 * by the "dismiss preserves siblings" test). Dismissing a SUGGESTED (never
 * filed) row is a client-side, in-session hide — nothing was written, so
 * hiding it needs no write (mirrors the production PCF's dismissal model).
 *
 * "Change" (on a filed row) and "Link another record…" both re-use the
 * shared `PolymorphicPicker` (`@spaarke/ui-components`) for record selection
 * — §11 reuse, no new picker/search UI is built here. Picking a record calls
 * straight back into `applyRegardingSelection`.
 *
 * Fluent v9 tokens only (ADR-021, dark-mode correct via the host
 * `FluentProvider`). No `as React.ComponentType` cast (NFR-05) — this is a
 * Layer-2 (React 19 code-page) view; it is not shared across the PCF boundary.
 *
 * Split at code-review time (Step 9.5) into four files to keep each under the
 * review-metric line thresholds — no behavior change:
 *   - `EmailConnectionsReview.styles.ts`  — Fluent v9 styles.
 *   - `EmailConnectionsReview.helpers.ts` — constants + pure helpers.
 *   - `EmailConnectionsReviewRows.tsx`    — the three row sub-components.
 *   - this file                          — state + write orchestration + composition.
 */
import * as React from 'react';
import { mergeClasses, Text, Button, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { Link20Regular } from '@fluentui/react-icons';
import { PolymorphicPicker } from '@spaarke/ui-components';
import {
  parseProvenance,
  deriveConnections,
  mergeFiledConnections,
  groupConnectionsByAction,
  applyRegardingSelection,
  unlinkRegarding,
  type Connection,
  type IRegardingSelection,
} from '../../logic/connections';
import type { EmailConnectionsReviewProps } from './EmailAssociationsAndTracking.types';
import { useConnectionsReviewStyles } from './EmailConnectionsReview.styles';
import { ASSOCIATION_STATUS_RESOLVED, EMPTY_PROVENANCE, DEFAULT_LINK_CATALOG, fieldFor } from './EmailConnectionsReview.helpers';
import { DecisionBlock, FiledRow, SuggestedRow } from './EmailConnectionsReviewRows';

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
  } = props;
  const s = useConnectionsReviewStyles();

  const [confirmedFields, setConfirmedFields] = React.useState<Set<string>>(new Set());
  const [dismissed, setDismissed] = React.useState<Set<string>>(new Set());
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [changingEntity, setChangingEntity] = React.useState<string | null>(null);
  const [linking, setLinking] = React.useState(false);

  // Session-local review state is per-selected-email — reset on selection change.
  React.useEffect(() => {
    setConfirmedFields(new Set());
    setDismissed(new Set());
    setError(null);
    setChangingEntity(null);
    setLinking(false);
  }, [communicationId]);

  const provenance = React.useMemo(() => parseProvenance(associationProvenanceJson), [associationProvenanceJson]);
  const isResolved = associationStatus === ASSOCIATION_STATUS_RESOLVED;

  // SAME data path as the production PCF: derive engine slots, then fold in
  // what's ACTUALLY filed on the record (authoritative — includes manual
  // "Link another" associations not suggested by the engine).
  const connections = React.useMemo(
    () => mergeFiledConnections(deriveConnections(provenance ?? EMPTY_PROVENANCE, isResolved), filedAssociations),
    [provenance, isResolved, filedAssociations]
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

  // "Dismiss" on a FILED row — removes ONLY this one association (unlinkRegarding
  // nulls exactly the one typed lookup); every sibling regarding stays filed.
  const handleDismissFiled = React.useCallback(
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

  // "Dismiss" on a SUGGESTED row — client-side, in-session hide only (never
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

  const suggestedCount = suggested.length;
  const showSuggestedSection = suggestedCount > 0 || !readOnly;
  const catalog = linkAnotherCatalog ?? DEFAULT_LINK_CATALOG;
  const hasAnyRows = needsDecision.length > 0 || filed.length > 0 || suggestedCount > 0;

  return (
    <div className={s.root} data-testid="email-connections-review">
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {needsDecision.length > 0 && (
        <section className={s.section}>
          <div className={s.secHead}>
            <span className={mergeClasses(s.dot, s.dotDecide)} />
            <Text className={s.secTitle}>Needs your decision</Text>
            <Text className={s.secCount}>
              · {needsDecision.length} conflict{needsDecision.length === 1 ? '' : 's'}
            </Text>
          </div>
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
        </section>
      )}

      {filed.length > 0 && (
        <section className={s.section}>
          <div className={s.secHead}>
            <span className={mergeClasses(s.dot, s.dotFiled)} />
            <Text className={s.secTitle}>Filed automatically</Text>
            <Text className={s.secCount}>· {filed.length} linked</Text>
          </div>
          {filed.map((conn, i) => (
            <FiledRow
              key={conn.field}
              conn={conn}
              first={i === 0}
              busy={busy}
              readOnly={readOnly}
              resolveDisplayName={resolveDisplayName}
              isChanging={changingEntity === conn.entity}
              onRequestChange={() => setChangingEntity(conn.entity)}
              onCancelChange={() => setChangingEntity(null)}
              onChangeSelected={handleChangeSelected}
              onDismiss={() => handleDismissFiled(conn)}
              pickerWebApi={pickerWebApi}
              catalog={catalog}
              s={s}
            />
          ))}
        </section>
      )}

      {showSuggestedSection && (
        <section className={s.section}>
          <div className={s.secHead}>
            <span className={mergeClasses(s.dot, s.dotSuggest)} />
            <Text className={s.secTitle}>Suggested</Text>
            <Text className={s.secCount}>· {suggestedCount} to confirm</Text>
          </div>
          {suggested.map((conn, i) => (
            <SuggestedRow
              key={conn.field}
              conn={conn}
              first={i === 0}
              busy={busy}
              readOnly={readOnly}
              resolveDisplayName={resolveDisplayName}
              onConfirm={handleConfirm}
              onDismiss={handleDismissSuggested}
              s={s}
            />
          ))}
          {suggestedCount === 0 && <Text className={s.recWhy}>No suggestions right now.</Text>}
          {!readOnly &&
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
        </section>
      )}

      {!hasAnyRows && readOnly && <Text className={s.empty}>No connections yet.</Text>}
    </div>
  );
}
