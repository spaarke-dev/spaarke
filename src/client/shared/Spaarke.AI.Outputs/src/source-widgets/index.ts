/**
 * @spaarke/ai-outputs — Source Widgets barrel export
 *
 * All 6 source pane widget components and their data types.
 * Added by Wave 2 task 022.
 *
 * NOTE: These widgets are lazily loaded via the source registry (source-registry.ts).
 * Direct imports from this barrel are primarily for consumers that need the
 * data types or want to use a widget without the registry abstraction.
 */

// DocumentViewerWidget — SPE document preview via iframe / object
export { default as DocumentViewerWidget } from './DocumentViewerWidget';
export type { DocumentViewerData } from './DocumentViewerWidget';

// WebSourceWidget — URL bar + sandboxed iframe web preview
export { default as WebSourceWidget } from './WebSourceWidget';
export type { WebSourceData } from './WebSourceWidget';

// LegalLibraryWidget — structured legal case / statute citation card
export { default as LegalLibraryWidget } from './LegalLibraryWidget';
export type { LegalLibraryData } from './LegalLibraryWidget';

// CitationWidget — numbered citation reference list
export { default as CitationWidget } from './CitationWidget';
export type { CitationData, Citation, CitationSourceType } from './CitationWidget';

// ImageViewerWidget — image with pan/zoom via CSS transform
export { default as ImageViewerWidget } from './ImageViewerWidget';
export type { ImageViewerData } from './ImageViewerWidget';

// CodeViewerWidget — monospace code block with line numbers + copy
export { default as CodeViewerWidget } from './CodeViewerWidget';
export type { CodeViewerData } from './CodeViewerWidget';
