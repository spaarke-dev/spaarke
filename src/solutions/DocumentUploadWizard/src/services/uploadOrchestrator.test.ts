/**
 * uploadOrchestrator.test.ts — per-file results are attributed to the file the user PICKED.
 *
 * Task 128 (owner UAT 2026-10-11, Test User 1 on CMRCL-497830): a name collision, then "keep both",
 * made SPE store `… 2023.docx` as `… 2023 1.DOCX`. The document was created and indexed, but the
 * orchestrator matched results by the STORED name, so the picked file's row stayed at
 * "Uploading (100%)", a stray row appeared under the stored name, and the wizard reported
 * "None of the selected files could be uploaded".
 *
 * The real `orchestrateUpload` runs against stubbed collaborators (no HTTP at this seam, ADR-038).
 *
 * Classification (ADR-038 §7): MAINTAIN — the wizard's per-file outcome contract.
 */

type Scripted = { uploads: { source: string; stored: string }[]; failedUploads?: string[]; failedCreates?: string[] };

let script: Scripted = { uploads: [] };

/** What the orchestrator handed the shared document pipeline (field-mapping-server-write-path-r1 task 029). */
const seen: {
    createDocumentsArgs: unknown[][];
    withBffChildCreatesArgs: unknown[][];
} = { createDocumentsArgs: [], withBffChildCreatesArgs: [] };

jest.mock(
    "@spaarke/ui-components/services/document-upload",
    () => {
        class MultiFileUploadService {
            async uploadFiles(request: { files: File[] }) {
                const uploads = script.uploads
                    .filter((u) => request.files.some((f) => f.name === u.source))
                    .map((u) => ({ sourceFileName: u.source, metadata: { id: `item-${u.stored}`, name: u.stored, driveId: "drive-1" } }));
                const errors = (script.failedUploads ?? []).map((name) => ({ fileName: name, error: "File upload failed: HTTP 500" }));
                return {
                    success: uploads.length > 0,
                    totalFiles: request.files.length,
                    successCount: uploads.length,
                    failureCount: errors.length,
                    uploadedFiles: uploads.map((u) => u.metadata),
                    uploads,
                    errors,
                };
            }
        }
        class DocumentRecordService {
            async createDocuments(files: { id: string; name: string; driveId?: string }[], ...rest: unknown[]) {
                seen.createDocumentsArgs.push([files, ...rest]);
                return files.map((f, i) =>
                    (script.failedCreates ?? []).includes(f.name)
                        ? { success: false, fileName: f.name, error: "Create failed" }
                        : { success: true, fileName: f.name, recordId: `doc-${i}`, documentId: `doc-${i}`, driveId: f.driveId, itemId: f.id },
                );
            }
        }
        return {
            FileUploadService: class {},
            MultiFileUploadService,
            NavMapClient: class {},
            DocumentRecordService,
            withBffChildCreates: (client: unknown, ...rest: unknown[]) => {
                seen.withBffChildCreatesArgs.push([client, ...rest]);
                return client;
            },
            consoleLogger: { info: () => undefined, warn: () => undefined, error: () => undefined, debug: () => undefined },
        };
    },
    { virtual: true },
);
// SdapApiClient comes from the main barrel since DEF-001 (2026-11-24) — it was @spaarke/sdap-client.
// `getXrm` hands back a host whose parent record NAMES a search index: a wizard that still resolved one on the client would
// read it and send it. The wizard no longer reads it (field-mapping-server-write-path-r1 task 029, FR-40).
jest.mock(
    "@spaarke/ui-components",
    () => ({
        cleanGuid: (s: string) => s,
        // Both shapes a resolver could read the parent through: `xrm.WebApi.retrieveRecord` and `xrm.WebApi.WebApi.retrieveRecord`.
        getXrm: () => {
            const webApi = { retrieveRecord: async () => ({ sprk_searchindexname: "client-resolved-index" }) };
            return { WebApi: { ...webApi, WebApi: webApi } };
        },
        SdapApiClient: class {},
    }),
    { virtual: true },
);
jest.mock("@spaarke/auth", () => ({ authenticatedFetch: jest.fn(async () => ({ ok: true, json: async () => ({}) })) }), {
    virtual: true,
});

import { authenticatedFetch } from "@spaarke/auth";
import { orchestrateUpload, type OrchestratorFileProgress } from "./uploadOrchestrator";

const CONFIG = {
    bffBaseUrl: "https://bff.example",
    bffTokenProvider: async () => "token",
    dataverseClient: {},
    entityConfigResolver: () => undefined,
    parentContext: { parentEntityName: "sprk_matter", parentRecordId: "rec-1", parentDisplayName: "CMRCL-1" },
    tenantId: "tenant-1",
    logger: { info: () => undefined, warn: () => undefined, error: () => undefined, debug: () => undefined },
} as unknown as Parameters<typeof orchestrateUpload>[1];

const file = (name: string): File => ({ name, size: 1 }) as unknown as File;

async function run(files: string[], s: Scripted) {
    script = s;
    const progress: OrchestratorFileProgress[] = [];
    const result = await orchestrateUpload(files.map(file), CONFIG, (p) => progress.push(p), "rename");
    return { result, progress };
}

describe("orchestrateUpload — results belong to the picked file, not the stored name", () => {
    it("counts a renamed upload as a success on the picked file's entry", async () => {
        const { result } = await run(["a.docx"], { uploads: [{ source: "a.docx", stored: "a 1.docx" }] });

        expect(result.successCount).toBe(1);
        expect(result.failureCount).toBe(0);
        expect(result.fileResults).toHaveLength(1);
        expect(result.fileResults[0]).toMatchObject({ fileName: "a.docx", success: true });
        expect(result.fileResults[0].speMetadata?.name).toBe("a 1.docx");
        expect(result.fileResults[0].createResult?.recordId).toBe("doc-0");
    });

    it("emits every progress event under the picked name — no row appears for the stored name", async () => {
        const { progress } = await run(["a.docx"], { uploads: [{ source: "a.docx", stored: "a 1.docx" }] });

        expect(new Set(progress.map((p) => p.fileName))).toEqual(new Set(["a.docx"]));
        expect(progress.some((p) => p.phase === "complete" && p.documentRecordId === "doc-0")).toBe(true);
    });

    it("keeps a batch without renames as before", async () => {
        const { result } = await run(["a.docx", "b.docx"], {
            uploads: [
                { source: "a.docx", stored: "a.docx" },
                { source: "b.docx", stored: "b.docx" },
            ],
        });

        expect(result.fileResults.map((r) => [r.fileName, r.success, r.createResult?.recordId])).toEqual([
            ["a.docx", true, "doc-0"],
            ["b.docx", true, "doc-1"],
        ]);
    });

    it("still fails a file whose upload failed, beside a renamed success", async () => {
        const { result } = await run(["a.docx", "broken.docx"], {
            uploads: [{ source: "a.docx", stored: "a 1.docx" }],
            failedUploads: ["broken.docx"],
        });

        expect(result.fileResults.map((r) => [r.fileName, r.success])).toEqual([
            ["a.docx", true],
            ["broken.docx", false],
        ]);
        expect(result.failureCount).toBe(1);
    });

    it("fails the picked file when its record create fails after a rename", async () => {
        const { result, progress } = await run(["a.docx"], {
            uploads: [{ source: "a.docx", stored: "a 1.docx" }],
            failedCreates: ["a 1.docx"],
        });

        expect(result.fileResults[0]).toMatchObject({ fileName: "a.docx", success: false, errorMessage: "Create failed" });
        expect(progress.filter((p) => p.phase === "error").map((p) => p.fileName)).toEqual(["a.docx"]);
    });

    it("pairs each create result with its own picked file in a mixed batch", async () => {
        const { result } = await run(["a.docx", "b.docx"], {
            uploads: [
                { source: "a.docx", stored: "a 1.docx" },
                { source: "b.docx", stored: "b.docx" },
            ],
            failedCreates: ["a 1.docx"],
        });

        expect(result.fileResults.map((r) => [r.fileName, r.success, r.createResult?.recordId])).toEqual([
            ["a.docx", false, undefined],
            ["b.docx", true, "doc-1"],
        ]);
    });
});

describe('orchestrateUpload — the document create carries no value the server derives (field-mapping-server-write-path-r1 task 029)', () => {
    const fetchMock = authenticatedFetch as unknown as jest.Mock;

    beforeEach(() => {
        seen.createDocumentsArgs.length = 0;
        seen.withBffChildCreatesArgs.length = 0;
        fetchMock.mockClear();
    });

    /** The orchestrator's background index-file call (fire-and-forget) has started once the pipeline returns. */
    const indexFileBodies = async (): Promise<Record<string, unknown>[]> => {
        await new Promise((resolve) => setImmediate(resolve));
        return fetchMock.mock.calls
            .filter(([url]) => url === '/api/ai/rag/index-file')
            .map(([, init]) => JSON.parse((init as { body: string }).body) as Record<string, unknown>);
    };

    it('hands createDocuments only the uploaded files, the filing parent and the form: no search index, no mapped value', async () => {
        await run(['a.docx'], { uploads: [{ source: 'a.docx', stored: 'a.docx' }] });

        expect(seen.createDocumentsArgs).toHaveLength(1);
        const [files, parentContext, formData, ...extra] = seen.createDocumentsArgs[0];
        expect(files).toHaveLength(1);
        expect(parentContext).toMatchObject({ parentEntityName: 'sprk_matter', parentRecordId: 'rec-1' });
        // The user entered no name or description: the form is only the (empty) document name.
        expect(formData).toEqual({ documentName: '' });
        // Negative: no fourth or fifth argument (the removed searchIndexName / searchIndexId).
        expect(extra).toEqual([]);
    });

    it('names the filing parent as the mapping source on the BFF create, so the server maps from it', async () => {
        await run(['a.docx'], { uploads: [{ source: 'a.docx', stored: 'a.docx' }] });

        expect(seen.withBffChildCreatesArgs).toHaveLength(1);
        const [, , bffBaseUrl, mappingSource] = seen.withBffChildCreatesArgs[0];
        expect(bffBaseUrl).toBe('https://bff.example');
        expect(mappingSource).toEqual({ entity: 'sprk_matter', id: 'rec-1' });
    });

    it('names no mapping source for an unfiled upload', async () => {
        const unfiled = {
            ...CONFIG,
            parentContext: { parentEntityName: '', parentRecordId: '' },
        } as unknown as Parameters<typeof orchestrateUpload>[1];
        script = { uploads: [{ source: 'a.docx', stored: 'a.docx' }] };

        await orchestrateUpload([file('a.docx')], unfiled, undefined, 'rename');

        expect(seen.withBffChildCreatesArgs).toHaveLength(1);
        expect(seen.withBffChildCreatesArgs[0][3]).toBeUndefined();
    });

    it('sends no searchIndexName in the rag/index-file body (the server ignores it, FR-40)', async () => {
        await run(['a.docx'], { uploads: [{ source: 'a.docx', stored: 'a.docx' }] });

        const bodies = await indexFileBodies();
        expect(bodies).toHaveLength(1);
        expect(Object.keys(bodies[0]).sort()).toEqual(
            ['documentId', 'driveId', 'fileName', 'itemId', 'parentEntity', 'tenantId'].sort(),
        );
        expect(bodies[0]).not.toHaveProperty('searchIndexName');
    });
});
