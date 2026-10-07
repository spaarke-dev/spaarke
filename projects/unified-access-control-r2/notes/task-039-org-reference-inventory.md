# Task 039 step 1 — org-typed lookup inventory on the three root entities

> **Enumerated from LIVE metadata** (`spaarkedev1`, 2026-09-04), not from memory or from a written list.
> Feeds **task 039** (the deny veto's record→referenced-organizations resolution) **and task 041**
> (access-conferring registry), per the 039 POML's `<notes>`.

---

## 1. The inventory — lookups targeting `sprk_organization`

**All three roots are uniform.** Each carries exactly two, with identical logical names:

| Root entity | Org-typed lookups (→ `sprk_organization`) |
|---|---|
| `sprk_project` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |
| `sprk_matter` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |
| `sprk_workassignment` | `sprk_assignedlawfirm1`, `sprk_assignedlawfirm2` |

Uniformity is convenient but **must not be assumed forward** — derive the list from metadata
(`Related table : sprk_organization`), never from this table. A fourth root (task 028's
`sprk_servicerequest`) has not been enumerated here and must be added before it becomes a candidate type.

### `$select` for the batched resolution (NFR-02: one read per root entity type)

```
_sprk_assignedlawfirm1_value,_sprk_assignedlawfirm2_value
```

Mirror `ExternalParticipationService.GetRootRecordFlagsAsync`'s shape (chunked OR-filter of ids,
`FlagQueryChunkSize = 50`), which is the cited pattern and already proven at this exact clause count.

---

## 2. 🔴 THE GAP — `sprk_externalaccount` points at `account`, which the deny list cannot name

| Root entity | Lookup | Target table |
|---|---|---|
| `sprk_matter` | `sprk_externalaccount` | **`account`** — NOT `sprk_organization` |
| `sprk_project` | `sprk_externalaccount` | **`account`** — NOT `sprk_organization` |
| `sprk_workassignment` | *(absent)* | — |

Spec **FR-23** says the record-side match uses *"EVERY organization referenced by the record (any
org-typed lookup)"*, deliberately broader than FR-24's conferral registry. But the deny store's object
side is `_sprk_objectorganization_value`, a lookup to **`sprk_organization`**. An organization modeled as
an `account` row therefore **cannot be named as a deny object at all** — the ethical wall cannot reach it,
and no amount of over-matching on the record side fixes that, because there is no entry shape to match.

**Not a 039 blocker.** 039 implements the `sprk_organization` match, which is what the store supports and
what FR-23's acceptance criteria are written against. The gap is that a counterparty filed via
`sprk_externalaccount` is unwallable.

🔔 **Owner decision (new, filed here — pairs with decision A).** Options:
- **(a) Accept** — the ethical wall covers `sprk_organization` counterparties only; document the limit.
- **(b) Widen the store** — add an object-side `account` lookup to `sprk_noaccessentry` (schema change ⇒
  operator step under the 2026-09-04 code+docs-only directive).
- **(c) Retire `sprk_externalaccount`** in favour of `sprk_organization` — consistent with decision A,
  which just dropped `account` as a document-association type for the same "two tables for one concept"
  reason. Note `sprk_relatedorganization`/`sprk_relatedvendororg` already exist on `sprk_document`.

**(c) is the coherent direction** given decision A, but it is a data-migration call, not a 039 call.

---

## 3. Two observations for neighbouring tasks

**For task 041 (registry).** These same two columns are the org-typed *conferring* candidates. 039's use is
deliberately the opposite polarity — **denial over-matches on purpose**, so the deny resolution must NOT be
narrowed to the FR-24 conferral registry. Keep the two lists separate even though they name the same
columns today; they answer different questions and will diverge.

**For 054/055/056 (child inheritance).** `sprk_workassignment` DOES carry the
`sprk_regarding{core}` convention — `sprk_regardingmatter`, `sprk_regardingproject`,
`sprk_regardinginvoice`, `sprk_regardingevent`, `sprk_regardingcommunication`. So
[`CoreAncestorResolver.cs:96-99`](../../../src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs#L96-L99)'s
hard-coded `(core → "sprk_regarding{core}")` map is **correct for work assignment and wrong for
`sprk_document`**, which uses the `sprk_matter`/`sprk_relatedmatter` vocabulary instead. The map is not
uniformly wrong — it is right for some children and wrong for others, which is exactly why a single
convention-derived string is the wrong instrument. See `current-task.md` § "The two lookup families".
