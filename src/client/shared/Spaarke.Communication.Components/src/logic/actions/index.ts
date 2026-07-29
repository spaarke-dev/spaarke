/**
 * `@spaarke/communication-components/logic/actions` — React-agnostic (Layer 1)
 * logic for the action-bar / composer-prefill / suggested-create surface,
 * extracted verbatim from the production `CommunicationActions` PCF
 * (email-communication-solution-r5 task 022, per spec FR-08/FR-18 two-layer
 * split + design Lens 4).
 *
 * Three pure-TS / host-seam modules, NO React import (NFR-05):
 *   - `composerPrefill.ts`   — pure recipient-split + Reply/ReplyAll/Forward
 *                              subject + quoted-thread derivation
 *                              (`deriveComposerFields`, `splitRecipients`).
 *   - `launchCreate.ts`      — the single "create from this email" launch seam
 *                              over the OOB `Xrm.Navigation.navigateTo` global
 *                              (`launchCreate`, `CREATE_LAUNCH_TARGETS`).
 *   - `attachmentsSource.ts` — carry-forward attachment enumeration behind an
 *                              injected `IActionsWebApi` host seam
 *                              (`fetchSourceAttachments`).
 *
 * Consumed by BOTH the OOB-form PCF (React 16 view) and the future Phase-3
 * reading-pane full-width toolbar (task 032 shell / task 036 compose) — one
 * copy of the logic, no React component shared across the PCF boundary
 * (ADR-022 slim-first). The canonical `SendEmailPage`/`EmailComposer` is
 * CONSUMED by callers of `deriveComposerFields`, never forked here.
 */

export * from './composerPrefill';
export * from './launchCreate';
export * from './attachmentsSource';
