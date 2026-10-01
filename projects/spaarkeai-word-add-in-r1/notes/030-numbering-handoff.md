# Hand-off: server-side record numbering (`sprk_matternumber`) — for the future numbering project

> **From**: `spaarkeai-word-add-in-r1`, task 030 (FR-13), 2026-09-11
> **To**: the separate record-numbering project the owner is setting up
> **Status**: numbering was **removed from task 030** by owner decision (2026-09-11). Nothing in the codebase assigns a matter number server-side today.

## 1. The owner's requirement, as given

- **Server-side, triggered on record create.** The number is assigned when a record is created, whatever created it. It does not need to be shown in the Office add-in.
- **Not a Dataverse plugin.** The policy is **no new plugins**; the Field Mapping architecture records the same constraint. One legacy plugin assembly does exist in the repo, `src/dataverse/plugins/Spaarke.CustomApiProxy` (`GetFilePreviewUrlPlugin`), and root CLAUDE.md §13 still lists a plugins entry point. It is not a precedent for this component.
- **The component does not exist yet.** The main session verified this on 2026-09-11 in the repo, on `origin/master` `e0a6f87c4`, and in dev Dataverse:
  - no autonumber on `sprk_matternumber`
  - no custom API, environment variable, PCF, web resource, custom plugin or flow on `sprk_matter` Create
  - the only non-Microsoft plugin assembly registered in dev is `PreferredSolutionPlugins`

## 2. Required format

**`{typeCode}-{6 digits}`**, e.g. `PAT-191111`, `CMRCL-285858`.

- **Type-code source**: `sprk_mattertype_ref.sprk_mattertypecode`, reached through the matter's `sprk_mattertype` lookup.
- The six digits today are `100000–999999`, from the wizard's `Math.floor(100000 + Math.random() * 900000)`.
- There is no sequence, so the numbers carry no ordering.

## 3. Live facts (read-only Web API GETs against `spaarkedev1`, 2026-09-11)

| Fact | Value |
|---|---|
| Active `sprk_mattertype_ref` codes | 5: `LITG`, `CMRCL`, `PAT`, `TMRK`, `EMPL`, all `^[A-Z]+$` |
| `sprk_matter` rows | 59, all with a number; **0 empty, 0 duplicates** |
| Numbers matching `^[A-Z]+-\d{6}$` | 53 of 59 |
| Legacy / hand-entered values | 6: `asasf`, `REAL-2026-123456.02`, `COPR-2026-123456.01`, `LIT-2025-0847`, `REAL-2026-123456.01`, `Form D - 2023`. All unique. |
| `sprk_matternumber` attribute | String, max length 100, RequiredLevel **Recommended**, **no `AutoNumberFormat`** |
| Alternate keys on `sprk_matter` | **none** (`EntityDefinitions(LogicalName='sprk_matter')/Keys` is empty) |
| `sprk_mattertype` attribute | Lookup to `sprk_mattertype_ref`, RequiredLevel **None** (optional) |
| 🔴 **Primary name attribute** | **`sprk_matter.PrimaryNameAttribute = sprk_matternumber`**, and `sprk_project.PrimaryNameAttribute = sprk_projectnumber` |

**How the primary-name fact was verified.** A read-only `GET EntityDefinitions(LogicalName='sprk_matter')?$select=LogicalName,PrimaryNameAttribute,PrimaryIdAttribute` returned `"PrimaryNameAttribute":"sprk_matternumber"`; the same call for `sprk_project` returned `"sprk_projectnumber"`. The codebase agrees: `PolymorphicResolverService.ts` documents that "Matter's Primary Name column is sprk_matternumber".

**Why it matters.** A matter created without a number has a **blank primary name** in every lookup, grid, picker and view that shows the primary name. The number is also what fills `sprk_regardingrecordnumber` for records filed against a matter: `Spaarke.Dataverse/Models.cs:995` `GetReferenceNumberField` maps `sprk_matter` → `sprk_matternumber`. Its neighbour `GetPrimaryNameField` returns `sprk_mattername`, so that denormalized *display* name is unaffected.

After task 030, every matter created from the Office pane is in this state until the numbering component exists. That is the practical urgency.

Production data was **not** reachable. Uniqueness there is unverified, so re-check it before adding a key (§5).

## 4. Every create path the component must cover

A trigger on create covers all of these, which is why the owner asked for "on create" rather than per-caller code:

| Path | Where | Current numbering |
|---|---|---|
| Create Matter wizard (shared library) | `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts:255-273` | **client-side random**, `${typeCode}-${random6}`, no uniqueness check, only `if (form.matterTypeId)` |
| Legacy LegalWorkspace Create Matter | `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts:187-199` | **client-side random**, same generator |
| Office add-in quick-create | `POST /api/office/quickcreate/matter` → `RecordCreationService` (task 030) | **none**. The server never writes the number, and a field-mapping rule of any type (Copy / Default / Concat / Template, any casing) cannot write it either. |
| AI chat "Dataverse Create Record" tool | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/DataverseCreateRecordHandler.cs` (runs as the user via `IDataverseUserClient`) | none. The tool description lists `sprk_matternumber (text)` as writable, so the model *may* set free text. |
| MDA native form / `MatterHeader` PCF | `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx:176` (`saveText('sprk_matternumber', …)`) | manual entry / edit |
| Imports, integrations | — | whatever the source sends |

**Decisions for the numbering project (flagged, not made here):**
- **Overwrite or fill?** Should the component overwrite a client-supplied number, which is what the wizards send today, or only fill an empty one?
  - "Fill only" keeps manual and legacy values but leaves the two random client generators in charge for wizard-created matters.
  - "Always assign" needs those generators removed. Wizard migration was explicitly deferred past r1 (design.md §7.1).
- **No type at create.** What happens when there is no type code at create time? `sprk_mattertype` is optional. The pane will always send one (the owner's decision; a client task adds a required field), but task 030 deliberately creates a matter even when the type is missing, empty or unknown. Also decide whether a later type change re-numbers the matter.
- **Mechanism.** It must be server-side, triggered on create, and not a plugin. Candidates:
  - a Dataverse webhook / Service Endpoint → BFF or Azure Function (ADR-001 allows Functions for out-of-band work)
  - a Power Automate flow
  - the platform's own autonumber column

  Note that Dataverse autonumber (`AutoNumberFormat`) supports only `{SEQNUM}`, `{RANDSTRING}` and `{DATETIMEUTC}` tokens. It **cannot** include a related record's field such as the type code, so on its own it cannot produce `{typeCode}-…`.

## 5. Concurrency

- A **random suffix + uniqueness probe** has a check-then-write window. Two concurrent creates of the same type could draw the same value between probe and write: roughly 1 in 900,000 per coincident pair, tiny but non-zero.
- **Recommendation**: add a Dataverse **alternate key on `sprk_matternumber`**. That makes a collision a hard, retryable write failure instead of a silent duplicate, and it is the only complete close.
  - Creating a key requires the existing data to be unique. It is in dev (§3), and the 6 legacy values are unique too. **Verify production before adding it.**
- A per-type **sequence** (e.g. a counter row per `sprk_mattertype_ref`) removes randomness and collisions, but it needs its own concurrency control and changes the number's meaning. That is a format decision for the owner.

## 6. Project numbers (task 031): likely the same component, not decided here

- `sprk_projectnumber` is `sprk_project`'s **primary name attribute** (verified live, §3).
- `projectService.ts` generates **no** number at all today (plan.md §3: "FR-13's halves are not symmetric").
- Task 031 must state Project semantics explicitly. If Project needs a generated number too, it is very likely the same on-create numbering component with a different format rule. **Flagged, not decided.**

## 7. A reusable starting point: commit `0d53d3146`

Task 030's first commit, **`0d53d3146`** on the project branch, contains a **working, tested, probe-based generator**, removed in the follow-up commit.

- **Code**: `src/server/api/Sprk.Bff.Api/Services/Office/RecordCreationService.cs`, read with `git show 0d53d3146:<path>`. The relevant pieces:
  - `GenerateUniqueMatterNumberAsync`: CSPRNG suffix in `100000–999999`, at most 5 candidates.
  - `IsMatterNumberTakenAsync`: a typed `QueryExpression` on `sprk_matternumber`. Inactive rows count. A missing result is treated as a failure, never as "free".
  - `ReadMatterTypeCodeAsync`, plus type-code validation `^[A-Z]+$`.
  - Structured failures:
    - `matter_number_unavailable` (409)
    - `matter_number_probe_failed` (503)
    - `matter_type_code_unusable` (409)
    - `matter_type_not_found` (400)
    - `matter_type_lookup_failed` (503)
- **Tests**: `tests/integration/contract/Api/Office/OfficeQuickCreateContractTests.cs` at that commit. Covered: collision → retry, 5-probe exhaustion, probe failure, null probe result, unusable code, type not found, mapped-type numbering.
- **Design notes**: `notes/030-creation-service-decisions.md` at `0d53d3146`, §6 "Numbering scheme".

It ran in the BFF request path, which is **not** where an on-create trigger lives. Reuse the logic, not the placement.

## 8. What task 030 guarantees so it cannot conflict with you

- The Office quick-create path **never sends `sprk_matternumber`** on create. A field-mapping rule that targets it (Copy, Default, Concat or Template, in any casing, padded or not) is skipped with a warning. This is pinned by the contract test `Post_Matter_WithSourceContextAndProfile_AppliesEveryRule_ButNeverTheProtectedFields`, which asserts that no payload key equals `sprk_matternumber` ignoring case.
- It sets `sprk_mattertype` when the pane supplies a type that exists, so an on-create component can read the type code from the created row.
- A missing, empty or unknown type, or one whose existence could not be checked, creates the matter **without** a type, with a warning. Such a matter has no type code to number from; see the "No type at create" decision in §4.
