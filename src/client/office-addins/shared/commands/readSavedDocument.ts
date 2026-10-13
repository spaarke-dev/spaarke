/**
 * Reading a Quick Save's job for the document it landed on — shared by the Word and Outlook ribbon commands
 * (task 118 moved it here from `word/commands/index.ts`; behaviour unchanged).
 */

import { apiClient } from '@shared/services';
import { cleanGuid } from '@spaarke/ui-components/guid';
import type { QuickSaveSavedDocument } from '@shared/taskpane/services/quickSaveHelpers';

/** How many times Quick Save reads its job for the saved document, and how long it waits between reads. */
const JOB_READ_ATTEMPTS = 3;
const JOB_READ_DELAY_MS = 500;

/** The fields of `POST /api/office/save`'s 202 / 200 body that Quick Save reads (server `SaveResponse`). */
export interface QuickSaveResponse {
  duplicate?: boolean;
  jobId?: string;
  statusUrl?: string;
}

/** The fields of `GET /api/office/jobs/{id}` that Quick Save reads (server `JobStatusResponse`). */
interface QuickSaveJobStatus {
  status?: string;
  result?: { artifact?: { id?: string; webUrl?: string | null } | null } | null;
  error?: { message?: string } | null;
}

/** What reading the save's job told Quick Save. */
export type SavedDocumentRead =
  | { kind: 'saved'; document: QuickSaveSavedDocument }
  | { kind: 'unknown' }
  | { kind: 'failed'; reason: string };

/**
 * Read the save's job for the document it landed on (`result.artifact` — task 039 finding 3: the created document
 * on a create, the existing one on a version). The synchronous save records it before answering, so the first read
 * normally has it; a few short retries cover a slow write. A read that fails is `unknown` — the save itself already
 * succeeded, so this never turns a success into an error. A version save already knows its document, so an
 * unreadable job still names it.
 */
export async function readSavedDocument(
  statusUrl: string | undefined,
  knownDocumentId: string | null
): Promise<SavedDocumentRead> {
  const fallback: SavedDocumentRead = knownDocumentId
    ? { kind: 'saved', document: { documentId: knownDocumentId } }
    : { kind: 'unknown' };
  if (!statusUrl) {
    return fallback;
  }

  for (let attempt = 0; attempt < JOB_READ_ATTEMPTS; attempt++) {
    if (attempt > 0) {
      await new Promise(resolve => setTimeout(resolve, JOB_READ_DELAY_MS));
    }

    let job: QuickSaveJobStatus;
    try {
      job = await apiClient.get<QuickSaveJobStatus>(statusUrl);
    } catch (error) {
      console.warn('[Spaarke] Quick Save: the save succeeded, but its job could not be read', error);
      return fallback;
    }

    const artifactId = cleanGuid(job.result?.artifact?.id);
    if (artifactId) {
      return { kind: 'saved', document: { documentId: artifactId, webUrl: job.result?.artifact?.webUrl ?? null } };
    }
    if (job.status === 'Failed' || job.status === 'Cancelled') {
      return { kind: 'failed', reason: job.error?.message?.trim() || `the save ended ${job.status.toLowerCase()}` };
    }
    if (job.status === 'Completed') {
      break;
    }
  }

  return fallback;
}
