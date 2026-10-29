# Task 036 — concise ADR edits for owner decisions (a) and (f) (draft for the main session)

> **Why a draft:** sub-agents cannot write `.claude/` (root CLAUDE.md §3). The main session applies these.
> **Decisions:** owner, 2026-10-18. (a) A WorkforceHome-linked systemuser is confirmed app-only, naming that systemuser.
> (f) A capped impersonated set keeps the most recently modified records.
> **Built in:** `feat/uac-036-fr20-flag-gated-swap` (PR #2078); what was built is in `036-fr20-implementation.md` §12.
> The full ADR-034 text (`docs/adr/ADR-034-user-record-membership.md` § Amendment A2) is already updated in the same
> commit.
> **Anchors:** each `Old` below occurs exactly once in the file named, as on this branch at `953fd6446`. The ADR-028
> anchors also occur once on `origin/master` and `work/unified-access-control-r2`.

## ADR-034 (`.claude/adr/ADR-034-user-record-membership.md`)

### Edit A1 — the second A2 MUST NOT bullet (line 116): remove the WorkforceHome precondition

This removes the clause that fix round 2's Edit 5c added (`036-adr034-concise-amendment.md`).

Old:

```markdown
, nor — in a tenant whose internal users sign in through WorkforceHome (ADR-028 A9) — before task 036 open question (a) is settled (their customer-directory tokens cannot be exchanged for Dataverse, so every write of theirs is refused under the flag, except service-request writes, which keep the requester rule, and writes a contact grant allows).
```

New:

```markdown
.
```

The bullet then ends: "… the task 034 canary passes there **strictly smaller** (equal sets are a failure). The
checked-in value stays `false` (enforced by `ImpersonationNegativeCanaryTests`' config tripwire)."

### Edit A2 — the A2 MUST bullet (line 93): add the WorkforceHome path

Old:

```markdown
a delete-own needs Delete on the row (the creator stamp still required). Keep the contact-grants union
```

New:

```markdown
a delete-own needs Delete on the row (the creator stamp still required). A WorkforceHome-linked systemuser (ADR-028 A9, resolved through the stored home link) holds a customer-directory token that cannot be exchanged, so the same two questions are asked app-only, naming that systemuser: `RetrievePrincipalAccess` with `Principal = systemusers(<id>)` and `RetrieveUserSetOfPrivilegesByNames` on that systemuser (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyAsync` / `PrincipalHoldsPrivilegeAppOnlyAsync`, under ADR-028 A5's trust basis; task 036 decision a, owner 2026-10-18). The path is chosen from the resolved principal (`CallerPrincipal.ResolvedThroughHomeLink`), never by trying OBO and falling back, and the same answer drives that user's list `canCreate`. Keep the contact-grants union
```

### Edit A3 — the same bullet (line 93): the capped set's order (decision f)

Old:

```markdown
the impersonated read through `IImpersonatedRootSetSource`, at **Read** —
```

New:

```markdown
the impersonated read through `IImpersonatedRootSetSource`, at **Read**, newest first (`$orderby=modifiedon desc` before the 5,000-row cap, which stays flagged; task 036 decision f) —
```

### Edit A4 — the first A2 MUST NOT bullet (line 115): app-only named-principal is not a fallback

Old:

```markdown
fall back to the membership walk, an app-only read or a constant when Dataverse's answer cannot be read, or stamp
```

New:

```markdown
fall back to the membership walk, an app-only read or a constant when Dataverse's answer cannot be read (the app-only, named-principal confirmation of a WorkforceHome-linked systemuser is not a fallback: it is chosen from how that principal was resolved, and a fault on it adds no rights either), or stamp
```

## ADR-028 (`.claude/adr/ADR-028-spaarke-auth-architecture.md`)

### Edit B1 — Amendment A5: a scope extension for the write confirmation

Inserts a new sub-section between A5's 2026-09-15 scope extension and "Deployment prerequisites".

Old:

```markdown
`projects/unified-access-control-r2/notes/decisions/function-impersonation-proposal.md`.

### Deployment prerequisites (register E-2 / E-3)
```

New:

```markdown
`projects/unified-access-control-r2/notes/decisions/function-impersonation-proposal.md`.

### Scope extension (2026-10-18, owner-accepted): confirming a WorkforceHome-linked user's write

A licensed core user resolved through A9's stored home link holds a customer-directory token, which cannot be exchanged
for a Dataverse token in the Spaarke tenant. To confirm such a user's write on `/api/v1/external` (ADR-034 A2), the BFF
asks Dataverse app-only, impersonating that systemuser: `RetrievePrincipalAccess` with `Principal = systemusers(<id>)` on
the target record, and `RetrieveUserSetOfPrivilegesByNames` on that systemuser for the child table's Create privilege
(task 036 decision a). The trust basis is this amendment's: the id is the one the validated token was resolved to through
the stored link, never client input, and Dataverse answers from that user's own roles, teams and shares. Conditions:
- chosen from the resolved principal (`CallerPrincipal.ResolvedThroughHomeLink`), never by trying a token exchange and
  falling back;
- fail closed: a fault, a missing systemuser id or an unreadable answer adds no rights (both `Spaarke.Dataverse` calls
  refuse `Guid.Empty`);
- uncached, and logged with the path that answered (no tokens, no PII);
- the same deployment prerequisite as the read (E-2, `prvActOnBehalfOfAnotherUser`).

No token exchange is added. Evidence: `projects/unified-access-control-r2/notes/036-fr20-implementation.md` §12.

### Deployment prerequisites (register E-2 / E-3)
```

### Edit B2 (optional) — Amendment A9 "Scope and preserved invariants": point to B1

Old:

```markdown
- **Unchanged:** no OBO on any collaboration plane; app-only Dataverse and SPE; plane selection from validated `iss`/`tid`;
```

New:

```markdown
- **Write confirmation for a linked core user** (2026-10-18): a licensed core user resolved through the stored link is confirmed for ADR-034 A2 writes app-only, impersonating their systemuser ([A5 scope extension](#amendment-a5-2026-09-04-workforce-systemuser-root-sets-derive-from-dataverses-impersonated-answer)); their home-directory token is never exchanged.
- **Unchanged:** no OBO on any collaboration plane; app-only Dataverse and SPE; plane selection from validated `iss`/`tid`;
```

## Observation for the main session (no edit proposed)

ADR-028 A5 ("No OBO is sanctioned anywhere by A5") and A9 ("no OBO on any collaboration plane") read against ADR-034
A2's OBO probe for Spaarke-tenant callers (`CallerRecordAccessProbe`, WhoAmI + RetrievePrincipalAccess as the caller).
That tension predates decision (a); this change does not widen it. Decision (a) adds no token exchange.

## Option C (owner 2026-10-20) — the edits to apply instead

> Option C makes the app-only, named-principal confirmation the write confirmation of EVERY systemuser on
> `/api/v1/external`, with no OBO on that plane. It resolves the observation above. Anchors are as on this branch after
> the option C round. There is no full ADR-028 under `docs/adr/`, so every ADR-028 change is a concise edit.

- **ADR-034 A1** (line 116): apply as drafted above.
- **ADR-034 A2** (line 93): **superseded** by A2c below.
- **ADR-034 A3** (line 93): apply as drafted above.
- **ADR-034 A4** (line 115): apply with this New text instead:
  `fall back to the membership walk, an app-only read or a constant when Dataverse's answer cannot be read (the app-only, named-principal confirmation is not a fallback: it is the plane's only confirmation, for every systemuser, and a fault on it adds no rights either), or stamp`
- **ADR-034 A2c** (line 93). Old: `` against Dataverse before writing, through the existing `CallerRecordAccessProbe`: adding a child ``
  New: `` against Dataverse before writing, app-only, naming the caller's resolved systemuser with `MSCRMCallerID` impersonation (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyAsync` / `PrincipalHoldsPrivilegeAppOnlyAsync`, ADR-028 A5's mechanism) — for every systemuser, never OBO (task 036 decision a, widened by option C, owner 2026-10-20); a 404 is re-asked on the replication-lag schedule, an impersonation fault is UNKNOWN and adds no rights, and each question is bounded at 15 s: adding a child ``
  and, in the same bullet, Old: `` and `RouteAuthorizationGuardTests.ExternalWrites` (every `` New: `` `ExternalPlaneNoOboGuardTests` (no token-taking probe question in `Api/ExternalAccess/External*.cs`), and `RouteAuthorizationGuardTests.ExternalWrites` (every ``
- **ADR-034 header** (line 26). Old: `` (`CallerRecordAccessProbe`). The membership walk `` New: `` (asked app-only, naming the user's systemuser; never OBO on that plane — task 036 option C). The membership walk ``
- **ADR-028 B1, widened.** Insert as drafted above, with the heading `### Scope extension (2026-10-18, widened 2026-10-20, owner-accepted): confirming a systemuser's write on /api/v1/external`
  and this first paragraph: "A licensed core user's SPA/Teams token is never exchanged on `/api/v1/external` (A1–A3), and
  one resolved through A9's stored home link holds a customer-directory token that could not be exchanged anyway. To
  confirm **every systemuser's** write there (ADR-034 A2) …"; the condition "chosen from the resolved principal
  (`CallerPrincipal.ResolvedThroughHomeLink`) …" becomes "the only confirmation on the plane, for every systemuser
  (`ExternalProjectDataEndpoints.AppOnlyConfirmationPrincipal`), never a fallback after a token exchange"; add the
  conditions "a 404 is re-asked on `CallerRecordAccessProbe.NotFoundRetryDelay`'s schedule; a 403 that is not an access
  answer (e.g. `0x8004A110`) is UNKNOWN and logged as a Warning; each question is bounded at 15 s" and "Enforced by:
  `tests/Spaarke.ArchTests/ExternalPlaneNoOboGuardTests.cs`".
- **ADR-028 A5 deployment prerequisite 3** (after line 370), from the independent critique: "3. **Conditional Access
  targets the BFF API and the SPA client.** On `/api/v1/external` the BFF never exchanges the user's token (A1–A3, and
  the write confirmation above), so a Conditional Access or sign-in-risk policy that targets only Dataverse, Graph or
  SharePoint is never evaluated for SPA/Teams users. Such policies MUST target the BFF API app registration and the SPA
  client, where those users sign in."
- **ADR-028 B2, widened** (line 642): "- **Write confirmation for every core user** (2026-10-18, widened 2026-10-20): a
  licensed core user, Spaarke-tenant or resolved through the stored link, is confirmed for ADR-034 A2 writes app-only,
  impersonating their systemuser ([A5 scope extension](#…)); no token of theirs is exchanged."

## Round 3 (fix round 3, 2026-10-20) — for the main session to apply

> Anchors are the `.claude/adr/` files as applied in `cd258fead`. The full `docs/adr/ADR-034-user-record-membership.md`
> was updated on the branch in the same round (no `.claude/` write needed for it).

- **ADR-034 A2** (`.claude/adr/ADR-034-user-record-membership.md`, line 93) — the guard now covers the plane's bound
  filter and handler files and bans the caller-token read (F3-1).
  Old: `` `ExternalPlaneNoOboGuardTests` (no token-taking probe question in `Api/ExternalAccess/External*.cs`), ``
  New: `` `ExternalPlaneNoOboGuardTests` (no token-taking probe question, OBO exchange or caller-token read in `Api/ExternalAccess/External*.cs` or in any filter or handler file the `/api/v1/external` mappings bind), ``
- **ADR-034 A2** (same bullet) — the impersonation fault (K5).
  Old: `an impersonation fault is UNKNOWN and adds no rights`
  New: `an impersonation fault (including a 0x80040220 whose message names prvActOnBehalfOfAnotherUser) is UNKNOWN and adds no rights`
- **ADR-028 A5 scope extension** (`.claude/adr/ADR-028-spaarke-auth-architecture.md`, lines 376–377) — K5.
  Old: `` a 403 that is not an access answer
  (e.g. `0x8004A110`) is UNKNOWN and logged as a Warning; ``
  New: `` a 403 that is not an access answer
  (e.g. `0x8004A110`, or `0x80040220` whose message names `prvActOnBehalfOfAnotherUser`) is UNKNOWN and logged as a
  Warning; ``
- **No edit needed for K7.** No `.claude/adr/` text cites `ResolvedThroughHomeLink` after `cd258fead`; the property is
  removed. The decision-(a) drafts above (Edits A2, B1) still name it as history and are superseded by the option C
  section.

## Round 4 (fix round 4, 2026-10-20) — for the main session to apply

> Anchors are the `.claude/adr/` files on this branch (after `cd258fead`). The full
> `docs/adr/ADR-034-user-record-membership.md` was updated on the branch in this round (method names, whole-token match,
> the 503).

- **ADR-028 A5 scope extension** (`.claude/adr/ADR-028-spaarke-auth-architecture.md`, lines 374–378) — F1 (a fault is
  not a denial) and K3 (whole-token match; the JIT sync keeps the grant).
  Old: `` - fail closed: a fault, a missing systemuser id or an unreadable answer adds no rights (both `Spaarke.Dataverse` calls
  refuse `Guid.Empty`);
- a 404 is re-asked on `CallerRecordAccessProbe.NotFoundRetryDelay`'s schedule; a 403 that is not an access answer
  (e.g. `0x8004A110`, or `0x80040220` whose message names `prvActOnBehalfOfAnotherUser`) is UNKNOWN and logged as a
  Warning; each question is bounded at 15 s; ``
  New: `` - fail closed, and a fault is not a denial: a missing systemuser id adds no rights (both `Spaarke.Dataverse` calls
  refuse `Guid.Empty`); a fault, a timeout or an unreadable answer adds no rights either, and a write refused only for
  that reason is a retry-able 503 that writes nothing, never a "no access" 403;
- a 404 is re-asked on `CallerRecordAccessProbe.NotFoundRetryDelay`'s schedule; a 403 that is not an access answer
  (e.g. `0x8004A110`, or `0x80040220` whose message names `prvActOnBehalfOfAnotherUser` as a whole token) is UNKNOWN
  and logged as a Warning — the same reading keeps a JIT container grant in `SpeContainerMembershipSync` for that run
  rather than removing it (task 171); each question is bounded at 15 s; ``
- **ADR-034 A2** (`.claude/adr/ADR-034-user-record-membership.md`, line 93) — the method names and F1.
  Old: `` (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyAsync` / `PrincipalHoldsPrivilegeAppOnlyAsync`, ADR-028 A5's mechanism) ``
  New: `` (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync` / `PrincipalHoldsPrivilegeAppOnlyOrUnknownAsync`, ADR-028 A5's mechanism) ``
  and, in the same bullet,
  Old: `an impersonation fault (including a 0x80040220 whose message names prvActOnBehalfOfAnotherUser) is UNKNOWN and adds no rights, and each question is bounded at 15 s:`
  New: `an impersonation fault (including a 0x80040220 whose message names prvActOnBehalfOfAnotherUser as a whole token) is UNKNOWN and adds no rights, each question is bounded at 15 s, and a write refused only because Dataverse gave no answer is a retry-able 503 that writes nothing, never a 403:`
- **ADR-034 A2 fail-closed bullet** (line 94).
  Old: `a failed or absent rights answer adds no rights.`
  New: `a failed or absent rights answer adds no rights (and a write it refuses is a retry-able 503, not a 403).`
