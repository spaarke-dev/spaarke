/**
 * MessageQuickView barrel (task 023 / FR-05).
 *
 * The MAIN SESSION wires `export * from './MessageQuickView'` into the
 * components barrel (`src/components/index.ts`) — this local index only
 * re-exports the component + its public props type. (`truncatePreview` /
 * `MESSAGE_QUICK_VIEW_MAX_CHARS` are module-private — exercised through the
 * rendered component, not part of the package public API.)
 */
export { MessageQuickView } from './MessageQuickView';
export type { IMessageQuickViewProps } from './MessageQuickView';
