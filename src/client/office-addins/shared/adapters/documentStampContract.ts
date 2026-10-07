/**
 * The FR-02 document-identity stamp contract — the ONE client-side copy (spaarkeai-word-add-in-r1 tasks 051, 089).
 *
 * The server writes the stamp into every stored `.docx` (task 014, `OfficeDocumentStamp`); the client reads it
 * (task 051, `WordAdapter.readDocumentStamp`) and, since task 089, also WRITES it into the document open in Word
 * after a save (`WordAdapter.writeDocumentStamp`). All three must agree on these values:
 *
 *   namespace  urn:spaarke:office:document-identity:1   — the root's DEFAULT xmlns (019 condition 2: without it
 *                                                          `getByNamespaceAsync` silently finds nothing)
 *   root       documentIdentity
 *   payload    <documentId>{guid}</documentId>          — bare lowercase GUID (ADR-044)
 *
 * TypeScript cannot reference a C# constant, so these are a VALUE mirror of `OfficeDocumentStamp.StampNamespace` /
 * `.StampRootElement` / `.StampIdElement` (`src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentStamp.cs`).
 * `__tests__/documentStampContract.test.ts` reads that C# file and fails if the two sides ever differ.
 */

/** The stamp's XML namespace — also its schema version. A wire contract: changing it is a breaking change. */
export const STAMP_NAMESPACE = 'urn:spaarke:office:document-identity:1';

/** The stamp root element's local name. */
export const STAMP_ROOT_ELEMENT = 'documentIdentity';

/** The element carrying the canonical bare-lowercase GUID (ADR-044). */
export const STAMP_ID_ELEMENT = 'documentId';

/**
 * The stamp part's XML for `documentId`, in the server's shape minus the XML declaration (Word adds its own when it
 * serializes the part). `documentId` must already be a canonical bare-lowercase GUID — callers validate it first;
 * a GUID contains no character that needs XML escaping.
 */
export function buildStampXml(documentId: string): string {
  return `<${STAMP_ROOT_ELEMENT} xmlns="${STAMP_NAMESPACE}"><${STAMP_ID_ELEMENT}>${documentId}</${STAMP_ID_ELEMENT}></${STAMP_ROOT_ELEMENT}>`;
}
