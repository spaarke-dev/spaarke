/**
 * summarizeService.ts
 * Service layer for the Summarize New File(s) wizard.
 * Calls the BFF summarize endpoint with uploaded files.
 */
import { getBffBaseUrl } from '../../config/bffConfig';
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import type { ISummarizeResult } from './summarizeTypes';

const LOG_PREFIX = '[SummarizeService]';

/**
 * Calls POST /api/workspace/files/summarize with the uploaded files
 * as multipart/form-data. Returns the structured summary result.
 */
export async function runSummarize(
  files: IUploadedFile[],
  signal?: AbortSignal,
): Promise<ISummarizeResult> {
  const bffBaseUrl = getBffBaseUrl();
  const url = `${bffBaseUrl}/workspace/files/summarize`;

  const formData = new FormData();
  for (const f of files) {
    formData.append('files', f.file, f.name);
  }

  console.info(`${LOG_PREFIX} Sending ${files.length} file(s) to ${url}`);

  const response = await fetch(url, {
    method: 'POST',
    body: formData,
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
    console.error(`${LOG_PREFIX} BFF returned ${response.status}: ${errorText}`);
    throw new Error(`Summarize failed (${response.status}): ${errorText}`);
  }

  const json = await response.json();
  console.info(`${LOG_PREFIX} Summary received, confidence=${json.result?.confidence}`);
  return json.result as ISummarizeResult;
}
