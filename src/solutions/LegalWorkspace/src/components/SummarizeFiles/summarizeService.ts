/**
 * summarizeService.ts
 * Service layer for the Summarize New File(s) wizard.
 * Calls the BFF summarize endpoint with uploaded files.
 */
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/bffAuthProvider';
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

  const response = await authenticatedFetch(url, {
    method: 'POST',
    body: formData,
    signal,
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => 'Unknown error');
    console.error(`${LOG_PREFIX} BFF returned ${response.status}: ${errorText}`);
    throw new Error(`Summarize failed (${response.status}): ${errorText}`);
  }

  const json = await response.json();
  console.info(`${LOG_PREFIX} Raw response:`, JSON.stringify(json).substring(0, 500));

  // The BFF returns { result: <playbook output> }
  // The playbook output may be the ISummarizeResult directly, or wrapped in rawResponse
  let result = json.result;

  // Unwrap rawResponse envelope if present (GenericAnalysisHandler wraps output this way)
  if (result && typeof result === 'object' && 'rawResponse' in result) {
    try {
      result = typeof result.rawResponse === 'string'
        ? JSON.parse(result.rawResponse)
        : result.rawResponse;
    } catch {
      // If rawResponse isn't valid JSON, use it as the summary text
      result = {
        tldr: String(result.rawResponse).substring(0, 200),
        summary: String(result.rawResponse),
        shortSummary: String(result.rawResponse).substring(0, 200),
        confidence: 0.5,
      };
    }
  }

  // Diagnostic logging — show actual shapes of array fields
  if (result && typeof result === 'object') {
    if (result.fileHighlights) {
      console.info(`${LOG_PREFIX} fileHighlights (${Array.isArray(result.fileHighlights) ? result.fileHighlights.length : typeof result.fileHighlights} items):`,
        JSON.stringify(result.fileHighlights).substring(0, 800));
    }
    if (result.mentionedParties) {
      console.info(`${LOG_PREFIX} mentionedParties:`, JSON.stringify(result.mentionedParties).substring(0, 400));
    }
  }

  // Defensive normalization — ensure array fields match ISummarizeResult shape
  if (result && typeof result === 'object') {
    // fileHighlights: ensure each entry is { fileName, documentType, highlights[] }
    if (result.fileHighlights && Array.isArray(result.fileHighlights)) {
      const raw = result.fileHighlights;
      // Check if it's a flat array of strings (AI returned line-by-line format)
      if (raw.length > 0 && typeof raw[0] === 'string') {
        const parsed: { fileName: string; documentType: string; summary: string; highlights: string[] }[] = [];
        let current: { fileName: string; documentType: string; summary: string; highlights: string[] } | null = null;
        // Detect file extension pattern to identify filename lines
        const fileExtPattern = /\.(docx?|pdf|xlsx?|pptx?|txt|csv|rtf|msg|eml)$/i;
        type ParsePhase = 'expect-type' | 'expect-summary' | 'in-summary' | 'in-highlights';
        let phase: ParsePhase = 'expect-type';

        for (const line of raw as string[]) {
          const trimmed = line.trim();
          if (!trimmed) continue;

          // Check for "Document:" prefix (original format)
          const docPrefixMatch = trimmed.match(/^Document:\s*(.+)/);
          // Check for bare filename with extension
          const isFileName = fileExtPattern.test(trimmed) || docPrefixMatch;

          if (isFileName) {
            // Start a new file entry
            if (current) parsed.push(current);
            const name = docPrefixMatch ? docPrefixMatch[1] : trimmed;
            current = { fileName: name, documentType: '', summary: '', highlights: [] };
            phase = 'expect-type';
          } else if (/^Type:\s*/i.test(trimmed) && current) {
            current.documentType = trimmed.replace(/^Type:\s*/i, '');
            phase = 'expect-summary';
          } else if (/^Summary:\s*/i.test(trimmed) && current) {
            const inlineText = trimmed.replace(/^Summary:\s*/i, '');
            if (inlineText) current.summary = inlineText;
            phase = 'in-summary';
          } else if (/^Highlights?:/i.test(trimmed)) {
            phase = 'in-highlights';
          } else if (trimmed.startsWith('-') && current) {
            phase = 'in-highlights';
            current.highlights.push(trimmed.replace(/^-\s*/, ''));
          } else if (current) {
            if (phase === 'expect-type' && !current.documentType) {
              // Line right after filename = document type
              current.documentType = trimmed;
              phase = 'expect-summary';
            } else if (phase === 'expect-summary' || phase === 'in-summary') {
              // Lines before "Highlights:" = summary text
              current.summary = current.summary ? `${current.summary} ${trimmed}` : trimmed;
              phase = 'in-summary';
            } else if (phase === 'in-highlights') {
              // Non-bulleted highlight line
              current.highlights.push(trimmed);
            }
          }
        }
        if (current) parsed.push(current);
        result.fileHighlights = parsed;
        console.info(`${LOG_PREFIX} Parsed ${parsed.length} file highlights from flat string format`);
      } else {
        // Already structured objects — normalize property names
        result.fileHighlights = raw
          .map((fh: unknown) => {
            if (typeof fh !== 'object' || fh === null) return null;
            const obj = fh as Record<string, unknown>;
            const fileName = (obj.fileName ?? obj.file_name ?? obj.name ?? 'Unknown') as string;
            const documentType = (obj.documentType ?? obj.document_type ?? obj.type ?? '') as string;
            const summary = (obj.summary ?? obj.fileSummary ?? obj.file_summary ?? '') as string;
            const highlights = Array.isArray(obj.highlights) ? obj.highlights
              : Array.isArray(obj.key_points) ? obj.key_points
              : [];
            return { fileName, documentType, summary, highlights };
          })
          .filter(Boolean);
      }
    } else if (result.fileHighlights && !Array.isArray(result.fileHighlights)) {
      result.fileHighlights = [];
    }

    // practiceAreas: ensure array of strings
    if (result.practiceAreas && !Array.isArray(result.practiceAreas)) {
      result.practiceAreas = [];
    }

    // mentionedParties: ensure each entry is { name, role }
    if (result.mentionedParties && Array.isArray(result.mentionedParties)) {
      result.mentionedParties = result.mentionedParties
        .map((p: unknown) => {
          if (typeof p === 'string') return { name: p, role: '' };
          if (typeof p !== 'object' || p === null) return null;
          const obj = p as Record<string, unknown>;
          const name = (obj.name ?? obj.partyName ?? obj.party_name ?? '') as string;
          const role = (obj.role ?? obj.partyRole ?? obj.party_role ?? '') as string;
          return name ? { name, role } : null;
        })
        .filter(Boolean);
    } else if (result.mentionedParties && !Array.isArray(result.mentionedParties)) {
      result.mentionedParties = [];
    }
  }

  console.info(`${LOG_PREFIX} Summary received, confidence=${result?.confidence}`);
  return result as ISummarizeResult;
}
