/**
 * fileSelection.test.ts — a batch never holds two files with the same name.
 *
 * Task 128 review K1: the wizard keys rows, retries and results by file name, and one SPE folder holds one file per
 * name (ignoring case). Two picked `report.docx` files of different sizes both got uploaded rows, but the second one's
 * outcome landed on the first row: the summary said 1 succeeded and 1 failed when 2 documents existed.
 *
 * Classification (ADR-038 §7): MAINTAIN — the wizard's selection contract.
 */
import { addSelectedFiles } from "./fileSelection";
import type { IUploadedFile } from "./types";

const file = (id: string, name: string, sizeBytes: number): IUploadedFile =>
    ({ id, name, sizeBytes, fileType: "word" }) as unknown as IUploadedFile;

describe("addSelectedFiles", () => {
    it("adds files with new names", () => {
        const { selectedFiles, validationErrors } = addSelectedFiles([file("1", "a.docx", 10)], [file("2", "b.docx", 20)]);

        expect(selectedFiles.map((f) => f.id)).toEqual(["1", "2"]);
        expect(validationErrors).toEqual([]);
    });

    it("ignores a re-picked file (same name and size) without an error, as before", () => {
        const { selectedFiles, validationErrors } = addSelectedFiles([file("1", "a.docx", 10)], [file("2", "a.docx", 10)]);

        expect(selectedFiles.map((f) => f.id)).toEqual(["1"]);
        expect(validationErrors).toEqual([]);
    });

    it("refuses a different file with a selected file's name, ignoring case, and says why", () => {
        const { selectedFiles, validationErrors } = addSelectedFiles(
            [file("1", "report.docx", 10)],
            [file("2", "Report.DOCX", 99)],
        );

        expect(selectedFiles.map((f) => f.id)).toEqual(["1"]);
        expect(validationErrors).toHaveLength(1);
        expect(validationErrors[0].fileName).toBe("Report.DOCX");
        expect(validationErrors[0].reason).toContain('"report.docx" is already selected');
    });

    it("refuses the second of two same-named files picked together", () => {
        const { selectedFiles, validationErrors } = addSelectedFiles([], [file("1", "a.docx", 10), file("2", "a.docx", 11)]);

        expect(selectedFiles.map((f) => f.id)).toEqual(["1"]);
        expect(validationErrors.map((e) => e.fileName)).toEqual(["a.docx"]);
    });
});
