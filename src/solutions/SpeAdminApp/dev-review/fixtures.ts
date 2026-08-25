/**
 * Fixtures for the nine-screen render review. DEV-ONLY — see `authInit.mock.ts`.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * PROVENANCE IS RECORDED PER ROUTE, DELIBERATELY.
 *
 * This project exists because an admin tool presented things it had not established as if it had.
 * A review harness that quietly mixed real captured payloads with invented ones would reproduce that
 * defect inside the tool used to inspect it. So each route is tagged:
 *
 *   live      — captured from Spaarke Dev on 2026-08-24 via a real Graph call.
 *   partial   — values live-captured, but identifiers not recorded at capture time are ZERO-PADDED
 *               (visibly fake tails, never plausible-looking GUIDs).
 *   synthetic — shape-correct, values invented. Proves the screen renders; proves NOTHING about
 *               what the BFF returns.
 * ─────────────────────────────────────────────────────────────────────────────
 */

export type Provenance = "live" | "partial" | "synthetic";

export const PAYGO1_CONFIG_ID = "c3a25b9a-e81f-f111-88b2-7c1e525abd8b";
export const PAYGO1_CONTAINER_TYPE_ID = "8a6ce34c-6055-4681-8f87-2f4f9f921c06";
const OWNING_APP_PAYGO1 = "170c98e1-d486-4355-bcbe-170454e0207c";
const BU_ID = "b0000001-0000-0000-0000-000000000000";
const ENV_ID = "e0000001-0000-0000-0000-000000000000";

/**
 * Containers — LIVE byte counts (notes/storage-consumption-spike.md §6).
 * Five containers totalling 902,616,643 bytes = 860.8 MB, which the Dashboard rendered as `0 B`.
 * Names and byte counts exact; GUIDs were never recorded, so they are zero-padded.
 */
const CONTAINERS = [
  { id: "c0000001-0000-0000-0000-000000000000", displayName: "Spaarke Inc", storageUsedInBytes: 869320675, createdDateTime: "2025-09-30T14:43:59Z" },
  { id: "c0000002-0000-0000-0000-000000000000", displayName: "Spaarke Dev Container 2", storageUsedInBytes: 33112969, createdDateTime: "2025-09-30T14:51:26Z" },
  { id: "c0000003-0000-0000-0000-000000000000", displayName: "Test New Container 8-20-2026", storageUsedInBytes: 86624, createdDateTime: "2026-08-20T00:00:00Z" },
  { id: "c0000004-0000-0000-0000-000000000000", displayName: "Full Flow Test 2025-09-30 14:51:26", storageUsedInBytes: 49081, createdDateTime: "2025-09-30T14:51:26Z" },
  { id: "c0000005-0000-0000-0000-000000000000", displayName: "API Test 2025-09-30 14:43:59", storageUsedInBytes: 47294, createdDateTime: "2025-09-30T14:43:59Z" },
].map((c) => ({
  ...c,
  containerTypeId: PAYGO1_CONTAINER_TYPE_ID,
  status: "active",
  isItemVersioningEnabled: true,
}));

const TOTAL_BYTES = CONTAINERS.reduce((n, c) => n + c.storageUsedInBytes, 0); // 902,616,643

/**
 * Container types — LIVE values, padded ids (notes/live-verification-2026-08-24.md §1).
 *
 * 🔑 `billingStatus` is absent on EVERY row, and that is the truth: nothing in the codebase asked
 * for it until task 029, so it was never captured. These rows therefore exercise task 029's
 * "Unknown" branch — the branch that must never read as "valid".
 */
const CONTAINER_TYPES = [
  {
    id: PAYGO1_CONTAINER_TYPE_ID,
    displayName: "Spaarke PAYGO 1",
    billingClassification: "standard",
    owningAppId: OWNING_APP_PAYGO1,
    createdDateTime: null,
    expiryDateTime: null,
    // Live settings payload, captured verbatim.
    settings: {
      urlTemplate: "https://localhost",
      isDiscoverabilityEnabled: false,
      isSearchEnabled: true,
      isItemVersioningEnabled: true,
      itemMajorVersionLimit: 500,
      maxStoragePerContainerInBytes: 27487790694400,
      isSharingRestricted: false,
      isOfficeRestricted: false,
      consumingTenantOverridables: "sharingCapability,itemMajorVersionLimit,isOfficeRestricted",
    },
  },
  {
    id: "11111111-0000-0000-0000-000000000000",
    displayName: "Spaarke DMS-SPE Trial",
    billingClassification: "trial",
    owningAppId: "2c708318-0000-0000-0000-000000000000",
    createdDateTime: null,
    // 🔴 Real, and eleven months past. Graph returned this all along; the BFF never mapped it.
    expiryDateTime: "2025-10-10T00:00:00Z",
    settings: null,
  },
  {
    id: "22222222-0000-0000-0000-000000000000",
    displayName: "Spaarke DMS Dev 1",
    billingClassification: "directToCustomer",
    owningAppId: "fd1325aa-0000-0000-0000-000000000000",
    createdDateTime: null,
    expiryDateTime: null,
    settings: null,
  },
  {
    id: "33333333-0000-0000-0000-000000000000",
    displayName: "Spaarke Demo Documents",
    billingClassification: "standard",
    owningAppId: "da03fe1a-0000-0000-0000-000000000000",
    createdDateTime: null,
    expiryDateTime: null,
    settings: null,
  },
];

/**
 * Recycle bin — SYNTHETIC.
 *
 * The LIVE bin is empty (200, no OData error — notes/live-verification-credential.md §3), which
 * cannot exercise task 022's fix. Row 2 carries the exact case that fix addressed: Graph sent a
 * timestamp, the `is string` guard dropped it, and the row must render a muted "Unknown" sorted
 * last rather than a blank or a fabricated date.
 */
const RECYCLE_BIN = [
  { id: "d0000001-0000-0000-0000-000000000000", displayName: "Deleted With Timestamp", deletedDateTime: "2026-08-18T09:12:00Z", containerTypeId: PAYGO1_CONTAINER_TYPE_ID },
  { id: "d0000002-0000-0000-0000-000000000000", displayName: "Deleted, Timestamp Unreadable", deletedDateTime: null, containerTypeId: PAYGO1_CONTAINER_TYPE_ID },
];

export const ROUTE_PROVENANCE: Record<string, Provenance> = {
  "/spe/dashboard/metrics": "live",
  "/spe/containers": "partial",
  "/spe/containertypes": "partial",
  "/spe/recyclebin": "synthetic",
  "/spe/search/containers": "synthetic",
  "/spe/security/score": "synthetic",
  "/spe/security/alerts": "live",
  "/spe/audit": "synthetic",
  "/spe/configs": "partial",
};

/*
 * ⚠️ ENVELOPE vs BARE ARRAY — the shapes are NOT uniform, and getting one wrong is fatal.
 *
 * A first pass at these fixtures wrapped everything in `{ items, count }`. The app rendered for one
 * frame and then went white with `d.filter is not a function`, because six of these routes return a
 * BARE ARRAY and the components call `.filter()` on the response directly.
 *
 * TypeScript could not catch it: `speApiClient` uses `get<T>(...)`, which CASTS the parsed JSON
 * rather than validating it — the same root cause as task 030's row-selection defect, where the DTO
 * sent `id` and the client read `containerTypeId` and nothing objected.
 *
 * Verified against `speApiClient.ts` return types AND the BFF's `.Produces<T>` declarations:
 *
 *   BARE ARRAY  businessunits · configs · environments · security/alerts · search/* · audit
 *   ENVELOPE    containers · containertypes · recyclebin        → { items, count }
 *   BARE OBJECT dashboard/metrics · security/score
 */
export const FIXTURES: Record<string, { body: unknown; status: number }> = {
  // ── Selectors — BARE ARRAYS ──
  "/spe/businessunits": {
    status: 200,
    body: [{ businessUnitId: BU_ID, name: "Spaarke", isRootUnit: true, parentBusinessUnitId: null }],
  },
  "/spe/environments": {
    status: 200,
    body: [{ id: ENV_ID, name: "Spaarke Dev", tenantId: "a221a95e-0000-0000-0000-000000000000", tenantName: "Spaarke", rootSiteUrl: "https://spaarke.sharepoint.com", isDefault: true, status: "active" }],
  },
  "/spe/configs": {
    status: 200,
    body: [
      {
        id: PAYGO1_CONFIG_ID, name: "Spaarke PAYGO 1",
        businessUnitId: BU_ID, businessUnitName: "Spaarke",
        environmentId: ENV_ID, environmentName: "Spaarke Dev",
        containerTypeId: PAYGO1_CONTAINER_TYPE_ID, containerTypeName: "Spaarke PAYGO 1",
        billingClassification: "standard",
        owningAppId: OWNING_APP_PAYGO1, owningAppDisplayName: "SDAP-PCF-CLIENT",
        keyVaultSecretName: "spe-owning-app-secret",
        isRegistered: true, registeredOn: "2025-09-30T00:00:00Z",
        delegatedPermissions: "FileStorageContainer.Selected",
        applicationPermissions: "FileStorageContainer.Selected",
        maxStoragePerBytes: 27487790694400,
        sharingCapability: "externalUserSharingOnly",
        isItemVersioningEnabled: true, itemMajorVersionLimit: 500,
        status: "active",
      },
    ],
  },

  // ── Dashboard: the real 861 MB, 5 of 5 reporting ──
  "/spe/dashboard/metrics": {
    status: 200,
    body: {
      totalContainerCount: CONTAINERS.length,
      totalStorageUsedInBytes: TOTAL_BYTES,
      storageReportingContainerCount: CONTAINERS.length,
      containerCountByConfig: { [PAYGO1_CONFIG_ID]: CONTAINERS.length },
      lastSyncedAt: "2026-08-24T12:00:00Z",
      syncSucceeded: true,
      syncStatus: "OK",
      syncHealth: "Healthy",
      concerns: [
        { concern: "Containers", succeeded: true, reason: null },
        { concern: "ContainerTypes", succeeded: true, reason: null },
        { concern: "Storage", succeeded: true, reason: null },
      ],
    },
  },

  // ── Screens ──
  "/spe/containers": { status: 200, body: { items: CONTAINERS, count: CONTAINERS.length } },
  "/spe/containertypes": { status: 200, body: { items: CONTAINER_TYPES, count: CONTAINER_TYPES.length } },
  "/spe/recyclebin": { status: 200, body: { items: RECYCLE_BIN, count: RECYCLE_BIN.length } },
  /*
   * File Browser — `GET /spe/containers/:id/items` → BARE `DriveItem[]`.
   *
   * 🔴 This route was MISSING entirely, and the old substring matcher silently served it the
   * containers-list envelope instead, white-screening the File Browser. Its absence should have
   * produced a loud NO FIXTURE warning; instead a wrong-but-plausible payload was returned. That is
   * the same failure this project exists to remove, reproduced inside the review tool.
   *
   * SYNTHETIC. The 2026-08-20 walkthrough observed real documents (signed NDAs, Compose drafts) in
   * "Spaarke Dev Container 2", but no item payload was ever captured, so these are invented — a
   * folder and two files, enough to exercise the folder/file split, sizes, and the empty-folder path.
   */
  "/spe/containers/:containerId/items": {
    status: 200,
    body: [
      {
        id: "i0000001-0000-0000-0000-000000000000",
        name: "Matter Files",
        createdDateTime: "2025-10-02T09:00:00Z",
        lastModifiedDateTime: "2026-08-19T16:20:00Z",
        folder: { childCount: 3 },
        webUrl: "https://spaarke.sharepoint.com/contentstorage/example/folder",
      },
      {
        id: "i0000002-0000-0000-0000-000000000000",
        name: "Mutual NDA - Executed.pdf",
        size: 284431,
        createdDateTime: "2025-10-05T11:12:00Z",
        lastModifiedDateTime: "2025-10-05T11:12:00Z",
        file: { mimeType: "application/pdf" },
        webUrl: "https://spaarke.sharepoint.com/contentstorage/example/nda.pdf",
        createdBy: { user: { displayName: "Ralph Schroeder", email: "ralph.schroeder@spaarke.com" } },
      },
      {
        id: "i0000003-0000-0000-0000-000000000000",
        name: "Engagement Letter.docx",
        size: 51872,
        createdDateTime: "2026-08-19T16:20:00Z",
        lastModifiedDateTime: "2026-08-19T16:20:00Z",
        file: { mimeType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        webUrl: "https://spaarke.sharepoint.com/contentstorage/example/engagement.docx",
        createdBy: { user: { displayName: "Ralph Schroeder", email: "ralph.schroeder@spaarke.com" } },
      },
    ],
  },
  // Single container GET — the File Browser and detail panes resolve the container itself.
  "/spe/containers/:containerId": { status: 200, body: CONTAINERS[0] },

  /*
   * Search — BARE ARRAY of NESTED results.
   *
   * 🔴 `ContainerSearchResult` is `{ container: Container, score? }`, NOT a flat container. A flat
   * fixture made the results grid read `result.container.displayName` off `undefined` — the Search
   * white screen. `DriveItemSearchResult` is likewise `{ item: DriveItem, containerId, score?, … }`.
   */
  "/spe/search/containers": {
    status: 200,
    body: [{ container: CONTAINERS[0], score: 0.92 }],
  },
  "/spe/search/items": {
    status: 200,
    body: [
      {
        item: {
          id: "i0000002-0000-0000-0000-000000000000",
          name: "Mutual NDA - Executed.pdf",
          size: 284431,
          createdDateTime: "2025-10-05T11:12:00Z",
          lastModifiedDateTime: "2025-10-05T11:12:00Z",
          file: { mimeType: "application/pdf" },
        },
        containerId: CONTAINERS[0].id,
        score: 0.87,
        hitHighlightedSummary: "…mutual <c0>NDA</c0> executed by both parties…",
      },
    ],
  },
  "/spe/audit": {
    status: 200,
    body: [
        { id: "a0000001-0000-0000-0000-000000000000", operation: "CreateContainer", category: "Configuration", targetResourceId: CONTAINERS[2].id, targetResourceName: CONTAINERS[2].displayName, responseStatus: 201, responseSummary: "Created", environmentId: ENV_ID, environmentName: "Spaarke Dev", containerTypeConfigId: PAYGO1_CONFIG_ID, containerTypeConfigName: "Spaarke PAYGO 1", businessUnitId: BU_ID, businessUnitName: "Spaarke", performedBy: "ralph.schroeder@spaarke.com", performedOn: "2026-08-20T10:14:00Z" },
        // A real failure shape — the container-type PATCH that 400s in the live tenant today.
        { id: "a0000002-0000-0000-0000-000000000000", operation: "UpdateContainerTypeSettings", category: "Configuration", targetResourceId: PAYGO1_CONTAINER_TYPE_ID, targetResourceName: "Spaarke PAYGO 1", responseStatus: 400, responseSummary: "invalidRequest / badArgument", environmentId: ENV_ID, environmentName: "Spaarke Dev", containerTypeConfigId: PAYGO1_CONFIG_ID, containerTypeConfigName: "Spaarke PAYGO 1", businessUnitId: BU_ID, businessUnitName: "Spaarke", performedBy: "ralph.schroeder@spaarke.com", performedOn: "2026-08-24T11:02:00Z" },
    ],
  },
  /*
   * ── WRITE PATHS (method-qualified) ──
   *
   * A GET list and a POST create share a path but not a response shape, so these must be keyed by
   * method. Without them, "+ New" received a list envelope where it expected a single record.
   *
   * ⚠️ Nothing is PERSISTED. The grid will not gain a row — deliberately. A fake persistence layer
   * would make the container-type settings save look like it worked, when the real PATCH returns 400
   * in the live tenant today (notes/live-verification-2026-08-24.md §2). Showing a convincing
   * success for an operation that fails in production is exactly this project's core defect.
   */
  "POST /spe/containers": {
    status: 201,
    body: {
      id: "c0000009-0000-0000-0000-000000000000",
      displayName: "Newly Created Container (not persisted)",
      containerTypeId: PAYGO1_CONTAINER_TYPE_ID,
      status: "active",
      createdDateTime: "2026-08-24T12:00:00Z",
      storageUsedInBytes: 0,
      isItemVersioningEnabled: true,
    },
  },
  "POST /spe/environments": {
    status: 201,
    body: { id: "e0000009-0000-0000-0000-000000000000", name: "New Environment (not persisted)", tenantId: "a221a95e-0000-0000-0000-000000000000", tenantName: "Spaarke", rootSiteUrl: "https://spaarke.sharepoint.com", isDefault: false, status: "active" },
  },
  "PUT /spe/environments/:id": {
    status: 200,
    body: { id: ENV_ID, name: "Spaarke Dev", tenantId: "a221a95e-0000-0000-0000-000000000000", tenantName: "Spaarke", rootSiteUrl: "https://spaarke.sharepoint.com", isDefault: true, status: "active" },
  },

  /*
   * Container-type settings save — this is how task 026's post-save state is seen.
   *
   * Returns the FULL settings read-back (task 025's `ContainerTypeSettingsResponseDto`), which is
   * what lets a caller confirm a write applied rather than trusting a 200. The UI should respond
   * with "Saved — replication is pending", NOT a green "saved successfully".
   */
  "PUT /spe/containertypes/:typeId/settings": {
    status: 200,
    body: {
      id: PAYGO1_CONTAINER_TYPE_ID,
      displayName: "Spaarke PAYGO 1",
      billingClassification: "standard",
      billingStatus: null,
      createdDateTime: null,
      settings: CONTAINER_TYPES[0].settings,
    },
  },

  "/spe/security/score": {
    status: 200,
    body: {
      id: "score-synthetic", currentScore: 47, maxScore: 82, percentage: 57,
      createdDateTime: "2026-08-24T00:00:00Z",
      controlScores: [
        { controlName: "MFA enabled for admins", score: 8, maxScore: 10, description: "Synthetic control — shape only." },
        { controlName: "Legacy auth blocked", score: 0, maxScore: 10, description: "Synthetic control — shape only." },
      ],
    },
  },

  /*
   * Security → Alerts is left FAILING on purpose, and this one is LIVE-accurate.
   *
   * The tenant returns 403 "Account is not provisioned" because it has no Microsoft 365 Defender
   * workload. Proven not to be a permissions problem: the legacy /security/alerts endpoint returns
   * 200 with an empty array on the same token, same tenant, same moment
   * (notes/security-grant-record.md). Making it succeed here would hide the one screen state we
   * know is genuinely broken — and would be this project's core defect, in the review tool.
   */
  "/spe/security/alerts": {
    status: 403,
    body: {
      status: 403,
      title: "Forbidden",
      detail:
        "Account is not provisioned. The Security Alerts (v2) API requires a Microsoft 365 Defender " +
        "workload in this tenant. This is not a missing permission — no broader grant resolves it.",
    },
  },
};
