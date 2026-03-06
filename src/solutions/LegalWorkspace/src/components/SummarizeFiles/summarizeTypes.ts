/**
 * summarizeTypes.ts
 * Type definitions for the Summarize New File(s) wizard.
 */

// ---------------------------------------------------------------------------
// Playbook output schema
// ---------------------------------------------------------------------------

/** A single file highlight entry (only present for multi-file uploads). */
export interface IFileHighlight {
  fileName: string;
  documentType: string;
  highlights: string[];
}

/** A mentioned party extracted from the documents. */
export interface IMentionedParty {
  name: string;
  role: string;
}

/**
 * Structured JSON output returned by the Summarize File playbook.
 * All optional fields are only present when the AI could confidently extract them.
 */
export interface ISummarizeResult {
  /** 2-3 sentence TL;DR. Always present. */
  tldr: string;
  /** Cross-file narrative summary. Always present. */
  summary: string;
  /** Per-file highlights — ONLY present when multiple files were uploaded. */
  fileHighlights?: IFileHighlight[];
  /** Detected practice areas. */
  practiceAreas?: string[];
  /** People/organizations mentioned in the documents. */
  mentionedParties?: IMentionedParty[];
  /** Actionable next steps extracted from the documents. */
  callToAction?: string;
  /** Condensed version for email embedding. Always present. */
  shortSummary: string;
  /** AI confidence score 0.0-1.0. */
  confidence: number;
}

// ---------------------------------------------------------------------------
// Analysis status
// ---------------------------------------------------------------------------

/** Lifecycle of the summarize playbook execution. */
export type SummarizeStatus = 'idle' | 'loading' | 'success' | 'error';

// ---------------------------------------------------------------------------
// BFF request / response
// ---------------------------------------------------------------------------

/** Response shape from POST /api/workspace/files/summarize. */
export interface ISummarizeResponse {
  result: ISummarizeResult;
}
