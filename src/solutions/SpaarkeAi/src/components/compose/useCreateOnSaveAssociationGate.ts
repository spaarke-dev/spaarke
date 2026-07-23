/**
 * useCreateOnSaveAssociationGate.ts
 *
 * FR-05 — the coordinator that connects the create-on-save COMPLETION event to
 * the Tier-2c association GATE DIALOG.
 *
 * This is the piece that finally makes the FR-05 picker reachable: it opens the
 * gate dialog when a transient draft is first persisted (create-on-save), lets
 * the user optionally pick a parent (feeding `setAssociation`), and — on
 * confirm — fires the already-landed `associate(newDocumentId)` write so the
 * chosen parent is bound onto the new `sprk_document`. Before this hook,
 * `setAssociation` was never called, so `associate()` always ran with a `null`
 * selection (a graceful no-op) and no picker ever surfaced.
 *
 * Contract with the compose host (`ThreePaneShell` → `ComposeLaunchContext`):
 *   - `onCreateOnSaveComplete(documentId)` is wired into the launch context and
 *     is invoked by `ComposeWorkspace` AFTER the new document is persisted
 *     (`saveSucceeded` is dispatched first). It simply OPENS the gate — it does
 *     NOT block the save (spec FR-05: Save is never blocked on a parent).
 *   - `dialogProps` is spread onto `<CreateOnSaveAssociationGateDialog />`.
 *
 * @see useCreateOnSaveAssociation.ts — the underlying selection-state + write hook (reused unmodified)
 * @see CreateOnSaveAssociationGateDialog.tsx — the dialog these props drive
 * @see documentAssociationWrite.ts — the write path (cleanGuid-wraps both ids before the @odata.bind)
 */

import * as React from 'react';
import { createXrmNavigationService } from '@spaarke/ui-components';
import type { AssociationResult, EntityTypeOption, IDataService, INavigationService } from '@spaarke/ui-components';
import { useCreateOnSaveAssociation } from './useCreateOnSaveAssociation';
import type { ICreateOnSaveAssociationGateDialogProps } from './CreateOnSaveAssociationGateDialog';

export interface IUseCreateOnSaveAssociationGateOptions {
  /** Dataverse write surface. Defaults to `createXrmDataService()` (via the underlying hook). Tests inject a mock. */
  dataService?: IDataService;
  /** Lookup surface for the embedded picker. Defaults to `createXrmNavigationService()`. Tests inject a mock. */
  navigationService?: INavigationService;
  /** Optional restriction on offered parent types (e.g. from a `GateAssociationAffordance.allowedTargets`). */
  allowedTargets?: ReadonlyArray<EntityTypeOption>;
}

export interface IUseCreateOnSaveAssociationGateResult {
  /**
   * Wire this into `ComposeLaunchContext.onCreateOnSaveComplete`. Opens the
   * gate dialog for the newly-created document. Non-blocking (returns `void`) —
   * the document is already saved; the association is optional.
   */
  onCreateOnSaveComplete: (newDocumentId: string) => void;
  /** Spread onto `<CreateOnSaveAssociationGateDialog {...dialogProps} />`. */
  dialogProps: ICreateOnSaveAssociationGateDialogProps;
}

/**
 * Owns the FR-05 create-on-save association gate: open-state, the pending
 * document id, and the confirm/skip handlers that drive the already-landed
 * `associate()` write.
 *
 * @example
 * ```tsx
 * const { onCreateOnSaveComplete, dialogProps } = useCreateOnSaveAssociationGate();
 *
 * const composeLaunch = { ...otherFields, onCreateOnSaveComplete };
 *
 * return (
 *   <ComposeLaunchContext.Provider value={composeLaunch}>
 *     {children}
 *     <CreateOnSaveAssociationGateDialog {...dialogProps} />
 *   </ComposeLaunchContext.Provider>
 * );
 * ```
 */
export function useCreateOnSaveAssociationGate(
  options?: IUseCreateOnSaveAssociationGateOptions
): IUseCreateOnSaveAssociationGateResult {
  const { association, setAssociation, associate, isAssociating, error } = useCreateOnSaveAssociation({
    ...(options?.dataService ? { dataService: options.dataService } : {}),
  });

  // Lazy-init the navigation service once (the Xrm adapter binds lazily -- it
  // only touches `Xrm.Navigation` when `openLookup` is actually called, so
  // constructing it eagerly is safe even in a non-Dataverse host). Tests inject a mock.
  const [navigationService] = React.useState<INavigationService>(
    () => options?.navigationService ?? createXrmNavigationService()
  );

  const [open, setOpen] = React.useState(false);
  const pendingDocumentIdRef = React.useRef<string | null>(null);

  const onCreateOnSaveComplete = React.useCallback(
    (newDocumentId: string): void => {
      pendingDocumentIdRef.current = newDocumentId;
      // Fresh gate per create -- clear any stale prior selection so a second
      // create-on-save in the same session doesn't inherit the last parent.
      setAssociation(null);
      setOpen(true);
    },
    [setAssociation]
  );

  const handleChange = React.useCallback(
    (result: AssociationResult | null): void => {
      setAssociation(result);
    },
    [setAssociation]
  );

  const handleConfirm = React.useCallback(async (): Promise<void> => {
    const documentId = pendingDocumentIdRef.current;
    if (!documentId) {
      setOpen(false);
      return;
    }
    // associate() no-ops (returns success) when the selection is "none", and
    // cleanGuid-wraps both the parent id and the document id before the
    // @odata.bind write (see documentAssociationWrite.ts). A standalone
    // document is a valid outcome -- Save is never blocked on a parent.
    const result = await associate(documentId);
    if (result.success) {
      pendingDocumentIdRef.current = null;
      setOpen(false);
    }
    // On a non-fatal write failure: keep the gate open so the surfaced `error`
    // is visible and the user can retry or Skip (the document already exists).
  }, [associate]);

  const handleSkip = React.useCallback((): void => {
    pendingDocumentIdRef.current = null;
    setAssociation(null);
    setOpen(false);
  }, [setAssociation]);

  const dialogProps: ICreateOnSaveAssociationGateDialogProps = {
    open,
    navigationService,
    association,
    onChange: handleChange,
    onConfirm: () => {
      void handleConfirm();
    },
    onSkip: handleSkip,
    isAssociating,
    error,
    ...(options?.allowedTargets ? { allowedTargets: options.allowedTargets } : {}),
  };

  return { onCreateOnSaveComplete, dialogProps };
}
