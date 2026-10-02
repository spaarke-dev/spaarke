/**
 * provisioningService.ts
 * BFF API client for Secure Project infrastructure provisioning.
 *
 * Calls POST /api/v1/external-access/provision-project to orchestrate:
 *   - The explicit share to the creating user, issued BEFORE the move where the platform allows it (task 133)
 *   - Assignment of the project to the canonical Secure Record business unit's owner team
 *   - Proof of the creator's share on the moved record — or the move undone (task 133)
 *   - Shares to any named colleagues, SPE container provisioning, and recording the container on the project
 *
 * CONTRACT EXTENDED 2026-10-01 (BFF task 133, C11). A creator-share failure no longer strands the project with a
 * memberless owner: the move is undone, or the record is left with the creator's share in place, and a project
 * owned by the secure team with no container recorded is RESUMED on the next call instead of refused. Six reason
 * codes were added, and the result now says whether the SAME caller can retry (`retryable`).
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
  /** The canonical Secure Record business unit — resolved by name, not created. */
  businessUnitId: string;
  businessUnitName: string;
  /** The business unit's NAMED owner team (task 144 — never its default team), which now owns the project. */
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
  /**
   * Task 133: true when this call FINISHED an earlier run that stopped after the owner move. The creator share then
   * went to the record's `createdby` user. Optional because the server added it.
   */
  resumed?: boolean;
}

/**
 * Why provisioning did not succeed — derived from the endpoint's `reasonCode` extension, not from
 * its prose. Callers branch on this to choose which designed state to render.
 *
 * Re-cut by task 133 (C11) around one question the person in front of the wizard needs answered: what state is
 * the project in, and can THEY do anything about it? Whether they can is `retryable` on the result — the kinds say
 * what happened.
 */
export type ProvisioningFailureKind =
  /**
   * The environment is not set up — or not in a safe state — for secure projects: no `Secure Record` business unit,
   * more than one, its NAMED owner team missing/ambiguous, that team has members, the business unit holds users,
   * either of the last two unreadable (task 144), or the SPE container type unconfigured (task 133). Every one is
   * refused BEFORE any change. Not retryable: an administrator fixes the setup.
   */
  | 'environment-not-configured'
  /** Already secured: owned by the secure owner team with its container recorded (or under the retired team). */
  | 'already-provisioned'
  /** The project carries a legacy per-project security BU; migrating it is a manual operation. */
  | 'legacy-provisioning'
  /**
   * Refused before any change. Retryable only for `creator_unresolved` (the caller's identity could not be confirmed,
   * which can be transient); an ownerless row (`record_owner_unreadable`) or a resume request naming colleagues from
   * someone other than the creator (`resume_colleagues_not_permitted`) is refused again on the same call.
   */
  | 'not-started'
  /**
   * The creator share could not be issued, and the attempt was undone (ownership read back as it was), or a resume
   * could not confirm the share and changed nothing. The caller still passes the Write gate. Retryable.
   */
  | 'share-failed'
  /** The owner move was refused and read back unchanged: nothing moved. An administrator checks the setup. */
  | 'not-secured'
  /**
   * Secured and shared, but the document container could not be created or recorded. The next call resumes from
   * here (task 133). Retryable.
   */
  | 'storage-incomplete'
  /**
   * The owner move could not be read back; a share to the caller was issued — read back on the record only when the
   * server's `creatorShareConfirmed` extension is true (task 133 verifier round 2: the copy follows it). The next call
   * resumes or restarts. Retryable; when the share is unconfirmed and the caller can no longer open the record, that
   * call is refused at the Write gate and an administrator finishes, which the unconfirmed copy says.
   */
  | 'interrupted'
  /**
   * Only an administrator can finish: the share AND the undo failed (the record may be owned by the memberless team
   * with no share — its creator no longer passes the Write gate), or a resume found the record's creator unusable
   * (absent, disabled, an application user, unreadable). For the latter an administrator re-enables the creator, or
   * assigns the record to the person who should hold it, who then secures it (task 133 verifier round 2).
   */
  | 'needs-administrator'
  /** Anything else — transport failure, unexpected 5xx, or an unrecognised or absent reason code. */
  | 'error';

export interface IProvisionProjectResult {
  success: boolean;
  data?: IProvisionProjectResponse;
  /**
   * AUTHORED, user-facing explanation of the failure. Never the server's raw ProblemDetails
   * `detail`: that text is written for an operator reading a log, and the wizard's audience is the
   * attorney who just pressed Create. It describes the project's STATE and never advises trying again — that
   * advice belongs to a host that can offer the action (see `retryable`).
   */
  errorMessage?: string;
  /** Which designed state the caller should render. Absent on success. */
  failureKind?: ProvisioningFailureKind;
  /**
   * Task 133: whether the SAME caller can complete securing by calling again — the server's state allows it, and
   * the caller still passes its Write gate. A host advises a retry ONLY when this is true AND it renders the action
   * that performs it ("Try securing again", `SecureProvisioningOutcome`); a sentence saying "you can retry" with
   * nothing to click is the FR-31 defect class.
   */
  retryable?: boolean;
  /** The raw `reasonCode` extension, when the server sent one — for logs and support, not for display. */
  reasonCode?: string;
}

// ---------------------------------------------------------------------------
// Reason codes (mirror ProvisionProjectEndpoint's ProblemDetails extensions)
// ---------------------------------------------------------------------------

/**
 * The reasons that mean "this environment's secure-record setup is missing or not safe to use".
 *
 * All are returned as HTTP 500 by the endpoint, NOT 4xx — the server treats them as server-side
 * configuration faults. So status code cannot classify these; the `reasonCode` extension is the only
 * reliable discriminator, which is why this client reads it. Every one of them is raised BEFORE any
 * mutation (task 144 checks the named owner team's members and the business unit's users first; task 133 checks
 * the container type first), so "nothing was moved" is true of each.
 */
const ENVIRONMENT_REASON_CODES: ReadonlySet<string> = new Set([
  'sdap.provision.secure_bu_not_found',
  'sdap.provision.secure_bu_ambiguous',
  'sdap.provision.secure_owner_team_not_found',
  'sdap.provision.secure_owner_team_ambiguous',
  'sdap.provision.secure_owner_team_has_members',
  'sdap.provision.secure_owner_team_membership_unreadable',
  'sdap.provision.secure_bu_has_users',
  'sdap.provision.secure_bu_users_unreadable',
  'sdap.provision.container_type_not_configured',
]);

/**
 * One designed state per reason code that is not an environment refusal. `retryable` mirrors the server: true only
 * where the endpoint's detail tells the same caller they may call again (task 133).
 *
 * Copy rules, both learned from what FR-31 had to repair:
 *  - **Never assert a state the client cannot observe.** Each message says only what the endpoint's response for that
 *    code establishes. `sprk_issecure` is written `true` before provisioning and never cleared on refusal, so no
 *    message calls the project "a normal project".
 *  - **Never advise trying again in the message.** A retry is offered by the host that renders the action, keyed on
 *    `retryable` — so the advice and the button cannot come apart.
 */
const REASON_STATES: Readonly<
  Record<string, { failureKind: ProvisioningFailureKind; errorMessage: string; retryable: boolean }>
> = {
  'sdap.provision.already_provisioned': {
    failureKind: 'already-provisioned',
    errorMessage: 'This project is already secured, with its own document container. Nothing was changed.',
    retryable: false,
  },
  // Task 144: secured before the named owner team existed, under the retired default team — already claimed.
  'sdap.provision.owned_by_other_secure_team': {
    failureKind: 'already-provisioned',
    errorMessage:
      'This project is already secured, under an earlier secure owner. Nothing was changed; an administrator moves it to the current one.',
    retryable: false,
  },
  'sdap.provision.legacy_per_project_bu': {
    failureKind: 'legacy-provisioning',
    errorMessage:
      'This project was secured by an earlier mechanism that gave it its own business unit. Moving it onto the current one is a manual administrator step.',
    retryable: false,
  },
  'sdap.provision.creator_unresolved': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project did not start, because your account could not be confirmed. Nothing about the project changed.',
    retryable: true,
  },
  // Not retryable (task 133 verifier round 1): the row was read without an owner, which is deterministic for that row —
  // calling again repeats the refusal, and the server's detail says an administrator checks the owner first.
  'sdap.provision.record_owner_unreadable': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project did not start, because its current owner could not be read. Nothing about the project changed; an administrator needs to check its owner.',
    retryable: false,
  },
  // Task 133 verifier round 1: a resume request naming colleagues, from someone other than the project's creator. The
  // wizard never sends colleagues, so it never meets this; a host that does must drop them, not repeat the same call.
  'sdap.provision.resume_colleagues_not_permitted': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project was not finished, because the request named people to share it with and only the person who created it can do that at this stage. Nothing about the project changed; people are added through Manage Access.',
    retryable: false,
  },
  'sdap.provision.creator_share_failed': {
    failureKind: 'share-failed',
    errorMessage:
      'The project could not be shared back to you, so this attempt to secure it was stopped. Its ownership is as it was before the attempt.',
    retryable: true,
  },
  'sdap.provision.owner_assignment_failed': {
    failureKind: 'not-secured',
    errorMessage:
      'The project was created but not secured: it could not be moved to the secure owner, and its ownership did not change. Documents cannot be added to it until it is secured; an administrator needs to check the secure setup.',
    retryable: false,
  },
  'sdap.provision.owner_assignment_not_applied': {
    failureKind: 'not-secured',
    errorMessage:
      'The project was created but not secured: the move to the secure owner did not take effect, and its ownership did not change. Documents cannot be added to it until it is secured; an administrator needs to check the secure setup.',
    retryable: false,
  },
  // The copy for this code follows the server's `creatorShareConfirmed` extension: the message here is the
  // UNCONFIRMED one (also used when the extension is absent); classifyProvisioningFailure swaps in the confirmed one.
  'sdap.provision.owner_assignment_unverified': {
    failureKind: 'interrupted',
    errorMessage:
      'Securing the project was interrupted: it could not be confirmed whether its ownership changed, and a share to you was issued but could not be confirmed. If you cannot open the project, an administrator needs to finish securing it.',
    retryable: true,
  },
  'sdap.provision.container_creation_failed': {
    failureKind: 'storage-incomplete',
    errorMessage:
      'The project was secured and shared with you, but its document container could not be created, so files cannot be stored on it yet.',
    retryable: true,
  },
  'sdap.provision.container_not_recorded': {
    failureKind: 'storage-incomplete',
    errorMessage:
      'The project was secured and shared with you, but its document container could not be linked to it, so files cannot be stored on it yet.',
    retryable: true,
  },
  'sdap.provision.creator_share_failed_resumable': {
    failureKind: 'needs-administrator',
    errorMessage:
      'Securing the project stopped partway, and you may not be able to open it. An administrator needs to finish securing it.',
    retryable: false,
  },
  'sdap.provision.resume_creator_unavailable': {
    failureKind: 'needs-administrator',
    errorMessage:
      'Securing the project could not be finished, because the person who created it cannot be given access to it. An administrator needs to finish securing it.',
    retryable: false,
  },
};

/**
 * `owner_assignment_unverified` with `creatorShareConfirmed: true` (task 133 verifier round 2). Only a share the server
 * read back on the record lets the copy say the project can be opened. Unconfirmed, the copy names the administrator:
 * if the move DID land and the unconfirmed share did not take, the caller no longer passes the Write gate and their
 * own retry is refused.
 */
const OWNER_UNVERIFIED_SHARE_CONFIRMED =
  'Securing the project was interrupted: it could not be confirmed whether its ownership changed. Your share on it was read back, so you can open it either way.';

/** The ProblemDetails extensions besides `reasonCode` that change the designed state a code maps to. */
export interface IProvisioningFailureExtensions {
  /** `owner_assignment_unverified` only: whether the creator's share was read back on the record. */
  creatorShareConfirmed?: boolean;
}

/**
 * Maps a reason code to the designed state to render, with its authored copy and whether the same caller can retry.
 *
 * The environment message names the missing setup ("secure-record setup") because that is the one thing an
 * administrator needs to hear to fix it — and because "provisioning failed: HTTP 500" tells the person in front of
 * the wizard nothing they can act on.
 */
export function classifyProvisioningFailure(
  reasonCode?: string,
  extensions?: IProvisioningFailureExtensions
): {
  failureKind: ProvisioningFailureKind;
  errorMessage: string;
  retryable: boolean;
} {
  if (reasonCode != null && ENVIRONMENT_REASON_CODES.has(reasonCode)) {
    return {
      failureKind: 'environment-not-configured',
      errorMessage:
        'Secure projects cannot be set up in this environment right now — its Secure Record business unit, owner team or document storage is missing or not in a safe state. The project was created and marked secure, but nothing about its ownership changed and documents cannot be added to it until it is secured; an administrator can secure it once the setup is fixed.',
      retryable: false,
    };
  }

  if (reasonCode === 'sdap.provision.owner_assignment_unverified' && extensions?.creatorShareConfirmed === true) {
    return { ...REASON_STATES[reasonCode], errorMessage: OWNER_UNVERIFIED_SHARE_CONFIRMED };
  }

  if (reasonCode != null && Object.prototype.hasOwnProperty.call(REASON_STATES, reasonCode)) {
    return { ...REASON_STATES[reasonCode] };
  }

  return {
    failureKind: 'error',
    errorMessage:
      'The project was created, but securing it did not finish. Its current state needs checking — an administrator can see how far it got and finish securing it.',
    retryable: false,
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
 * Sharing is listed first since task 133: the endpoint shares to the creator BEFORE the owner move
 * (and proves the share after it), so the creator never depends on a write made after they lost access.
 * Ownership precedes the container — it is the security step, so a container failure must not leave the
 * record owned outside the Secure Record business unit.
 */
export const PROVISIONING_STEPS = [
  { key: 'sharing', label: 'Sharing the project with you\u2026' },
  { key: 'ownership', label: 'Securing project ownership\u2026' },
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
      const extensions: IProvisioningFailureExtensions = {};
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const problem: any = await response.json();
        reasonCode = typeof problem?.reasonCode === 'string' ? problem.reasonCode : undefined;
        serverDetail = problem?.detail ?? problem?.title;
        if (typeof problem?.creatorShareConfirmed === 'boolean') {
          extensions.creatorShareConfirmed = problem.creatorShareConfirmed;
        }
      } catch {
        /* ignore JSON parse failure — classification falls through to 'error' */
      }

      const { failureKind, errorMessage, retryable } = classifyProvisioningFailure(reasonCode, extensions);

      // The server's detail goes to the console for support, and ONLY there. It is written for an
      // operator reading a log; putting it in front of the user is the raw-ProblemDetails failure
      // this classification exists to prevent.
      console.error('[ProvisioningService] Provisioning failed:', {
        status: response.status,
        reasonCode,
        failureKind,
        serverDetail,
      });

      return { success: false, errorMessage, failureKind, retryable, reasonCode };
    }

    const data: IProvisionProjectResponse = await response.json();

    // A 2xx is not on its own proof the share happened. The endpoint contract says a successful
    // response ALWAYS carries the creator share ("provisioning fails rather than returning without
    // it"), so a body missing it means the contract moved underneath us. Without this check a shape
    // change ships as success and the wizard tells the user the project is "shared with you" on no
    // evidence — the record would be unopenable and the UI would say otherwise. Fail closed instead.
    if (!data?.sharedToCreatorSystemUserId) {
      console.error('[ProvisioningService] 2xx response did not report the creator share; treating as failure.', {
        received: data,
      });
      const { failureKind, errorMessage, retryable } = classifyProvisioningFailure();
      return { success: false, errorMessage, failureKind, retryable };
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
    const { failureKind, errorMessage, retryable } = classifyProvisioningFailure(undefined);
    return { success: false, errorMessage, failureKind, retryable };
  }
}
