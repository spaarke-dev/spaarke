/**
 * launchParams.test.ts — the Document Upload Wizard's launch envelope (spaarke-ontology-platform-r1
 * task 113). The code page (`main.tsx`) and the in-app host (`SpaarkeAi` inAppWizardRenderers) resolve
 * the parent record from the SAME function, so what `WorkspaceGrid` sends is what both read.
 *
 * Classification (ADR-038 §7): MAINTAIN - the launch-envelope contract between WorkspaceGrid and the wizard.
 */
import { resolveUploadLaunchParams } from "./launchParams";

describe("resolveUploadLaunchParams", () => {
    it("decodes the once-URI-encoded envelope WorkspaceGrid sends (the standalone, no-parent launch)", () => {
        const dataString =
            "parentEntityType=sprk_document&parentEntityId=&parentEntityName=&theme=dark&bffBaseUrl=" +
            encodeURIComponent("https://bff.example");
        expect(resolveUploadLaunchParams(encodeURIComponent(dataString))).toEqual({
            parentEntityType: "sprk_document",
            parentEntityId: "",
            parentEntityName: "",
        });
    });

    it("reads a parent record launch (subgrid / semantic search style)", () => {
        const dataString =
            "parentEntityType=sprk_matter&parentEntityId=abc-123&parentEntityName=" + encodeURIComponent("Acme & Co");
        expect(resolveUploadLaunchParams(encodeURIComponent(dataString))).toEqual({
            parentEntityType: "sprk_matter",
            parentEntityId: "abc-123",
            parentEntityName: "Acme & Co",
        });
    });

    it("falls back to the page's own query params when there is no data envelope", () => {
        const fallback = new URLSearchParams("parentEntityType=sprk_project&parentEntityId=p1");
        expect(resolveUploadLaunchParams(null, fallback)).toEqual({
            parentEntityType: "sprk_project",
            parentEntityId: "p1",
            parentEntityName: "",
        });
    });

    it("never reads containerId (task 076): a caller that still appends it cannot select a destination", () => {
        const params = resolveUploadLaunchParams(
            encodeURIComponent("parentEntityType=sprk_document&containerId=attacker-container"),
        );
        expect(params).not.toHaveProperty("containerId");
        expect(Object.keys(params).sort()).toEqual(["parentEntityId", "parentEntityName", "parentEntityType"]);
    });

    it("an empty or missing envelope with no fallback yields empty strings (standalone mode)", () => {
        expect(resolveUploadLaunchParams(null)).toEqual({ parentEntityType: "", parentEntityId: "", parentEntityName: "" });
        expect(resolveUploadLaunchParams("")).toEqual({ parentEntityType: "", parentEntityId: "", parentEntityName: "" });
    });
});
