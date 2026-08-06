/**
 * `@spaarke/communication-components/logic/attachments` — React-agnostic
 * (Layer 1) logic for the attachment-list surface, extracted verbatim from the
 * production `CommunicationAttachments` PCF (email-communication-solution-r5
 * task 021, per spec FR-12/FR-18 two-layer split + design Lens 4).
 *
 * Three pure-TS modules, NO React import (NFR-05):
 *   - `types.ts`                         — `IAttachmentItem`/`IAttachmentRecord`/
 *                                           `IDocumentRecord`/`AttachmentType` contracts.
 *   - `CommunicationAttachmentsService.ts` — host `context.webAPI` read + pure
 *                                           projection/filter helpers.
 *   - `AttachmentApiService.ts`           — the `authenticatedFetch` BFF adapter
 *                                           over `/preview-url` + `/open-links`.
 *
 * Consumed by BOTH the OOB-form PCF (React 16 view) and the future Phase-3 code-page
 * reading-pane attachments view (task 034) — one copy of the logic, no React component
 * shared across the PCF boundary at THIS layer (ADR-022 slim-first). The presentational
 * `AttachmentList` core lives one layer up in `../../components/AttachmentList`.
 */

export * from './types';
export * from './CommunicationAttachmentsService';
export * from './AttachmentApiService';
