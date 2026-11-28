/**
 * Find Similar step's index request (GitHub #3299). The request used to carry no `tenantId`, which the BFF requires,
 * so every request was refused with 400 and the failure was swallowed. It now sends the upload's own body, so it also
 * names the upload's parent: a body without one lets the server clear the parent the upload stored when the document
 * row names no record (a document filed under a contact).
 *
 * Classification (ADR-038 §7): MAINTAIN — regression test for #3299; the step's request contract with
 * POST /api/ai/rag/index-file.
 */
jest.mock(
    "@spaarke/auth",
    () => ({ buildBffApiUrl: (base: string, path: string) => `${base.replace(/\/+$/, "")}/api${path}` }),
    { virtual: true },
);

import { requestFindSimilarIndexing, type IndexFetch } from "./findSimilarIndexing";
import { buildRagIndexFileBody } from "./ragIndexFileBody";

const contact = { parentEntityName: "contact", parentRecordId: "contact-1", parentDisplayName: "Pat Doe" };
const target = { documentId: "doc-1", driveId: "drive-1", itemId: "item-1", fileName: "a.docx", parentContext: contact };

const okFetch = () => jest.fn(async () => ({}));
const asIndexFetch = (fn: jest.Mock) => fn as unknown as IndexFetch;
const sentBody = (fn: jest.Mock) => JSON.parse((fn.mock.calls[0] as unknown as [string, RequestInit])[1].body as string);

describe("Find Similar index request", () => {
    it("posts to rag/index-file with the tenant, the document and the upload's parent, and reports success", async () => {
        const fetchFn = okFetch();

        const ok = await requestFindSimilarIndexing(asIndexFetch(fetchFn), "https://bff.example", target, async () => "tenant-1");

        expect(ok).toBe(true);
        const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
        expect(url).toBe("https://bff.example/api/ai/rag/index-file");
        expect(init.method).toBe("POST");
        expect(sentBody(fetchFn)).toEqual({
            driveId: "drive-1",
            itemId: "item-1",
            fileName: "a.docx",
            tenantId: "tenant-1",
            documentId: "doc-1",
            parentEntity: { entityType: "contact", entityId: "contact-1", entityName: "Pat Doe" },
        });
    });

    it("sends exactly the body the upload sends for the same document", async () => {
        const fetchFn = okFetch();

        await requestFindSimilarIndexing(asIndexFetch(fetchFn), "https://bff.example", target, async () => "tenant-1");

        const uploadBody = buildRagIndexFileBody({
            driveId: "drive-1", itemId: "item-1", fileName: "a.docx", tenantId: "tenant-1", documentId: "doc-1", parentContext: contact,
        });
        expect(sentBody(fetchFn)).toEqual(JSON.parse(JSON.stringify(uploadBody)));
    });

    it("reports failure when the server refuses the request", async () => {
        const fetchFn = jest.fn(async () => {
            throw Object.assign(new Error("Bad Request"), { status: 400 });
        });

        await expect(
            requestFindSimilarIndexing(asIndexFetch(fetchFn), "https://bff.example", target, async () => "tenant-1"),
        ).resolves.toBe(false);
    });

    it("does not send a request without a tenant, and reports failure", async () => {
        const fetchFn = okFetch();

        await expect(
            requestFindSimilarIndexing(asIndexFetch(fetchFn), "https://bff.example", target, async () => ""),
        ).resolves.toBe(false);
        expect(fetchFn).not.toHaveBeenCalled();
    });

    it("reports failure when the tenant cannot be resolved", async () => {
        const fetchFn = okFetch();

        await expect(
            requestFindSimilarIndexing(asIndexFetch(fetchFn), "https://bff.example", target, async () => {
                throw new Error("not signed in");
            }),
        ).resolves.toBe(false);
        expect(fetchFn).not.toHaveBeenCalled();
    });
});

describe("buildRagIndexFileBody", () => {
    it("names the parent by its short type when the logical name is prefixed", () => {
        const body = buildRagIndexFileBody({
            driveId: "d", itemId: "i", fileName: "f", tenantId: "t", documentId: "doc",
            parentContext: { parentEntityName: "sprk_matter", parentRecordId: "m-1", parentDisplayName: "Acme" },
        });

        expect(body.parentEntity).toEqual({ entityType: "matter", entityId: "m-1", entityName: "Acme" });
    });

    it("names no parent when the upload had none (unassociated)", () => {
        const body = buildRagIndexFileBody({
            driveId: "d", itemId: "i", fileName: "f", tenantId: "t", documentId: "doc",
            parentContext: { parentEntityName: "", parentRecordId: "", parentDisplayName: "" },
        });

        expect(body.parentEntity).toBeUndefined();
    });
});
