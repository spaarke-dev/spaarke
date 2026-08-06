/**
 * agreementTypeLookupWrite.test.ts — task 023 (spec FR-09 classifier door).
 *
 * Mock-webApi-assert evidence for the classifier-path `sprk_agreementtype` lookup write (the
 * REQUIRED acceptance evidence per the task's own step 3 — a live Dataverse MCP read_query proof
 * is optional). Proves:
 *   - resolveAgreementTypeRowId reads the registry by `sprk_key`, degrading to `null` on no-match
 *     or a read failure (never throws).
 *   - writeAgreementTypeLookup uses discoverNavProps when it resolves the CORRECT column name
 *     (`sprk_agreementtype` — task 022's empirically-confirmed naming, NOT the still-unfixed A1
 *     `sprk_agreementtypeid` bug), falls back to the PascalCase guess when discovery is
 *     unavailable/doesn't match, and canonicalizes every GUID (ADR-044) before building the
 *     `@odata.bind` payload.
 *   - applyAgreementTypeToAnalysis orchestrates both steps and degrades gracefully (never throws)
 *     when the key doesn't resolve to a registry row.
 */

import type { IDataService } from "@spaarke/ui-components";

const discoverNavPropsMock = jest.fn();

jest.mock("@spaarke/ui-components", () => {
  const actual = jest.requireActual("@spaarke/ui-components");
  return {
    ...actual,
    discoverNavProps: (...args: unknown[]) => discoverNavPropsMock(...args),
  };
});

// eslint-disable-next-line import/first
import {
  resolveAgreementTypeRowId,
  writeAgreementTypeLookup,
  applyAgreementTypeToAnalysis,
} from "../agreementTypeLookupWrite";

function makeDataService(overrides?: Partial<IDataService>): IDataService {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn(),
    ...overrides,
  } as unknown as IDataService;
}

beforeEach(() => {
  discoverNavPropsMock.mockReset();
  discoverNavPropsMock.mockResolvedValue([]);
  // eslint-disable-next-line no-console
  jest.spyOn(console, "error").mockImplementation(() => {});
  jest.spyOn(console, "warn").mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe("resolveAgreementTypeRowId", () => {
  it("reads the registry filtered by sprk_key and returns the row's GUID", async () => {
    const dataService = makeDataService({
      retrieveMultipleRecords: jest
        .fn()
        .mockResolvedValue({ entities: [{ sprk_agreementtypeid: "row-guid-1" }] }),
    });

    const id = await resolveAgreementTypeRowId(dataService, "employment");

    expect(id).toBe("row-guid-1");
    expect(dataService.retrieveMultipleRecords).toHaveBeenCalledWith(
      "sprk_agreementtype",
      expect.stringContaining("sprk_key eq 'employment'")
    );
  });

  it("returns null when no row matches the key", async () => {
    const dataService = makeDataService();
    const id = await resolveAgreementTypeRowId(dataService, "unknown-type");
    expect(id).toBeNull();
  });

  it("degrades to null on a read failure — never throws", async () => {
    const dataService = makeDataService({
      retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error("network error")),
    });
    await expect(resolveAgreementTypeRowId(dataService, "nda")).resolves.toBeNull();
  });

  it("returns null for an empty key", async () => {
    const dataService = makeDataService();
    expect(await resolveAgreementTypeRowId(dataService, "")).toBeNull();
  });
});

describe("writeAgreementTypeLookup", () => {
  it("uses the discovered nav-prop when discoverNavProps resolves the CORRECT column name (sprk_agreementtype)", async () => {
    discoverNavPropsMock.mockResolvedValue([
      { columnName: "sprk_agreementtype", navPropName: "sprk_AgreementType_discovered", referencedEntity: "sprk_agreementtype" },
    ]);
    const dataService = makeDataService();

    const result = await writeAgreementTypeLookup(dataService, "analysis-guid-1", "type-guid-1");

    expect(result).toEqual({ success: true });
    expect(dataService.updateRecord).toHaveBeenCalledWith("sprk_analysis", "analysis-guid-1", {
      "sprk_AgreementType_discovered@odata.bind": "/sprk_agreementtypes(type-guid-1)",
    });
  });

  it("falls back to the PascalCase guess (sprk_AgreementType) when discovery finds nothing", async () => {
    discoverNavPropsMock.mockResolvedValue([]);
    const dataService = makeDataService();

    const result = await writeAgreementTypeLookup(dataService, "analysis-guid-2", "type-guid-2");

    expect(result).toEqual({ success: true });
    expect(dataService.updateRecord).toHaveBeenCalledWith("sprk_analysis", "analysis-guid-2", {
      "sprk_AgreementType@odata.bind": "/sprk_agreementtypes(type-guid-2)",
    });
  });

  it("falls back to the PascalCase guess when discoverNavProps rejects (never throws)", async () => {
    discoverNavPropsMock.mockRejectedValue(new Error("discovery unavailable"));
    const dataService = makeDataService();

    const result = await writeAgreementTypeLookup(dataService, "analysis-guid-3", "type-guid-3");

    expect(result).toEqual({ success: true });
    expect(dataService.updateRecord).toHaveBeenCalledWith("sprk_analysis", "analysis-guid-3", {
      "sprk_AgreementType@odata.bind": "/sprk_agreementtypes(type-guid-3)",
    });
  });

  it("does NOT use a discovered nav-prop for an unrelated/stale column name (e.g. the still-unfixed sprk_agreementtypeid)", async () => {
    discoverNavPropsMock.mockResolvedValue([
      { columnName: "sprk_agreementtypeid", navPropName: "sprk_AgreementTypeIdWrong", referencedEntity: "sprk_agreementtype" },
    ]);
    const dataService = makeDataService();

    await writeAgreementTypeLookup(dataService, "analysis-guid-4", "type-guid-4");

    // The stale columnName never matches -> falls to the PascalCase fallback, NOT the wrong discovered prop.
    expect(dataService.updateRecord).toHaveBeenCalledWith("sprk_analysis", "analysis-guid-4", {
      "sprk_AgreementType@odata.bind": "/sprk_agreementtypes(type-guid-4)",
    });
  });

  it("canonicalizes GUIDs (ADR-044) — braces + uppercase are stripped/lowercased before the @odata.bind", async () => {
    const dataService = makeDataService();

    await writeAgreementTypeLookup(dataService, "{ANALYSIS-GUID-5}", "{TYPE-GUID-5}");

    expect(dataService.updateRecord).toHaveBeenCalledWith("sprk_analysis", "analysis-guid-5", {
      "sprk_AgreementType@odata.bind": "/sprk_agreementtypes(type-guid-5)",
    });
  });

  it("degrades to {success:false, warning} on an updateRecord failure — never throws", async () => {
    const dataService = makeDataService({
      updateRecord: jest.fn().mockRejectedValue(new Error("dataverse write failed")),
    });

    const result = await writeAgreementTypeLookup(dataService, "analysis-guid-6", "type-guid-6");

    expect(result.success).toBe(false);
    expect(result.warning).toContain("dataverse write failed");
  });
});

describe("applyAgreementTypeToAnalysis", () => {
  it("resolves the registry row for subDomainKey, then writes it onto the Analysis", async () => {
    const dataService = makeDataService({
      retrieveMultipleRecords: jest
        .fn()
        .mockResolvedValue({ entities: [{ sprk_agreementtypeid: "row-guid-employment" }] }),
    });

    const result = await applyAgreementTypeToAnalysis(dataService, "analysis-guid-7", "employment");

    expect(result).toEqual({ success: true });
    expect(dataService.updateRecord).toHaveBeenCalledWith("sprk_analysis", "analysis-guid-7", {
      "sprk_AgreementType@odata.bind": "/sprk_agreementtypes(row-guid-employment)",
    });
  });

  it("degrades gracefully — never throws — when the key has no matching registry row", async () => {
    const dataService = makeDataService();

    const result = await applyAgreementTypeToAnalysis(dataService, "analysis-guid-8", "unknown-key");

    expect(result.success).toBe(false);
    expect(result.warning).toContain("unknown-key");
    expect(dataService.updateRecord).not.toHaveBeenCalled();
  });
});
