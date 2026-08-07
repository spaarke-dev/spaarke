/**
 * `@spaarke/communication-components/logic/citations` — reconciliation reader
 * quoted-text citation anchor (email-communication-intelligence-r2 task 054,
 * NFR-11). Pure, React-agnostic logic (ADR-022, deep-importable from /logic):
 * maps an email proposal `Citation{Source,Locator,QuotedText}` onto the reader's
 * normalized text (body + attachment folds) → located segment + span, or an
 * explicit NOT-LOCATED (never a guessed passage). See readerReferenceMap.ts for
 * the §6.5 exception rationale (NOT the legal-number composeCitationResolver).
 */
export { normalizeForAnchor, buildReaderReferenceMap, resolveQuotedCitation } from './readerReferenceMap';
export type {
  EmailCitation,
  ReaderSegmentKind,
  ReaderSegment,
  ReaderReferenceMap,
  ReaderReferenceMapInput,
  QuotedCitationMatch,
  QuotedCitationMiss,
  QuotedCitationResolution,
} from './readerReferenceMap';
