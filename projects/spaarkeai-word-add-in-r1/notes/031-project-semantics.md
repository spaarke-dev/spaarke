# Task 031: Project creation semantics for r1

> **Task**: `tasks/031-creation-service-project.poml` (FR-13, Project half)
> **Date**: 2026-09-17
> **Author**: task-execute sub-agent (opus / high)
> **Status of the POML's blocking question**: **ANSWERED by the owner on 2026-09-17** — this note records the
> answer, it does not make the call. It was written **before** the implementation commit (POML AC1).

---

## 1. The two as-built facts the task rests on

### (a) `sprk_projectnumber` IS the `sprk_project` primary name attribute

`GET EntityDefinitions(LogicalName='sprk_project')?$select=LogicalName,PrimaryNameAttribute,PrimaryIdAttribute`
returned `"PrimaryNameAttribute":"sprk_projectnumber"`.

**Evidence and its provenance.** The Dataverse MCP server was **down in this session** (connection timeout), so I did
not re-run the call. I am citing task 030's live verification against `spaarkedev1` on 2026-09-11, recorded in
[`030-numbering-handoff.md`](030-numbering-handoff.md) §3 (the 🔴 row) and §6, and again in
[`030-creation-service-decisions.md`](030-creation-service-decisions.md) §2. That task made the same call for both
entities in one pass: `sprk_matter` → `sprk_matternumber`, `sprk_project` → `sprk_projectnumber`.

Corroborating code, independent of that call: `PolymorphicResolverService.ts` documents Matter's primary name column
as `sprk_matternumber`, the same asymmetry in the same shape.

**Consequence**: a Project created with no number has a **blank display name** in every lookup, grid, picker and view
that renders the primary name.

### (b) `projectService.ts` generates no number — none at all

Read end to end this session:
`src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts`. `createProject`
builds its payload from `sprk_projectname`, optional `sprk_projectdescription`, optional `sprk_issecure`, the BU
cascade, five lookup bindings and the Field Mapping Framework. **`sprk_projectnumber` appears nowhere in the file.**

This is the one real asymmetry with Matter: `matterService.ts:255-273` has a client-side `{typeCode}-{random6}`
generator, and Project has no equivalent to lift onto the server. (An earlier version of the POML said the asymmetry
was that `sprk_matternumber` is "a secondary reference field" — that was false and is corrected in the POML body.)

---

## 2. The chosen r1 semantics: this task writes NO number

**Owner decision, 2026-09-17** (project CLAUDE.md Decisions; POML `<owner-decisions>` `no-numbering-here`):

> Project numbering is a **SEPARATE project** and is **NOT a dependency** of this one — a Project may be created with
> no number. Task 031 MUST NOT generate or write `sprk_projectnumber`: not a generated token, not a name-derived
> value, nothing. Leave it to whatever the user supplied (which may be empty) and let the numbering project fill it
> later.

**A pane-created Project showing a blank name until the numbering project ships is EXPECTED, not a defect.**

### Rationale (why this is the right call, not merely the instructed one)

1. **It is the same call the owner already made for Matter** on 2026-09-11, for the same reason: numbering must be
   server-side and **triggered on create**, so that it covers every create path (two wizards, the Office pane, the AI
   chat create tool, MDA forms, imports) rather than being re-implemented per caller. See
   [`030-numbering-handoff.md`](030-numbering-handoff.md) §4. Solving it in this request path would pre-empt that
   component and guarantee a second place to fix later.
2. **The alternative the POML feared is the actively harmful one.** Generating `{typeCode}-{6 digits}` for Project
   would write a machine token into the Project's *display name* — a user-visible regression, not a fix. Deriving the
   number from the typed name would put the same string in two columns and silently make `sprk_projectnumber` mean
   something different for pane-created Projects than for every other Project.
3. **A blank primary name is recoverable; a wrong one is not.** Nothing overwrites a value that was never written, so
   the numbering component can backfill cleanly. A wrong token would have to be detected and undone.

### What this means in code

`sprk_projectnumber` joins `ownerid` and `sprk_containerid` in the **protected-attribute set for the Project path** —
never written directly, and never writable by a field-mapping rule of any type (Copy / Default / Concat / Template),
in any casing or padding. This mirrors exactly what task 030 did for `sprk_matternumber`, and it is what makes the
"MUST NOT write it — nothing" instruction actually hold: without it, an admin-authored profile rule targeting
`sprk_projectnumber` would be a back door straight through the decision.

The protected sets are **per entity**, not shared: Matter keeps `{sprk_matternumber, ownerid, sprk_containerid}`
unchanged, Project gets `{sprk_projectnumber, ownerid, sprk_containerid}`. A single merged set would have changed
Matter's behavior (a rule targeting `sprk_projectnumber` on a Matter create would newly be skipped-with-warning),
which POML AC7 forbids.

### The POML escalation trigger is NOT fired

The trigger reads: *"if the evidence does not support a single defensible semantics — STOP and escalate … let the
owner choose."* The owner chose, in advance, on 2026-09-17. Firing it would be asking a question that has been
answered. The second trigger (live metadata contradicting either premise) is also not fired: nothing contradicts
them — see §1 on why the premise is cited rather than re-measured.

---

## 3. What task 031 therefore implements — the half that was never in doubt

Symmetric with Matter, reusing task 030's implementation unchanged:

| Behaviour | Matter (task 030) | Project (task 031) |
|---|---|---|
| Name / description | `sprk_mattername` / `sprk_matterdescription` | `sprk_projectname` / `sprk_projectdescription` |
| Number | never written | **never written** (§2) |
| Owner `ownerid` | load-bearing — unresolved caller → 403, no row | **load-bearing** (§4) |
| BU defaults `sprk_searchindexname` + `sprk_ai_search_index` | ✅ | ✅ (§5) |
| `sprk_containerid` | never written (task 076 W1) | never written |
| Field Mapping Framework | ✅ one profile read, one source read | ✅ same code path |
| Mapping blanks the name | reverted to the requested name + warning | same |
| Type lookup | `sprk_mattertype` when supplied and verified | **not in scope** (§6) |

## 4. Owner posture: Project becomes load-bearing, like Matter

Task 030 left Project and Invoice on the old best-effort posture and wrote (`030-creation-service-decisions.md` §8)
that **task 031 decides Project**. This task decides: **load-bearing**. An unresolved caller is refused with 403
`owner_unresolved` and **no row is written**.

- POML AC2 requires the created row to have a **non-null `ownerid`**. Best-effort cannot guarantee that.
- Best-effort *is the defect FR-13 exists to fix*: a silently app-owned record is exactly the UAT incompleteness the
  FR names. Shipping Project with the posture Matter was just moved off would re-introduce it in the other half.
- The refusal is safe: it happens **before** any write, so the failure mode is "nothing created, told why" rather
  than "created, owned by nobody the user recognises".

**Invoice is untouched** and keeps the best-effort posture on the generic path.

## 5. Business-unit defaults are valid for `sprk_project` — evidence

MCP being down, this is established from code rather than a live metadata call:

- `SearchIndexNameResolver.cs:105` names `sprk_ai_search_index` as the *"lookup on source entities
  (Matter/**Project**/Doc/...)"*, and its resolver chain walks a parent record's `sprk_ai_search_index` (§31-36).
- `projectService.ts` calls `EntityCreationService.applyUserBuDefaults` on the `sprk_project` create payload (which
  sets `sprk_searchindexname`, INV-5 guarded) and binds `sprk_aisearchindexes(<guid>)` onto the new Project under
  the comment *"Phase G: cascade BU's `sprk_ai_search_index` lookup onto the new Project"*.

Both fields are therefore valid on `sprk_project` for create, and the server path mirrors the client wizard. If a
future live check contradicts this, the failure mode is already non-fatal: `ApplyBusinessUnitDefaultsAsync` catches,
warns and still creates.

## 6. Deliberate non-additions (CLAUDE.md §11 — new surface needs justification)

- **No `projectTypeId`.** `sprk_projecttype_ref` exists and the wizard offers it, but `QuickCreateRequest` has no such
  field, the pane does not send one, and the owner's re-scope lists owner / BU / field-mapping / routing / contract
  test only. Adding a request field, validation and an existence read would be new surface this task was not asked
  for. The Matter type field exists because the owner explicitly required it for Matter on 2026-09-11.
- **No `sprk_issecure`.** The wizard sets it from a user choice the pane does not offer. It is deliberately **not**
  protected from field-mapping rules either — the client engine lets a profile write it, and the server matches the
  client. It is a one-way "more secure" flag set from trusted admin config, so matching is safe.
- **No new DI registration** (ADR-010): this extends the `RecordCreationService` that `OfficeModule` already
  registers. No interface was introduced — task 030 already recorded that ADR-010 path-C deviation from the POML's
  `IRecordCreationService.cs`.
- **No membership event for Project.** `QuickCreateAsync`'s fire-and-forget `MembershipChangedEvent` is Matter-only
  (R3 task 081; event-source-inventory §3A names the quick-create matter endpoint as the only BFF-side `sprk_matter`
  write path). Project already set `ownerid` best-effort on the generic path and published nothing; making the owner
  load-bearing does not change whether an event is owed. Out of scope — flagged, not fixed.

## 7. Residuals inherited from task 030 (unchanged, not re-litigated here)

`030-creation-service-decisions.md` §9 lists them; the ones that now also apply to Project: field-level security on
the mapping source (app-only read after a record-level Read check), the caller's Create privilege not being probed,
and Default-rule CLR typing. None is newly introduced by this task.
