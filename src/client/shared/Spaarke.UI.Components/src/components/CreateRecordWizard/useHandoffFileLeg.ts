/**
 * useHandoffFileLeg — the Assistant create-flow "file leg" (UAT W-2/W-5 + R5-7).
 *
 * When a Create* wizard is launched from the Assistant's surface-launch flow, the
 * hand-off envelope carries the drafted-from session file references
 * (`{ sessionId, fileIds, fileNames }`) BY REFERENCE (never inline binary). This
 * hook fetches each file's binary from the BFF session document-content endpoint,
 * wraps it as a browser `File` → `IUploadedFile`, and returns the list so the
 * wizard can pass it as `config.initialFiles` (which the CreateRecordWizard ADD_FILES
 * effect pre-seeds into the Add file(s) step). The files then ride the wizard's
 * EXISTING upload+link+index pipeline into the new record's container.
 *
 * Extracted from CreateMatterWizard's inline impl (the reference file leg) so
 * CreateEventWizard (and any future wizard) reuses ONE implementation rather than
 * forking the fetch loop (CLAUDE.md §11 — default to reuse).
 *
 * Fail-soft: a failed fetch / unsupported extension is skipped (not injected as an
 * invalid entry); `undefined`/empty refs → returns `[]` and the files step opens
 * empty exactly as a direct open (no regression).
 */

import * as React from 'react';
import type { IUploadedFile, UploadedFileType } from '../FileUpload/fileUploadTypes';

/** The hand-off's session file references (subset of the surface-handoff seed). */
export interface HandoffFileRefs {
  readonly sessionId?: string;
  readonly fileIds: ReadonlyArray<string>;
  readonly fileNames?: ReadonlyArray<string>;
}

/** Wizard-supported upload types keyed by lowercase extension. */
const HANDOFF_EXT_TO_FILE_TYPE: Readonly<Record<string, UploadedFileType>> = {
  '.pdf': 'pdf',
  '.docx': 'docx',
  '.xlsx': 'xlsx',
};

/**
 * Build an {@link IUploadedFile} from a browser File fetched via the session
 * document-content endpoint. Returns null for an unsupported extension (the Add
 * file(s) step only accepts pdf/docx/xlsx) so unsupported session files are
 * skipped rather than injected as invalid entries.
 */
export function handoffFileToUploaded(file: File): IUploadedFile | null {
  const dot = file.name.lastIndexOf('.');
  const ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  const fileType = HANDOFF_EXT_TO_FILE_TYPE[ext];
  if (!fileType) return null;
  return {
    id: `handoff-${file.name}-${file.size}`,
    name: file.name,
    sizeBytes: file.size,
    fileType,
    file,
  };
}

/**
 * Fetch the hand-off's session files and return them as `IUploadedFile[]` once
 * the wizard is `open`. Re-fetches when `initialFileRefs` identity changes; clears
 * on close. Never inlines binary through the URL (envelope invariant).
 */
export function useHandoffFileLeg(
  open: boolean,
  initialFileRefs: HandoffFileRefs | undefined,
  authenticatedFetch: (input: string, init?: RequestInit) => Promise<Response>,
  bffBaseUrl: string
): IUploadedFile[] {
  const [handoffFiles, setHandoffFiles] = React.useState<IUploadedFile[]>([]);

  React.useEffect(() => {
    if (!open) {
      setHandoffFiles([]);
      return;
    }
    const sessionId = initialFileRefs?.sessionId;
    const fileIds = initialFileRefs?.fileIds;
    if (!sessionId || !fileIds || fileIds.length === 0) {
      return;
    }
    const fileNames = initialFileRefs?.fileNames;
    let cancelled = false;
    void (async () => {
      const built: IUploadedFile[] = [];
      for (let i = 0; i < fileIds.length; i++) {
        const fileId = fileIds[i];
        try {
          const res = await authenticatedFetch(
            `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/documents/${encodeURIComponent(fileId)}/content`
          );
          if (!res.ok) continue;
          const blob = await res.blob();
          const name = fileNames?.[i] ?? `document-${i + 1}`;
          const file = new File([blob], name, { type: blob.type || 'application/octet-stream' });
          const uploaded = handoffFileToUploaded(file);
          if (uploaded) built.push(uploaded);
        } catch {
          // Non-fatal: a fetch failure (expired binary, network) just means that
          // file isn't pre-attached; the user can add it manually.
        }
      }
      if (!cancelled && built.length > 0) setHandoffFiles(built);
    })();
    return () => {
      cancelled = true;
    };
  }, [open, initialFileRefs, authenticatedFetch, bffBaseUrl]);

  return handoffFiles;
}
