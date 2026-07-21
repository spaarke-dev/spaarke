/**
 * composeDraftRouting.ts — client-side PRESENTATION-only intent detection for the R7-4 (UAT
 * 2026-07-21) "substantial output → Compose tab" routing. Mirrors `composeReviseRouting.ts`:
 * a pure, total, side-effect-free two-signal regex guard. It decides only WHETHER a typed message
 * is a request to DRAFT a substantial DOCUMENT (a brief / memo / analysis / opinion) — never
 * which Binding to run. The `compose-draft-document` bindingId is resolved from capability
 * discovery at dispatch time (ADR-039: bindingId comes only from discovery, never invented here).
 *
 * Why this exists: a "write a brief on the chevron doctrine" / "analyze this agreement" ask should
 * produce an editor-ready document in a Compose tab, NOT a long answer dumped into the Assistant
 * chat. Detecting it client-side lets us dispatch the shipped `compose-draft-document` capability
 * (disposition = compose) deterministically and suppress the raw agent turn, instead of relying on
 * the model to pick the tool (which it does not do reliably — the reported bug).
 *
 * Scope boundary (same ADR-039 exception the revise NL detectors carve out): we detect *whether*
 * to draft a document, never fabricate a capability id or author content client-side.
 *
 * @see composeReviseRouting.ts — the sibling NL-detection precedent this mirrors
 * @see DocumentUploadedEventStream.isComposeDraftDocument — the output-shape guard for the result
 */

/** A generate/author verb — the first required signal. Excludes "create" (collides with
 *  create-matter/-project/-task surface launches) and "revise/rewrite" (the revise path). */
const DRAFT_VERB_RE = /\b(write|draft|compose|prepare|produce|author)\b/i;

/** A document-output noun — the second required signal for the verb+noun branch. Deliberately
 *  excludes email-shaped nouns (those route to `draft-correspondence`, per its toolDescription). */
const DOC_NOUN_RE =
  /\b(brief|memo|memorandum|analysis|opinion|motion|report|argument|outline|position paper|white paper|legal analysis|summary of law)\b/i;

/** An "analyze <this document/agreement/…>" ask — an analysis IS a substantial document output.
 *  Requires the analyze verb PLUS a document-object reference so it doesn't fire on a bare
 *  "what's your analysis of the market" question. */
const ANALYZE_DOC_RE =
  /\banaly[sz]e\b[\s\S]{0,40}\b(this|these|the|attached|document|agreement|contract|file|filing|nda|lease|clause|policy|letter|brief)\b/i;

/** Email-shaped asks route to `draft-correspondence`, not `compose-draft-document`. */
const EMAIL_RE = /\b(e-?mail)\b/i;

/** An EXPLICIT "…in / into the (open) Compose tab / editor / document / this document" target
 *  (UAT R7-5). When the user names the Compose surface as the destination, the ask is
 *  unambiguously a draft-INTO-Compose request — it wins even over the email exclusion (the
 *  screenshot case: "write me an engagement letter email in the open Compose tab", which today is
 *  answered in chat instead of drafted into the tab). */
const COMPOSE_TARGET_RE =
  /\b(in|into|on|to)\b[\s\S]{0,24}\b(the\s+)?(open\s+|current\s+)?(compose(\s+(tab|editor|document|pane|window))?|(this|the\s+open)\s+(document|editor|tab))\b/i;

export interface DraftDocumentDetection {
  /** True when the message is an imperative request to draft a substantial DOCUMENT. */
  isDraftDocument: boolean;
}

/**
 * Detect whether a typed message asks the assistant to DRAFT a substantial document. Pure + total:
 * empty input, hard-slash commands (`/…`), and email-shaped asks are never draft-document. The
 * caller (ConversationPane's decorate seam) uses this to route to `compose-draft-document` and
 * suppress the raw agent turn; a `false` result delegates the message verbatim to the normal turn.
 */
export function detectDraftDocumentIntent(message: string): DraftDocumentDetection {
  const text = (message ?? "").trim();
  if (text.length === 0 || text.startsWith("/")) {
    return { isDraftDocument: false };
  }
  // R7-5: an EXPLICIT "draft this INTO the (open) Compose tab/document" instruction is
  // unambiguous — honor it even for email-/letter-worded asks (they are being placed in a
  // document, not sent as mail). This wins over the email exclusion below.
  if (DRAFT_VERB_RE.test(text) && COMPOSE_TARGET_RE.test(text)) {
    return { isDraftDocument: true };
  }
  if (EMAIL_RE.test(text)) {
    return { isDraftDocument: false };
  }
  const verbNoun = DRAFT_VERB_RE.test(text) && DOC_NOUN_RE.test(text);
  const analyzeDoc = ANALYZE_DOC_RE.test(text);
  return { isDraftDocument: verbNoun || analyzeDoc };
}
