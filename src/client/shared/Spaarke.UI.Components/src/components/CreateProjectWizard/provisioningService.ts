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
 * codes were added, and the result now says whether the SAME caller can retry (`retryable`). Round b2 (2026-10-02)
 * added two codes for a container ALREADY recorded on the project (kept when it is the project's own, refused when
 * another record holds it, unreadable → retry), and reads `creatorState` so an unreadable creator is a retry, not an
 * administrator's job. Round r2 reads `containerKept`: for a project that kept its own container the administrator's
 * recovery is a Manage Access share, not another securing call, and the copy says so. Round c1 (owner round 10 item 4)
 * added two codes for the records Dataverse moves together with the project (SharePoint document locations and
 * documents): `cascade_children_unreadable` (refused before any change; `cascadeChildState` tells a retry from an
 * administrator's job) and `cascade_children_not_restored` (the attempt was undone, but some of those records are not
 * back with their own owners — an administrator puts them back first). Round c1-r4 (owner round 14 item 3) applies the
 * same split to two existing codes: `container_ownership_unreadable` now carries `containerOwnershipState`, and
 * `resume_creator_unavailable` a `creatorState: refused` — a read Dataverse REFUSED (the service's sign-in or Read
 * privilege) is an administrator's job, not a retry.
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
  /**
   * The sprk_project GUID that has just been created. It arrives NOT flagged secure (task 150): `sprk_issecure` is
   * field-secured, and this call is what marks it secure — the server's first write.
   */
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
   * The creating user the project was explicitly shared to (task 061). Normally present — the owner team
   * has no members, so without this share the project is unreachable, and provisioning fails rather than
   * return without it. Exception (task 114, owner rounds 67/76): on a Restricted record a creator flagged
   * external is NOT shared to; the value is then the empty GUID, the creator is listed in
   * `skippedPrincipals` (`principal_external_on_restricted`), and `noInternalReader` may be set.
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
   * went to the person who created the record — its `createdby` user, or, for a record the BFF created app-only (Office
   * quick-create), the person the BFF recorded in `sprk_createdbyperson` (owner round 7 item 2). Optional because the
   * server added it.
   */
  resumed?: boolean;
  /**
   * Task 148: what the call did to the project's EXISTING related records (documents, events, to-dos, communications,
   * memos) — re-owned into the secure owner team and shared with the project's sharees. Optional because the server added
   * it; a successful response always carries a complete pass.
   */
  children?: {
    status: string;
    reowned: number;
    remaining: number;
    tables: Array<{
      table: string;
      examined: number;
      alreadyCorrect: number;
      reowned: number;
      refused: number;
      failed: number;
    }>;
  };
  /**
   * Task 148: true when the project was ALREADY secured and this call only completed its related records. No share was
   * written by this call (the earlier call proved the creator's), so `sharedToCreatorSystemUserId` is the empty GUID.
   */
  childrenOnly?: boolean;
  /**
   * Task 143 (owner N6): named colleagues from `sharePrincipalIds` who were NOT shared to, each with a reason code — on
   * the record's No Access list, or that list could not be checked. The other colleagues are still shared. Optional
   * because the server added it. The server's `message` is not shown: the per-person copy is authored here
   * (`describeSkippedPrincipal`, round 29) and returned as `warnings` on the result.
   */
  skippedPrincipals?: IProvisionSkippedPrincipal[];
  /**
   * unified-access-control-r2 task 114 (owner round 67: Restricted wins over the last-reader rule): on a Restricted record
   * the person it would be shared to was flagged external and NOT shared to; `true` when nobody internal can open the
   * record now. Optional because the server added it.
   */
  noInternalReader?: boolean | null;
  /** Task 114: the server's plain-language sentence for `noInternalReader`, shown as a warning when present. */
  noInternalReaderMessage?: string | null;
}

/** One named colleague the server did not share to (mirrors `ProvisionSkippedPrincipal`). */
export interface IProvisionSkippedPrincipal {
  systemUserId: string;
  reasonCode: string;
  message: string;
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
   * Refused before any change. Retryable where a READ failed and the same call can succeed once it works — and
   * `shared_container_not_cleared` (task 133 r1: a shared container's link could not be removed before the move; the
   * same caller calls again):
   * `creator_unresolved` (the caller's identity could not be confirmed), `container_ownership_unreadable` (whether the
   * container already on the record is shared could not be checked — task 133 b2; with `containerOwnershipState:
   * refused` it is deterministic and not retryable — owner round 14 item 3), `resume_creator_unavailable` with
   * `creatorState: unreadable` (the record's creator could not be looked up) and `cascade_children_unreadable` with
   * `cascadeChildState: unreadable` (the records that move with it could not be read — task 133 c1; `refused` is
   * deterministic and not retryable). Deterministic refusals are not retryable —
   * an ownerless row (`record_owner_unreadable`), a resume request naming colleagues from someone other than the creator
   * (`resume_colleagues_not_permitted`), and a container already recorded on another record
   * (`container_shared_with_another_record`, an administrator decides which record it belongs to).
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
   * Also (task 148, `children_incomplete`): secured and shared, but the project's EXISTING related records are not all
   * secured yet; each is left as it was, never more exposed. The same caller's next call completes them. Retryable.
   * Also (round 26 item 3, `files_incomplete`): secured, shared, its container exists and its related records are
   * secured, but some of its EXISTING files are not moved into its own container yet; none is more exposed than before.
   * The same caller's next call completes them. Retryable.
   */
  | 'interrupted'
  /**
   * Only an administrator can finish: the share AND the undo failed (the record may be owned by the memberless team
   * with no share — its creator no longer passes the Write gate), or a resume found no usable person recorded as the
   * record's creator (absent, disabled, an application user with no person recorded). For the latter an administrator
   * re-enables the creator, or assigns the record to the person who should hold it, who then secures it (task 133
   * verifier round 2). A creator whose read FAILED (`creatorState: unreadable`) is not this state: see 'not-started'; one
   * whose read Dataverse REFUSED (`creatorState: refused`, owner round 14 item 3) is. Also (task 133 c1): the
   * attempt was undone but records that moved together with the project are not back with their own owners
   * (`cascade_children_not_restored`) — an administrator puts them back before it is secured again.
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
  /**
   * Round 29: per-person warnings on a SUCCESS — one authored line per named colleague the server did not share to
   * (`skippedPrincipals`). The host shows them with its other warnings, as it does a failed file upload. Absent when
   * none was skipped.
   */
  warnings?: string[];
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
 *    code establishes. Since task 150 `sprk_issecure` is the server's FIRST write (the client never writes it): a
 *    refusal before it leaves the project not marked secure ("did not start … nothing changed"), and one after it
 *    leaves it marked secure with uploads refused ("not secured … documents cannot be added"). No message calls the
 *    project "a normal project" — whether it later becomes secure is still open.
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
  // Resume-neutral (task 150, round 17 item 2): this code also answers a RESUME of an unflagged record that the Secure
  // Record owner team already owns, where "did not start" would be false. Like row 2's option D, the copy claims neither
  // a first call nor a resume: securing could not be finished, and this call changed nothing.
  'sdap.provision.creator_unresolved': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project could not be finished, because your account could not be confirmed. Nothing about the project changed.',
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
  // Task 133 b2: the project already records a document container that another record also records. Keeping it would put
  // a secure project's files where another record reaches them; replacing it would strand this project's files. Refused
  // before any change; deterministic until an administrator decides which record the container belongs to.
  'sdap.provision.container_shared_with_another_record': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project did not start, because the document container already linked to it is also linked to another record. Nothing about the project changed; an administrator needs to decide which record the container belongs to.',
    retryable: false,
  },
  // Task 133 b2: whether the container already on the project is shared storage could not be checked (a read failed).
  // Refused before any change; the server tells the same caller they may call again. The copy here is the transient one
  // (`containerOwnershipState: unreadable`, also used when the extension is absent); classifyProvisioningFailure swaps in
  // the deterministic one for `containerOwnershipState: refused` (owner round 14 item 3).
  'sdap.provision.container_ownership_unreadable': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project did not start, because the document container already linked to it could not be checked. Nothing about the project changed.',
    retryable: true,
  },
  // Task 133 r1: the project recorded a SHARED document container, which is unlinked before the project is moved to the
  // secure owner — and that failed. Its ownership did not change; the link may or may not be gone. The server tells the
  // same caller they may call again.
  'sdap.provision.shared_container_not_cleared': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project stopped before it was moved: the shared document container linked to it could not be unlinked first. Its ownership did not change.',
    retryable: true,
  },
  // Task 133 c1 (owner round 10 item 4): the records Dataverse moves together with the project (SharePoint document
  // locations and documents) could not be read before any change, so a failure later could not put them back. Refused
  // before any change. The copy here is the transient one (`cascadeChildState: unreadable`, also used when the extension
  // is absent); classifyProvisioningFailure swaps in the deterministic one for `cascadeChildState: refused`.
  'sdap.provision.cascade_children_unreadable': {
    failureKind: 'not-started',
    errorMessage:
      'Securing the project did not start, because the records linked to it that move together with it could not be read. Nothing about the project changed.',
    retryable: true,
  },
  // Task 133 c1: the attempt was undone (ownership as before), but some records that moved together with the project
  // are not back with their own owners. The server names each with the call that puts it back; securing again before an
  // administrator makes those calls would record the wrong owners for them, so no retry is offered.
  'sdap.provision.cascade_children_not_restored': {
    failureKind: 'needs-administrator',
    errorMessage:
      'The project could not be shared back to you, so this attempt to secure it was undone and its ownership is as it was before the attempt. Some records linked to it could not be returned to their own owners; an administrator needs to put them back before it is secured.',
    retryable: false,
  },
  // Task 150: marking the project secure is the server's FIRST write, and it could not be set (or did not read back).
  // Nothing else changed; the server tells the same caller they may call again. Copy: owner round 10 item 9 (F6 row 3,
  // option A — notes/task-150-issecure-lock.md §6).
  'sdap.provision.secure_flag_not_set': {
    failureKind: 'not-started',
    errorMessage:
      'The project could not be marked secure, so securing it stopped before anything else changed: its ownership, sharing and document storage are as they were.',
    retryable: true,
  },
  // Task 150 (owner round 10 item 10): a record NOT yet marked secure is secured through this call only by the person
  // who created it, and the caller is not that person. Refused before any change; deterministic for that caller. The
  // two wizards secure only a record their user has just created, so neither reaches this in normal use. A RESUME of an
  // unflagged record is held to the same rule (verifier c1 item 4).
  // Copy: owner round 13 item 10 (F6 row 7, option B — notes/task-150-issecure-lock.md §6).
  'sdap.provision.not_record_creator': {
    failureKind: 'not-started',
    errorMessage: 'Only the person who created this project can secure it this way. Nothing about the project changed.',
    retryable: false,
  },
  // Task 150 (owner round 10 item 10): whether the caller created the record could not be checked (a read failed).
  // Refused before any change; the server tells the same caller they may call again. Copy: owner round 13 item 10 (F6
  // row 8, option B), made resume-neutral by round 17 item 2 — an unflagged RESUME meets this code with the record
  // already owned by the Secure Record owner team, so "it was not secured" would describe a first call only. With
  // `creatorState: column-missing` (round 17 item 1) the same code is deterministic: see CREATOR_COLUMN_MISSING.
  'sdap.provision.record_creator_unverifiable': {
    failureKind: 'not-started',
    errorMessage:
      'Who created this project could not be checked, so securing it could not be finished. Nothing about the project changed.',
    retryable: true,
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
  // Task 148: the project is secured and shared, but some of its existing related records are not secured yet (each left
  // as it was). The server tells the same caller that calling again completes them.
  'sdap.provision.children_incomplete': {
    failureKind: 'interrupted',
    errorMessage:
      'The project was secured and shared with you, but some of its existing documents, events, to-dos or messages are not secured yet.',
    retryable: true,
  },
  // Round 26 item 3 (wired at the batch-4 integration): secured, shared, its container exists and its related records are
  // secured, but some of its existing files are not moved into its own container yet (each still where it was). The
  // server tells the same caller that calling again completes them; the copy never advises it (FR-31, round 60 item 1).
  'sdap.provision.files_incomplete': {
    failureKind: 'interrupted',
    errorMessage:
      'The project was secured and shared with you, but some of its existing files have not been moved into its secure storage yet.',
    retryable: true,
  },
  'sdap.provision.creator_share_failed_resumable': {
    failureKind: 'needs-administrator',
    errorMessage:
      'Securing the project stopped partway, and you may not be able to open it. An administrator needs to finish securing it.',
    retryable: false,
  },
  // The copy for this code follows the server's `creatorState` extension: the message here is for a creator that cannot
  // be used (absent, disabled, an application user with no person recorded); classifyProvisioningFailure swaps in the
  // transient state for `creatorState: unreadable`, and the deterministic read refusals for `column-missing` and
  // `refused`.
  'sdap.provision.resume_creator_unavailable': {
    failureKind: 'needs-administrator',
    errorMessage:
      'Securing the project could not be finished, because no person who can be given access to it is recorded as its creator. An administrator needs to finish securing it.',
    retryable: false,
  },
  // Task 143 (owner N6): the caller is on the record's No Access list (directly, through an organization they belong to,
  // or one the record references). Refused 403 before any change; deterministic for that caller. Copy: round 29.
  'sdap.provision.creator_no_access': {
    failureKind: 'not-started',
    errorMessage:
      "You are on this project's No Access list, so you cannot secure it. Nothing about the project changed.",
    retryable: false,
  },
  // Task 143: whether the caller is on the No Access list could not be checked (a read failed). Refused 500 before any
  // change; the server tells the same caller they may call again. Copy: round 29 (resume-neutral, "could not be
  // finished").
  'sdap.provision.creator_no_access_unverifiable': {
    failureKind: 'not-started',
    errorMessage:
      'Whether you may access this project could not be checked, so securing it could not be finished. Nothing about the project changed.',
    retryable: true,
  },
  // Task 150 round 53 item 1 (for round 46 item 1): on the form's Make Secure command (transition "make-secure"), which
  // access the caller holds could not be read, so the share they keep could not be floored on it. Refused 500 before any
  // change; the server tells the same caller they may call again. The wizards send no transition, so the server makes no
  // such read for them — classified so a host that ever sends one is never left on the generic copy. The copy is round
  // 53's ratified sentence ({record} = project — the ribbon and the server carry it whole), less its closing "you may try
  // again": in this client the retry is the host's action, keyed on `retryable` (the copy rule above).
  'sdap.provision.caller_rights_unverifiable': {
    failureKind: 'not-started',
    errorMessage:
      'Which access you hold on this project could not be read, so securing it could not make sure you keep that access. Nothing was changed.',
    retryable: true,
  },
};

/**
 * `resume_creator_no_access` (task 143) answered **409**: a RESUME's person — the record's creator — is on its No Access
 * list, so no share was issued to them and nothing was written. Deterministic: an administrator reviews the record's
 * access (assigns it to the person who should hold it). Not retryable. Copy: round 29.
 */
const RESUME_CREATOR_NO_ACCESS = {
  failureKind: 'needs-administrator' as const,
  errorMessage:
    "Securing the project could not be finished, because the person who created it is on its No Access list. Nothing about the project changed. An administrator needs to review the project's access.",
  retryable: false,
};

/**
 * `resume_creator_no_access` answered **500**: whether the RESUME's person is on the No Access list could not be checked
 * (a read failed). Nothing was written; the server tells the same caller they may call again. Copy: round 29.
 */
const RESUME_CREATOR_NO_ACCESS_UNVERIFIABLE = {
  failureKind: 'not-started' as const,
  errorMessage:
    'Securing the project could not be finished, because the access of the person who created it could not be checked. Nothing about the project changed.',
  retryable: true,
};

/**
 * Round 29: the per-person warnings for task 143's skipped colleagues (`skippedPrincipals`). Not failures — the project is
 * secured and shared with everyone else; these say who was left out and why. `{name}` is the colleague's display name.
 *
 * `principal_share_failed` (task 150, round 33 items 1 and 5: the server names a colleague whose share failed instead of
 * only logging it) is composed from two approved sentences — round 33 item 5's generic warning and round 29's Manage
 * Access recovery — adjustable in UAT (owner round 27's stance).
 */
const SKIPPED_PRINCIPAL_COPY: Readonly<Record<string, (name: string) => string>> = {
  'sdap.provision.principal_no_access': name =>
    `${name} is on this project's No Access list, so the project was not shared with them.`,
  'sdap.provision.principal_no_access_unverifiable': name =>
    `Whether ${name} may access this project could not be checked, so the project was not shared with them. You can share it with them later from Manage Access.`,
  'sdap.provision.principal_share_failed': name =>
    `${name} was not given access to this project. You can share it with them later from Manage Access.`,
  // Task 114 (owner round 67, owner wording): a person flagged external on a Restricted record.
  'sdap.provision.principal_external_on_restricted': name =>
    `${name} is flagged as an external user and can't be given access to a Restricted record.`,
};

/**
 * Round 33 item 5: the per-person warning for a skipped colleague whose reason code this client does not know. Never
 * silent — the person is named, and the code is logged for support. The server's own `message` is still never shown.
 * `name` is always one {@link skippedPersonName} resolved — never blank (an unnamed person is already
 * {@link UNNAMED_PERSON} there), so this composes it as given: one place decides "Someone".
 */
const SKIPPED_PRINCIPAL_GENERIC = (name: string) => `${name} was not given access to this project.`;

/**
 * Round 40 item 3: the `{name}` of a per-person warning whose person cannot be named — an empty id, or an id the host gave
 * no name for. Never an empty name; the warning is shown either way (never silent). The ribbon script uses the same word.
 */
export const UNNAMED_PERSON = 'Someone';

/** A skipped colleague's display name: the host's non-blank name for the id, else {@link UNNAMED_PERSON}. */
function skippedPersonName(principalNames: Readonly<Record<string, string>> | undefined, systemUserId: string): string {
  const name = systemUserId ? principalNames?.[systemUserId] : undefined;
  return typeof name === 'string' && name.trim() ? name : UNNAMED_PERSON;
}

/**
 * The authored per-person warning for one skipped colleague, or `undefined` for a reason code this client does not know
 * (the caller then shows the generic warning, `SKIPPED_PRINCIPAL_GENERIC`, and logs the code). A blank `name` is
 * {@link UNNAMED_PERSON}.
 */
export function describeSkippedPrincipal(reasonCode: string, name: string): string | undefined {
  const who = name?.trim() ? name : UNNAMED_PERSON;
  return Object.prototype.hasOwnProperty.call(SKIPPED_PRINCIPAL_COPY, reasonCode)
    ? SKIPPED_PRINCIPAL_COPY[reasonCode](who)
    : undefined;
}

/**
 * `resume_creator_unavailable` with `creatorState: unreadable` (task 133 b2, verifier finding). The record's creator could
 * not be LOOKED UP — a read failed — so nothing is known to be wrong with them: the server's detail and the setup guide
 * (§7a) tell the same caller they may call again once the read works. Retryable, and the copy says only what happened.
 */
const RESUME_CREATOR_UNREADABLE = {
  failureKind: 'not-started' as const,
  errorMessage:
    'Securing the project could not be finished yet, because the person who created it could not be looked up. Nothing about the project changed.',
  retryable: true,
};

/**
 * `creatorState: column-missing` — the column that records the person who created the project does not exist in this
 * environment yet. Answered on `resume_creator_unavailable` (task 133 r1, verifier finding 10) and, since task 150 round
 * 17 item 1, on `record_creator_unverifiable` (the creator rule for an unflagged record: unverifiable, not "not the
 * creator"). The server's 400 is deterministic, so calling again repeats the refusal until an administrator applies the
 * schema. Not retryable: a "Try securing again" here would fail every time. One environment fact, one message — and it
 * is resume-neutral ("could not be finished"), true of a first call and of a resume alike.
 */
const CREATOR_COLUMN_MISSING = {
  failureKind: 'needs-administrator' as const,
  errorMessage:
    'Securing the project could not be finished, because this environment is not yet set up to record who created a project. Nothing about the project changed; an administrator needs to finish setting it up.',
  retryable: false,
};

/**
 * `resume_creator_unavailable` with `creatorState: refused` (owner round 14 item 3, task 133 c1-r4). Dataverse REFUSED
 * the read of who created the project — a 401/403 refusing the service's own sign-in or its Read privilege (or a 400).
 * Deterministic, so calling again repeats the refusal until an administrator acts: not retryable, and the copy names
 * what the server's detail and the setup guide (§7a) send the administrator to — the service's permission to look the
 * creator up — as the cascade refusal's copy does.
 */
const RESUME_CREATOR_REFUSED = {
  failureKind: 'needs-administrator' as const,
  errorMessage:
    "Securing the project could not be finished, because the person who created it could not be looked up. Nothing about the project changed; an administrator needs to check the service's permission to look them up first.",
  retryable: false,
};

/**
 * `container_ownership_unreadable` with `containerOwnershipState: refused` (owner round 14 item 3, task 133 c1-r4).
 * Dataverse REFUSED the read that checks whether the document container already on the project is shared — a 401/403
 * refusing the service's own sign-in or its Read privilege (or a 400). Deterministic: not retryable, and the copy names
 * the service's permission to make that check, as the server's detail and the setup guide (§7a) do.
 */
const CONTAINER_OWNERSHIP_REFUSED = {
  failureKind: 'not-started' as const,
  errorMessage:
    "Securing the project did not start, because the document container already linked to it could not be checked. Nothing about the project changed; an administrator needs to look at the service's permission to check it first.",
  retryable: false,
};

/**
 * `owner_assignment_unverified` with `creatorShareConfirmed: true` (task 133 verifier round 2). Only a share the server
 * read back on the record lets the copy say the project can be opened. Unconfirmed, the copy names the administrator:
 * if the move DID land and the unconfirmed share did not take, the caller no longer passes the Write gate and their
 * own retry is refused.
 */
const OWNER_UNVERIFIED_SHARE_CONFIRMED =
  'Securing the project was interrupted: it could not be confirmed whether its ownership changed. Your share on it was read back, so you can open it either way.';

/**
 * The server's `containerKept: true` (task 133 r1 server, read here since task 133 r2 — verifier round 5 finding 8). The
 * project kept the document container it already had, so once the secure owner holds it every later securing call
 * answers `already_provisioned` and finishes nothing: "an administrator needs to finish securing it" would name a
 * recovery that does not exist for it. The server's detail names the one that does — an administrator shares the
 * project to its creator through Manage Access — and these two messages follow it. Only the two codes whose default copy
 * names the administrator's securing call are swapped; every other code's copy is already true for a kept container.
 */
const OWNER_UNVERIFIED_CONTAINER_KEPT =
  'Securing the project was interrupted: it could not be confirmed whether its ownership changed, and a share to you was issued but could not be confirmed. If you cannot open the project, an administrator needs to share it with you through Manage Access.';

const RESUMABLE_CONTAINER_KEPT =
  'Securing the project stopped partway, and you may not be able to open it. If you cannot, an administrator needs to share it with you through Manage Access.';

/**
 * `cascade_children_unreadable` with `cascadeChildState: refused` (task 133 c1). Dataverse REFUSED the read of the records
 * that move together with the project — a 400, or (since task 133 c1-r2) a 401/403 refusing the service's own sign-in or
 * its Read privilege on that table — or answered it incompletely. Deterministic, so calling again repeats the refusal.
 * Not retryable: a "Try securing again" here would fail every time. The copy names both things the server's detail and
 * the setup guide (§7a) send the administrator to: those records, and the service's permission to read them (task 133
 * c1-r3).
 */
const CASCADE_CHILDREN_REFUSED = {
  failureKind: 'not-started' as const,
  errorMessage:
    "Securing the project did not start, because the records linked to it that move together with it could not be read. Nothing about the project changed; an administrator needs to look at those records, and the service's permission to read them, first.",
  retryable: false,
};

/** The ProblemDetails extensions besides `reasonCode` that change the designed state a code maps to. */
export interface IProvisioningFailureExtensions {
  /** `owner_assignment_unverified` only: whether the creator's share was read back on the record. */
  creatorShareConfirmed?: boolean;
  /**
   * Task 133 r1: the project keeps the document container it already had (`owner_assignment_unverified`,
   * `creator_share_failed_resumable`, and the pre-move `creator_share_failed` refusal). The administrator's recovery is
   * then a Manage Access share, not another securing call.
   */
  containerKept?: boolean;
  /**
   * `resume_creator_unavailable` (task 133 b2): why no creator could be shared to — `absent`, `disabled`,
   * `application-user`, `column-missing` (task 133 r1: the creator column is not in this environment), `refused` (owner
   * round 14 item 3: Dataverse refused the read — the service's sign-in or Read privilege) — all deterministic: an
   * administrator acts — or `unreadable` (a read failed: the same caller may retry). Also read on
   * `record_creator_unverifiable` (task 150 round 17 item 1), where only `column-missing` changes the state.
   */
  creatorState?: string;
  /**
   * `container_ownership_unreadable` only (owner round 14 item 3, task 133 c1-r4): `unreadable` — a read failed, the same
   * caller may retry — or `refused` — Dataverse refused the read (the service's sign-in or Read privilege), deterministic:
   * an administrator acts.
   */
  containerOwnershipState?: string;
  /**
   * `cascade_children_unreadable` only (task 133 c1): `unreadable` — a read failed, the same caller may retry — or
   * `refused` — Dataverse refused the read (including a 401/403: the service's sign-in or Read privilege) or answered it
   * incompletely, deterministic: an administrator acts.
   */
  cascadeChildState?: string;
}

/**
 * Maps a reason code to the designed state to render, with its authored copy and whether the same caller can retry.
 *
 * The environment message names the missing setup ("secure-record setup") because that is the one thing an
 * administrator needs to hear to fix it — and because "provisioning failed: HTTP 500" tells the person in front of
 * the wizard nothing they can act on.
 *
 * `httpStatus` decides only `resume_creator_no_access`, the one code the endpoint answers with two statuses (409: the
 * creator is on the No Access list; 500: that could not be checked). Without either status it is not classified — the
 * client cannot tell which state the record is in, so it says neither (the generic copy, not retryable).
 */
export function classifyProvisioningFailure(
  reasonCode?: string,
  extensions?: IProvisioningFailureExtensions,
  httpStatus?: number
): {
  failureKind: ProvisioningFailureKind;
  errorMessage: string;
  retryable: boolean;
} {
  if (reasonCode != null && ENVIRONMENT_REASON_CODES.has(reasonCode)) {
    // Task 150: on a FIRST call every environment refusal comes before the server marks the project secure, so it was
    // created and is not secured. On a RESUME (a retry after a partial run), or for a row an older client flagged, the
    // project is already flagged — and on a resume already owned by the secure team. The response does not say which
    // case applies, so the copy claims neither: owner round 10 item 9 chose the resume-neutral option D for this row
    // (F6 row 2, notes/task-150-issecure-lock.md §6).
    return {
      failureKind: 'environment-not-configured',
      errorMessage:
        'Secure projects cannot be set up in this environment right now — its Secure Record business unit, owner team or document storage is missing or not in a safe state. The project was created, but securing it could not be finished; an administrator can finish securing it once the setup is fixed.',
      retryable: false,
    };
  }

  if (reasonCode === 'sdap.provision.owner_assignment_unverified' && extensions?.creatorShareConfirmed === true) {
    return { ...REASON_STATES[reasonCode], errorMessage: OWNER_UNVERIFIED_SHARE_CONFIRMED };
  }

  if (reasonCode === 'sdap.provision.owner_assignment_unverified' && extensions?.containerKept === true) {
    return { ...REASON_STATES[reasonCode], errorMessage: OWNER_UNVERIFIED_CONTAINER_KEPT };
  }

  if (reasonCode === 'sdap.provision.creator_share_failed_resumable' && extensions?.containerKept === true) {
    return { ...REASON_STATES[reasonCode], errorMessage: RESUMABLE_CONTAINER_KEPT };
  }

  if (reasonCode === 'sdap.provision.resume_creator_unavailable' && extensions?.creatorState === 'unreadable') {
    return { ...RESUME_CREATOR_UNREADABLE };
  }

  if (
    (reasonCode === 'sdap.provision.resume_creator_unavailable' ||
      reasonCode === 'sdap.provision.record_creator_unverifiable') &&
    extensions?.creatorState === 'column-missing'
  ) {
    return { ...CREATOR_COLUMN_MISSING };
  }

  if (reasonCode === 'sdap.provision.resume_creator_unavailable' && extensions?.creatorState === 'refused') {
    return { ...RESUME_CREATOR_REFUSED };
  }

  if (
    reasonCode === 'sdap.provision.container_ownership_unreadable' &&
    extensions?.containerOwnershipState === 'refused'
  ) {
    return { ...CONTAINER_OWNERSHIP_REFUSED };
  }

  if (reasonCode === 'sdap.provision.cascade_children_unreadable' && extensions?.cascadeChildState === 'refused') {
    return { ...CASCADE_CHILDREN_REFUSED };
  }

  if (reasonCode === 'sdap.provision.resume_creator_no_access' && httpStatus === 409) {
    return { ...RESUME_CREATOR_NO_ACCESS };
  }

  if (reasonCode === 'sdap.provision.resume_creator_no_access' && httpStatus === 500) {
    return { ...RESUME_CREATOR_NO_ACCESS_UNVERIFIABLE };
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

/**
 * Task 150: the line a host shows when it HELD BACK what the user asked to add to a secure-requested record, because
 * securing it did not finish. Since the server — not the client — marks a record secure, a record whose provisioning
 * stopped before that write is an ordinary record, and a file, child record or email added to it now would land in
 * shared storage that cannot be taken back. `items` are short noun phrases in the order the host skipped them
 * ("the files you attached", "the event"); `undefined` when nothing was held back.
 *
 * Copy: owner round 10 item 9 (F6 row 1, option A — notes/task-150-issecure-lock.md §6). Never advises trying again
 * (the retry is the host's action).
 */
export function describeHeldBackForSecure(items: readonly string[]): string | undefined {
  if (items.length === 0) return undefined;

  const list = items.length === 1 ? items[0] : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;

  const one = items.length === 1;
  return (
    `Because securing the project did not finish, ${one ? 'this was' : 'these were'} not added to it, so nothing ` +
    `reached shared storage: ${list}. Add ${one ? 'it' : 'them'} once the project is secured.`
  );
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
 * @param principalNames - Optional. Display names of the `sharePrincipalIds` the host sent, keyed by systemuser id — the
 *   `{name}` of the per-person warnings (round 29). A host that names colleagues knows their names; the server's response
 *   carries only ids. An id with no (or a blank) name here, or an empty id, is shown as "Someone" (`UNNAMED_PERSON`, round
 *   40 item 3) — never an empty name.
 * @returns IProvisionProjectResult — never throws.
 */
export async function provisionSecureProject(
  request: IProvisionProjectRequest,
  authenticatedFetch: typeof fetch,
  bffBaseUrl: string,
  principalNames?: Readonly<Record<string, string>>
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
        if (typeof problem?.creatorState === 'string') {
          extensions.creatorState = problem.creatorState;
        }
        if (typeof problem?.containerKept === 'boolean') {
          extensions.containerKept = problem.containerKept;
        }
        if (typeof problem?.cascadeChildState === 'string') {
          extensions.cascadeChildState = problem.cascadeChildState;
        }
        if (typeof problem?.containerOwnershipState === 'string') {
          extensions.containerOwnershipState = problem.containerOwnershipState;
        }
      } catch {
        /* ignore JSON parse failure — classification falls through to 'error' */
      }

      const { failureKind, errorMessage, retryable } = classifyProvisioningFailure(
        reasonCode,
        extensions,
        response.status
      );

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
    // Task 148: a `childrenOnly` response completed the related records of a project secured earlier — its creator share
    // was proven by that earlier call, so this response carries none.
    if (!data?.childrenOnly && !data?.sharedToCreatorSystemUserId) {
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

    // Round 29: each colleague the server did not share to is a per-person warning, in authored copy.
    const warnings: string[] = [];
    for (const skipped of data.skippedPrincipals ?? []) {
      const name = skippedPersonName(principalNames, skipped.systemUserId);
      const warning = describeSkippedPrincipal(skipped.reasonCode, name);
      if (warning) {
        warnings.push(warning);
      } else {
        // Round 33 item 5: an unknown reason is never silent — the generic per-person warning, and the code logged.
        warnings.push(SKIPPED_PRINCIPAL_GENERIC(name));
        console.error('[ProvisioningService] A colleague was not shared to, for a reason this client does not know:', {
          systemUserId: skipped.systemUserId,
          reasonCode: skipped.reasonCode,
        });
      }
    }

    // Task 114: when nobody internal can open the record any more, the server says so — shown last, verbatim.
    if (typeof data.noInternalReaderMessage === 'string' && data.noInternalReaderMessage.trim()) {
      warnings.push(data.noInternalReaderMessage);
    }

    return warnings.length > 0 ? { success: true, data, warnings } : { success: true, data };
  } catch (err) {
    // Transport failure — no reason code exists, so this is an unclassified 'error'. The exception
    // message stays in the console for the same reason the server's detail does.
    console.error('[ProvisioningService] Provisioning error:', err);
    const { failureKind, errorMessage, retryable } = classifyProvisioningFailure(undefined);
    return { success: false, errorMessage, failureKind, retryable };
  }
}
