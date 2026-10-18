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
