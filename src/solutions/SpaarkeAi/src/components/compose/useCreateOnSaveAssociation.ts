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
import type { AssociationResult } from '@spaarke/ui-components';
import {
  associateDocumentToParent,
  bffDocumentRefile,
  type DocumentRefile,
  type IAssociateDocumentResult,
} from './documentAssociationWrite';
import { authenticatedFetch } from '../../services/authInit';
import { getBffBaseUrl } from '../../config/runtimeConfig';

export interface IUseCreateOnSaveAssociationOptions {
  /**
   * How the document is filed under the chosen record. Defaults to the BFF re-file
   * (`PUT /api/v1/documents/{id}` over the SpaarkeAi authenticated fetch) — UAC-r2 task 147 r1: filing a document under a
   * record changes its owner, so it is never a client-side Dataverse write. Tests inject a mock.
   */
  refileDocument?: DocumentRefile;
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
  // Lazy-init via useState initializer (runs once) -- avoids mutating a ref during render. The base URL is read at call
  // time, not here: the runtime config may resolve after the first render.
  const [refileDocument] = React.useState<DocumentRefile>(
    () =>
      options?.refileDocument ??
      ((documentId, body) => bffDocumentRefile(authenticatedFetch, getBffBaseUrl())(documentId, body))
  );

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
        const result = await associateDocumentToParent(refileDocument, documentId, association);
        if (!result.success) {
          setError(result.warning ?? 'Failed to associate the document.');
        }
        return result;
      } finally {
        setIsAssociating(false);
      }
    },
    [association, refileDocument]
  );

  return { association, setAssociation, associate, isAssociating, error };
}
