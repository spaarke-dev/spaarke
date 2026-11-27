/**
 * Find Similar step's index request (GitHub #3299). The request used to carry no `tenantId`, which the BFF requires,
 * so every request was refused with 400 and the failure was swallowed.
 *
 * Classification (ADR-038 §7): MAINTAIN — regression test for #3299; the step's request contract with
 * POST /api/ai/rag/index-file.
 */
jest.mock(
    "@spaarke/auth",
    () => ({ buildBffApiUrl: (base: string, path: string) => `${base.replace(/\/+$/, "")}/api${path}` }),
    { virtual: true },
);

import { buildFindSimilarIndexBody, requestFindSimilarIndexing } from "./findSimilarIndexing";

const target = { documentId: "doc-1", driveId: "drive-1", itemId: "item-1", fileName: "a.docx" };

describe("Find Similar index request", () => {
    it("carries the tenant and the document id the BFF needs", () => {
        expect(buildFindSimilarIndexBody(target, "tenant-1")).toEqual({
            documentId: "doc-1",
            driveId: "drive-1",
            itemId: "item-1",
            fileName: "a.docx",
            tenantId: "tenant-1",
        });
    });

    it("posts that body to rag/index-file and reports success", async () => {
        const fetchFn = jest.fn(async () => ({}));

        const ok = await requestFindSimilarIndexing(fetchFn, "https://bff.example", target, async () => "tenant-1");

        expect(ok).toBe(true);
        expect(fetchFn).toHaveBeenCalledTimes(1);
        const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
        expect(url).toBe("https://bff.example/api/ai/rag/index-file");
        expect(init.method).toBe("POST");
        expect(JSON.parse(init.body as string)).toEqual(buildFindSimilarIndexBody(target, "tenant-1"));
    });

    it("reports failure when the server refuses the request", async () => {
        const fetchFn = jest.fn(async () => {
            throw Object.assign(new Error("Bad Request"), { status: 400 });
        });

        await expect(
            requestFindSimilarIndexing(fetchFn, "https://bff.example", target, async () => "tenant-1"),
        ).resolves.toBe(false);
    });

    it("does not send a request without a tenant, and reports failure", async () => {
        const fetchFn = jest.fn(async () => ({}));

        await expect(requestFindSimilarIndexing(fetchFn, "https://bff.example", target, async () => "")).resolves.toBe(false);
        expect(fetchFn).not.toHaveBeenCalled();
    });

    it("reports failure when the tenant cannot be resolved", async () => {
        const fetchFn = jest.fn(async () => ({}));

        await expect(
            requestFindSimilarIndexing(fetchFn, "https://bff.example", target, async () => {
                throw new Error("not signed in");
            }),
        ).resolves.toBe(false);
        expect(fetchFn).not.toHaveBeenCalled();
    });
});
