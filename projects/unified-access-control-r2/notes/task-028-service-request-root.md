# Task 028 — service request is core, but never externally grantable

> **Closed 2026-09-09 as a DOC CORRECTION, not a code build-out.** The escalation trigger fired, the
> owner answered, and the answer inverted the task.

---

## 1. What the task assumed, and what is actually true

The POML's premise: *"This project's model names FOUR core record types … but `CallerPrincipal`
carries accessible sets for only three. Service request is missing … Add the fourth root by the same
mechanism as the other three."*

**That would have been the wrong change.** Its escalation trigger — *"If service-request grants turn
out not to exist in the grant model at all (no lookup on `sprk_externalrecordaccess`), STOP and
escalate: whether service requests are grantable is a product decision, and the answer may be that
the model doc is wrong rather than the code"* — fired on the first metadata query, and the model doc
was indeed the thing that was wrong.

### Live evidence (spaarkedev1, 2026-09-09)

| Question | Answer |
|---|---|
| Does `sprk_servicerequest` exist? | **Yes** — set `sprk_servicerequests`, primary name `sprk_name` |
| Can a child be parented to one? | **Yes, seven ways** — `sprk_regardingservicerequest` on `sprk_todo`, `sprk_event`, `sprk_memo`, `sprk_analysis`, `sprk_communication`, `sprk_communicationthread`; `sprk_relatedservicerequest` on `sprk_document` |
| Does the grant table have a service-request lookup? | 🔴 **No.** `sprk_externalrecordaccess` carries `sprk_project`, `sprk_matter`, `sprk_workassignment`, `sprk_invoice`, `sprk_organization`, `sprk_contact` — and no service request |
| How many service requests exist in dev? | **Zero** (`sprk_servicerequests?$top=3` → 0 rows) |

So a child can *point at* a service request, but no external grant can ever *name* one.

## 2. 🔴 Owner decision (2026-09-09)

> *"Service requests are internally submitted records; they are sent via the SPA by internal
> workforce users; the service request is not accessible by external users (e.g., law firms)."*

**Binding. Do not re-litigate.**

## 3. What this means — "core" and "externally grantable" are two different lists

The model doc collapsed two distinct properties into one word:

| Sense of "core" | Service request |
|---|---|
| Nothing else confers access to it — it is not a child that inherits via an ancestor | ✅ **Core** |
| An external contact can be granted it | ❌ **Never** |

The **code was already right** in both halves. The **doc** said *"core (project, matter, work
assignment, service request) need direct grants"*, which reads as though all four are grantable to
externals. That sentence is the entire defect. Corrected in `CLAUDE.md`, `design.md` §4.3 (the
single Core row is now split into *Core* and *Core, internal-only*), and `spec.md` (taxonomy row +
FR-08's "fourth root" clause).

### Supporting evidence that the two lists were never the same

The grant table also carries **`sprk_invoice`** (a *child* in this model, which inherits one hop) and
**`sprk_organization`** (which drives the org-expansion term). Neither is an accessible root set on
`CallerPrincipal`. So "grantable type" and "accessible root" have always been different concepts, in
both directions — the service-request gap was the only one anyone mistook for a bug.

## 4. Service-request scoping ALREADY EXISTS — by the correct, different mechanism

Found in `ExternalAccessModule.cs`, shipped by **`spaarke-SPA-external-access-platform-r2` #028 on
2026-08-10** (`b8abd51b5` server, `e5cc39cce` client). Its task number is the same as ours by pure
coincidence; different project.

```csharp
services.AddExternalModule(new ExternalModuleDescriptor
{
    Name = "service-requests",
    RecordEntity = "sprk_servicerequest",
    RecordIdAttribute = "sprk_requestedby",
    AccessibleRecordIds = p =>
        p.Plane == CallerPrincipalPlane.Workforce && p.ContactId != Guid.Empty
            ? new HashSet<Guid> { p.ContactId }   // the caller's OWN submitted requests
            : EmptyRecordIds,                     // any partner caller: zero rows
});
```

Note what makes this **right rather than merely present**:

- It scopes by **requester**, not by grant — the correct model for an internally-submitted record.
- It is **fail-closed on the plane**: a CIAM partner gets an empty set *server-side*, so
  "internal-only" does not depend on the client hiding the tab. `ServiceRequestsWidget.tsx` is also
  registered `planes: ['workforce']` — defence in depth, in that order.

**This satisfies the POML's acceptance criterion 2** ("a ScopeDimension exists for
`sprk_servicerequest`, so child scoping can use it") — by a module, which is the same mechanism.

## 5. What was deliberately NOT built, and why

**`AccessibleServiceRequestIds` on `CallerPrincipal` — refused.** It would be composed from
`sprk_externalrecordaccess` rows that cannot exist, producing an **always-empty set**: code that
looks like a fix, denies exactly as much as today, and encodes into the principal surface the false
claim that service requests are externally grantable. The next reader would then "complete" the model
by adding the missing lookup to the grant table — the exact outcome the owner decision forbids.

This is the CLAUDE.md §11 cost-of-doing-nothing test failing in reverse: there is no concrete
behaviour that fails without it.

## 6. Residual — filed, not fixed (ISS-003)

`/api/v1/external/todos/{id}` serves **both** planes (CIAM contact *and* workforce user, per
`teams-app-r1` task 025). `GetTodoRootAsync` has no service-request arm, so a to-do parented to a
service request resolves to `None` and is denied **for everyone** — including a workforce user who
submitted that very service request and can see it in their own widget. A genuine over-denial.

**Not fixed here, for two reasons that are about accuracy, not effort:**

1. **It needs a different rights shape.** The other three roots answer "is this id in my set?" from
   `CallerPrincipal` synchronously. A service request answers "is `sprk_requestedby` me?", which is a
   Dataverse read the to-do path does not currently make. Bolting it into `RightsForRoot` would put
   an I/O call behind a signature that is pure everywhere else.
2. **What may a requester DO to their own service request's to-dos?** Read? Write? Create? That is a
   product question with no current answer, and inventing one to close a task is how the level
   asymmetry got into task 009 in the first place.

**Currently unexercised**: zero service requests exist in dev, and no to-do can be parented to one
that does not exist.

## 7. §11 justification — for the record

| Question | Answer |
|---|---|
| **Existing** | `ExternalAccessModule.cs:270-279` — the `service-requests` module, live since 2026-08-10. Verified by reading the registration, not by trusting its comment. |
| **Extension** | Nothing to extend. The mechanism exists and is correct. |
| **Cost of doing nothing** | **None for the main deliverable** — which is precisely why it was not built. The only real cost is the residual in §6, filed as ISS-003. |

## 8. Lesson

Three of the five stale-premise findings in this project now share one shape: **a document
generalised across a boundary the code never crossed.** Here, "core" was true of service request in
one sense and false in another, and the doc used the word for both. The task built on the doc; the
code disagreed; the code was right.

The tell was cheap to spot and cost one metadata query: **the grant table's own column list**. When a
task says "add the fourth X", check first whether the data model has a place to put it.
