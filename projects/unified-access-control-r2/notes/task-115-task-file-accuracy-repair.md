# Task 115 — task-file accuracy repair (nine files)

> **Executed 2026-09-21.** Nine open task files each carried at least one wrong load-bearing sentence.
> No production code changed. No other POML's `<status>` changed. The nine files stay open — repairing
> a premise is not completing a task.
>
> **Verification**: XML parse 9/9 (real `<?xml?>` PIs) · `Validate-TaskPoml.ps1` **116 scanned, 110
> clean, 0 errors, 6 pre-existing warnings, exit 0** · `check-task-status-drift.ps1` **116 = 116,
> exit 0** · diff touches exactly 9 POMLs + this note · zero `<status>` lines in the diff.

---

## 0. The headline finding: BOTH source notes under-counted, and so did this task's own POML

The task's constraint said to use the notes as a starting inventory and never a closed one. That was
correct, and the margin was larger than expected. **Every file read end-to-end produced more sites
than the notes enumerated**, and in two files the *task 115 POML itself* — written after both notes —
still under-counted.

| File | Plan §2 said | Task 115 POML said | **Actually found** | Missed sites lived in |
|---|---|---|---|---|
| 087 | 3 | 7 (gate, goal, background, dependency, knowledge, step 3, justification) | **9** | `<justification><existing>`, `<notes>` |
| 088 | 4 | 7 | **8** | `<goal>` |
| 089 | 2 | 5 | **5** ✅ | — |
| 036 | 2 | 2 | **2** ✅ | — |
| 054 | 2 regions (`:23`, `:87-89`) | ~6 | **14** | title, prompt, background, 7 steps, outputs, `<file role="modify">`, NFR-02 constraint |
| 055 | (not listed) | 3 | **3** ✅ | — |
| 056 | (omitted entirely) | 4 | **8** | prompt, step 1, escalation, `<justification><extension>` |
| 094 | 1 addition | 1 addition | **1 addition + 2 criteria + 1 step** | — |
| 095 | 1 (the 3-claim sentence) | 2 | **2** ✅ | — |

**Two sites were found only by the residual-reference grep, after I believed the file was done** —
087's `<notes>` and 088's `<goal>`. A third pair (054's `<file role="modify">` and its NFR-02
constraint) was found only by a follow-up semantic grep for `fourth root` *after* the mechanical pass
and after both gate scripts were already green. **The expected-occurrence discipline is what caught
all four**; a read-and-patch pass would have shipped them, and both gate scripts were blind to them.

> **Lesson for the next audit**: a count is a floor, never a ceiling — including a count written by
> the task that is doing the fixing. And a green validator/drift pair proves *schema* and *status*
> agreement, not *claim* agreement. Nothing in the toolchain can see a stale sentence.

---

## 1. Per-file record

### 036 — a cited file that does not exist

| | |
|---|---|
| **Wrong** | `<file role="modify">src/server/api/Sprk.Bff.Api/appsettings.json` and step 1 "document both states in appsettings.json comments" |
| **Corrected to** | `appsettings.template.json`, documented via the file's existing `_ExternalAccess_comment` sibling-key convention |
| **Source that settled it** | Directory listing at HEAD. The five files that exist: `appsettings.template.json`, `appsettings.Testing.json`, `appsettings.Development.json.template`, `appsettings.Production.json.template`, `appsettings.tokens.md`. **No `appsettings.json`.** |

The template already carries an `ExternalAccess` section (`:66`) documented by `_ExternalAccess_comment`
(`:65`) — so the repair had a real home rather than needing invention. Note the incidental correction:
JSON has no comment syntax, so "appsettings.json comments" was doubly wrong.

⚠️ Tasks **041** and **061** also cite the nonexistent `appsettings.json`. Both are ✅ done —
historical, out of scope, recorded here so the next audit does not re-discover them as new.

### 054 — the retired premise, live in every binding element

| | |
|---|---|
| **Wrong** | Title, prompt, goal, background, all 7 steps, 3 of 6 acceptance criteria, an output, a `<file role="modify">` and the NFR-02 constraint all asserted an accessible-service-request root set with the grant allow-list extended to `sprk_servicerequest` |
| **Corrected to** | The task-028 ruling in every binding element: a service request **IS core** but is **NEVER externally grantable**; `CallerPrincipal` composes exactly **three** externally-grantable root sets; the remaining in-scope work is ISS-003 (#964) |
| **Source that settled it** | `notes/task-028-service-request-root.md` §2 (owner ruling 2026-09-09, "Binding. Do not re-litigate.") + §5 (the refusal rationale) + project `CLAUDE.md` |

**This was the most dangerous file in the set.** The amendment existed only in `<notes>` at the
bottom, so an executor reading top-down would have built the refused thing and met the correction
last — after the work. The amendment is now promoted into every binding element, and the `<notes>`
block records *why* a note could not bind.

**Scope NOT improvised** (task 115 escalation trigger 2 respected): ISS-003's product question — what
a requester may DO to their own service request's to-dos — is **still open and still the owner's**.
Step 0 is now a hard gate that stops on it; step 1 requires transcribing the owner's answer into the
acceptance criterion rather than inventing a verb set. Three **negative** criteria were added that
hold *whatever* the answer turns out to be:

1. no `AccessibleServiceRequestIds` set exists (grep clean);
2. `GrantSupportedRootEntities` still exactly `{ sprk_project, sprk_matter, sprk_workassignment }`;
3. a CIAM partner still resolves an empty service-request set **server-side**.

Also added: a `<constraint source="owner 2026-09-09">` quoting the ruling verbatim, and an escalation
trigger that fires on any attempt to add a service-request lookup to `sprk_externalrecordaccess`.
The task-115 constraint against overcorrecting was honored — the file says a service request **is**
core, and cites the grant table's deliberate omission as the evidence for the distinction.

### 055 — inherited the premise in gate, dependency and step 1

| | |
|---|---|
| **Wrong** | `<gate>` "requires 054 (fourth root set)"; `<dependency task="054">` "Fourth root set (service request) so inheritance covers all four core classes"; step 1 reading a `servicerequest` stamp into the inherited term |
| **Corrected to** | The inheritance term covers the **three** externally-grantable roots. The `sprk_regardingservicerequest` / `sprk_relatedservicerequest` stamp columns **do exist**, but there is no principal-side set to resolve them against — so a child parented *only* to a service request has no inherited term and is denied |
| **Source that settled it** | `task-028-service-request-root.md` §1 (a child can be parented to a service request **seven ways**) + §6 (the resulting over-denial IS ISS-003) |

The precise distinction matters and is now stated in the file: **the stamp column exists; the
resolvable set does not.** Step 1 previously implied both existed. The over-denial is named, attributed
to task 054, and explicitly *not* closed here.

### 056 — the premise in prompt, goal, background, constraint, two steps, escalation and justification

| | |
|---|---|
| **Wrong** | "scope dimensions over the FOUR core-ancestor stamp columns"; goal listing `sprk_regardingservicerequest` as a dim; background "(054 added the fourth)"; constraint "ONLY the four composed core root sets"; step 1 "which of the four"; step 2 "including the service-request dim where the column exists (054's root set)"; escalation "lacks ALL four"; justification "054/055 supplied the model generalization" |
| **Corrected to** | **Three** externally-grantable dims. A `sprk_regardingservicerequest` dim is now explicitly **FORBIDDEN even where the column exists**, with the 06 §F1 reason stated (a ScopeDimension can only point at a set that exists on the principal) |
| **Source that settled it** | Same ruling; plus `ExternalAccessModule.cs` — the shipped `service-requests` module is the correct, separate mechanism |

The escalation trigger was sharpened: a service-request column alone no longer counts as a usable
stamp target, because it cannot be a dim. Without that, the trigger would have failed to fire on an
entity that has *only* a service-request column.

### 087 / 088 / 089 — systematic pre-renumbering references

The real Phase-5 chain is **086** (table) → **087** (append hooks) → **088** (versioning + replay) →
**089** (seam tests). The files cited **080 / 081 / 082**, which are **not vacant**:

| Cited | Actually is |
|---|---|
| 080 | cross-record search authorization |
| 081 | tenant-container-resolver diagnostic |
| **082** | **the LIVE caller-identity primitive census** |

So **089's `<gate>` blocked on real, unrelated work** — the single most actionable defect in the set.
Each file's `<deps>` element was *already* correct, so each file contradicted itself; TASK-INDEX row
086 corroborates independently.

- **087**: 9 sites, `080`→`086` (7) and `082`→`088` (2) — gate, goal, background, dependency,
  knowledge file, step 3, justification `<existing>`, justification `<cost-of-doing-nothing>` (both
  numbers in one sentence), `<notes>`.
- **088**: 8 sites, `081`→`087` — gate, prompt, goal, background, canonical-reference, dependency,
  constraint, step 1, justification `<existing>`.
- **089**: 5 sites — `<gate>` now "requires 087 + 088"; background "unit-level replay tests live in
  088"; both `<dependency>` elements; the escalation trigger.

**Residual greps all return zero**: no `080`/`082` in 087, no `081` in 088, no `081`/`082` in 089.
**Legitimate references survived untouched** — `<deps>` reads `086, 060, 063, 064, 106` / `087, 032,
036, 043, 055, 064` / `087, 088` respectively, and the `06 §F4` / `06 §F1` investigation-note citations
were correctly *not* matched by a `081`-style grep.

### 094 — accurate as written; needed an addition that contradicts its own hint

| | |
|---|---|
| **Wrong** | Not false, but dangerously incomplete: *"Check `DriveItemOperations.ListChildrenAsync` for reuse BEFORE adding an endpoint"* reads as though reuse-as-is were the preferred answer |
| **Corrected to** | The probe **MUST be record-keyed `(entity, recordId, name)`** with the container resolved server-side. `ListChildrenAsync` may be reused **internally, behind a record-keyed seam** — never as the client-facing shape |
| **Source that settled it** | Verified signature `ListChildrenAsync(string driveId, string? itemId = null, CancellationToken ct = default)` — `DriveItemOperations.cs:40-43`, surfaced via `SpeFileStore.cs:104-108`. **Drive-keyed.** Plus a machine count: `grep -c "^            Provenance.ClientSupplied," tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs` → **0** |

Following the original hint literally would have reintroduced exactly the `ClientSupplied` shape task
083 deleted, and **failed the guard that now pins that allow-list at zero** — correctly. The §11
question is now narrowed to the *seam* (new endpoint vs extending an existing record-keyed one);
the *keying* is settled. Two acceptance criteria added, including one requiring the guard be **run**,
not inspected.

### 095 — wrong on two of three named absences, and the false claim was the argument

| | |
|---|---|
| **Wrong** | *"`sprk_document` has real lookup columns for matter / project / invoice / workassignment / event — but **NOT** for `sprk_todo`, `account` or `contact`"* |
| **Corrected to** | `sprk_relatedtodo` → `sprk_todo` **exists**; `sprk_relatedcontact` → `contact` **exists**; **`account` is the only genuine absence** |
| **Source that settled it** | `DocumentLinkFields.All`, `Spaarke.Dataverse/Models.cs:198-217` (live-metadata-verified against `spaarkedev1`, 2026-09-05), corroborated by the `ContactLookup` doc comment at `:404-409`: *"`sprk_document` has no account lookup in either family."* |

**This mattered more than a typo** because the false claim *was the stated argument for the
intersection entity's shape*. The argument had to be rebuilt, not just corrected — and the replacement
is stronger: the lookup family is **16 columns across two naming families with non-uniform casing**,
where three of the twelve `sprk_related*` schema names break the `$"sprk_Related{type}"` convention
(`sprk_relatedmatter`, `sprk_relatedproject`, `sprk_relatedvendororg` are lowercase). Adding an
association means adding a seventeenth column *and remembering its casing*. That is the real case
against piecemeal lookups.

**Second correction in the same file**: Document→WorkAssignment was described as having **one**
Many-to-one relationship. It is a **pair** — `sprk_workassignment` + `sprk_relatedworkassignment`
(`Models.cs:206-207`) — matching the two-slots-per-type pattern the same block describes for matter
and project. The 2026-08-31 screenshot summary named relationship names and missed the second; the
lookup enumeration is ground truth.

---

## 2. Out of scope, by decision not oversight

Tasks **082** and **093** were **not** touched. Both need judgment, not a sentence patch: 093 has four
false premises and closes as delivered; 082 rescopes to a single CLAUDE.md §11 decision memo. A
mechanical pass over either would produce a file that reads consistently and still describes the wrong
work — the exact failure mode this task exists to remove. They are sequenced in the 2026-09-18 plan's
wave 3.

---

## 3. What this project's premise-rot counter now reads

The project's own count of *"a task file was wrong and the code was right"* stood at **19** before this
pass. Nine more files are now reconciled. **The code won every single time, again** — in all nine
files, without exception, the artifact that needed changing was the task file.

Three of the nine share the project's signature shape: **a document generalized across a boundary the
code never crossed.**

- **054/055/056** — "core" was true of a service request in one sense (nothing else confers access to
  it) and false in another (an external contact can be granted it). The doc used one word for both,
  three task files inherited it, and the tell cost one metadata query: **the grant table's own column
  list**. When a task says "add the fourth X", check first whether the data model has a place to put it.
- **095** — a confident three-item absence list where two items were present.
- **094** — a hint that pointed at a real method without checking what that method is keyed on.

And **087/088/089** are a fourth shape worth naming: a **renumbering that updated `<deps>` but not
prose**, leaving each file contradicting itself, with one of the stale numbers pointing at live
unrelated work.
