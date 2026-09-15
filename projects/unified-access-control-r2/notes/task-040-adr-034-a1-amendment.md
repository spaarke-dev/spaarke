# Task 040 — ADR-034 Amendment A1: the live-consumer check, and three staleness fixes verified in source

> **Completed** 2026-09-04. Path **B**. Outputs: both ADR-034 versions + `.claude/CHANGELOG.md`.

---

## 1. The escalation trigger was evaluated and did NOT fire

Trigger: *"If codifying the per-surface split reveals a LIVE consumer that depends on unfiltered
systemuser-plane descriptors **as an access answer** (beyond `AccessibleRecordSetService`, which Phase 1
already rehomes), STOP and escalate."*

Every consumer of `IMembershipResolverService` was enumerated and classified before the amendment was
written — because narrowing a surface is only safe if you know who is standing on it:

| Consumer | Surface | Access answer? |
|---|---|---|
| `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` | Authorization | **Yes** — the one the trigger excludes; Phase 1 rehomes it |
| `Api/Membership/MembershipEndpoints.cs` | Scoping | **No** — returns the **caller's own** memberships under OBO. A self-query, not a decision about another principal |
| `Services/Ai/Narrators/DailyBriefingCollector.cs`, `Services/Workspace/BriefingService.cs` | AI scoping | No |
| `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs`, `Services/Ai/NodeService.cs` | AI scoping (playbook node) | No |
| `Services/Communication/Access/IThreadPrivateGrantProvider.cs` | — | **Not a consumer.** The grep hit is a **doc-comment cross-reference**; there is no code dependency (verified: zero non-comment `IMembershipResolverService` / `_membership.` references anywhere under `Services/Communication/`) |

`MembershipEndpoints` is the one that could be misclassified in a hurry: it *is* a membership answer
served over HTTP, so it looks like an access surface. It is not — the endpoint is OBO and answers only
about the caller themselves, so an over-inclusive answer discloses nothing the caller may not already
see. Recorded because the next reader will have the same moment of doubt.

**Conclusion**: no consumer outside `AccessibleRecordSetService` uses these descriptors as an access
answer. The narrowing changes no other surface's behaviour contract, so the amendment stayed as scoped.

## 2. Three staleness items — each verified in source, not taken from the investigation note

Investigation 02 §10.4 listed them; they were re-derived from code before being written into the ADR.

| Item | Verified at | What the ADR said | What is true |
|---|---|---|---|
| `ResolveByContactAsync` missing from Key Types | `Services/Ai/Membership/IMembershipResolverService.cs:104` | Interface shown with `ResolveAsync` only | There are **two** planes. Showing only the systemuser overload implies the **contact plane has no membership path at all** — the opposite of the truth, on the plane external users live on |
| `MembershipResponse.RelatedByRole` missing | `MembershipResolverService.cs:332` | Record ended at `ContinuationToken` | `RelatedByRole` carries the 1-hop transitive results; null on paths that do not compute it |
| Contact resolution documented as AAD-oid only | `WorkforcePrincipalResolver.cs:121`, `AccessibleRecordSetService.cs:393` | *"cross-referenced via `azureactivedirectoryobjectid`"* | **`systemuser.sprk_primarycontact` is PRIMARY**; the AAD cross-ref is the **fallback**. The ADR documented the fallback as if it were the only path |

The third is the one that would have cost time: an implementer following the ADR would have built the
fallback and wondered why the primary link was being ignored.

## 3. What was deliberately NOT amended, and why that is a finding rather than an omission

- **The 1-hop cap (M7 / N4).** It needs **no exception** — FR-26 denormalizes the core ancestor onto
  each child, so every child→core chain is **one hop by construction**. This is worth stating out loud:
  the natural instinct on meeting a cap that blocks a requirement is to seek an exception to it. Here
  the data model removed the need entirely, so the rule stands untouched. An exception would have been
  a real weakening bought for nothing.
- **M8 / M9 event semantics**, **N2 non-existent-entity ban**, **M1 canonical resolver** — unchanged.
  N2 carries one precision: the ban is on joining through entities that **do not exist**
  (`sprk_matterteammember`). Real `teammembership` **is** legitimately used, and always was — several
  project docs state the ban more broadly than the ADR ever did.

Verified by diff: only **three** lines were removed from the concise ADR, and all three are the
staleness fixes in §2. No MUST/MUST NOT was deleted or weakened.

## 4. What this unblocks

Task **041** (the access-conferring column registry, contact **+ org**) can now land under an ADR that
sanctions it. Task **043** consumes the same registry.
