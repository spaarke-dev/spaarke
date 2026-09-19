/**
 * provisioningService.ts
 * BFF API client for Secure Project infrastructure provisioning.
 *
 * Calls POST /api/v1/external-access/provision-project to orchestrate:
 *   - Assignment of the project to the canonical Secure Project business unit's owner team
 *   - The explicit share back to the creating user (and any named colleagues)
 *   - SPE container provisioning
 *   - Recording the container on the project record
 *
 * CONTRACT CHANGED 2026-08-25 (BFF task 021). The backend no longer creates a business unit per
 * project, no longer creates an External Access Account, and no longer supports umbrella BU
 * selection: there is ONE canonical `Secure Project` business unit, resolved by name from server
 * configuration. `umbrellaBuId`, `accountId`, `accountName` and `wasUmbrellaBu` are gone; the owner
 * team the project now belongs to is reported instead.
 *
 * CONTRACT EXTENDED 2026-09-08 (BFF task 061), mirrored here by task 068. The owner team is
 * memberless, so ownership grants nobody access; provisioning therefore issues the explicit share
 * that makes the project reachable at all. Request gained optional `sharePrincipalIds`; response
 * gained `sharedToCreatorSystemUserId` and `additionalPrincipalsShared`.
 *
 * FAILURE CLASSIFICATION (task 068). The endpoint carries a stable machine-readable
 * `reasonCode` in its ProblemDetails extensions. This client reads it and returns a
 * `failureKind` plus an AUTHORED message, so callers never surface the server's raw
 * `detail` prose to an end user. The environment-setup reasons are the important ones: pre-UAT
 * environments have no `Secure Project` business unit, and the endpoint fails closed on that — a
 * designed, explainable state, not a bug to render as a stack-trace-flavoured toast.
 *
 * Dependencies are injected as parameters (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Returns result object — never throws.
 */

// ---------------------------------------------------------------------------
// Request / Response types (mirror BFF Dtos)
// ---------------------------------------------------------------------------

export interface IProvisionProjectRequest {
  /** The sprk_project GUID that has just been created with sprk_issecure = true. */
  projectId: string;
  /**
   * Optional. Short project reference code (e.g. "P-2024-0042"), used only as a fallback for the
   * SPE container's display name when the project record has no name. It no longer names a business
   * unit, so it is no longer required.
   */
  projectRef?: string;
  /**
   * Optional. Additional Dataverse `systemuser` ids to share the project with at provisioning time,
   * alongside the creator — who is ALWAYS shared to and is identified server-side from the caller's
   * own token, never from this list (BFF task 061).
   *
   * The Create Project wizard does not collect colleagues today, so it sends nothing here; the field
   * exists because the server accepts it, and mirroring the shipped shape is what keeps this file
   * from drifting. Shares to these principals are best-effort — see `additionalPrincipalsShared`.
   */
  sharePrincipalIds?: string[];
}

export interface IProvisionProjectResponse {
  /** The canonical Secure Project business unit — resolved by name, not created. */
  businessUnitId: string;
  businessUnitName: string;
  /** The business unit's default owner team, which now owns the project. */
  ownerTeamId: string;
  ownerTeamName: string;
  /** The project's own SPE container, recorded on sprk_containerid. */
  speContainerId: string;
  /**
   * The creating user the project was explicitly shared to (task 061). A successful response always
   * carries it — the owner team has no members, so without this share the project is unreachable,
   * and provisioning fails rather than return without it.
   */
  sharedToCreatorSystemUserId: string;
  /**
   * How many of the request's optional `sharePrincipalIds` were also shared to. Can be lower than
   * the number requested: colleague shares are best-effort and the rest are added afterwards through
   * Manage Access.
   */
  additionalPrincipalsShared: number;
}

/**
 * Why provisioning did not succeed — derived from the endpoint's `reasonCode` extension, not from
 * its prose. Callers branch on this to choose which designed state to render.
 */
export type ProvisioningFailureKind =
  /**
   * The environment has not been set up for secure projects: no `Secure Project` business unit,
   * more than one, or its default owner team is missing/ambiguous. Expected in pre-UAT environments.
   * Nothing was created and the project is NOT in a wrong business unit — the endpoint fails closed
   * before it assigns anything.
   */
  | 'environment-not-configured'
  /** The project was already provisioned, or a previous run claimed it. Re-running would orphan a container. */
  | 'already-provisioned'
  /** The project carries a legacy per-project security BU; migrating it is a manual operation. */
  | 'legacy-provisioning'
  /**
   * Ownership moved but the mandatory creator share could not be issued (or the caller's Dataverse
   * identity could not be resolved). The endpoint stops rather than leave a record nobody can open.
   */
  | 'share-failed'
  /**
   * The ONLY partial outcome. Ownership moved and the creator share was issued — so the project IS
   * secured — but its SPE container was created and could not be recorded on the record, leaving an
   * orphaned container an operator has to reconcile. Distinguished from `'error'` because the
   * generic copy says the project was left as a normal project, and here that would be false in
   * both halves: it is secured, and something WAS created.
   */
  | 'container-not-recorded'
  /**
   * Anything else — transport failure, unexpected 5xx, unrecognised reason code, and the
   * owner-assignment reasons, for which the endpoint states "Nothing has been provisioned."
   */
  | 'error';

export interface IProvisionProjectResult {
  success: boolean;
  data?: IProvisionProjectResponse;
  /**
   * AUTHORED, user-facing explanation of the failure. Never the server's raw ProblemDetails
   * `detail`: that text is written for an operator reading a log, and the wizard's audience is the
   * attorney who just pressed Create.
   */
  errorMessage?: string;
  /** Which designed state the caller should render. Absent on success. */
  failureKind?: ProvisioningFailureKind;
  /** The raw `reasonCode` extension, when the server sent one — for logs and support, not for display. */
  reasonCode?: string;
}

// ---------------------------------------------------------------------------
// Reason codes (mirror ProvisionProjectEndpoint's ProblemDetails extensions)
// ---------------------------------------------------------------------------

/**
 * The four reasons that mean "this environment has no secure-project topology yet".
 *
 * All four are returned as HTTP 500 by the endpoint, NOT 4xx — the server treats a missing
 * environment as a server-side configuration fault. So status code cannot classify these; the
 * `reasonCode` extension is the only reliable discriminator, which is why this client reads it.
 */
const ENVIRONMENT_REASON_CODES: ReadonlySet<string> = new Set([
  'sdap.provision.secure_bu_not_found',
  'sdap.provision.secure_bu_ambiguous',
  'sdap.provision.secure_owner_team_not_found',
  'sdap.provision.secure_owner_team_ambiguous',
]);

const SHARE_REASON_CODES: ReadonlySet<string> = new Set([
  'sdap.provision.creator_unresolved',
  'sdap.provision.creator_share_failed',
]);

const REASON_ALREADY_PROVISIONED = 'sdap.provision.already_provisioned';
const REASON_LEGACY_PER_PROJECT_BU = 'sdap.provision.legacy_per_project_bu';
const REASON_CONTAINER_NOT_RECORDED = 'sdap.provision.container_not_recorded';

/**
 * The endpoint's remaining two codes — `owner_assignment_failed` and
 * `owner_assignment_not_applied` — are deliberately NOT listed anywhere here. They fall through to
 * `'error'`, whose copy says the project was left as a normal project, and that is exactly what the
 * endpoint reports for both: *"Nothing has been provisioned."* Giving them their own branch would
 * add a case that said the same thing.
 */

/**
 * Maps a reason code to the designed state to render, with its authored copy.
 *
 * The environment message names the missing setup explicitly ("the Secure Project business unit")
 * because that is the one thing an administrator needs to hear to fix it — and because "provisioning
 * failed: HTTP 500" tells the person in front of the wizard nothing they can act on.
 */
/**
 * Two rules govern the copy below, both learned from what FR-31 had to repair.
 *
 * **Never assert a state the client cannot observe.** The generic branch is reached by SIX endpoint
 * responses that carry no `reasonCode` at all — including all three SPE-container failures
 * (`ProvisionProjectEndpoint.cs` ContainerTypeId-unset / Graph-null / Graph-threw). Those fire AFTER
 * ownership has moved to the memberless owner team and the creator share has been issued, so the
 * record is secured-but-containerless. Copy saying "created as a normal project" would be false
 * there — and would be the FR-31 defect (a sentence that renders and is untrue) reintroduced through
 * the fallback path. Note also that `sprk_issecure` is written `true` BEFORE provisioning is
 * attempted and is never cleared on refusal, so "normal project" is wrong about the flag too.
 *
 * **Never advise "try again" once ownership may have moved.** The endpoint's idempotency marker IS
 * the owner-team assignment, so a retry past that point returns 409 `already_provisioned` — a second
 * misleading message on the same record. Only the environment branch, which fails before anything is
 * touched, can honestly describe a clean slate.
 */
export function classifyProvisioningFailure(reasonCode?: string): {
  failureKind: ProvisioningFailureKind;
  errorMessage: string;
} {
  if (reasonCode != null && ENVIRONMENT_REASON_CODES.has(reasonCode)) {
    return {
      failureKind: 'environment-not-configured',
      errorMessage:
        'Secure projects are not set up in this environment yet — the Secure Project business unit and its owner team have to exist before a project can be secured. The project was created as a normal project and nothing was moved; an administrator can secure it once the setup is in place.',
    };
  }

  if (reasonCode === REASON_ALREADY_PROVISIONED) {
    // Deliberately hedged. This code covers TWO states the client cannot tell apart, because the
    // endpoint's idempotency marker is the owner-team assignment (step 5) — which lands BEFORE the
    // creator share (5.5) and the container (6). So a retry after `creator_share_failed` reaches
    // this branch on a project that is claimed but that the creator still cannot open. Saying
    // flatly "already secured, nothing to do" would send that user away from a locked record.
    return {
      failureKind: 'already-provisioned',
      errorMessage:
        'This project was already claimed as secure, so securing it again was skipped — repeating it could leave a second, unusable document container behind. If you cannot open the project, an earlier attempt stopped partway and an administrator needs to finish it.',
    };
  }

  if (reasonCode === REASON_LEGACY_PER_PROJECT_BU) {
    return {
      failureKind: 'legacy-provisioning',
      errorMessage:
        'This project was secured by an earlier mechanism that gave it its own business unit. Moving it onto the current one is a manual administrator step.',
    };
  }

  if (reasonCode === REASON_CONTAINER_NOT_RECORDED) {
    return {
      failureKind: 'container-not-recorded',
      errorMessage:
        'The project was secured, but its document container could not be linked to it, so files cannot be stored on it yet. An administrator needs to finish this — until then, treat the project as secured but without document storage.',
    };
  }

  if (reasonCode != null && SHARE_REASON_CODES.has(reasonCode)) {
    return {
      failureKind: 'share-failed',
      errorMessage:
        'The project could not be shared back to you, so securing it was stopped rather than leaving a project nobody can open. The project was created but you may not be able to open it yet — an administrator needs to finish securing it.',
    };
  }

  return {
    failureKind: 'error',
    errorMessage:
      'The project was created, but securing it did not finish. Its current state needs checking — an administrator can see how far it got and finish securing it.',
  };
}

// ---------------------------------------------------------------------------
// Provisioning step progress
// ---------------------------------------------------------------------------

/**
 * Ordered steps shown in the provisioning progress UI.
 *
 * ⚠️ NO CURRENT CONSUMER (noted 2026-09-09, task 068). The component that rendered these,
 * `ProvisioningProgressStep`, was deleted 2026-09-04 — see `./index.ts`. What remains is this
 * constant and its barrel export, read by nothing. It is kept accurate rather than pinned by a test:
 * a test over a constant nothing renders asserts only that the constant equals itself. Decide
 * whether to retire it when the progress UI is next revisited.
 *
 * Mirrors what the backend actually does, in order. The retired 'bu' and 'account' steps described
 * creating a business unit and an External Access Account per project; neither happens any more.
 * Ownership is listed first because it is done first — it is the security step, so a container
 * failure must not leave the record owned outside the Secure Project business unit.
 */
export const PROVISIONING_STEPS = [
  { key: 'ownership', label: 'Securing project ownership\u2026' },
  // Added by task 068 to mirror the step task 061 added server-side (endpoint step 5.5). It sits
  // between ownership and the container for the same reason it does there: the owner team has no
  // members, so between the reassignment and this share the record is reachable by nobody.
  { key: 'sharing', label: 'Sharing the project back to you\u2026' },
  { key: 'container', label: 'Provisioning document container\u2026' },
  { key: 'storing', label: 'Recording the container on the project\u2026' },
] as const;

export type ProvisioningStepKey = (typeof PROVISIONING_STEPS)[number]['key'];

// ---------------------------------------------------------------------------
// Service function
// ---------------------------------------------------------------------------

/**
 * Calls the BFF /api/v1/external-access/provision-project endpoint.
 *
 * Dependencies are injected as parameters to avoid solution-specific imports.
 *
 * @param request - Provisioning request payload
 * @param authenticatedFetch - MSAL-backed fetch function for BFF API calls
 * @param bffBaseUrl - Base URL for the BFF API (e.g. "https://spe-api-dev.azurewebsites.net/api")
 * @returns IProvisionProjectResult — never throws.
 */
export async function provisionSecureProject(
  request: IProvisionProjectRequest,
  authenticatedFetch: typeof fetch,
  bffBaseUrl: string
): Promise<IProvisionProjectResult> {
  const url = `${bffBaseUrl}/api/v1/external-access/provision-project`;

  try {
    const response = await authenticatedFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      let reasonCode: string | undefined;
      let serverDetail: string | undefined;
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const problem: any = await response.json();
        reasonCode = typeof problem?.reasonCode === 'string' ? problem.reasonCode : undefined;
        serverDetail = problem?.detail ?? problem?.title;
      } catch {
        /* ignore JSON parse failure — classification falls through to 'error' */
      }

      const { failureKind, errorMessage } = classifyProvisioningFailure(reasonCode);

      // The server's detail goes to the console for support, and ONLY there. It is written for an
      // operator reading a log; putting it in front of the user is the raw-ProblemDetails failure
      // this classification exists to prevent.
      console.error('[ProvisioningService] Provisioning failed:', {
        status: response.status,
        reasonCode,
        failureKind,
        serverDetail,
      });

      return { success: false, errorMessage, failureKind, reasonCode };
    }

    const data: IProvisionProjectResponse = await response.json();

    // A 2xx is not on its own proof the share happened. The endpoint contract says a successful
    // response ALWAYS carries the creator share ("provisioning fails rather than returning without
    // it"), so a body missing it means the contract moved underneath us. Without this check a shape
    // change ships as success and the wizard tells the user the project is "shared with you" on no
    // evidence — the record would be unopenable and the UI would say otherwise. Fail closed instead.
    if (!data?.sharedToCreatorSystemUserId) {
      console.error(
        '[ProvisioningService] 2xx response did not report the creator share; treating as failure.',
        { received: data }
      );
      const { failureKind, errorMessage } = classifyProvisioningFailure();
      return { success: false, errorMessage, failureKind };
    }

    console.info('[ProvisioningService] Provisioning complete:', {
      buId: data.businessUnitId,
      buName: data.businessUnitName,
      ownerTeamId: data.ownerTeamId,
      containerId: data.speContainerId,
      sharedToCreator: data.sharedToCreatorSystemUserId,
      additionalPrincipalsShared: data.additionalPrincipalsShared,
    });

    return { success: true, data };
  } catch (err) {
    // Transport failure — no reason code exists, so this is an unclassified 'error'. The exception
    // message stays in the console for the same reason the server's detail does.
    console.error('[ProvisioningService] Provisioning error:', err);
    const { failureKind, errorMessage } = classifyProvisioningFailure(undefined);
    return { success: false, errorMessage, failureKind };
  }
}
