# Task 036 — Email on subsequent grant (FR-07): notes

**Date:** 2026-10-11 · **Rigor:** FULL (BFF grant write path, tag `bff-api`) · **Model tier:** sonnet (as the POML) · **Base:** `ab4e91011` ("Merge task 034 …"). Status at hand-off: code + tests done, committed locally on the agent worktree branch (scope corrected 2026-10-11, see below). POML step 8 (TASK-INDEX / POML status / drift script) not run — outside this agent's edit scope (main session).

## Scope correction (2026-10-11, owner decision 2026-10-08)

The first commit (`4a2a767a2`) also emailed the other member contacts when a message was posted. That contradicts the owner decision in `spec.md`, and the task POML was wrong on this point:

- `spec.md:42` — "C6 — Message send. A thread post plus a notification to the record's members. No outbound email from the sender."
- `spec.md:64` (out of scope) — "Outbound email on message send."
- `spec.md:391` — "C6 sends no email."
- `spec.md:402` — "Send — thread post + notify members, no email (2026-10-08)".
- Project `CLAUDE.md:18` and `:73` say the same.

Members learn about messages through the in-portal notification feed (task 037), not email. The message-post email path was removed completely: the `onPosted` hand-off and fire-and-forget launcher in `ExternalMessageEndpoints.cs`, `NotifyMessagePostedAsync` and its member-recipient resolution in `GrantAccessNotifier`, and their tests. `ExternalMessageEndpoints.cs` and `ExternalMessageSendTests.cs` are back to their `ab4e91011` state (empty `git diff`). The grant email and its tests are unchanged. The shared `PortalNotificationTemplate.html` and `SendPortalNotificationEmailAsync` had no message-specific copy of their own, apart from one footer clause ("or of any message") and one doc phrase, both dropped.

## What it does

| Event | Who is emailed | What the email carries |
|---|---|---|
| `/grant` gives an **already-onboarded external contact** access it did not hold (a new grant, a lapsed one restored, a higher level) | That contact — once | A link `{PortalUrl}/?customer={key}&record={type}:{id}` |

Nothing is emailed for: a same-level re-grant, an expiry-only change, a lower level, an organization grant (all decided by the existing `GrantUpsertOutcome.AccessGained` gate in `/grant`), a contact that is **not yet onboarded** (no readable identity binding — the invite path emails those), an inactive contact, a contact with no email address, a contact that represents an internal user (they get the in-app notification), or the granter.

## Design

- **One notifier, extended** (`Api/ExternalAccess/GrantAccessNotifier.cs`). `NotifyContactGrantAsync` now ends its "represents nobody" branch in `EmailExternalContactGrantAsync` (it had returned `NotApplicable`). `BuildPortalLink` is `internal static` and pure.
- **Sender.** `RegistrationEmailService` gains one method, `SendPortalNotificationEmailAsync` (+ `PortalNotificationTemplate.html`, embedded like the others). It uses the same `FromMailbox`, `SendMode.SharedMailbox` and `CommunicationService.SendAsync` as `SendCiamOnboardingEmailAsync`: app-only, no OBO, never the user's mailbox. The method is `virtual` (the class is no longer `sealed`) purely as the test seam: the grant-email tests (`GrantAccessNotifierExternalTests`, and `RecordingEmailService` in `AssignedAccessTestDoubles` for `GrantExternalContactEmailTests`) record the send instead of reaching the shared mailbox. Kept because the grant email needs it.
- **If the sender is refused** (not approved on that stamp, daily limit) the send throws `SdapProblemException`; the notifier logs `[GRANT-NOTIFY] … was refused ({Code}); skipped. A stamp whose sender is not configured or approved sends nothing.` and moves on. There is no fallback sender.
- **Link config.** `ExternalAccess:PortalUrl` (existing, trusted) + the stamp's customer key = `CustomerIdentity.Id` (`Customer:Id`, existing). No new setting. If either is unusable the link is not built, a warning is logged and nothing is sent. The portal URL must be an absolute https URL (http only on loopback) with no credentials; its own query/fragment are dropped; each query value is `Uri.EscapeDataString`-encoded. The link holds the portal origin, the customer key and `type:id` only: no BFF URL, scope or token (NFR-02) — asserted by `TheLink_CarriesNoBffUrlScopeOrToken` and `BuildPortalLink_*`.
- **Delivery.** The grant email is **awaited** inline in `/grant`, like the task-181 in-app notification, so the response can report `notificationFailed`. A failure is logged and never undoes the grant. Cost: the mailbox send time is added to `/grant`.

## Placement Justification (BFF hygiene §10; `.claude/constraints/bff-extensions.md`)

- **Host:** the BFF (`Sprk.Bff.Api`). ADR-052 signals: **B2** (reuses `GrantAccessNotifier`, `CommunicationService`, the identity store — BFF domain code), **B3** (low volume: one email per grant), **B4** (started by one live request). No F-signal is material, so no Function.
- **No new endpoint, no new DI registration, no new package, no new Dataverse column, no new config key, no new secret, no new background service.** `GrantAccessNotifier` was already `AddScoped` (task 181); `RegistrationEmailService` and `CustomerIdentity` are unconditional singletons (ADR-032: no Null-Object needed).
- No route is added or moved; the ArchTests route ledger passes unchanged.

## CLAUDE.md §11 answers (new surface)

| New thing | 1 Existing | 2 Extension | 3 Cost of doing nothing |
|---|---|---|---|
| `RegistrationEmailService.SendPortalNotificationEmailAsync` + `PortalNotificationTemplate.html` | The five fixed-copy methods of the same service and their templates | Extended the same service/sender; a new generic method is needed because each existing method is bound to its own subject and body | The grant email cannot be sent from the one approved sender |
| `GrantAccessNotifier.EmailExternalContactGrantAsync`, `BuildPortalLink` | The notifier itself (task 181) | Extended it; the brief forbids a second notifier | UAT 6A: an onboarded contact is never told about a new record |
| `RegistrationEmailService` unsealed, method `virtual` | — | A one-method test seam is cheaper than a new interface | The notifier's send could not be unit-tested without a live communication pipeline |
| Customer key from `Customer:Id` | `CustomerIdentity` (task 123) | Reused as is; **no new config** | The link would have no `customer=` value |

## Deviations and decisions to confirm

1. **The sender is a constant, not the per-customer shared mailbox.** `RegistrationEmailService.FromMailbox` is `demo@demo.spaarke.com`. The brief says to use the sender `SendCiamOnboardingEmailAsync` uses, so that is what this email uses. On a stamp whose approved-senders list lacks it, the send is refused and logged (see above) — the "no sender configured" escalation is therefore detected at send time, not from configuration. When the per-customer shared mailbox (#1562) is provisioned, `FromMailbox` needs to become per-stamp for **all** the registration emails; that is a separate change. **Owner/main session: decide before go-live.**
2. **Customer key = `Customer:Id`.** The SPA's `?customer=` key is resolved by the T240c directory (not built; dev uses `VITE_BACKENDS` keys such as `default`). The BFF has no setting named for it, so the stamp's own customer id (3–8 lowercase letters/digits; fits the SPA key syntax `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`) is used. **If the directory keys customers differently, one line changes** (`GrantAccessNotifier.TryBuildLink`). On a dev BFF whose `Customer:Id` is unresolved, no link can be built and nothing is sent (logged).
3. **Type token.** `type` in `record={type}:{id}` is the BFF's existing wire token: `project`, `matter`, `workassignment` (the `recordType` the grant routes already parse). Task 055 must parse these three tokens. The colon is literal; type and id are each encoded.
4. **Not covered (named in the brief as `/grant`, so left):** `/invite-and-grant` for a contact that is `AlreadyProvisioned` (an onboarded contact granted another record through the modal's "+ Contact") and `/contact-grants` (a contact grantor's grant) also confer new access to an onboarded contact and send nothing. The notifier method is reusable; wiring each is a few lines plus a test. **Recommend a follow-up task.**
5. The integration-style tests (`tests/integration/auth/**`) are compiled into `Sprk.Bff.Api.Tests` (csproj link), so `GrantExternalContactEmailTests` runs in the unit project filter. The harness (`AssignedAccessTestDoubles`) gained a `RecordingEmailService` and the three new notifier constructor arguments.
6. Test scope beyond the stated contract (one line each): `BuildPortalLink_*` theories (the link is the security-relevant output); `ThePortalNotificationTemplate_IsAnEmbeddedResource…` (a missing embedded resource would fail every send at run time).

## Email copy — DRAFT FOR OWNER APPROVAL

Sent from the shared mailbox, HTML, one "button" link. `{name}` is the record's display name (omitted, and "a matter" used, when it cannot be read); `{label}` is `project`, `matter` or `work assignment`.

**Grant to an onboarded external contact**
- Subject: `You were given access to the {label} "{name}" in the Spaarke portal`
- Headline: `You were given access to the {label} "{name}"`
- Body: `{Granter's name, or "Someone"} gave you access in the Spaarke portal. Open it with the button below.`
- Button: `Open the {label}` → `{PortalUrl}/?customer={key}&record={type}:{id}`

**Fixed footer:** `You will be asked to sign in to the portal first. For your security, this email does not include the content of the record.` · `Need help signing in? Reach out to us at support@spaarke.com.` · `© {year} Spaarke. All rights reserved.`

Open copy questions: whether to name the granter (an internal user's display name goes to an external person); whether the record name belongs in the subject line; whether to say the level that was granted.

## Evidence (after the scope correction)

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/` | 0 warnings, 0 errors |
| `dotnet test tests/Spaarke.ArchTests/` | 940 passed, 0 failed |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~External\|…~Grant\|…~Communication"` | 4,288 passed, 8 skipped (pre-existing `[Fact(Skip)]` in `InboundPipelineTests` / `CommunicationIntegrationTests`), 0 failed |
| Task tests | `GrantAccessNotifierExternalTests` 29; `GrantExternalContactEmailTests` 8; `ExternalMessageSendTests` 43 (unchanged from `ab4e91011`); `GrantAccessNotificationTests` (task 181) 21 still pass |
| `git diff ab4e91011 -- ExternalMessageEndpoints.cs ExternalMessageSendTests.cs` | empty |

**Fails-before proof** (from the first commit; the mutated code is unchanged by the correction): external-contact email branch disabled (`return NotApplicable`) failed 13 grant-email, link-token, no-link, failed-send and refused-sender cases; link query values not encoded failed `BuildPortalLink_EncodesEveryQueryValue…`. The message-path mutations were dropped with that path.

### Publish size (BFF hygiene §10 / NFR-01)

Convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish`, framework-dependent linux-x64, **Release**, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**; each side a fresh `git archive` export at a short path (`C:\wt036m`, `C:\wt036b`), net10 SDK; 192 files on both sides. Re-measured after the correction.

| Tree | Zip | Bytes |
|---|---|---|
| Base `ab4e91011` (merge base) | 36.47 MB | 38,238,684 |
| This task (after correction) | 36.47 MB | 38,242,002 |
| **Delta** | **+0.003 MB (+3,318 bytes)** | |

Total 36.47 MB ≤ 60 MB; delta ≪ +5 MB. (Before the correction the delta was +10,808 bytes.) The comparison is against the branch's own base, not `master`, so the number isolates this task.
