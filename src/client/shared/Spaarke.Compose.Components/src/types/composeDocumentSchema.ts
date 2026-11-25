/**
 * composeDocumentSchema.ts — the one place the Compose package names Dataverse schema.
 *
 * Compose edits Spaarke Documents: it opens a `sprk_document` record, reads its SharePoint Embedded
 * pointer, and creates a `sprk_document` on first Save. ADR-012 forbids entity-name string literals in
 * shared packages; the 2026-11-24 Compose amendment allows this package exactly the names below, kept
 * in this module (P-8). `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` fails when any other
 * non-test source file of the package names a `sprk_` entity or column outside a comment.
 *
 * Mirrors the field constants in `src/solutions/SpaarkeAi/src/ribbon/DocumentComposeLaunch.ts` (the
 * "Open in Compose" ribbon launcher, a separate web-resource bundle). Update both if the schema is renamed.
 */
export const COMPOSE_DOCUMENT_SCHEMA = {
  /** Logical name of the Spaarke Document entity. */
  entityName: 'sprk_document',
  fields: {
    documentId: 'sprk_documentid',
    graphItemId: 'sprk_graphitemid',
    graphDriveId: 'sprk_graphdriveid',
    fileName: 'sprk_filename',
  },
} as const;
