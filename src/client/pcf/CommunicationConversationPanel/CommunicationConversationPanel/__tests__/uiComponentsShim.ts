/**
 * Test-only shim for `@spaarke/ui-components`.
 *
 * The shared lib ships an ESM-only `dist/index.js` that Jest's node runtime
 * cannot `require()` (the shared lib's OWN jest runs against its TS source, not
 * dist). To exercise the REAL shared code from this PCF's tests (NFR-06 — no
 * re-implementation, no behavioral mock), this shim re-exports the exact
 * surface the tests touch straight from the shared TypeScript SOURCE, which
 * ts-jest transforms. `@fluentui/*` resolves to its CommonJS build under Jest
 * exactly as it does in the shared lib's own test suite. Wired via
 * `jest.config.js` `moduleNameMapper` — build/runtime resolution is unchanged
 * (production imports the real `@spaarke/ui-components` barrel).
 */

export {
  buildTimeline,
  mapRegardingReadResultToGroups,
} from '../../../../shared/Spaarke.UI.Components/src/components/CommunicationTimeline/CommunicationTimeline.buildTimeline';
export type { TimelineEntry } from '../../../../shared/Spaarke.UI.Components/src/components/CommunicationTimeline/CommunicationTimeline.buildTimeline';

export type {
  TimelineMessage,
  RegardingThreadGroup,
} from '../../../../shared/Spaarke.UI.Components/src/components/CommunicationTimeline/CommunicationTimeline.types';

export { MessageQuickView } from '../../../../shared/Spaarke.UI.Components/src/components/MessageQuickView/MessageQuickView';
export type { IMessageQuickViewProps } from '../../../../shared/Spaarke.UI.Components/src/components/MessageQuickView/MessageQuickView';

export type {
  IRegardingReadResultDto,
  IThreadMessageDto,
} from '../../../../shared/Spaarke.UI.Components/src/services/communicationTimelineApi';
