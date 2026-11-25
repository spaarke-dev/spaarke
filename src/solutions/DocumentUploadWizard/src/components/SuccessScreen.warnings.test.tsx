/**
 * SuccessScreen — the server's create warnings are shown as non-blocking warnings (field-mapping-server-write-path-r1
 * task 029 review F1). The shared `WizardSuccessScreen` renders each `config.warnings` entry as a Fluent v9
 * `MessageBar intent="warning"`; this proves the wizard hands it the warnings of a document that WAS created, and none
 * when there are none.
 *
 * Classification (ADR-038 §7): MAINTAIN — the wizard's per-file outcome contract.
 */
import { buildSuccessConfig } from "./SuccessScreen";
import type { OrchestratorFileResult, OrchestratorResult } from "../services/uploadOrchestrator";

function results(fileResults: OrchestratorFileResult[]): OrchestratorResult {
    const successCount = fileResults.filter((r) => r.success).length;
    return {
        success: successCount > 0,
        totalFiles: fileResults.length,
        successCount,
        failureCount: fileResults.length - successCount,
        fileResults,
    };
}

const created = (fileName: string, warnings?: string[]): OrchestratorFileResult => ({
    fileName,
    success: true,
    createResult: { success: true, fileName, recordId: `id-${fileName}` },
    ...(warnings ? { warnings } : {}),
});

describe("buildSuccessConfig — server warnings on created documents", () => {
    it("lists a created document's warning, prefixed with the file name", () => {
        const config = buildSuccessConfig({
            uploadResults: results([created("a.docx", ["The source could not be read."])]),
            onClose: () => undefined,
            closeWindow: false,
        });

        expect(config.warnings).toEqual(["a.docx: The source could not be read."]);
        // The upload itself still succeeded.
        expect(config.title).toBe("1 document created successfully");
    });

    it("lists the warnings of each file, beside a failure", () => {
        const config = buildSuccessConfig({
            uploadResults: results([
                created("a.docx", ["w1", "w2"]),
                { fileName: "b.docx", success: false, errorMessage: "Upload failed: HTTP 500" },
            ]),
            onClose: () => undefined,
            closeWindow: false,
        });

        // In file order: a.docx's warnings, then b.docx's failure.
        expect(config.warnings).toEqual(["a.docx: w1", "a.docx: w2", "b.docx: Upload failed: HTTP 500"]);
    });

    it("shows no warnings when no document had any (no key, or an empty list)", () => {
        const config = buildSuccessConfig({
            uploadResults: results([created("a.docx"), { ...created("b.docx"), warnings: [] }]),
            onClose: () => undefined,
            closeWindow: false,
        });

        expect(config.warnings).toBeUndefined();
    });
});
