/**
 * Hands bytes to the browser under a file name (an object URL and a temporary anchor). Shared by the document download and
 * the invoice Files list (R4 task 021), whose content route sends no file name of its own. Kept out of DocumentLibrary.tsx:
 * that file mounts the shared grid, and the grid guard refuses createElement in such a file.
 */
export function saveBlobAs(blob: Blob, fileName: string): void {
  const objectUrl = window.URL.createObjectURL(blob);
  try {
    // Trigger browser download via a temporary anchor element
    const anchor = window.document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.style.display = 'none';
    window.document.body.appendChild(anchor);
    anchor.click();
    window.document.body.removeChild(anchor);
  } finally {
    // Revoke on the next tick - revoking synchronously can cancel the in-flight navigation the anchor click just started.
    window.setTimeout(() => window.URL.revokeObjectURL(objectUrl), 0);
  }
}
