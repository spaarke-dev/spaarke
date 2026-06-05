/**
 * Filter chip composition layer — public barrel.
 *
 * Exposes the discovery + FetchXML + composite-bar API consumed by
 * `DataGrid.tsx` (host-side) to render the filter strip and inject chip
 * conditions into the savedquery's FetchXML.
 *
 * @see types.ts             — runtime model
 * @see chipDiscovery.ts     — configjson + metadata → ChipDescriptor[]
 * @see chipFetchXml.ts      — descriptors + state → augmented FetchXML
 * @see FilterChipBar.tsx    — composite Fluent v9 strip component
 */

export type { ChipDescriptor, ChipKind, ChipState, ChipValue } from './types';

export { discoverChips, deriveChipKindFromMetadata } from './chipDiscovery';

export { augmentFetchXmlWithChips } from './chipFetchXml';

export { FilterChipBar, default as FilterChipBarDefault } from './FilterChipBar';
export type { FilterChipBarProps } from './FilterChipBar';
