/**
 * fileSelection.ts — which picked files join the batch.
 *
 * One batch uploads to one SPE folder, where names are unique ignoring case, and the wizard keys every
 * per-file row, retry and result by file name. Two picked files with the same name therefore cannot
 * both succeed: the second always meets a name conflict, and its outcome lands on the first one's row
 * (task 128 review K1). A same-named file is refused at selection instead, with a message.
 */

import type { IFileValidationError, IUploadedFile } from "./types";

export interface FileSelectionResult {
    selectedFiles: IUploadedFile[];
    validationErrors: IFileValidationError[];
}

/**
 * Add `incoming` to `selected`. Re-picking a file already selected (same name and size) is ignored
 * silently, as before; a different file with a selected file's name is refused with an error.
 */
export function addSelectedFiles(selected: IUploadedFile[], incoming: IUploadedFile[]): FileSelectionResult {
    const byName = new Map(selected.map((f) => [f.name.toLowerCase(), f]));
    const added: IUploadedFile[] = [];
    const validationErrors: IFileValidationError[] = [];

    for (const file of incoming) {
        const key = file.name.toLowerCase();
        const existing = byName.get(key);
        if (!existing) {
            byName.set(key, file);
            added.push(file);
        } else if (existing.sizeBytes !== file.sizeBytes) {
            validationErrors.push({
                fileName: file.name,
                reason: `A file named "${existing.name}" is already selected. Rename one of them, or remove the selected one first.`,
            });
        }
    }

    return { selectedFiles: [...selected, ...added], validationErrors };
}
