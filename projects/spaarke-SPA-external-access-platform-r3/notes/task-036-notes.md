# Task 036 — Email on subsequent grant + member notify on message send (FR-07; C6 notify): notes

**Date:** 2026-10-11 · **Rigor:** FULL (BFF grant write path, tag `bff-api`) · **Model tier:** sonnet (as the POML) · **Base:** `ab4e91011` ("Merge task 034 …"). Status at hand-off: code + tests done, committed locally on the agent worktree branch. POML step 8 (TASK-INDEX / POML status / drift script) not run — outside this agent's edit scope (main session).

## What it does

| Event | Who is emailed | What the email carries |
|---|---|---|
| `/grant` gives an **already-onboarded external contact** access it did not hold (a new grant, a lapsed one restored, a higher level) | That contact — once | A link `{PortalUrl}/?customer={key}&record={type}:{id}` |
| A message is **posted** to a record's thread (`POST /api/v1/external/{root}/{id}/messages`, 201) | Each OTHER member contact of the record (direct, active, unexpired contact grant), once | The same kind of link to that root. No message text. |

Nothing is emailed for: a same-level re-grant, an expiry-only change, a lower level, an organization grant (all decided by the existing `GrantUpsertOutcome.AccessGained` gate in `/grant`), a contact that is **not yet onboarded** (no readable identity binding — the invite path emails those), an inactive contact, a contact with no email address, a contact that represents an internal user (they get the in-app notification), the granter, or the message author.

## Design

- **One notifier, extended** (`Api/ExternalAccess/GrantAccessNotifier.cs`). `NotifyContactGrantAsync` now ends its "represents nobody" branch in `EmailExternalContactGrantAsync` (it had returned `NotApplicable`). New entry point `NotifyMessagePostedAsync(MessagePostedNotification, ct)`. `BuildPortalLink` is `internal static` and pure.
- **Sender.** `RegistrationEmailService` gains one method, `SendPortalNotificationEmailAsync` (+ `PortalNotificationTemplate.html`, embedded like the others). It uses the same `FromMailbox`, `SendMode.SharedMailbox` and `CommunicationService.SendAsync` as `SendCiamOnboardingEmailAsync`: app-only, no OBO, never the user's mailbox. The method is `virtual` (the class is no longer `sealed`) purely as the unit-test seam.
- **If the sender is refused** (not approved on that stamp, daily limit) the send throws `SdapProblemException`; the notifier logs `[GRANT-NOTIFY] … was refused ({Code}); skipped. A stamp whose sender is not configured or approved sends nothing.` and moves on. There is no fallback sender.
- **Link config.** `ExternalAccess:PortalUrl` (existing, trusted) + the stamp's customer key = `CustomerIdentity.Id` (`Customer:Id`, existing). No new setting. If either is unusable the link is not built, a warning is logged and nothing is sent. The portal URL must be an absolute https URL (http only on loopback) with no credentials; its own query/fragment are dropped; each query value is `Uri.EscapeDataString`-encoded. The link holds the portal origin, the customer key and `type:id` only: no BFF URL, scope or token (NFR-02) — asserted by `TheLink_CarriesNoBffUrlScopeOrToken` and `BuildPortalLink_*`.
- **Message hand-off.** `PostMessageCoreAsync` gets an optional trailing `Action<MessagePostedNotification>? onPosted`. It is called after `PostToRecordThreadAsync` returns (the row exists), inside its own try/catch, and only when there is a root to tell. The route binds `BackgroundMemberNotifier(scopeFactory, logger)`.
- **Which roots (decision on the 034 "dual-root" note).** `GetRecordRightsAsync` now returns `(Rights, NotifyRoots)`: the record itself for a project / matter / work assignment; for an **invoice** only the parent roots (`sprk_matter`, `sprk_project`) on which the caller holds **Create** (the roots the post was made through — never the other parent's members); **none** for a service request (it has no grant roots, so no members).
- **Members** = `sprk_externalrecordaccess` rows for the root with `statecode eq 0` and the enforcement path's own expiry predicate (`ExternalParticipationService.ExpiryPredicate`), `_sprk_contact_value` set, at most 50 rows per root (`MaxMessageRecipientsPerRoot`; a warning is logged if the cap is reached). Each contact is then read through `IContactIdentityStore.GetContactAsync` and must be active, bound (onboarded) and have an email address. The author (`principal.ContactId`) is marked "told" first, so a contact that is a member of two roots, or the author, is never emailed twice or at all.

## Placement Justification (BFF hygiene §10; `.claude/constraints/bff-extensions.md`)

- **Host:** the BFF (`Sprk.Bff.Api`). ADR-052 signals: **B2** (reuses `GrantAccessNotifier`, `CommunicationService`, the identity store — BFF domain code), **B3** (low volume: one email per member per post), **B4** (started by one live request). No F-signal is material (no bursty/heavy work, no event intake, no durable orchestration), so no Function.
- **No new endpoint, no new DI registration, no new package, no new Dataverse column, no new config key, no new secret, no new background service.** `GrantAccessNotifier` was already `AddScoped` (task 181); `RegistrationEmailService` and `CustomerIdentity` are unconditional singletons (ADR-032: no Null-Object needed).
- No route is added or moved: the POST stays in the `/api/v1/external` group (ExternalCollaboration policy + `CallerPrincipalAuthorizationFilter`); the ArchTests route ledger passes unchanged.

### ADR-052 / ADR-004 choice: fire-and-forget, not a queued `IJobHandler`

Chosen: a `Task.Run` in a **new DI scope** (`BackgroundMemberNotifier`), 2-minute timeout, every fault caught and logged, no retry.

- Why not a job: a queued job (ADR-004) needs a job type, payload, handler and registration — new surface (CLAUDE.md §11) for one best-effort email. Jobs are at-least-once and ADR-052 requires every handler to be idempotent per unit of work; an email cannot be made idempotent without a sent-ledger, so a redelivery would break "exactly one email per recipient". The spec says best effort: losing at most one notice on a process restart is acceptable; a duplicate is not.
- Why a new scope: `GrantAccessNotifier` and its guards are scoped and are disposed when the request ends; the notifier is resolved from `IServiceScopeFactory.CreateAsyncScope()` inside the task.
- Precedent: `RegistrationEndpoints` (`_ = SendAdminNotificationAsync…`) and `PrecedentAdminEndpoints` (`Task.Run`, "inner try/catch … fire-and-forget").
- ADR-052 "MUST NOT let the caller's token leave the request": the background work carries no token, only ids; every call is app-only.
- The response never waits: proved by `TheBackgroundLauncher_ReturnsAtOnce_WhileTheNotificationIsStillRunning_AndLogsItsFault`. A throwing hand-off cannot fail the post: `ACallbackThatThrows_NeverFailsOrChangesThePost_ItIsStillA201WithOneRow`.
- `/grant` is different: the existing task-181 notification is **awaited** inline so the response can report `notificationFailed`. The external email follows that pattern (best effort; a failure is logged, reported as `notificationFailed: true`, and never undoes the grant). Cost: the mailbox send time is added to `/grant`.

## CLAUDE.md §11 answers (new surface)

| New thing | 1 Existing | 2 Extension | 3 Cost of doing nothing |
|---|---|---|---|
| `RegistrationEmailService.SendPortalNotificationEmailAsync` + `PortalNotificationTemplate.html` | The five fixed-copy methods of the same service and their templates | Extended the same service/sender; a new generic method is needed because each existing method is bound to its own subject and body | The two emails cannot be sent from the one approved sender |
| `GrantAccessNotifier.NotifyMessagePostedAsync`, `EmailExternalContactGrantAsync`, `BuildPortalLink`, `MessagePostedNotification` / `MessageNotifyRoot` | The notifier itself (task 181) | Extended it; the brief forbids a second notifier | UAT 6A (an onboarded contact is never told about a new record) and spec FR-11 (members are told) both stay broken |
| `onPosted` parameter + `BackgroundMemberNotifier` + `NotifyMembers` in `ExternalMessageEndpoints` | `PostMessageCoreAsync` | Extended it; the optional trailing parameter leaves every existing caller and test unchanged | Without a scope-owning launcher the notifier would run on disposed scoped services, or the 201 would wait on the mailbox |
| `RegistrationEmailService` unsealed | — | A one-method test seam is cheaper than a new interface | The notifier's send could not be unit-tested without a live communication pipeline |
| Customer key from `Customer:Id` | `CustomerIdentity` (task 123) | Reused as is; **no new config** | The link would have no `customer=` value |

## Deviations and decisions to confirm

1. **The sender is a constant, not the per-customer shared mailbox.** `RegistrationEmailService.FromMailbox` is `demo@demo.spaarke.com`. The brief says to use the sender `SendCiamOnboardingEmailAsync` uses, so that is what these emails use. On a stamp whose approved-senders list lacks it, the send is refused and logged (see above) — the "no sender configured" escalation is therefore detected at send time, not from configuration. When the per-customer shared mailbox (#1562) is provisioned, `FromMailbox` needs to become per-stamp for **all** the registration emails; that is a separate change. **Owner/main session: decide before go-live.**
2. **Customer key = `Customer:Id`.** The SPA's `?customer=` key is resolved by the T240c directory (not built; dev uses `VITE_BACKENDS` keys such as `default`). The BFF has no setting named for it, so the stamp's own customer id (3–8 lowercase letters/digits; fits the SPA key syntax `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`) is used. **If the directory keys customers differently, one line changes** (`GrantAccessNotifier.TryBuildLink`). On a dev BFF whose `Customer:Id` is unresolved, no link can be built and nothing is sent (logged).
3. **Type token.** `type` in `record={type}:{id}` is the BFF's existing wire token: `project`, `matter`, `workassignment` (the `recordType` the grant routes already parse). Task 055 must parse these three tokens. The colon is literal; type and id are each encoded.
4. **Spec tension — "Outbound email on message send" is listed out of scope** (`spec.md` §Out of scope; project CLAUDE.md §1) and FR-11 says "no outbound email" (no `/me/sendMail`, #1464). This task's POML and brief direct a notification email to members through the shared mailbox, which is not the message sent as the user. Implemented as directed; the message text is never emailed. The route's `WithDescription` and file header now say so. **Owner may want to reword the spec line.**
5. **Not covered (named in the brief as `/grant`, so left):** `/invite-and-grant` for a contact that is `AlreadyProvisioned` (an onboarded contact granted another record through the modal's "+ Contact") and `/contact-grants` (a contact grantor's grant) also confer new access to an onboarded contact and send nothing. The notifier method is reusable; wiring each is a few lines plus a test. **Recommend a follow-up task.**
6. **Members without a direct grant are not emailed:** contacts who hold access only through an organization grant, internal users, and members who have never signed in (no identity binding). The in-portal feed (task 037) is their notice.
7. **No throttling or digest.** Every post emails every member; a busy thread is one email per post per member. A per-recipient digest/throttle is a follow-up if the owner wants one.
8. `GetRecordRightsAsync` (internal) changed its return type to a tuple; its only caller is `PostMessageCoreAsync`. The ArchTests ledger (`RouteAuthorizationGuardTests.Ledger`) still passes.
9. The integration-style tests (`tests/integration/auth/**`) are compiled into `Sprk.Bff.Api.Tests` (csproj link), so `GrantExternalContactEmailTests` runs in the unit project filter. The harness (`AssignedAccessTestDoubles`) gained a `RecordingEmailService` and the three new notifier constructor arguments.
10. Test scope beyond the stated contract (one line each): `BuildPortalLink_*` theories (the link is the security-relevant output); `ThePortalNotificationTemplate_IsAnEmbeddedResource…` (a missing embedded resource would fail every send at run time); `ACancelledNotification…` (the 2-minute timeout path).

## Email copy — DRAFTS FOR OWNER APPROVAL

Sent from the shared mailbox, HTML, one "button" link. `{name}` is the record's display name (omitted, and "a matter" used, when it cannot be read); `{label}` is `project`, `matter` or `work assignment`.

**Grant to an onboarded external contact**
- Subject: `You were given access to the {label} "{name}" in the Spaarke portal`
- Headline: `You were given access to the {label} "{name}"`
- Body: `{Granter's name, or "Someone"} gave you access in the Spaarke portal. Open it with the button below.`
- Button: `Open the {label}` → `{PortalUrl}/?customer={key}&record={type}:{id}`

**New message on a record (to each other member)**
- Subject: `New message on the {label} "{name}" in the Spaarke portal`
- Headline: `There is a new message on the {label} "{name}"`
- Body: `Open the record in the Spaarke portal to read it and reply.`
- Button: `Open the {label}` → the same kind of link

**Fixed footer (both):** `You will be asked to sign in to the portal first. For your security, this email does not include the content of the record or of any message.` · `Need help signing in? Reach out to us at support@spaarke.com.` · `© {year} Spaarke. All rights reserved.`

Open copy questions: whether to name the granter (an internal user's display name goes to an external person); whether the record name belongs in the subject line; whether to say the level that was granted.

## Evidence

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/` | 0 warnings, 0 errors |
| `dotnet test tests/Spaarke.ArchTests/` | 940 passed, 0 failed |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~External\|…~Grant\|…~Communication"` | 4,309 passed, 8 skipped (pre-existing `[Fact(Skip)]` in `InboundPipelineTests` / `CommunicationIntegrationTests`), 0 failed |
| New tests | `GrantAccessNotifierExternalTests` 41; `GrantExternalContactEmailTests` 8; `ExternalMessageSendTests` +12 (now 52); `GrantAccessNotificationTests` (task 181) 21 still pass |
| `dotnet list package --vulnerable --include-transitive` (BFF) | no vulnerable packages; no package reference changed |

**Fails-before proof** (each production mutation applied alone, the new tests run, production restored):

| Mutation | Failed |
|---|---|
| external-contact email branch disabled (`return NotApplicable`) | 13 (grant email, link tokens, no-link, failed-send, refused-sender cases) |
| author not excluded from the "told" set | 3 (`APostedMessage_*` author / no-other-members cases) |
| invoice notifies every parent, not just Create-bearing | 1 (`AnInvoicePost_NotifiesOnlyTheParentRoots…`) |
| hand-off `catch (Exception)` narrowed so a throwing callback escapes | 1 (`ACallbackThatThrows…`) |
| link query values not encoded | 1 (`BuildPortalLink_EncodesEveryQueryValue…`) |
| member read not limited to active/unexpired | 1 (`TheMemberRead_IsScopedToTheRootsPostedOn…`) |
| launcher blocks on the work (`.GetAwaiter().GetResult()`) | 1 (`TheBackgroundLauncher_ReturnsAtOnce…`) |

### Publish size (BFF hygiene §10 / NFR-01)

Convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish`, framework-dependent linux-x64 (the project's default RID), **Release**, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**; each side a fresh `git archive` export at a short path (`C:\wt036m`, `C:\wt036b`), net10 SDK 10.0.101; 192 files on both sides.

| Tree | Zip | Bytes |
|---|---|---|
| Base `ab4e91011` (the local R3 branch tip this task builds on) | 36.47 MB | 38,238,723 |
| This task | 36.48 MB | 38,249,531 |
| **Delta** | **+0.01 MB (+10,808 bytes)** | |

Total 36.48 MB ≤ 60 MB; delta ≪ +5 MB. The comparison is against the branch's own base (which includes tasks 001–034), not `master`, so the number isolates this task; master itself was not re-published here.
