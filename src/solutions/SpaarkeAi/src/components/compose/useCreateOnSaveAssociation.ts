/**
 * useCreateOnSaveAssociation.ts
 *
 * FR-05 — wires the `CreateOnSaveAssociationPrompt` selection to the actual
 * Dataverse write (`associateDocumentToParent`) for create-on-save
 * completion. Independent of any specific host: whichever surface owns the
 * save-completion flow (the Tier-2c gate dialog, once its hosting wiring
 * lands) calls `associate(documentId)` once the new `sprk_document` record
 * exists.
 *
 * This hook deliberately does NOT know about `ComposeWorkspace` / the
 * create-on-save pipeline itself (`ComposeService.SaveAsync`, task 013/060
 * territory, outside this task's write boundary) -- it only owns the
 * selection state + the association write, per the task split described in
 * the POML ("Build the independent half; stub/guard the gate-hosting half").
 *
 * @see CreateOnSaveAssociationPrompt.tsx -- the UI this hook's state feeds
 * @see documentAssociationWrite.ts -- the write this hook calls
 */

import * as React from 'react';
import { createXrmDataService } from '@spaarke/ui-components';
import type { AssociationResult, IDataService } from '@spaarke/ui-components';
import { associateDocumentToParent, type IAssociateDocumentResult } from './documentAssociationWrite';

export interface IUseCreateOnSaveAssociationOptions {
  /**
   * Dataverse write surface. Defaults to `createXrmDataService()` (the
   * client-side Dataverse call per ADR-028 -- no BFF, no raw Bearer). Tests
   * inject a mock.
   */
  dataService?: IDataService;
}

export interface IUseCreateOnSaveAssociationResult {
  /** Current selection (`null` = "none" / standalone). */
  association: AssociationResult | null;
  /** Controlled setter -- pass directly as `CreateOnSaveAssociationPrompt`'s `onChange`. */
  setAssociation: (result: AssociationResult | null) => void;
  /**
   * Writes the current selection onto the given `sprk_document` record.
   * No-ops (returns `{ success: true }`) when the selection is "none" --
   * Save is never blocked on a parent (spec FR-05).
   */
  associate: (documentId: string) => Promise<IAssociateDocumentResult>;
  /** True while the association write is in flight. */
  isAssociating: boolean;
  /** Set when the most recent `associate()` call failed (non-fatal -- the document itself was already created). */
  error: string | null;
}

/**
 * Owns the FR-05 association selection + write for create-on-save.
 *
 * @example
 * ```tsx
 * const { association, setAssociation, associate, isAssociating, error } = useCreateOnSaveAssociation();
 *
 * <CreateOnSaveAssociationPrompt
 *   navigationService={navigationService}
 *   value={association}
 *   onChange={setAssociation}
 *   disabled={isAssociating}
 * />
 *
 * // After the create-on-save pipeline produces a new sprk_document id:
 * await associate(newDocumentId);
 * ```
 */
export function useCreateOnSaveAssociation(
  options?: IUseCreateOnSaveAssociationOptions
): IUseCreateOnSaveAssociationResult {
  // Lazy-init via useState initializer (runs once) -- avoids mutating a ref
  // during render. Production callers omit `options.dataService` and get the
  // real Xrm-backed adapter; tests inject a mock.
  const [dataService] = React.useState<IDataService>(() => options?.dataService ?? createXrmDataService());

  const [association, setAssociation] = React.useState<AssociationResult | null>(null);
  const [isAssociating, setIsAssociating] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const associate = React.useCallback(
    async (documentId: string): Promise<IAssociateDocumentResult> => {
      setError(null);

      // "None" -- a standalone Document is valid. No write, no spinner.
      if (!association) {
        return { success: true };
      }

      setIsAssociating(true);
      try {
        const result = await associateDocumentToParent(dataService, documentId, association);
        if (!result.success) {
          setError(result.warning ?? 'Failed to associate the document.');
        }
        return result;
      } finally {
        setIsAssociating(false);
      }
    },
    [association, dataService]
  );

  return { association, setAssociation, associate, isAssociating, error };
}
