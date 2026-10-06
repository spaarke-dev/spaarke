# Task 168: lock the four root columns on the child forms

> **Task**: 168 (#1107) · owner round 8 item 3 · amendments: owner rounds 9-10 (round 10 item 2 = add the picker to the analysis form, then lock)
> **Branch**: `task/uac-r2-168`, from `task/uac-r2-156-c1-r2` with `work/unified-access-control-r2` merged in (clean merge, no conflicts). Base `144b36b51`.
> **Rigor**: FULL (security edge, live form definitions, TEST-MODIFYING). Model tier sonnet @ high (run on Opus).
> **Status (v1, 2026-10-05, branch `task/uac-r2-168-f1-v1`)**: **completed-with-escalation** (trigger 9 and the live gate only). The f1 verifier's items are closed: the picker script's `-Verify` now also fails on a business rule or business process flow that references a raw filing column (`WORKFLOW_REFERENCE`, a refusal and a gap, fixtures 19-20 + 10 inline cases); the SpaarkeGridCustomizer jest suite runs on a clean checkout (`globalSetup` runs `pcf-scripts refreshTypes`; the stub and the false comment are gone); the grid script's "no grid control" gap is pinned and its dry run / `-Apply` plan a publish when only the JSON lags (never "nothing to do" while `-Verify` would fail). Owner round 38 is built: ONE list `config/regarding-filing-columns.json`, read by both form scripts and by `SpaarkeGridCustomizer` **v1.1.1**, which now locks the pair and the non-root regarding lookups in the grids as well. The f1 first-class stop (§12.6) is closed by round 38. Trigger 9 is unchanged (156 still unmerged). See **§13**.
> **Status (f1, 2026-10-04, branch `task/uac-r2-168-f1`, superseded)**: **completed-with-escalation.** Owner/main-session round 25 item 8 is built: the picker script hides every raw pair / non-root `sprk_regarding*` lookup control on its target forms and gives the To Do main form its missing hidden cells; the grid lock extends the existing `SpaarkeGridCustomizer` (v1.1.0) and is set on the two editable grids by the new `Set-SpaarkeGridCustomizerOnChildGrids.ps1`; the lock script's `PICKER_WITHOUT_PRESAVE` reads the form-level OnLoad only, and an unread in-scope form is `FORM_UNREADABLE`. §10.5's escalation text is corrected. Trigger 9 is unchanged (merge 156 first). One new first-class stop: the raw filing columns in the grids (§12.6). See **§12**.
> **Status (r2, 2026-10-04, branch `task/uac-r2-168-r2`, superseded)**: **completed-with-escalation.** The r1 verifier's two surviving seeds are pinned (picker fixture 14, inline "foreign section" case), the stale §3 line numbers, I-1 row, grid REPORT pointer and §10.7 jest wording are corrected, and a third first-class stop is raised: the raw pair and intermediate lookup controls that stay visible and editable after the picker (§11.2). Trigger 9 is unchanged: merge 156 first. See **§11**.
> **Status (r1, 2026-10-04, superseded)**: **completed-with-escalation.** The verifier's findings are closed in code, and owner round 19 items 1, 2 and 4 are delivered as two new operator scripts. Two first-class stops are reported: **trigger 9** (built on 156's unmerged branch; merge 156 first) and **round 19 item 3** (the grid has no OnRowLoad event). Live runs are the main session's gate, §10.4. See **§10**.
> **Status (2026-10-03, superseded)**: **completed-with-escalation.** The code, fixtures, presave, tests and docs are done and green. Three escalation triggers fired on live dev and one blocker was found in the analysis amendment (below). The live `-Apply` is the main session's manual gate. As the script stands it REFUSES the full four-table apply until the owner decides on trigger 1.

---

## 0. Outcome in one screen

| Item | State |
|---|---|
| `scripts/Lock-CoreAncestorStampColumnsOnForms.ps1`: dry run, `-Apply` (snapshot-before-write, re-read before each PATCH), `-Verify`, `-RestoreFrom` (two refusals), `-SelfTest` | ✅ written; `-SelfTest` exit 0; dry run and `-Verify` run read-only on dev |
| `tests/fixtures/form-lock-core-ancestor/`: the ten POML cases + 11 (trigger 1) + 12 (trigger 3) | ✅ 12/12 + 5 inline checks |
| Presave v1.4.0: `setSubmitMode("always")` after every staged or cleared lookup, guarded | ✅ 21/21 presave tests; full RegardingResolver suite 114/114; PCF `build:prod` OK |
| Seeds S1-S11, each red then restored byte-identical | ✅ §6 |
| I-1 row + 156 note "Resolved" line | ✅ |
| **Trigger 1 FIRED**: three forms show the roots as visible, editable controls and host no filing picker | ✅ decided (round 19 item 1); `Add-RegardingFilingPickerToForms.ps1` adds the picker first (§10). Live gate pending. |
| **Trigger 3 FIRED (literally)**: the Event main form hosts the RegardingResolver without the presave | ✅ decided (round 19 item 2); the same picker script adds the presave and hidden cells (§10). Live gate pending. |
| **Trigger 5 FIRED**: the `sprk_event` and `sprk_analysis` home grids are EDITABLE Power Apps grids | ✅ decided (round 25 item 8): the existing `SpaarkeGridCustomizer` PCF (v1.1.0) cancels editing of the four roots, set on both grids by `Set-SpaarkeGridCustomizerOnChildGrids.ps1` (§12). Live gate pending. (r1 had stopped round 19 item 3: the grid has no OnRowLoad event.) |
| **Round 10 item 2 (add the RegardingResolver to the analysis form) BLOCKED**: `sprk_analysis` has no `sprk_regardingrecordurl`, and the picker writes it unconditionally | ✅ decided (round 19 item 4): `Add-AnalysisRegardingRecordUrlColumn.ps1`, then the picker script, then the lock (§10). Live gate pending. |
| **Trigger 9** (r1): built on task 156's unmerged branch | 🔔 reported (§10.5): merge 156 first, then 168. Re-checked r2: still unmerged; both trial merges conflict-free. |
| **r2 stop**: raw pair / intermediate lookup controls stay visible and editable on event `90d2eff7` / `835b8ee8` and message `b58ec3d8` after the picker | ✅ decided (round 25 item 8 (a)) and built in f1: the picker script hides them (picker host excepted); its `-Verify` fails on a visible one (§12). Live gate pending. |
| **f1 stop**: the raw pair / non-root lookup COLUMNS stay inline-editable in the two editable grids (round 25 item 8 names the four roots for the grid) | ✅ decided (round 38) and built in v1: `SpaarkeGridCustomizer` v1.1.1 locks every filing column, from the ONE list `config/regarding-filing-columns.json` the form scripts read (§13). Live gate pending. |
| **v1**: f1 verifier items 1-5 (business rules / process flows in the picker's `-Verify`, the jest suite on a clean checkout, the unpinned grid gap, the grid's misleading "nothing to do", stale help text) | ✅ closed (§13.1) |
| Live `-Apply` | ⏳ manual gate (**§12.5 as amended by §13.5**, which supersede §10.4 and §8): presave → schema script → picker script → lock → customizer PCF **v1.1.1** deploy → grid script → checks. |

---

## 1. No form source for these tables exists in the repo

Re-verified 2026-10-03 on this branch with a case-insensitive search of every `*.xml` for the four column names. The only hits outside this task's fixtures are two PARENT-form fragments:
`src/solutions/SpaarkeCore/entities/sprk_matter/FormXml/analyses/matter-analyses-tab.xml` and `.../sprk_project/FormXml/analyses/project-analyses-tab.xml`. Both are subgrids on the Matter and Project forms and are out of scope.
The other evidence in the POML background still holds. `src/solutions/SpaarkeCore/entities/sprk_todo/` holds only `entity-schema.md`. There is no event, communication or analysis folder. `customizations.xml` has no form. `src/dataverse/**` holds Container and Matter patches only. The ribbon folders hold no form XML.
The change is therefore delivered as the script, following the repo's live-edit convention (systemforms PATCH + PublishXml).

## 2. Step-1 live inventory (spaarkedev1, read-only, 2026-10-03)

### 2.1 Columns (EntityDefinitions/Attributes)

All four root columns exist on all four tables: `sprk_regardingproject`, `sprk_regardingmatter`, `sprk_regardingworkassignment`, `sprk_regardingservicerequest`. All are unmanaged, `IsValidForCreate`/`IsValidForUpdate` = true, and not field-secured. The pair columns are present on all four tables except **`sprk_analysis`, which has NO `sprk_regardingrecordurl`** (see §5.4).

### 2.2 Forms and their locked-column controls

"Hidden" means the control's cell, section or tab has `visible="false"`. "Disabled now" was false for every control found. No locked-column control has a `uniqueid`, so none hosts a custom control.

| Table | Form (first 8) | Name | Type | Active | Managed | Locked-column controls (id · hidden? · disabled now) | Filing picker | Presave onLoad |
|---|---|---|---|---|---|---|---|---|
| sprk_todo | `eca59df4` | To Do main form | 2 | 1 | no | matter · **hidden** (section `general_relatedrecord` visible=false) · false; project · hidden · false; workassignment · hidden · false. No service-request control. | RegardingResolver (on `sprk_regardingrecordtype`) | ✅ `sprk_todo_regarding_presave` |
| sprk_todo | `4e86f611` | Information | 2 | 0 | no | none | — | — |
| sprk_todo | `aeb7eb30` / `dfc9bb95` | Information | 11 / 6 | 1 | no | none (out-of-scope types) | — | — |
| sprk_event | `eaf22dcb` | Event main form | 2 | 1 | no | **none** | RegardingResolver | ❌ (trigger 3, §5.2) |
| sprk_event | `642e1a65` | Event quick create form | 7 | 1 | no | matter, project, workassignment · all **hidden** · false | none (hosts EventAutoAssociate on `sprk_regardingrecordid`) | — |
| sprk_event | `90d2eff7` | Event modal form | 2 | 1 | no | matter, project, workassignment · **VISIBLE** · false | **none** (EventFormController only) | — |
| sprk_event | `835b8ee8` | Event Assign Work main form | 2 | 1 | no | matter, project, workassignment · **VISIBLE** · false | **none** (EventFormController only) | — |
| sprk_event | `c4c2a2ba`, `c4d7c4ee`, `70a2a423`, `7feaa63d`, `187bef8a` | side pane forms (Action / Task / Communication / Approval / Meeting) | 2 | 1 | no | none | — | — |
| sprk_event | `f1c03a21` | Information | 2 | 1 | no | none | — | — |
| sprk_event | `7ae9ad5e` / `c63d7598` | Information | 11 / 6 | 1 | no | none | — | — |
| sprk_communication | `b58ec3d8` | Message main form | 2 | 1 | no | matter, project, servicerequest, workassignment · **VISIBLE** · false | **none** (CommunicationTimeline, CommunicationMessageActions) | — |
| sprk_communication | `6c201780` | Information | 2 | 1 | no | none | — | — |
| sprk_communication | `91c113ba` / `b538470e` | Information | 11 / 6 | 1 | no | none | — | — |
| sprk_analysis | `d408a721` | Analysis main form | 2 | 1 | no | **none** | none | — |
| sprk_analysis | `4cd8b3c4` | Information | 2 | 1 | no | none | — | — |
| sprk_analysis | `c17f6183` / `ebaceffc` | Information | 11 / 6 | 1 | no | none | — | — |

No form of type 12 exists on any of the four tables. No out-of-scope form type (6, 11) carries a locked-column control.

**Custom controls that name a locked column as a parameter:** none on any form. **EventAutoAssociate** (`sprk_Spaarke.Controls.EventAutoAssociate` v1.0.0, customcontrol `21dd8ded`, unmanaged; there is no source in the repo) sits on the Event quick create form, bound to `sprk_regardingrecordid` with the parameter `triggerField`. Its live bundle (`cc_Spaarke.Controls.EventAutoAssociate/bundle.js`, 8,325 bytes) was read. It READS the lookups the platform pre-fills, including matter, project and work assignment, to detect the parent. It WRITES only `sprk_regardingrecordname`, `sprk_regardingrecordid`, `sprk_regardingrecordurl` and `sprk_regardingrecordtype`. It never calls `setValue` on a root column. Locking does not affect it, and trigger 2 does not fire. The AssociationResolver customcontrol (`a8307de5`) is still registered in the environment but is placed on none of these forms.

### 2.3 Business rules, process flows, form libraries

- Workflows of category 2 or 4 whose primary entity is one of the four tables: **none.**
- Form libraries on in-scope forms: `sprk_regardingrecordnumber_hyperlink` (v1.0.0), `sprk_todo_regarding_presave` (**live v1.2.0**, see below), `sprk_todo_score_onchange`, `sprk_todo_hide_tabnav` (to-do main), and `sprk_event_sidepane_form.js` (event side panes). **None calls `setDisabled`.** Only the presave names a locked column (in comments and the stamp logic).
- **Presave registration:** the live web resource is named **`sprk_todo_regarding_presave`** (id `2ae21d81`), not the `sprk_/scripts/smarttodo_regarding_presave.js` named in `projects/smart-todo-r4/notes/d-form-bind-instructions.md`. It is registered as `Spaarke.SmartTodo.RegardingPreSave.onLoad` on the To Do main form ONLY. **The live copy is v1.2.0** (modified 2026-08-16), behind the repo's v1.3.0 (task 051's FR-26 stamp staging). Deploying v1.4.0 (gate step b) also brings the v1.3.0 stamp staging live for the first time.

### 2.4 Editable grids

- Entity-level grid customization (`customcontroldefaultconfig`): **`sprk_event` and `sprk_analysis` use the Power Apps grid with `EnableEditing=yes`** (every form factor). `sprk_todo` and `sprk_communication` have an empty configuration (read-only grid).
- System views (`savedquery`) showing a locked column: **none** (19 event, 13 analysis, 8 to-do, 11 communication). Personal views visible to the operator showing one: none.
- Subgrids on ANY form (617 forms of type 2, 7 or 12 scanned) that target one of the four tables: 16, all read-only (no custom control and no editable grid). Examples: the Matter main form `4fa382f2` (messages, analyses), the Project main form `5aa00242` (to-dos, events, analyses), and the Document, Budget, Invoice, Work Assignment, Organization, Contact, Analysis and Event main forms (to-dos).

### 2.5 SpaarkeMaster membership

`SpaarkeMaster` (`678e07b0`, unmanaged, v1.0.0.0) holds all four tables as entity components with `rootcomponentbehavior = 0` (include all subcomponents), so every in-scope form ships with it. **Trigger 8 does not fire.**

### 2.6 How the platform treats script-set values on disabled controls today

On every CREATE-capable form inspected, no text-field control (`sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`, `sprk_regardingrecordnumber`) is disabled. They are either `disabled="false"` or carry no attribute. So dev holds no existing evidence of how the platform treats a script-set value on a disabled control. The presave's `setSubmitMode("always")` is what makes the INSERT independent of that behaviour; the live gate (c)-(j) proves it.

## 3. The script

`scripts/Lock-CoreAncestorStampColumnsOnForms.ps1` is structured on `Retire-CommunicationAccessPermission.ps1` (comment help with exit codes 0/2/1, `Get-DataverseToken`, `Invoke-Dv -AllowNotFound`, `Write-Step/Plan/Done/Info`, `Stop-Refused` exit 2, a pure transform, every write after the dry-run exit). Its `-Verify` follows the convention of `Set-SecureRecordOwnerRolePrivileges.ps1` (VERIFY PASS exits 0; VERIFY FAIL names each gap and exits 1).

- **Modes:** with no switch it runs as a dry run. `-Apply`, `-Verify`, `-RestoreFrom` and `-SelfTest` are mutually exclusive: passing two throws before any read (checked: `-Apply -Verify` exits 1 with "separate modes"). `-Tables` (default: the four tables) scans a subset. `-Apply` refuses any table outside the four.
- **Writes and their line numbers** (re-taken r2, 2026-10-04, on the r1/r2 script; r2 changed one REPORT string at :723 and no line count). No PATCH, POST or DELETE runs outside the `-RestoreFrom` branch (`if ($RestoreFrom)` at :564, ending in `exit 0` at :619) or the `-Apply` section (after `if (-not $Apply) { … exit 0 }` at :762-765). The script has no DELETE.
  - **:605** `PATCH systemforms(formid)`: restore of `formXmlBefore`. Runs inside `if ($RestoreFrom)`, after the environment-URL refusal (:571), the snapshot self-hash refusal (:577) and the "current hash = stored/written hash" refusal (:596).
  - **:609** `Publish-Table` → **:557** `POST PublishXml`: restore.
  - **:824** `PATCH systemforms(formid)`: apply. Runs after every refusal check (the refusal exit at :754), after the snapshot is written, read back and re-hashed (refused at :805), after a re-read of all forms (:811-812), and after a re-read of THIS form (:821-822).
  - **:829** `Publish-Table` → **:557** `POST PublishXml`: apply, once per changed table.

  `Publish-Table` (:555-558) is only a definition; it is called at :609 and :829 only. (The r0 numbers first recorded here, :512/:560/:564/:718/:780/:785, were for the r0 file; r1 moved them and the note was not re-taken — verifier r1 item 7.)
- **After the apply, the script reads every written form back** (added during the Step 9.5 review):
  - It records `formXmlStored` and `sha256Stored` in the snapshot. Dataverse may normalize the form XML it is given; without this, `-RestoreFrom`'s "unchanged since the apply" check could refuse forever.
  - It throws if any locked-column control is still not disabled.
  - `-RestoreFrom` compares against `sha256Stored` when it is present.
  - It skips a form that still holds its "before" XML, which happens when an apply was stopped part-way by FORM_CHANGED.
  - It warns, rather than fails, if a restored form does not hash back to "before".
- **`-RestoreFrom` refusals, run on dev (no write; the second one only GETs the to-do form):**
  - a snapshot with `environmentUrl = https://some-other-org.crm.dynamics.com` → `REFUSED: the snapshot was taken on '…some-other-org…', not 'https://spaarkedev1.crm.dynamics.com'.`, **exit 2**;
  - a self-consistent snapshot whose written hash is not the live to-do form's hash → `REFUSED: form 'To Do main form' (eca59df4-…, sprk_todo) changed after the apply (sha256 a759cd42…, expected 89c5c011…)`, **exit 2**.
- **Transform:** a pure string edit of each targeted `<control>` start tag. `Test-LockTransform` re-parses with `PreserveWhitespace` and walks both documents node by node. It uses .NET accessor methods throughout, because PowerShell's XML adapter makes `$section.Name` return the section's `name=` ATTRIBUTE. This was found during the run and is recorded in the code.
- **Refusals (exit 2):** MANAGED_FORM, HOSTED_CUSTOM_CONTROL, BOUND_PARAMETER, WORKFLOW_REFERENCE, LIBRARY_UNLOCKS, TRANSFORM_INCOMPLETE, TRANSFORM_PARSE, NO_MAIN_FORM, FORM_CHANGED. Two more, beyond the POML's list, encode escalation triggers so that a live run cannot step past an owner decision:
  - **NO_FILING_PICKER** (trigger 1): a visible, editable locked control on a form with neither the RegardingResolver nor CommunicationConnections;
  - **PICKER_WITHOUT_PRESAVE** (trigger 3): the RegardingResolver plus a locked control, with no presave onLoad registered.

  Both have fixtures (11, 12). Attribute names were confirmed against real forms read in step 1: `controlDescription@forControl` = `control@uniqueid`; `customControl@name` marks a PCF, and an id-only `customControl` is the platform renderer.
- **Snapshot:** `{script, task, environmentUrl, createdUtc, forms:[{formid, table, name, type, formXmlBefore, formXmlWritten, sha256Before, sha256Written}]}`. It is written, read back and re-hashed before the first PATCH.
- **Editable grids:** REPORTED in the dry run and in `-Verify` (trigger 5), never changed.

### 3.1 Dry run on dev (2026-10-03, read-only), exit 2

```
PLAN  form 'To Do main form' (eca59df4-…) type 2: lock sprk_regardingmatter, sprk_regardingproject, sprk_regardingworkassignment
PLAN  form 'Event quick create form' (642e1a65-…) type 7: lock sprk_regardingmatter, sprk_regardingproject, sprk_regardingworkassignment
PLAN  form 'Event modal form' (90d2eff7-…) type 2: lock sprk_regardingmatter, sprk_regardingproject, sprk_regardingworkassignment
PLAN  form 'Event Assign Work main form' (835b8ee8-…) type 2: lock sprk_regardingmatter, sprk_regardingproject, sprk_regardingworkassignment
PLAN  form 'Message main form' (b58ec3d8-…) type 2: lock sprk_regardingmatter, sprk_regardingproject, sprk_regardingservicerequest, sprk_regardingworkassignment
REPORT: sprk_event / sprk_analysis home grid is an EDITABLE grid — task 168 trigger 5
REFUSAL NO_FILING_PICKER: sprk_event 'Event modal form' (90d2eff7) …
REFUSAL NO_FILING_PICKER: sprk_event 'Event Assign Work main form' (835b8ee8) …
REFUSAL NO_FILING_PICKER: sprk_communication 'Message main form' (b58ec3d8) …
REFUSED: 3 refusal case(s)
```
`-Tables sprk_todo` dry run: exit 0, "1 form(s) would change".

### 3.2 `-Verify` on dev before any apply (read-only), exit 1, as expected

It names all 16 unlocked controls the inventory found: to-do `eca59df4` × 3 (hidden), event QC `642e1a65` × 3 (hidden), event modal `90d2eff7` × 3, Assign Work `835b8ee8` × 3, Message `b58ec3d8` × 4. It also lists the 3 NO_FILING_PICKER refusals, then prints `VERIFY FAIL: 16 unlocked control(s), 3 refusal case(s).`
`-Verify -Tables sprk_analysis`: exit 0. This passes only because no analysis form has a root control today; see §5.4.

### 3.3 `-SelfTest` (offline), exit 0

```
PASS 01-visible-editable-matter            locked sprk_regardingmatter
PASS 02-hidden-project-quick-create        locked sprk_regardingproject      (type 7, no picker: hidden => not trigger 1)
PASS 03-body-and-header-workassignment     locked header_sprk_regardingworkassignment, sprk_regardingworkassignment
PASS 04-mixed-case-servicerequest          locked sprk_regardingservicerequest
PASS 05-already-disabled-idempotent        nothing to do (unchanged)
PASS 06-disabled-false-rewritten           locked sprk_regardingmatter       (disabled="False" -> "true", no duplicate)
PASS 07-refuse-hosted-custom-control       refused HOSTED_CUSTOM_CONTROL
PASS 08-refuse-bound-parameter             refused BOUND_PARAMETER
PASS 09-negative-near-miss-columns         nothing to do (recordtype / communication / recordname / matterstatus untouched)
PASS 10-no-locked-columns                  nothing to do (unchanged)
PASS 11-refuse-no-filing-picker            refused NO_FILING_PICKER          (added: trigger 1)
PASS 12-refuse-picker-without-presave      refused PICKER_WITHOUT_PRESAVE    (added: trigger 3)
+ 5 inline checks (library setDisabled rule x3, workflow reference incl. near-miss x2)
SELF-TEST PASS: 12 fixture case(s) + 5 inline check(s).
```
Every non-refusal case also re-runs the transform on its own output and requires identical bytes, so idempotence is checked across all of them.

## 4. Presave v1.4.0

`src/client/webresources/js/sprk_todo_regarding_presave.js`:
- `setLookupIfPresent` (both the set and the empty-id null branch) and `clearLookupIfPresent` call the new `forceSubmit(attr, fieldName)` after a successful `setValue`.
- `forceSubmit` calls `attr.setSubmitMode("always")` only when it is a function, and catches and warns on a throw, so the return value never changes.
- `setAttributeIfPresent` (text fields) is unchanged. Clears still run before sets. The FR-26 `console.error` and the CREATE-only, read-only and never-block-the-save gates are unchanged.
- `VERSION` is "1.4.0", with one v1.4.0 history line.

Tests (`regardingPresave.test.ts`): the 13 existing tests pass unchanged, except the version test, which now expects 1.4.0. The fake attribute gains a `setSubmitMode` spy with three behaviours (spy, absent, throws) and records calls with the same sequence counter as `setValue`. Eight new tests:
- the chosen lookup, each stamp and each cleared lookup get "always" immediately AFTER their `setValue`;
- NEGATIVE: an attribute not on the form gets no call; formType 2, 3 and 4 stage nothing and make no call; the four text fields get no call;
- an attribute with no `setSubmitMode` is still staged, and the helpers return true;
- an attribute whose `setSubmitMode` throws is still staged; the summary log reports `ancestorStampsStaged '1/1'`; no `console.error`; the helpers return true.

## 5. Escalations (🔔 Human input required)

### 5.1 Trigger 1: three forms expose the roots as visible, editable controls and host no filing picker

| Form | Visible editable root controls | Hosted PCFs |
|---|---|---|
| sprk_event `90d2eff7` "Event modal form" (type 2) | matter, project, work assignment. The raw pair text fields (`sprk_regardingrecordtype`, `id`, `name`, `url`) are also visible and editable. | EventFormController |
| sprk_event `835b8ee8` "Event Assign Work main form" (type 2) | the same as the modal form | EventFormController |
| sprk_communication `b58ec3d8` "Message main form" (type 2) | matter, project, service request, work assignment | CommunicationTimeline, CommunicationMessageActions |

Locking these forms removes the only form input on them that files the record under a root. CommunicationConnections is on no live communication form. These are the forms where the owner's risk is real today. On the to-do main form the roots are already in a HIDDEN section, so the to-do lock is defence in depth.
**Options (owner):** (a) add the RegardingResolver to each form (and, for the communication form, the presave), then lock. This is the pattern round 10 item 2 chose for analysis. (b) Lock as-is: the record can then be filed only through a code page or the BFF. **Recommendation: (a)**, as for analysis.
The script refuses these forms (`NO_FILING_PICKER`) until the decision is made. Option (b) needs an explicit allow-list added to the script.

### 5.2 Trigger 3: the Event main form `eaf22dcb` hosts the RegardingResolver but does not register the presave

The trigger fires on its wording. It is **moot for the lock**, because that form carries no root control and the script changes nothing there (it is not refused). It does expose a defect that predates this task. A CREATE through that form stages only the bound `sprk_regardingrecordtype`, plus whatever text fields the PCF sets directly (`sprk_regardingrecordname` and `sprk_regardingrecordnumber` are hidden on the form; `sprk_regardingrecordid` and `url` are not on it). With no presave, the chosen lookup and the ancestor stamps are not staged. A new event is therefore saved without its chosen lookup or stamps, so it is invisible to the root's principals (fail closed) until the reconciliation job runs, and its `sprk_regardingrecordid` may be empty, which the job needs to find the source.
**Options:**
- (a) register the presave on `eaf22dcb` and add the hidden root and intermediate lookups and `sprk_regardingrecordid`/`url` cells (a form change);
- (b) leave it, and file the defect.

**Recommendation: (a)**, as a separate form task. Owner's call.

### 5.3 Trigger 5: editable home grids on `sprk_event` and `sprk_analysis`

Both tables' entity-level grid is the Power Apps grid with `EnableEditing=yes`. No system view, and no personal view the operator can see, displays a root column today. A user who adds a root column to a personal view, or to the grid through "Edit columns", could edit it inline on an EXISTING row without writing the pair. That is exactly the shape the lock removes from forms. This is recorded, not changed: the owner's decision names forms only.
**Options:**
- (a) turn off editing on those two grids;
- (b) keep editing and accept the shape; the job repairs it within one cycle, as for a raw Web API write;
- (c) make the four columns read-only in grids through a business rule. This conflicts with ADR-002's spirit, so it is not recommended.

**Recommendation: (b)**, consistent with the owner's acceptance of raw writes outside the forms. **(a)** if the owner wants the UI closed entirely. The script reports these grids in every run.

### 5.4 Round 10 item 2 (add the RegardingResolver to the analysis form) is BLOCKED by schema

`sprk_analysis` has no `sprk_regardingrecordurl` column (live metadata, 2026-10-03). The RegardingResolver writes it unconditionally on every UPDATE pick (`applyResolverFields`, `PolymorphicResolverService.ts:646`) and on every clear (`ResolverWriteHandler.clearRegarding`, `:525`). On an analysis form, every re-file and every clear made through the picker would therefore be rejected by Dataverse ("Invalid property"). A CREATE would work, because the presave skips a column that is not on the form.
Constraints prevent a fix inside this task: the POML forbids PCF source changes (ADR-006 scope); live writes are read-only for the executor; and the column would be a new schema surface that needs owner sign-off.
**Options:**
- (a) add `sprk_regardingrecordurl` (URL, the same definition as on the other three tables) to `sprk_analysis`. This completes the ADR-024 pair. It is a schema script with dry run, apply and verify, applied by the main session. Then a form change adds the picker (bound to `sprk_regardingrecordtype`, `entity=sprk_analysis`), the hidden pair, root and intermediate lookup cells, and the presave onLoad. Then this script locks the roots.
- (b) make `applyResolverFields` and `clearRegarding` drop `sprk_regardingrecordurl` when the host lacks it, which the code already does for lookups (SRFR-048). This is a shared-library and PCF change: a PCF version bump and deploy.

**Recommendation: (a)**. One complete pair on every child table is the ADR-024 contract, and (b) leaves analysis as the one host without a clickable parent link.
Until this is decided, the analysis forms have no root control, so there is nothing to lock. `-Verify -Tables sprk_analysis` passes for that reason only.

### 5.5 Not a trigger, recorded

The To Do main form has no `sprk_regardingservicerequest` cell. A to-do created under an intermediate whose root is a service request cannot stage that stamp on CREATE: the presave logs the FR-26 `console.error`, and the job repairs it. This predates this task and matches the I-1 row's "client create paths ... unstamped until the job's next run". Adding a hidden `sprk_regardingservicerequest` cell to `eca59df4` would close it; that is a form change for the owner or a follow-up.

## 6. Seeds: each guard proven to bite, then restored byte-identical

Script sha256 before and after every seed, final bytes: `65a30c56466992b6e9820319d2da48f3622bee198ec047b14562038f35e440b9`. S1-S6, S10 and S11 were re-run on these bytes after the review edits, with the same results. The first run was on `a3e4e8fe…`. Presave sha256 before and after: `6d34b82bbe2a61b3f244794166dfafc202322807e3520c4f6039ae42aaadd985`. Each restore was compared by SHA-256 and the file was touched.

| Seed | Mutation | Result while seeded |
|---|---|---|
| S1 | `Test-IsLockedColumn` compares case-sensitively (`Ordinal`) | SELF-TEST FAIL: **04-mixed-case-servicerequest** output differs |
| S2 | the HOSTED_CUSTOM_CONTROL refusal is dropped | FAIL: **07** "expected refusal HOSTED_CUSTOM_CONTROL, got []" |
| S3 | the BOUND_PARAMETER refusal is dropped | FAIL: **08** "expected refusal BOUND_PARAMETER, got []" |
| S4 | prefix match (`StartsWith`) | FAIL: **09-negative-near-miss-columns** output differs (matterstatus locked) |
| S5 | always write a new disabled attribute (`if ($false)`) | FAIL: **05** (duplicate `disabled`, not well-formed), plus 01-04 and 06 |
| S6 | re-serialize (`[xml]…OuterXml`) instead of editing the tag | FAIL: **01** (and 02-06, 09, 10) "child count changed under <form>" |
| S7 | `forceSubmit` removed from `setLookupIfPresent` | jest 3 failed: "the chosen lookup gets setSubmitMode…", "each ancestor stamp gets setSubmitMode…", "a throwing setSubmitMode leaves the value staged…" |
| S8 | `forceSubmit` removed from `clearLookupIfPresent` | jest 1 failed: "each cleared lookup gets setSubmitMode("always") after its null setValue" |
| S9 | the try/catch in `forceSubmit` is removed (a throw now changes the return value) | jest 1 failed: "a throwing setSubmitMode leaves the value staged…" |
| S10 | the `-Verify` read-fault branch exits 0 | `-Verify -EnvironmentUrl https://uac168-nonexistent-org.crm.dynamics.com`: guarded **exit 1** ("VERIFY FAIL: read fault — …"); seeded **exit 0** |
| S11 | the NO_MAIN_FORM refusal is dropped | `-Verify -Tables principalobjectattributeaccess` (a real table with no forms): guarded **exit 1** ("REFUSAL NO_MAIN_FORM … VERIFY FAIL: 0 unlocked control(s), 1 refusal case(s)"); seeded **exit 0** ("VERIFY PASS") |

## 7. Tests (counts)

| Suite | Result |
|---|---|
| `Lock-CoreAncestorStampColumnsOnForms.ps1 -SelfTest` | exit 0, 12 fixtures + 5 inline |
| RegardingResolver jest, full (`npx jest`) | **114 passed, 3 suites**. The shared libraries `Spaarke.Auth`, `Spaarke.SdapClient` and `Spaarke.UI.Components` had to be built first in this fresh worktree, because the PCF imports their `dist`. |
| RegardingResolver `npm run build:prod` | succeeded |
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`), full run | 14,393 passed, **6 failed**, 54 skipped (14,453), 29 m 46 s. Each failure took about 3 minutes (host timeouts while other agents ran suites concurrently). **All 6 pass when re-run on their own** (6/6, 30 s): OboDriveKeyedRouteRetirementTests, RelatedRecordCardContractTests, ComposePdfIntakeRoundTripSeamTests, OfficeSaveSpineIdempotencyContractTests, ExternalProjectDocumentUploadContractTests, KnowledgeBaseReindexReplaceStaleChunksContractTests. This is contention; the task changes no C#. |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 / 346 passed** (includes ADR002_PluginTests and RouteAuthorizationGuardTests) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** |
| `tests/integration/Spe.Integration.Tests` | **403 passed, 25 skipped** (428), 0 failed |

## 8. Manual live gate (main session, step 7, in this order)

> **Superseded by §10.4 (r1, 2026-10-04)**, which adds the schema and picker scripts before the lock and the new checks (k) and (l). Kept as the 2026-10-03 record.

1. **(a)** Confirm that no other formxml writer is running against dev: task 138's `Retire-CommunicationAccessPermission.ps1 -Apply` and `Deploy-TodoSubgridsToElevenParentForms.ps1`.
2. **(b)** Deploy the presave v1.4.0 to the EXISTING web resource **`sprk_todo_regarding_presave`** (id `2ae21d81`; live is v1.2.0) and publish, using the dataverse-deploy skill. Then open a to-do CREATE form and confirm the console line `[SmartTodo.RegardingPreSave v1.4.0] OnSave handler registered`.
3. **(c)** `pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1`, the dry run. It exits 2 on the three trigger-1 forms until the owner decides §5.1.
4. **(d)** Apply what is unblocked today:
   `pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -Apply -Tables sprk_todo -SnapshotPath <path>`. Keep the snapshot. After the §5.1 decision, run the same command for `sprk_event` and `sprk_communication` (and `sprk_analysis` after §5.4).
5. **(e)** `pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -Verify -Tables sprk_todo` must exit 0. The full four-table `-Verify` exits 0 only after every decision is applied.
6. **(f)** Run the live checks (b)-(j) of the last acceptance criterion with `uac.child.user@demo.spaarke.com` (round 11). Checks (c)-(h) are to-do checks and can run after step 4. Checks (i) and (j) wait for §5.1 and §5.4.
7. **(g)** On any failure:
   `pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -RestoreFrom <snapshot>`, then `-Verify -Tables sprk_todo` (it should exit 1 again), then STOP (trigger 7).

Live gate results: _pending (main session)_.

## 9. Scope check (git diff against base `144b36b51`)

- Nothing under `src/server/**` changed. The RegardingResolver PCF source is unchanged (only `__tests__/regardingPresave.test.ts`). CommunicationConnections is unchanged. No new web resource was added.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` is unchanged, and no waiver was added or removed: this is not a route task. No plugin was added (ADR002_PluginTests run in the ArchTests suite).
- The I-1 row edit touched only the "root typed directly onto a filed row" clause. The 156 note gained one "Resolved" line.
- `/conflict-check`: no file of this task is on the hot-path watchlist (BFF, SpaarkeAi, ci-workflows, skill directives, root CLAUDE.md), so it was not invoked. The known overlaps are named in the POML's parallel-reason: the I-1 row, also edited by 156, 158 and the round-8 follow-ups; the presave area, also touched by task 147; and live formxml writers.
- `.claude/**`: no edit needed.

## 10. Round r1 (2026-10-04): the verifier's findings and owner round 19

Branch `task/uac-r2-168-r1`, from `task/uac-r2-168` (`01c591391`). Binding inputs: the verifier's 16 findings, and **owner round 19** (2026-10-04, `notes/session27-owner-decisions-and-research.md` on `work/unified-access-control-r2`), which answers this task's §5 escalations:
1. trigger 1: the three forms get the picker, hidden cells and presave, THEN the lock;
2. trigger 3: the Event main form gets the presave and hidden cells;
3. trigger 5: an OnRowLoad handler on the editable `sprk_event` / `sprk_analysis` home grids disables the root columns;
4. the amendment blocker: add `sprk_regardingrecordurl` to `sprk_analysis` (schema script with the rootcomponentbehavior-0 check), then the picker, then the lock;
5. every live step is a main-session gate, run dry → apply → verify.

The main session's note for this round also named rounds 15 and 16. Both were read: they decide tasks 162-167 and contain no item for task 168.

### 10.1 What changed, per finding

| # | Finding | Closure |
|---|---|---|
| 1, 11 | Seed S5 as specified did not go red: fixture 05 uses the canonical `disabled="true"`, so rewriting it is byte-identical | **Fixed.** New fixture **13-noncanonical-disabled-true-idempotent**: `disabled="True"`, `disabled='true'`, `disabled = "TRUE"`; `expected.xml` = `input.xml`. Seed **S5r** (the verifier's exact mutation: delete `if ($value.Trim() -ieq 'true') { return $Tag }`) now fails fixture 13 ("output differs from expected.xml"). §6's S5 (`if ($false)`, always ADD) remains a separate, valid seed. |
| 2, 12 | The parse check (TRANSFORM_PARSE / TRANSFORM_INCOMPLETE) and MANAGED_FORM were not proven offline | **Fixed.** (a) The live mapping of parse problems to refusal codes moved into a pure `Get-TransformRefusals`, used by the live scan AND by `-SelfTest`. (b) Eight inline before/after cases call it: a correct lock (positive control, no refusal), a targeted control left enabled (TRANSFORM_INCOMPLETE), a non-target attribute changed, an attribute added to a non-target, text changed, an element added, a stray `Disabled=` plus `disabled="true"` (the duplicate check alone), a result that is not well-formed (each TRANSFORM_PARSE). (c) `-SelfTest` reads an optional `managed.txt`; new fixture **14-refuse-managed-form** expects MANAGED_FORM. Seeds S12a-S12i below each go red. |
| 3 | Removing `forceSubmit` from `setLookupIfPresent`'s empty-id branch left jest green | **Fixed.** New jest test "a lookup staged with an id that cleans to empty gets setSubmitMode("always") after its null setValue" (through `onSave` with a stamp whose id is `{}`, and through `_internals.setLookupIfPresent`). Seed S3p: removing that one call fails exactly this test (1 failed, 21 passed); restored byte-identical (`6d34b82b…`, unchanged — the presave source did not change in r1). |
| 4, 15 | Trigger 9 fired and was not reported as a trigger | **Reported as a first-class stop (§10.5).** It cannot be fixed from this branch: 156 is still `[open]` and not in `work/unified-access-control-r2` (`git merge-base --is-ancestor task/uac-r2-156-c1-r2 work/unified-access-control-r2` → false on 2026-10-04). Integration order: **156 first, then 168**. |
| 5, 13 | Round 10 item 2 (picker on the analysis form) not executed; blocked by the missing `sprk_regardingrecordurl` | **Delivered as code, per round 19 item 4.** New `scripts/Add-AnalysisRegardingRecordUrlColumn.ps1` adds the column (the `sprk_todo` definition: String / Url / 500, audited) after checking that `sprk_analysis` ships in SpaarkeMaster with rootcomponentbehavior 0. New `scripts/Add-RegardingFilingPickerToForms.ps1` then puts the picker, hidden cells and presave on the Analysis main form `d408a721` (it REFUSES that form with PAIR_INCOMPLETE until the column exists). Then the lock script locks it. **No PCF or shared-library source changed** (ADR-006 scope kept). Live runs are gates (§10.4). |
| 6, 14 | Goal (1) unmet for event `90d2eff7`, `835b8ee8` and communication `b58ec3d8` (NO_FILING_PICKER) | **Delivered as code, per round 19 item 1.** The picker script adds the RegardingResolver (entity = the table), hidden cells and the presave to the three forms; the lock script's NO_FILING_PICKER then clears. Proven offline on the live form XML (read-only snapshot, 2026-10-04): picker transform → lock transform on all five target forms gives no refusal and no parse problem, and each form ends complete (§10.3). The event Quick Create `642e1a65` gets live-gate check **(k)**. |
| 7 | Verified as claimed | No change. |
| 8 | Live presave is v1.2.0; deploying v1.4.0 brings v1.3.0's FR-26 staging live | **Recorded as a behaviour change** in gate step (b) (§10.4). |
| 9, 10 | Test runs / docs verified | No change. |
| 16 | Pending live gate | Still pending (main session). Its order now includes the schema and picker scripts (§10.4); (i) and (j) are no longer waiting on decisions. |

Owner round 19 item 2 (Event main form `eaf22dcb`) rides the same picker script: it already hosts the RegardingResolver, so the script adds only the presave registration and the hidden pair, root and intermediate lookup cells.

### 10.2 The two new scripts

**`scripts/Add-RegardingFilingPickerToForms.ps1`** (target forms: the five named by round 19; `-Forms` overrides). Additive only, as string insertions (never a re-serialization):
- a visible section `sprk_filing_picker` (first section of the first visible tab) with one control bound to `sprk_regardingrecordtype`, hosting `sprk_Spaarke.Controls.RegardingResolver` with `entity` = the table. The control description is the one on the live To Do and Event main forms. It is added only when the form hosts no RegardingResolver;
- a hidden section `sprk_filing_hidden` with a cell for every pair text column and every `sprk_regarding*` lookup of the table that has no control on the form;
- the form library `sprk_todo_regarding_presave` and the OnLoad handler `Spaarke.SmartTodo.RegardingPreSave.onLoad` (pass execution context), added to the FORM-LEVEL `<events>` / `<formLibraries>`.
- **Found during the r1 run:** the live Analysis main form has NO form-level `<events>`, but a web-resource cell carries its own nested `<events><event name="onload" … /></events>`. A text search for `</events>` or `<event name="onload"` finds that nested one. The parse check caught it on the offline end-state run ("no … handler after the transform"). Container and onload positions now come from an XmlReader (depth-1 elements only, `Get-TopLevelElementSpan`), and fixture 13 pins it.
- New ids are a stable hash of (form id, purpose), so a re-run writes identical bytes and a complete form is left byte-identical.
- Refusals (exit 2): FORM_NOT_FOUND, MANAGED_FORM, PAIR_INCOMPLETE, PICKER_MISCONFIGURED, PRESAVE_DISABLED, NO_VISIBLE_TAB, PREREQ_MISSING (live: web resource or customcontrol absent), TRANSFORM_PARSE, FORM_CHANGED.
- Modes are copied from the lock script: dry run, `-Apply` (snapshot written and read back first; re-read before each PATCH; read-back after), `-Verify`, a guarded `-RestoreFrom` and `-SelfTest`. Writes: `PATCH systemforms` and `POST PublishXml`, only in the `-RestoreFrom` branch or after the `if (-not $Apply)` exit.
- `-SelfTest`: 13 fixtures under `tests/fixtures/form-filing-picker/`, plus 9 inline parse-check cases.

**`scripts/Add-AnalysisRegardingRecordUrlColumn.ps1`**: dry run, `-Apply`, `-Verify`, `-SelfTest` (10 inline checks).
- It refuses (exit 2) SOLUTION_MEMBERSHIP (sprk_analysis is not a SpaarkeMaster entity component with rootcomponentbehavior 0) and COLUMN_MISMATCH (a column of that name exists with another type, format or length; it is never altered).
- `-Verify` exits 0 only when the column matches and the solution check passes. A read fault exits 1.

**Lock script changes in r1:**
- `Get-TransformRefusals`, the `managed.txt` read, fixtures 13 and 14 and eight inline checks;
- the NO_FILING_PICKER / PICKER_WITHOUT_PRESAVE texts and the grid REPORT now point to round 19.

The transform and the refusal logic are unchanged.

### 10.3 Live runs (spaarkedev1, read-only, 2026-10-04)

- Schema script dry run: "Column state: absent; Solution check: sprk_analysis ships in SpaarkeMaster with all subcomponents … would create" → **exit 0**.
- Schema script `-Verify`: "VERIFY FAIL: column absent" → **exit 1** (expected before the apply).
- Positive control: `-Verify -Table sprk_todo` → VERIFY PASS, **exit 0**.
- Picker dry run:
  - prerequisites present;
  - PLAN for `eaf22dcb` (16 hidden cells + presave), `90d2eff7` and `835b8ee8` (picker `sprk_regardingrecordtype1` + 5 cells + presave), `b58ec3d8` (picker + 6 cells + presave);
  - REFUSAL PAIR_INCOMPLETE for `d408a721` → **exit 2** (expected until the schema apply).
- Picker `-Verify`: 43 gaps + 1 refusal → **exit 1**.
- Lock dry run / `-Verify`: unchanged from §3.1/§3.2 (exit 2 / exit 1). The three NO_FILING_PICKER refusals now name the picker script.
- **Offline end-state proof on the live form XML.** The forms were read read-only into the scratchpad. The analysis specs had `sprk_regardingrecordurl` added, as after the schema apply. The run was picker transform → lock refusals → lock transform → parse checks:

| Form | Picker added | Picker parse | Lock refusals | Locked | Lock parse | Complete after lock |
|---|---|---|---|---|---|---|
| event main `eaf22dcb` | 18 | ok | none | matter, project, servicerequest, workassignment | ok | yes |
| event modal `90d2eff7` | 8 | ok | none | servicerequest, matter, project, workassignment | ok | yes |
| Assign Work `835b8ee8` | 8 | ok | none | servicerequest, matter, project, workassignment | ok | yes |
| Message `b58ec3d8` | 9 | ok | none | matter, project, servicerequest, workassignment | ok | yes |
| Analysis main `d408a721` | 15 | ok (after the top-level fix) | none | matter, project, servicerequest, workassignment | ok | yes |

### 10.4 Manual live gate (main session, dev, in this order; supersedes §8)

1. **(a)** Confirm that no other formxml writer is running: task 138's retirement script and `Deploy-TodoSubgridsToElevenParentForms.ps1`.
2. **(b)** Deploy presave v1.4.0 to the EXISTING web resource `sprk_todo_regarding_presave` (`2ae21d81`) and publish. Confirm `[SmartTodo.RegardingPreSave v1.4.0] OnSave handler registered` in a to-do CREATE form's console.
   - **Behaviour change (verifier item 8):** the live copy is v1.2.0. This deploy also brings v1.3.0 live: FR-26 core-ancestor stamp staging and reparent clears, on the To Do main form. Checks (c), (d) and (f) exercise it.
3. **(c)** `pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1`, then `-Apply`, then `-Verify` (must exit 0).
4. **(d)** `pwsh scripts/Add-RegardingFilingPickerToForms.ps1`, then `-Apply -SnapshotPath <picker-snapshot>`, then `-Verify` (must exit 0).
5. **(e)** `pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1` (the dry run must exit 0, with no refusal), then `-Apply -SnapshotPath <lock-snapshot>`, then `-Verify` with the default four tables (must exit 0).
6. **(f)** Run the live checks with `uac.child.user@demo.spaarke.com`:
   - (b)-(h) of the acceptance criterion;
   - (i) on the Event main, modal and Assign Work forms and on the Message form;
   - (j) "+ New" analysis from a Matter's Analyses tab, plus a re-file and a clear through the picker on the Analysis main form (the picker writes `sprk_regardingrecordurl`);
   - **(k), new (verifier item 6):** a "+ New" event from a parent record's Events subgrid that opens the Event quick create form `642e1a65`. That form has hidden roots, now disabled, and no presave; the parent lookup comes from the platform's relationship pre-fill. On read-back, the row must carry the parent's root lookup (for example `sprk_regardingmatter` from a Matter);
   - **(l), new (round 19 item 2):** an event CREATED on the Event main form `eaf22dcb` with a matter picked in the RegardingResolver. On read-back it carries `sprk_regardingmatter` and the pair.
7. **(g)** On any failure:
   - restore in REVERSE order: `Lock-CoreAncestorStampColumnsOnForms.ps1 -RestoreFrom <lock-snapshot>`, then `Add-RegardingFilingPickerToForms.ps1 -RestoreFrom <picker-snapshot>`. Each refuses if a later change touched its forms, which is why the order matters;
   - the analysis column is additive and stays. Deleting a column is destructive and needs its own decision;
   - then STOP (trigger 7).

Exact commands (dev):
```
pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1
pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1 -Apply
pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1 -Verify
pwsh scripts/Add-RegardingFilingPickerToForms.ps1
pwsh scripts/Add-RegardingFilingPickerToForms.ps1 -Apply -SnapshotPath .\filing-picker-snapshot-dev.json
pwsh scripts/Add-RegardingFilingPickerToForms.ps1 -Verify
pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1
pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -Apply -SnapshotPath .\form-lock-snapshot-dev.json
pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -Verify
```

Live gate results: _pending (main session)_.

### 10.5 Escalations (🔔 first-class stops, recorded and reported)

**Trigger 9 (POML gate): this work is built on task 156's unmerged branch.**
- `task/uac-r2-168` was branched from `task/uac-r2-156-c1-r2`. Task 156 is `[open]` in `work/unified-access-control-r2`'s TASK-INDEX, and its commits reach only `integ/uac-r2-batch4`.
- `git diff work/unified-access-control-r2...task/uac-r2-168-r1` therefore carries about 8,800 lines of 156's unaccepted `src/server/**` and `tests/**` changes. Merging 168 now would bring 156 in with it.
- The trigger's instruction is STOP and report. This round changed no 156 file.
- **Integration must merge 156 first, then 168 (whose own diff is then only the 168 files), or rebase 168 onto `work` after 156 lands.**
- 168 cannot be rebased off 156 today, because it edits the I-1 row and the 156 note in the form 156 left them.
- **Re-checked r2 (2026-10-04):** `9544c3b01` (156 c1-r2) is still not an ancestor of `work/unified-access-control-r2` (`8166dd9ea`); it is reached only by `integ/uac-r2-batch4`, the 156/168/169 task branches. `git diff work...task/uac-r2-168-r1` = 133 files, +14,255 / −418, of which 156 alone (`work...9544c3b01`) is 57 files, +8,773 / −408, and 168's own diff (`144b36b51..r1`) is 78 files, +5,483 / −11. In-memory trial merges (`git merge-tree --write-tree`, no ref moved) of `work` + `9544c3b01` and of `work` + this branch are both conflict-free, so the order "156, then 168" merges cleanly today.

🔔 **ADR Conflict — Resolution Required (owner round 19 item 3, the editable home grids)**

- **ADR in question**: ADR-006 (UI surface architecture).
- **Specific rule**: "No new legacy JavaScript web resources. This means no jQuery, no framework-free JS with business logic, no ad hoc scripts." A PCF is the surface for UI embedded in a Dataverse host.
- **Conflict**: round 19 item 3 decided "an **OnRowLoad handler** that disables the root columns in the grid". Two facts, found in this round, make that mechanism unbuildable as written:
  1. **The event does not exist.** The Power Apps grid (`Microsoft.PowerApps.PowerAppsOneGrid`, `EnableEditing=yes` on the `sprk_event` and `sprk_analysis` home grids, live 2026-10-04) exposes three client events to a table's grid: **OnRecordSelect, OnChange and OnSave**. Sources: Microsoft Learn, "Grids and subgrids in model-driven apps" (Events table) and "Grid OnSave event". The nearest documented event, OnRecordSelect + `GridCell.setDisabled`, does not hold:
     - a practitioner write-up (Diana Birkelbach, "Power Apps Grid API: disabling", 2025-01) reports that the Power Apps grid lets a user move between cells by keyboard and edit them without OnRecordSelect firing;
     - grid OnSave cancellation is not documented.
  2. **A handler of that kind is a new framework-free JS web resource**, which ADR-006 forbids.
- **Proposed path: C (pivot to comply), by EXTENDING an existing component** *(corrected in f1, verifier item 4: the r1 text proposed "one new PCF" and never mentioned that the component already exists)*. The ADR-006-compliant and documented mechanism is a **Power Apps grid customizer control**: a PCF named in the grid's "Customizer control" property (Microsoft Learn, "Customize the editable grid control"). The repo already has one: **`src/client/pcf/SpaarkeGridCustomizer`** (`sprk_Spaarke.Controls.SpaarkeGridCustomizer`, a general-purpose grid customizer with a per-column renderer registry; until f1 it had only renderer overrides, and a read-only check of the live `customcontroldefaultconfigs` shows it set on neither the `sprk_event` nor the `sprk_analysis` grid). The option is to extend it with `cellEditorOverrides` and a renderer override, and set it on the two grids by a dry-run / apply / verify script (CLAUDE.md §11: extend, do not add).
  - Its `cellEditorOverrides` cancel editing for the four root columns, the documented `stopEditing(true)` pattern. That pattern is per cell, so it also covers keyboard navigation.
  - Its `cellRendererOverrides` mark those cells read-only.
  - Every other column keeps inline editing, which is round 19's intent.
  - It is set on the `sprk_event` and `sprk_analysis` grid control configuration (`customcontroldefaultconfig`, property `GridCustomizerControlFullName`) by a dry run / `-Apply` / `-Verify` script.
- **Rationale**: it is the only documented grid hook that acts on the editor itself. The same practitioner thread reports one residual for the `stopEditing` pattern: the Delete key clearing a cell. It must be checked on dev, with a renderer-side guard if it reproduces. The server's reconciliation job still repairs any root changed outside the forms, as the owner accepted in round 8 item 3.
- **Impact if accepted**: no new PCF; the existing `SpaarkeGridCustomizer` gains the editor and renderer overrides (version bump, build:prod, jest), plus one grid-configuration script and a deploy gate. No BFF change. *(Corrected in f1; the r1 text said "one new PCF under `src/client/pcf/`".)*
- **Alternatives considered and rejected**:
  - an OnRecordSelect web resource: keyboard bypass, and it breaks ADR-006 (path A would document a known-bypassable lock);
  - turning off grid editing: round 19 rejected it.
- **Why this part is STOPPED, not built**: the decided mechanism cannot exist, and the replacement changes the decision's mechanism (a grid customizer PCF in place of a handler) *(corrected in f1: the r1 text called it "a new component"; it is an extension of the existing `SpaarkeGridCustomizer`)*. CLAUDE.md §6.5 and the round-12 rule make that a first-class stop for the decision-maker. Item 3 is also not among this round's 16 verifier findings. The lock script keeps REPORTING the two grids until it is built.
- **DECIDED (owner/main-session round 25 item 8, 2026-10-04):** exactly the corrected option: extend `SpaarkeGridCustomizer` (cellEditorOverrides cancel editing of the four roots; cellRendererOverrides mark them read-only), set on the two grids by a dry-run / `-Apply` / `-Verify` script; no new PCF, no web-resource handler. Built in f1 (§12).

**Not a trigger, recorded:** §5.5 (the To Do main form has no `sprk_regardingservicerequest` cell) is still open, because round 19 does not name the to-do form. The picker script already closes it if pointed at that form. `Add-RegardingFilingPickerToForms.ps1 -Forms eca59df4-1364-f111-ab0c-7ced8ddc4cc6` would add only the missing hidden cell(s). It is not in the default target list; that is the main session's call.

### 10.6 Seeds (r1): each red, then restored byte-identical (SHA-256 compared, file touched)

| Seed | Mutation | Red |
|---|---|---|
| S5r | drop `if ($value.Trim() -ieq 'true') { return $Tag }` (the verifier's mutation) | 13-noncanonical-disabled-true-idempotent "output differs" |
| S12a | `Test-LockTransform` returns `@()` | all 7 negative inline parse cases |
| S12b | TRANSFORM_INCOMPLETE check removed | inline "targeted control left enabled" |
| S12c | `-SelfTest` passes IsManaged=`$false` (the verifier's observation) | 14-refuse-managed-form |
| S12d | MANAGED_FORM check removed | 14-refuse-managed-form |
| S12e | duplicate-disabled check removed | inline "duplicate disabled (disabled + Disabled)" |
| S12f | attribute comparison removed | inline "non-target attribute changed", "attribute added to a non-target" |
| S12g | text comparison removed | inline "text changed" |
| S12h | child-count check removed | inline "an element added" |
| S12i | code mapping collapsed to TRANSFORM_PARSE | inline "targeted control left enabled" (got TRANSFORM_PARSE) |
| S3p | `forceSubmit` removed from the empty-id branch | jest: the new empty-id test (1 failed, 21 passed) |
| P1 | presave handler always re-added | 01, 02, 04 "not idempotent", 03 "output differs" |
| P2 | PAIR_INCOMPLETE removed | 06 |
| P3 | picker entity check removed | 07 |
| P4 | MANAGED_FORM removed | 08 |
| P5 | PRESAVE_DISABLED removed | 09 |
| P6 | NO_VISIBLE_TAB refusal removed | 10 |
| P7 | `Test-PickerTransform` returns `@()` | the 8 negative inline parse cases |
| P8 | control-id collisions ignored | 01, 04, 05 (and 11) output differs |
| P9 | pair text columns get no cells | 01, 02, 04, 05 output differs |
| P10 | `-Verify` read fault exits 0 | `-Verify -EnvironmentUrl https://uac168-nonexistent-org.crm.dynamics.com`: guarded exit 1, seeded exit 0 |
| P11 | shallow match ignores attributes | inline positive control (a real transform reported as broken) |
| P12 | post-condition (form complete) removed | inline "picker names another entity", "hidden cell dropped", "presave handler dropped" |
| P13 | containers found by text search, not top-level | 13-nested-cell-events-untouched |
| C1 | column format not compared | inline "column as plain text" |
| C2 | rootcomponentbehavior not checked | inline "entity with rcb 1" |
| C3 | `-Verify` read fault exits 0 | unreachable URL: guarded exit 1, seeded exit 0 |
| C4 | column length not compared | inline "column too short" |

Final hashes (after all seeds): see §10.7.

### 10.7 Tests (r1)

| Suite | Result |
|---|---|
| `Lock-CoreAncestorStampColumnsOnForms.ps1 -SelfTest` | exit 0: **14 fixtures + 13 inline** (5 library/workflow + 8 parse-check) |
| `Add-RegardingFilingPickerToForms.ps1 -SelfTest` | exit 0: **13 fixtures + 9 inline** |
| `Add-AnalysisRegardingRecordUrlColumn.ps1 -SelfTest` | exit 0: **10 inline** |
| The same two fixture sets converted to CRLF (what a Windows checkout produces) | both exit 0 |
| Mode exclusivity | `-Apply -Verify` (schema script) and `-Apply -SelfTest` (picker script) each throw before any read: exit 1 |
| RegardingResolver jest, full (`npx jest`) | **115 passed, 3 suites** (114 + the new empty-id test), run twice green. A third run, made while `npm run build:prod` was running, reported one suite that failed to load (48 tests ran, 0 failed). Re-run alone, all three suites passed. **Corrected in r2 (verifier r1 item 9):** this was not timing contention. The same symptom reproduces deterministically when `RegardingResolver/generated/ManifestTypes` is absent (`RegardingResolverApp.test.tsx` fails to load), and `build:prod` regenerates that file; the likely cause is that the third run read it while the concurrent `build:prod` was regenerating it. Run jest after `build:prod`, never alongside it. |
| RegardingResolver `npm run build:prod` | succeeded |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 / 346 passed** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** |
| `tests/integration/Spe.Integration.Tests` | **403 passed, 25 skipped** (428), 0 failed |
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`), full run | **14,399 passed, 0 failed, 54 skipped** (14,453), 17 m 48 s |

Working-tree SHA-256 (CRLF) after the last seed; every seed restored to these bytes:
- `Lock-CoreAncestorStampColumnsOnForms.ps1` `d64b4db3…`;
- `Add-RegardingFilingPickerToForms.ps1` `b4707a97…`;
- `Add-AnalysisRegardingRecordUrlColumn.ps1` `8fc0f591…`;
- `sprk_todo_regarding_presave.js` `6d34b82b…` (unchanged in r1).

Scope against `01c591391` (r0):
- no change under `src/server/**`, `tests/Spaarke.ArchTests/**`, the RegardingResolver or CommunicationConnections PCF source, `src/client/webresources/**` or `src/client/shared/**`;
- no new web resource and no waiver;
- `.claude/**` needed no edit.

## 11. Round r2 (2026-10-04): the r1 verifier's findings

Branch `task/uac-r2-168-r2`, from `task/uac-r2-168-r1` (`0dccaaed8`). Input: the r1 verifier's 17 items. Items 1-3 and 11-13 verify r1 and need no change.

### 11.1 What changed, per item

| # | Item | Closure |
|---|---|---|
| 1, 2, 3, 11, 12, 13 | Verified green / correct by the verifier | No change. |
| 4, 15 | Seed: the picker script's PICKER_MISCONFIGURED **host-column** guard (`if ($p.HostColumn -ine $RecordTypeColumn)`) could be replaced by `if ($false)` with `-SelfTest` green; fixture 07 pins only the entity mismatch | **Fixed.** New fixture **`tests/fixtures/form-filing-picker/14-refuse-picker-wrong-host-column`**: the RegardingResolver names the RIGHT entity (`sprk_event`) but is hosted by a control bound to `sprk_regardingrecordname`; expected refusal PICKER_MISCONFIGURED. Seed Q1 (the verifier's exact mutation) makes **only** fixture 14 fail ("expected refusal PICKER_MISCONFIGURED, got []"). |
| 5, 15 | Seed: `Test-PickerTransform`'s `isAllowed` `'section'` branch could be replaced by `return $true` with `-SelfTest` green | **Fixed.** New inline case **"parse: a foreign section added"**: a `<section name="sprk_rogue">` inserted beside the original section of the real transform's output must be reported. Seed Q2 (the verifier's exact mutation) makes **only** that case fail. |
| 6, 16 | Trigger 9: the branch carries 156's unmerged work; not mergeable into `work` as it stands | **Not closable on this branch** (re-checked, §10.5): 156 (`9544c3b01`) is still not in `work` (`8166dd9ea`). The integration order stays **156 first, then 168**; both trial merges are conflict-free today. The 156 dependency is the POML `<gate>` itself ("startable after 156 is merged"): 168 edits the I-1 row and the 156 note in the form 156 left them, so it cannot be rebased off 156. |
| 7, 14 | §3 listed the r0 line numbers of the PATCH / POST calls | **Fixed.** §3 now lists the current numbers: restore PATCH :605, publish :609; apply PATCH :824, publish :829; `PublishXml` POST :557 inside `Publish-Table` (:555-558); the dry-run exit :762-765; the refusal lines :571 / :577 / :596 / :754 / :805 / :812 / :822. r2 changed one string on :723 and no line count. For the two r1 scripts (not required by criterion 2, recorded for the reviewer): picker `-RestoreFrom` from :715, restore PATCH :744, publish :747; dry-run exit :832; apply PATCH :875, publish :879; `PublishXml` POST :696 in `Publish-Table` (:694). Schema script: dry-run exit :244; apply POST `EntityDefinitions(...)/Attributes` :257, `PublishXml` POST :259; no other write. |
| 8 | Stale docs: the I-1 row still said "three forms with no filing picker plus the event / analysis editable home grids await owner decisions"; the lock script's grid REPORT pointed to "section 5.3" | **Fixed.** The I-1 row's task-168 parenthetical now names owner round 19, the schema and picker scripts and their order, the manual gates, and the STOPPED grid item (note §10.4-§10.5). The rest of the row is byte-identical. The lock script's grid REPORT text now points to "task 168 note section 10.5" (same line, :723). |
| 9 | §10.7 called the 48-tests-ran jest run "contention" | **Corrected** in §10.7: the same symptom reproduces deterministically when `RegardingResolver/generated/ManifestTypes` is absent, and `build:prod` regenerates it; a concurrent `build:prod` is the likely cause. Run jest after `build:prod`, never alongside it. |
| 10 | Goal (2) residual: after the picker is added, the raw pair controls and intermediate lookups stay visible and editable | **Raised as a first-class stop** (§11.2), confirmed on the live form XML (read-only GET, 2026-10-04). Not implemented; §11.2 says why. |
| 17 | Pending live gate | Still pending (main session), §10.4. |

### 11.2 🔔 First-class stop: the raw pair and intermediate lookup controls stay visible and editable (r1 verifier item 10)

**Facts (live dev, read-only GET of the form XML, 2026-10-04; "visible" = no `visible="false"` on the cell, section or tab; none is disabled):**

| Form | Visible, editable controls bound to a pair column or a non-root `sprk_regarding*` lookup (the picker script adds only MISSING columns and leaves these untouched) |
|---|---|
| sprk_event `90d2eff7` Event modal form | `sprk_regardingrecordtype` (plain lookup, twice), `sprk_regardingrecordid` (twice), `sprk_regardingrecordname` (twice), `sprk_regardingrecordurl`; lookups account, agreement, analysis, budget, contact, invoice, organization |
| sprk_event `835b8ee8` Event Assign Work main form | `sprk_regardingrecordtype` (plain lookup), `sprk_regardingrecordid`, `sprk_regardingrecordname` (one copy each; the url and the second type / id / name copies are hidden); lookups account, agreement, analysis, budget, contact, invoice, organization |
| sprk_communication `b58ec3d8` Message main form | `sprk_regardingrecordname`; lookups account, invoice, event, organization, person |
| sprk_event `eaf22dcb` Event main form, sprk_analysis `d408a721` | none (the pair columns there are hidden or absent) |

**Why it matters.** The job and the cascade derive the root from the row's PAIR (rule 3) or, with no pair, from a single intermediate (rule 5). On the two event forms a user can type a pair (`sprk_regardingrecordtype` = matter, `sprk_regardingrecordid` = a matter id) on a filed row; the job then stamps that matter as the root, so the row is re-filed under a root without the picker. That is the form-level input owner round 8 item 3 meant to remove; the lock covers only the four root columns. The intermediate lookups matter only on a row with no pair.

**Why this is stopped, not built.**
- Round 19 item 1 decided "the filing picker (RegardingResolver) with hidden cells for the pair and lookups". r1 implemented that as written: hidden cells are ADDED for the pair and lookup columns the form lacks. The decision does not say to hide or disable the controls users already have, and the POML constraint ("do not touch any other control") forbids it. Hiding them removes 6-14 visible inputs per form that users see today (for example account, organization and person on the Message form). That is a user-facing change beyond the decision's text (CLAUDE.md §6: scope expansion), and the verifier classed it as an owner question.
- The decision is between two complete fixes, which change different things:
  - **(a) Recommended: hide them.** Extend `Add-RegardingFilingPickerToForms.ps1` to add `visible="false"` to the `<cell>` of every existing control on the round-19 target forms that is bound to a pair column or a non-root `sprk_regarding*` lookup, except the picker's host control. The parse check gains exactly one allowed change: `visible="false"` on exactly those cells. Fixtures and seeds as for the other guards; `-Verify` fails on a visible one. Hidden controls stay enabled, so the RegardingResolver's `setValue` writes on a re-file and on a clear are submitted as today. The picker shows the chosen record, so the filing stays visible to the user.
  - **(b) Lock them** (`disabled="true"`, the lock script's mechanism). This keeps them on screen. But the RegardingResolver writes the pair text columns and the chosen lookup with `setValue` on UPDATE, and whether a disabled control's script-set value is submitted on UPDATE is unproven on dev (§2.6). The presave's `forceSubmit` runs on CREATE only, and the PCF source is out of scope, so (b) needs either a PCF change (forceSubmit in `applyResolverFields` / `clearRegarding`) or a live proof first.
- Per the owner's standing directive (round 15), no "accept" option is offered. The raw write outside the forms (Web API, import, flow), which the owner accepted in round 8 item 3, is a different path and is not affected.

### 11.3 Seeds (r2): each red, then restored byte-identical (SHA-256 compared, file touched)

| Seed | Mutation | Red |
|---|---|---|
| Q1 | `if ($p.HostColumn -ine $RecordTypeColumn) {` → `if ($false) {` (verifier item 4) | only **14-refuse-picker-wrong-host-column**: "expected refusal PICKER_MISCONFIGURED, got []"; exit 1 |
| Q2 | `'section' { return (…name -ceq $PickerSectionName -or … -ceq $HiddenSectionName) }` → `'section' { return $true }` (verifier item 5) | only **inline "parse: a foreign section added"**; exit 1 |

`Add-RegardingFilingPickerToForms.ps1` SHA-256 before and after both seeds: `2eda6187f732a3ff2ecb7f5e6a92d43f4b766c70fecd5f32aaa52e821ea811e2` (working tree, CRLF).

### 11.4 Tests (r2)

| Suite | Result |
|---|---|
| `Add-RegardingFilingPickerToForms.ps1 -SelfTest` | exit 0: **14 fixtures + 10 inline** (was 13 + 9) |
| `Lock-CoreAncestorStampColumnsOnForms.ps1 -SelfTest` | exit 0: **14 fixtures + 13 inline** (unchanged; one REPORT string edited) |
| `Add-AnalysisRegardingRecordUrlColumn.ps1 -SelfTest` | exit 0: **10 inline** (unchanged) |
| RegardingResolver jest / `build:prod` | not re-run: r2 changes no client file (the presave and the PCF are byte-identical to r1) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 / 346 passed** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** |
| `tests/integration/Spe.Integration.Tests` | **403 passed, 25 skipped** (428), 0 failed |
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`), full run | **14,399 passed, 0 failed, 54 skipped** (14,453), 19 m 5 s |

Working-tree SHA-256 (CRLF) after r2: `Lock-CoreAncestorStampColumnsOnForms.ps1` `8940e905…`; `Add-RegardingFilingPickerToForms.ps1` `2eda6187…`; `Add-AnalysisRegardingRecordUrlColumn.ps1` `0323532f…` (unchanged since r1; the committed r1 bytes hash to `0323532f…` in CRLF, so the `8fc0f591…` recorded in §10.7 was not the committed file's hash); `sprk_todo_regarding_presave.js` unchanged.

Scope against `0dccaaed8` (r1): one new fixture folder, one inline case and one REPORT string in the scripts, the I-1 row's task-168 parenthetical, this note and the POML. No change under `src/**`, under `tests/**` other than the new fixture, or under `.claude/**`; no new web resource, waiver, column or component.

## 12. Round f1 (2026-10-04): owner/main-session round 25 item 8 and the r2 verifier's findings

Branch `task/uac-r2-168-f1`, from `task/uac-r2-168-r2` (`4ef3ca04e`). Binding inputs: **round 25 item 8** (`notes/session27-owner-decisions-and-research.md` on `work/unified-access-control-r2` @ `73296ce14`, read in full; rounds 26-29 decide tasks 143/147/150/166 and name no 168 item), the r2 verifier's 15 items, and `NOTE-FROM-MAIN.md` (rounds 15/16/19/21: re-read; 15/16/21 decide tasks 162-167, round 19 was built in r1; nothing new for 168). Round 25 item 8, verbatim in substance: (a) hide every raw pair / non-root `sprk_regarding*` lookup control on the round-19 target forms (the picker's host excepted; `-Verify` fails on a visible one); the grid lock EXTENDS the existing `src/client/pcf/SpaarkeGridCustomizer` (cellEditorOverrides cancel editing for the four roots; cellRendererOverrides mark them read-only), set on the `sprk_event` / `sprk_analysis` grid configuration by a dry-run/apply/verify script, no new PCF, no web-resource handler (ADR-006); the To Do main form gets its missing `sprk_regardingservicerequest` hidden cell via the picker script; the `PICKER_WITHOUT_PRESAVE` fail-open probe is closed and seeded.

### 12.1 What changed, per verifier item

| # | Item | Closure |
|---|---|---|
| 1 | Round 25 (a): hide the raw pair / non-root lookup controls | **Built.** `Add-RegardingFilingPickerToForms.ps1` sets `visible="false"` on the `<cell>` of every VISIBLE control bound to a pair column (`sprk_regardingrecordtype`, `…id`, `…name`, `…url`, `…number`) or a `sprk_regarding*` LOOKUP of the table (live metadata) that is not one of the four roots. The picker's host control (the control whose `uniqueid` a RegardingResolver `controlDescription` names) is excepted; the four roots stay visible and are DISABLED by the lock script. A pure start-tag text edit at XmlReader positions (`Hide-CellsByIndex`, `Set-CellTagHidden`); the parse check allows exactly one change on an original node — `visible` becoming exactly `"false"` on a cell that holds a raw filing control (`Test-AllowedCellHide`) — and its post-condition fails on any raw control still visible. `-Verify` names each visible one (`GAP … raw filing control '…' (…) is VISIBLE`). New refusals: `RAW_CONTROL_HOSTS_PCF` (hiding would hide another PCF), `RAW_CONTROL_NOT_IN_CELL`, and, live, `LIBRARY_SHOWS` (a library on the form, or the presave, names a raw filing column AND calls `setVisible`; the mirror of the lock script's `LIBRARY_UNLOCKS`). `MANAGED_FORM` now also counts a visible raw control as "needs a change". Hidden controls stay ENABLED, so the RegardingResolver's `setValue` writes on a re-file and a clear are submitted as before. |
| 2 | Round 25: the grid lock extends `SpaarkeGridCustomizer` | **Built.** PCF v1.0.0 → **v1.1.0**. v1.0.0 did not implement the grid's customizer contract at all (a class with `getRendererOverrides()` and no `init`, a `data-set` instead of the `EventName` property), so the existing renderer registry never ran; v1.1.0 implements the documented contract (`ReactControl`; `init` fires `context.factory.fireEvent(EventName, { cellRendererOverrides, cellEditorOverrides })`). New `customizers/RootColumnLock.ts`: for the four roots (whole name, case-insensitive) the editor override calls `stopEditing(true)` and marks the column definition `editable = false`; the renderer override sets `columnEditable = false` and keeps the default renderer; registered for EVERY column data type. `customizers/GridCustomizer.ts` builds the customizer (a PCF entry module may export only the control class, pcf-1023). The existing regarding-link registry is kept, ported to the documented `(props, rendererParams)` signature; where it cannot resolve the target it now returns null (the default renderer) instead of replacing the cell with a plain span. Version bumped in ControlManifest.Input.xml, `CUSTOMIZER_VERSION` / the index.ts header, `Solution/solution.xml`, `Solution/Controls/…/ControlManifest.xml` (copied from the build output with `bundle.js` and `styles.css`), plus `Solution/pack.ps1` and `package.json`. `npm run build:prod` succeeded (bundle 13.4 KB). New jest suite (26 tests; the package had none — code-quality-r3 D7-05). New script **`scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1`** (dry run / `-Apply` / `-Verify` / `-RestoreFrom` / `-SelfTest`) adds `<GridCustomizerControlFullName static="true" type="SingleLine.Text">sprk_Spaarke.Controls.SpaarkeGridCustomizer</GridCustomizerControlFullName>` to every `Microsoft.PowerApps.PowerAppsOneGrid` control (form factors 0, 1, 2) of the two grids' `customcontroldefaultconfig.controldescriptionxml` — the shape a live, maker-configured grid carries (read-only check of the dev `mailbox` grid). `controldescriptionjson` is platform-derived (47 parameters, including manifest defaults, vs 28 in the XML), so it is not written; the read-back and `-Verify` require it to carry the customizer too, which fails loudly if the platform does not regenerate it. Refusals: `NO_CONFIG`, `MANAGED_CONFIG`, `NO_POWERAPPS_GRID`, `OTHER_CUSTOMIZER`, `TRANSFORM_PARSE`, `PREREQ_MISSING` (customcontrol absent or below v1.1.0), `CONFIG_CHANGED`. `-Apply` also adds the customcontrol to SpaarkeMaster (`AddSolutionComponent`, componenttype 66, the body `Assemble-SpaarkeMasterSolution.ps1` uses) when absent, and `-Verify` requires it: the grid configuration ships with the tables, so the customizer must ship too. `docs/procedures/production-release.md` listed SpaarkeGridCustomizer under **Excluded PCFs**; it now ships (row added, exclusion removed). |
| 3 | Round 25: the To Do main form's missing hidden cell; close and seed the PICKER_WITHOUT_PRESAVE probe | **Built.** `eca59df4` is in the picker script's default `-Forms`. On the live XML it gains exactly two hidden cells: `sprk_regardingservicerequest` and `sprk_regardingagreement` (also a regarding lookup of `sprk_todo` with no control; the script adds every missing one, by design). It already hosts the picker and the presave, and every raw control on it is already hidden, so nothing else changes. The probe: items 5 and 6 below. |
| 4 | §10.5's escalation proposed "one new PCF" and never mentioned SpaarkeGridCustomizer | **Corrected in place** in §10.5 and in the POML r1 `<escalations>` (each correction marked "corrected in f1"); round 25 then decided exactly the corrected option. |
| 5 | Fail-open: `//events/event` at any depth | **Fixed.** `Get-FormRefusals` reads `/form/events/event` (the path the picker script uses). New fixture **15-refuse-presave-only-in-nested-cell-events** (fixture 12 plus a web-resource cell whose own nested `<events>` holds the presave) expects `PICKER_WITHOUT_PRESAVE`; seed L1 (back to `//events/event`) fails exactly it with the verifier's message ("expected refusal PICKER_WITHOUT_PRESAVE, got []"). |
| 6 | Handler-name and enabled filters unpinned | **Pinned.** Fixtures **16-refuse-only-another-onload-handler** (only `Spaarke.SmartTodo.HideTabNav.onLoad` registered), **17-refuse-presave-handler-disabled** (`enabled="false"`) and, for the event-name filter, **18-refuse-presave-on-form-onsave-only** (the presave on the form-level `onsave` event). Seeds L2 / L3 / L4 (the verifier's two mutations, and the `onload` filter replaced by `$true`) each fail exactly one of them. |
| 7 | Fail-open: an empty in-scope formxml was skipped | **Fixed.** New pure `Get-FormReadRefusal`: an in-scope form (type 2, 7, 12) with no formxml is `FORM_UNREADABLE` (a refusal: `-Verify` exit 1, dry run / `-Apply` exit 2); another type is reported only. Five inline cases (types 2, 7, 12, a Quick View 6, and a positive control). Seeds L5 / L6 go red. |
| 8 | Merge order (trigger 9) | **Not closable on this branch.** Re-checked 2026-10-04: `9544c3b01` is still not an ancestor of `work/unified-access-control-r2` (now `73296ce14`); TASK-INDEX shows 156 `[open]`. Merge 156 first, then 168; the trial merge result is in §12.4. |
| 9-12 | Verifications of r2 | Recorded; no change needed. |
| 13 | Criterion 10 only against base `144b36b51` | Unchanged until 156 merges (§12.4). f1's own diff touches no `src/server/**`, no RegardingResolver or CommunicationConnections source, no `src/client/shared/**`, no web resource and no ArchTests file; it changes one existing PCF (SpaarkeGridCustomizer), which round 25 decided. |
| 14 | ADR-038: the presave guard's filters and nested lookup | Closed by items 5 and 6 (seeds L1-L4). |
| 15 | Live gate | Pending (main session): §12.5, which supersedes §10.4. |

**Placement / reuse (CLAUDE.md §10/§11).** No BFF change. No Dataverse plugin (ADR-002). No new web resource or form script, and no new PCF: the grid lock extends the existing customizer (ADR-006, round 25). New surface, with the three questions:
- `scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1` — *existing*: no script in `scripts/` touches `customcontroldefaultconfigs` or `GridCustomizerControlFullName` (Grep); the lock script edits form XML only. *Extension*: folding it into the lock script would mix an attribute-only form transform with a grid-configuration insertion and a solution-membership write, each with its own snapshot and restore. *Cost of nothing*: the two editable grids keep letting a person type a secure root onto a filed row inline (round 25 item 8 unmet).
- jest + ts-jest + jest-environment-jsdom + @types/jest + scheduler as devDependencies of SpaarkeGridCustomizer — test-only (React and Fluent are platform libraries at runtime, ADR-022); without them the lock cannot be tested or seeded (ADR-038).
- `AddSolutionComponent` in `-Apply` — the same call and body the assembler uses; without it the grid configuration ships in SpaarkeMaster naming a customizer that was on the release's exclusion list.

### 12.2 Live runs (spaarkedev1, read-only, 2026-10-04)

- Picker dry run: prerequisites present; PLAN for `eaf22dcb`, `90d2eff7`, `835b8ee8`, `b58ec3d8` and the To Do `eca59df4` (two hidden cells); `PAIR_INCOMPLETE` for the Analysis form until the schema apply → **exit 2** (expected). No `LIBRARY_SHOWS` (the To Do form's four libraries were read: none calls `setVisible`).
- Picker `-Verify`: **75 gaps + 1 refusal → exit 1**; 30 of the gaps are VISIBLE raw filing controls (14 on `90d2eff7`, 10 on `835b8ee8`, 6 on `b58ec3d8`), and 2 are the To Do form's missing cells.
- Lock dry run → exit 2 (the three `NO_FILING_PICKER`); lock `-Verify` → **exit 1, 16 unlocked controls, 3 refusals** (no `FORM_UNREADABLE`: every form returned XML); its grid REPORT now names the grid script.
- Grid dry run: PLAN on form factors 0/1/2 of both grids and SpaarkeMaster membership; `PREREQ_MISSING` (dev holds customcontrol v1.0.0) → **exit 2** (expected until the PCF is deployed). Grid `-Verify`: **13 gaps + 1 refusal → exit 1** (6 XML and 6 JSON form-factor gaps, and SpaarkeMaster membership).
- **Offline end-state proof on the live XML of all six target forms** (read-only GET; the analysis specs given `sprk_regardingrecordurl` as after the schema apply): picker transform → lock refusals → lock transform → parse checks.

| Form | Picker adds | Raw cells hidden | Picker parse | Lock refusals | Roots locked | Complete after lock | Idempotent |
|---|---|---|---|---|---|---|---|
| event main `eaf22dcb` | 18 | 0 | ok | none | 4 | yes | yes |
| event modal `90d2eff7` | 22 | 14 | ok | none | 4 | yes | yes |
| Assign Work `835b8ee8` | 18 | 10 | ok | none | 4 | yes | yes |
| Message `b58ec3d8` | 15 | 6 | ok | none | 4 | yes | yes |
| Analysis main `d408a721` | 15 | 0 | ok | none | 4 | yes | yes |
| To Do main `eca59df4` | 2 | 0 | ok | none | 4 | yes | yes |

- **What can re-show a hidden control at runtime.** Form libraries: guarded by `LIBRARY_SHOWS`. EventFormController (on `90d2eff7` / `835b8ee8`, customcontrol v1.0.6, no source in the repo; its bundle read from `projects/pcf-orphan-cleanup-r1/backups-2026-06-22/…EventFormControllerSolution…zip`, manifest v1.0.6): `FieldVisibilityHandler` resets only the fields in `DEFAULT_FIELD_STATES` (no regarding column) and shows the fields an event type marks required, which it reads with `?$select=sprk_name,sprk_requiredfields,sprk_hiddenfields` — columns that do not exist on dev `sprk_eventtype_ref` (only `sprk_fieldconfigjson` exists; 1 of 17 event types has one, holding only `sectionDefaults`). No regarding column is named anywhere in the bundle. So no live path re-shows a hidden raw control.

### 12.3 Seeds (f1): each red, then restored

Script seeds ran on scratch copies (the repo files were never modified; SHA-256 compared before and after each); jest seeds edited the file in place and restored it byte-identical (SHA-256 compared, file touched).

| Seed | Mutation | Red |
|---|---|---|
| L1 | `/form/events/event` → `//events/event` | only fixture 15 ("expected refusal PICKER_WITHOUT_PRESAVE, got []") |
| L2 | drop `-and $_.GetAttribute('enabled') -ine 'false'` | only fixture 17 |
| L3 | `functionName -ceq $PresaveOnLoad` → `-ne ''` | only fixture 16 |
| L4 | the `onload` name filter → `$true` | only fixture 18 |
| L5 | `Get-FormReadRefusal` always `$null` | the three in-scope read cases |
| L6 | the in-scope type gate removed | inline "Quick View (6) only reported" |
| P14 | the hide step skipped | fixtures 01, 04, 05, 11, 12, 13, 15 and four inline cases |
| P15 | roots treated as raw filing columns | 01, 03, 04, 05, 11, 12, 13, 15 |
| P16 | any `sprk_regarding*` spec (not only lookups) raw | only fixture 15 (its Text column `sprk_regardingsummary`) |
| P17 | the picker-host exception removed | 01-05, 11-13, 15 and the inline positive control |
| P19 | `visible="true"` added to rather than rewritten | only fixture 15 (duplicate attribute, reported per case) |
| P20 | `RAW_CONTROL_HOSTS_PCF` removed | only fixture 16 |
| P21 | `RAW_CONTROL_NOT_IN_CELL` removed | only fixture 17 |
| P22 | `Test-AllowedCellHide` allows any cell | inline "a non-raw cell hidden" |
| P23 | the exact `"false"` comparison loosened | inline "raw cell visible set to a non-false value" |
| P24 | the "still visible" post-condition removed | inline "a raw filing cell left visible" |
| P25 | `Test-LibraryShows` without the `setVisible` test | inline "raw column, no setVisible" |
| P26 | `Test-LibraryShows` by substring | inline "setVisible, near-miss column only" |
| P27 | `Test-FilingComplete` ignores visible raw controls | only fixture 18 (managed form whose only change is the hide) |
| G1 | `OTHER_CUSTOMIZER` removed | only grid fixture 05 |
| G2 | `NO_POWERAPPS_GRID` removed | only 06 |
| G3 | `MANAGED_CONFIG` removed | only 07 |
| G4 | any extra child accepted | inline "customizer added to a non-grid control" |
| G5 | the exact-parameter check removed | inline "customizer added with other attributes" |
| G6 | the post-condition removed | inline "one form factor left without it" |
| G7 | the already-set tracking removed (always insert) | 01-04 |
| G8 | the JSON half of the verify predicate removed | the two JSON inline cases |
| G9 | `-Verify` read fault exits 0 | live, unreachable URL: guarded exit 1, seeded exit 0 |
| G10 | the v1.1.0 prerequisite removed | live dry run (read-only): guarded exit 2, seeded exit 0 |
| J1 | `stopEditing(true)` removed | jest: 7 failed |
| J2 | case-sensitive match | jest: 1 failed (the mixed-case test) |
| J3 | prefix/suffix match | jest: 3 failed (the near-miss negatives) |
| J4 | `columnEditable = false` removed | jest: 7 failed |
| J5 | `fireEvent` removed from `init` | jest: 1 failed |
| J6 | the editor override registered for Lookup only | jest: 1 failed ("every data type …") |
| J7 | the renderer root check removed | jest: 7 failed |
| J8 / J9 | `column.editable = false` removed (renderer / editor) | jest: 4 failed each |

Mode exclusivity of the new script: `-Apply -Verify`, `-Verify -SelfTest`, `-SelfTest -Apply` and `-RestoreFrom -Apply` each throw "separate modes", exit 1, before any read.

### 12.4 Tests (f1) and merge order

| Suite | Result |
|---|---|
| `Lock-CoreAncestorStampColumnsOnForms.ps1 -SelfTest` | exit 0: **18 fixtures + 18 inline** (was 14 + 13) |
| `Add-RegardingFilingPickerToForms.ps1 -SelfTest` | exit 0: **18 fixtures + 17 inline** (was 14 + 10) |
| `Set-SpaarkeGridCustomizerOnChildGrids.ps1 -SelfTest` (new) | exit 0: **7 fixtures + 15 inline** |
| `Add-AnalysisRegardingRecordUrlColumn.ps1 -SelfTest` | unchanged (10 inline) |
| SpaarkeGridCustomizer jest (new) | **26 / 26**; `npm run build:prod` succeeded, again after the pre-commit Prettier pass (bundle identical to the committed copy apart from CRLF) |
| RegardingResolver jest | not re-run: no file of that package changed since r1 (115 / 115) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 / 346 passed** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104 / 104 passed** |
| `tests/integration/Spe.Integration.Tests` | **403 passed, 25 skipped** (428), 0 failed |
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`), full run | 14,390 passed, **9 failed**, 54 skipped (14,453), 28 m 41 s under concurrent load; the 9 (WebApplicationFactory contract / seam tests in Compose, Office, Documents, Ai handlers, ExternalAccess upload, PipelineHealth) **all pass on an isolated re-run (34 / 34 with theories expanded)**. No `src/server/**` or `tests/unit/**` file changed in f1: contention. |

**Merge order (trigger 9), re-checked 2026-10-04:** `9544c3b01` (156) is not an ancestor of `work/unified-access-control-r2` (`73296ce14`). In-memory trial merges (`merge-tree --write-tree`) of work with 156 and of work with this branch are both conflict-free. The diff `work...task/uac-r2-168-f1` is 196 files (156's changes included); against the task base `144b36b51` it is 141 files, of which f1 adds 73 (`4ef3ca04e..`). Merge 156 first, then 168.

### 12.5 Manual live gate (main session, dev, in this order; supersedes §10.4)

1. **(a)** No other formxml / grid-configuration writer is running (task 138's retirement script, `Deploy-TodoSubgridsToElevenParentForms.ps1`).
2. **(b)** Presave v1.4.0 to the EXISTING web resource `sprk_todo_regarding_presave` and publish (as §10.4 (b)).
3. **(c)** Schema script: dry run, `-Apply`, `-Verify` (exit 0).
4. **(d)** Picker script: dry run (no refusal), `-Apply -SnapshotPath .\filing-picker-snapshot-dev.json`, `-Verify` (exit 0). It now also hides the raw controls and covers the To Do main form.
5. **(e)** Lock script: dry run (exit 0, no refusal), `-Apply -SnapshotPath .\form-lock-snapshot-dev.json`, `-Verify` (exit 0).
6. **(f)** Deploy SpaarkeGridCustomizer **v1.1.0** (pcf-deploy: `Solution/pack.ps1`, import `SpaarkeGridCustomizerSolution_v1.1.0.zip`, publish). Confirm the customcontrol reads version 1.1.0.
7. **(g)** Grid script: dry run (exit 0), `-Apply -SnapshotPath .\grid-customizer-snapshot-dev.json`, `-Verify` (exit 0 — this also proves the platform regenerated `controldescriptionjson`).
8. **(h)** Checks with `uac.child.user@demo.spaarke.com`: §10.4 (f) (b)-(l), plus:
   - **(m)** on the Event main, modal, Assign Work, Message and Analysis main forms no pair column and no non-root regarding lookup is visible; the picker shows the filing; a re-file through the picker (UPDATE) writes the new pair and stamps (the hidden controls are enabled);
   - **(n)** in the `sprk_event` and `sprk_analysis` home grids (add the four root columns with "Edit columns"): double-click, Enter, F2, typing, Delete, Backspace, Ctrl+V and a range paste on a root cell each leave its value unchanged on read-back; another column still edits inline; the console shows `[SpaarkeGridCustomizer v1.1.0] customizer registered`; a regarding name cell still renders.
9. **(i)** On any failure, restore in REVERSE order — grid `-RestoreFrom`, lock `-RestoreFrom`, picker `-RestoreFrom` (each refuses if a later change touched its rows) — and STOP (trigger 7). A root changed through any grid path in (n) is a failure of (n).

```
pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1
pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1 -Apply
pwsh scripts/Add-AnalysisRegardingRecordUrlColumn.ps1 -Verify
pwsh scripts/Add-RegardingFilingPickerToForms.ps1
pwsh scripts/Add-RegardingFilingPickerToForms.ps1 -Apply -SnapshotPath .\filing-picker-snapshot-dev.json
pwsh scripts/Add-RegardingFilingPickerToForms.ps1 -Verify
pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1
pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -Apply -SnapshotPath .\form-lock-snapshot-dev.json
pwsh scripts/Lock-CoreAncestorStampColumnsOnForms.ps1 -Verify
pwsh src/client/pcf/SpaarkeGridCustomizer/Solution/pack.ps1
pac solution import --path src/client/pcf/SpaarkeGridCustomizer/Solution/bin/SpaarkeGridCustomizerSolution_v1.1.0.zip --publish-changes
pwsh scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1
pwsh scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1 -Apply -SnapshotPath .\grid-customizer-snapshot-dev.json
pwsh scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1 -Verify
```

Live gate results: _pending (main session)_.

### 12.6 🔔 First-class stop (f1): the raw filing COLUMNS stay inline-editable in the two grids

- **Fact.** Round 25 item 8 locks the four ROOT columns in the `sprk_event` / `sprk_analysis` grids. The same grids let a person add `sprk_regardingrecordtype` / `sprk_regardingrecordid` (the pair) or a non-root `sprk_regarding*` lookup with "Edit columns" and edit it inline (no system view shows them today, §2.4). Typing a pair onto a filed row re-files it under a root on the job's next pass — the shape item 8 (a) closes on the forms.
- **Why stopped.** Item 8 names the four roots for the grid in the same sentence that names the raw pair for the forms; widening the grid lock is a scope change to a decided item (CLAUDE.md §6), and no decision answers it.
- **Complete fix (recommended).** The grid lock covers the same column set the forms hide: `RootColumnLock` gains the raw filing columns (the pair columns and every non-root `sprk_regarding*` lookup of the grid's table), by name, with the same editor/renderer overrides; jest cases and seeds as for the roots; v1.1.1, build:prod, the same grid script (no change). No new component.

### 12.7 `.claude/**` edits needed

None.

## 13. Round v1 (2026-10-05): the f1 verifier's items and owner round 38

Branch `task/uac-r2-168-f1-v1`, from `task/uac-r2-168-f1` (`a61d6a356`). Binding inputs: the f1 verifier's 13 items; owner/main-session **round 38** (`notes/session27-owner-decisions-and-research.md` on `work/unified-access-control-r2` @ `59014238d`, which decides the f1 stop of §12.6; rounds 39-42 decide tasks 158, 150, 165 and 140 and name no 168 item). Round 38, in substance: the grid lock covers the same columns the forms hide (the ADR-024 pair and every non-root `sprk_regarding*` lookup), BY NAME and from ONE shared list, the list the form scripts use (never a second copy), with the same editor and renderer overrides; v1.1.1 bumped in all four places; jest cases and a seed per override; the grid configuration script unchanged. No `NOTE-FROM-MAIN.md` was present.

### 13.1 What changed, per verifier item

| # | Item | Closure |
|---|---|---|
| 1, 11 | MEDIUM fail-open: the picker's `-Verify` passed with a business rule or business process flow that shows or exposes a raw filing column | **Fixed.** New pure `Get-WorkflowRefusals` in `Add-RegardingFilingPickerToForms.ps1`, the mirror of the lock script's `WORKFLOW_REFERENCE` over the raw filing columns: a business rule (category 2) whose `primaryentity` is the table, or a business process flow (category 4) whose `primaryentity` is the table or whose xaml / clientdata names it as a whole word, whose xaml or clientdata references a raw filing column of the table (whole name, case-insensitive). Other categories are not form inputs and are ignored. The raw set is the new `Get-RawFilingColumnNames` (record-type column, pair text columns, the table's non-root `sprk_regarding*` lookups; never a root), also used by `LIBRARY_SHOWS` now. The live scan reads `workflows` once per table (`(category eq 2 and primaryentity eq '<table>') or category eq 4`), whatever the form's own state, and each hit is BOTH a refusal (`WORKFLOW_REFERENCE`: dry run / `-Apply` exit 2) AND a `-Verify` gap (exit 1). A read fault there is a `VERIFY FAIL` (the existing catch). Fixtures **19-refuse-business-rule-shows-raw-column** (a SetVisibility rule on `sprk_regardingRecordId`) and **20-refuse-process-flow-exposes-raw-lookup** (a flow naming `sprk_event` with a `sprk_regardingcommunication` step), each on fixture 03's complete form so the workflow is the only refusal; the self-test runner feeds a case's optional `workflows` rows through the same function. Nine inline cases pin each filter, plus one for the raw column set. Seeds W1-W8 and the live wiring proof W9 (§13.3). |
| 2, 13 | The SpaarkeGridCustomizer jest suite failed on a clean checkout (TS2307), contradicting its config comment | **Fixed.** Reproduced first in this worktree (no `generated/`: TS2307 at `index.ts:22`, 0 tests). `moduleNameMapper` reaches only jest's run-time resolution, and a `tsconfig` `paths` entry cannot map a RELATIVE import. New `jest.globalSetup.js` runs the toolchain's own generator, `pcf-scripts refreshTypes`, once before any file is compiled (offline, about 1 s, telemetry opted out with `PP_TOOLS_TELEMETRY_OPTOUT`; a failure, or no emitted file, stops the run). The tests now type-check against the REAL manifest types; the hand-written stub `__tests__/__mocks__/manifestTypes.ts` and its mapper entry are deleted (index.ts imports only interfaces, which TypeScript erases). `jest.config.js`'s comment now says what the config does. Seed J19 (no `globalSetup`, `generated/` deleted) gives exactly the verifier's failure (TS2307, 0 tests). A fresh detached worktree at the commit: §13.4. |
| 3, 12 | ADR-038: the grid's "no PowerAppsOneGrid control in controldescriptionxml" gap was unpinned | **Pinned.** Inline case "verify: xml has no grid control (json valid)": a configuration with no grid control and a complete, valid JSON must report a gap. Seed G11 (the verifier's exact mutation, `if ($false)`) fails exactly that case. |
| 4 | The grid script's dry run / `-Apply` said "Nothing to do" (exit 0) when the XML named the customizer but `controldescriptionjson` did not | **Fixed.** New pure `Get-GridTableAction` (`edit` / `publish` / `none`) and `Get-GridRunOutcome` (`act` / `unresolved` / `nothing`). A table whose XML names the customizer everywhere but whose platform-derived JSON does not is planned as `publish` (the JSON is regenerated only by a publish): the dry run prints `PLAN publish <table>` and exits 0; `-Apply` re-reads it before its first write (`CONFIG_CHANGED`, nothing written, if the XML moved), publishes it, records it in the snapshot (`publishedOnly`), and reads it back (still a gap = exit 1). A gap that no planned change closes stops the run with `UNRESOLVED` and exit 1; "Nothing to do" only when there is no gap. Eight inline cases; seeds G12-G14; live simulation G15 reproduces the f1 bug and shows the fix (§13.3). |
| 5 | Stale help text in the picker script | **Corrected.** `.PARAMETER Forms` names the six default forms (the five of round 19 and the To Do main form of round 25); `.DESCRIPTION` says the To Do form gains only the hidden cells it lacks, on dev two: `sprk_regardingservicerequest` and `sprk_regardingagreement`. |
| 6 | Observation: §12.3 entries P27, P15 and J3 | **No change**, as the verifier found: the committed note reads correctly. |
| 7 | Observation: one flaky integration test and the f1 counts | Recorded; this round's full runs are in §13.4. |
| 8, 9, 10 | Verifications of f1 (seeds, live state, scope) | Recorded; no change. |
| round 38 | The grid lock covers the same columns the forms hide, from ONE shared list | **Built.** New **`config/regarding-filing-columns.json`** (`rootColumns`, `recordTypeColumn`, `pairTextColumns`, `lookupPrefix`, `lookupTypes`, plus the rule in words; the `config/secure-record-owner-role.json` precedent of a single-source list). `Lock-CoreAncestorStampColumnsOnForms.ps1` reads its locked columns (`rootColumns`) from it; `Add-RegardingFilingPickerToForms.ps1` reads the roots, the pair, the prefix and the lookup types from it (its `Get-LiveColumnSpecs`, `Get-NeededColumns` and `Test-IsRawFilingColumn` use the prefix and types); both take `-FilingColumnsPath` and stop (exit 1, `FILING_COLUMNS`) on a missing, malformed or incomplete file. The PCF imports the same file (webpack bundles only the used properties): `RootColumnLock.ts` now locks every FILING column (a root or a pair column whatever its type; a `sprk_regarding*` column whose type is a lookup type, read from the column definition, the cell props or the type the grid dispatched for), with the SAME editor (`stopEditing(true)`, `editable = false`) and renderer (`columnEditable = false`, `editable = false`) overrides; a pair name / id cell keeps its regarding link and is shown read-only; a root never gets a link (as before). **v1.1.1** in `ControlManifest.Input.xml`, `CUSTOMIZER_VERSION` / the index.ts header, `Solution/solution.xml`, `Solution/Controls/…/ControlManifest.xml` (copied from the build with `bundle.js` and `styles.css`), plus `Solution/pack.ps1`, `package.json` and `package-lock.json`. Jest 26 → **50** tests. The grid script writes the same configuration; its prerequisite is now **v1.1.1** (`$MinCustomizerVersion`), so `-Verify` cannot pass on a v1.1.0 customizer that leaves the pair and the intermediate lookups editable (fail closed, ADR-003). Docs: the I-1 row's task-168 parenthetical, `production-release.md`, `sdap-pcf-patterns.md`, `client-resources-inventory.md`. |

**Placement / reuse (CLAUDE.md §10/§11).** No BFF change (nothing under `src/server/**`). No Dataverse plugin, business rule or web resource (ADR-002, ADR-006); no new PCF. New surface, with the three questions:
- `config/regarding-filing-columns.json` — *existing*: the names lived in three copies (`$LockedColumns` in the lock script, `$RootColumns` / `$PairTextColumns` / `'sprk_regarding*'` in the picker script, `LOCKED_ROOT_COLUMNS` in the PCF; Grep); `config/secure-record-owner-role.json` is the repo's single-source-list precedent. *Extension*: a PowerShell script and a webpack bundle cannot share a code constant; one data file both read is the only single copy. *Cost of nothing*: round 38's "ONE shared list, never a second copy" is unmet, and the grid and form locks drift the first time a pair column is added.
- `src/client/pcf/SpaarkeGridCustomizer/jest.globalSetup.js` — *existing*: no jest setup in the package; `moduleNameMapper` cannot reach the type-checker. *Extension*: it calls the toolchain's own `refreshTypes`, adding no generator and no stub. *Cost of nothing*: the suite runs 0 tests on a clean checkout (verifier item 2).
- No new endpoint, service, registration, package, option, job or column. The CVE and publish-size checks do not apply (no BFF change).

### 13.2 Live runs (spaarkedev1, read-only, 2026-10-05)

- Picker `-Verify`: **exit 1, 75 gaps (30 visible raw controls) + 1 refusal (`PAIR_INCOMPLETE`)**, as in f1. New per-table line: `read 4 business rule(s) of the table and process flow(s) of the environment; WORKFLOW_REFERENCE: 0`. The four rows are the environment's process flows (`knowledgearticle` ×3, `contact` ×1; a read-only query of `workflows`); no business rule exists on the four tables (as inventory §2.3).
- Lock `-Verify`: **exit 1, 16 unlocked controls, 3 `NO_FILING_PICKER`** (unchanged; its columns now come from the shared list).
- Grid dry run: **exit 2** (`PREREQ_MISSING`: dev holds v1.0.0, the prerequisite is now v1.1.1). Grid `-Verify`: **exit 1, 13 gaps + 1 refusal** (as in f1).
- Unreachable URL: picker and grid `-Verify` each exit 1 (`VERIFY FAIL: read fault`).

### 13.3 Seeds (v1): each red, then restored

Script seeds ran on scratch copies (`Run-ScriptSeeds.ps1`; the repo scripts' SHA-256 unchanged before and after); jest seeds edited the file in place and restored it byte-identical (SHA-256 compared, file touched).

| Seed | Mutation | Red |
|---|---|---|
| W1 | `Get-WorkflowRefusals` never reports (`continue` for every row) | fixtures 19, 20 and three inline positives |
| W2 | the business-rule `primaryentity` filter removed | only inline "rule of another table" |
| W3 | the process-flow table filter removed | inline "flow of another table only" and "flow names the table only as a prefix" |
| W4 | substring column match | only inline "rule references a near-miss column" |
| W5 | substring table match for a flow | only inline "flow names the table only as a prefix" |
| W6 | the category filter removed (every row a business rule) | only inline "a classic workflow (category 0)" |
| W7 | a flow's `primaryentity` no longer counts | only inline "flow on the table (primaryentity)" |
| W8 | roots in the raw set (every lookup raw) | inline "rule references only a root" and "raw names" |
| W9 | LIVE wiring (read-only, scratch copy): a synthetic business rule on `sprk_event` that shows `sprk_regardingrecordid` injected into the query result | guarded: `-Verify` names `REFUSAL WORKFLOW_REFERENCE` and its `GAP` (76 gaps, 2 refusals), dry run exit 2; with the refusal/gap wiring removed: neither line (75, 1) |
| G11 | the "no grid control" gap → `if ($false)` (the verifier's mutation) | only inline "verify: xml has no grid control (json valid)" |
| G12 | the `publish` action removed | inline "xml has it, json does not" and "json names another" |
| G13 | `unresolved` removed | only inline "a gap, nothing planned -> unresolved" |
| G14 | a planned publish not counted as an action | only inline "only a publish planned -> act" |
| G15 | LIVE simulation (read-only, scratch copy, `-Tables sprk_event`; prerequisite and membership treated as met; the XML replaced in memory by the transformed XML while the live JSON lags) | fixed: dry run `PLAN publish sprk_event`, exit 0; `-Verify` exit 1 (3 JSON gaps). With the f1 guard: dry run "Nothing to do", exit 0, while `-Verify` exits 1 — the verifier's finding, reproduced |
| L7 | the lock script with a stale second copy of the roots (three, not read from the file) | only fixture 04 (`sprk_RegardingServiceRequest`) |
| C5 | the shared file without `sprk_regardingmatter` (scratch copy via `-FilingColumnsPath`; jest: in place) | lock: 14 cases; picker: 8 fixtures + 2 inline; jest: "names the four roots …" |
| C6 | the shared file missing, malformed, or with no `rootColumns` | both form scripts exit 1 (`FILING_COLUMNS`) before any work |
| J10 | the pair columns not locked | jest: 13 failed |
| J11 | the lookup branch never locks | jest: 9 failed |
| J12 | the lookup-type check removed (any `sprk_regarding*` locked) | jest: 5 failed (the non-lookup negatives) |
| J13 | the prefix check removed (any lookup locked) | jest: 3 failed (`x_sprk_regardingmatter`, `sprk_matter`) |
| J14 | the link renderer runs before the read-only marking | jest: 4 failed |
| J15 | a stale second copy of the roots in `RootColumnLock.ts` | jest: 3 failed (incl. "reads its columns from the shared file") |
| J16 / J17 / J18 | the column-definition / cell-props / dispatched data type ignored | jest: 1 / 1 / 7 failed |
| J19 | `globalSetup` removed, `generated/` deleted | TS2307 at `index.ts`, 0 tests (item 2's failure) |

Restored hashes (SHA-256): `RootColumnLock.ts` `DE04942B…2E33`, `GridCustomizer.ts` `3C75FB71…8F6C`, `jest.config.js` `79C801D1…11E9`, `config/regarding-filing-columns.json` `6B83E852…9250`.

### 13.4 Tests (v1)

| Suite | Result |
|---|---|
| `Add-RegardingFilingPickerToForms.ps1 -SelfTest` | exit 0: **20 fixtures + 27 inline** (was 18 + 17) |
| `Lock-CoreAncestorStampColumnsOnForms.ps1 -SelfTest` | exit 0: 18 fixtures + 18 inline (unchanged; its columns now come from the shared list) |
| `Set-SpaarkeGridCustomizerOnChildGrids.ps1 -SelfTest` | exit 0: **7 fixtures + 24 inline** (was 7 + 15) |
| `Add-AnalysisRegardingRecordUrlColumn.ps1 -SelfTest` | exit 0, 10 inline (unchanged) |
| SpaarkeGridCustomizer jest | **50 / 50** (was 26); `npm run build:prod` succeeded (bundle 13,998 bytes) |
| SpaarkeGridCustomizer on a clean copy of the commit (`git archive` of the package and `config/` only: no `generated/`, no `node_modules`) | `npm install --legacy-peer-deps --no-audit --no-fund`, then `npx jest`: **50 / 50, no prior build**. Its `build:prod` output (`bundle.js`, `ControlManifest.xml`, `styles.css`) is identical to the committed `Solution/Controls` copies (CRLF ignored). |
| ESLint (`src/client/pcf` config) on the changed PCF sources; Prettier `--check` | 0 problems; clean (the pre-commit hook changed nothing) |
| RegardingResolver jest | not re-run: no file of that package changed since r1 (115 / 115) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 / 346** |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | **104 / 104** |
| `tests/integration/Spe.Integration.Tests` | **403 passed, 25 skipped, 0 failed** (428) |
| BFF unit (`tests/unit/Sprk.Bff.Api.Tests`), full run | **14,399 passed, 54 skipped, 0 failed** (14,453), 20 m 6 s |

No file under `src/server/**` or `tests/unit/**` changed in this round. Final script hashes (SHA-256, the seeds re-run against them): picker `5AFFF21F…38FE`, grid `B26E7820…3098`, lock `EF41BC28…FE4B`.

### 13.5 Manual live gate: amendments to §12.5

- **(f)** Deploy SpaarkeGridCustomizer **v1.1.1** (`Solution/pack.ps1`, import `SpaarkeGridCustomizerSolution_v1.1.1.zip`, publish); confirm the customcontrol reads **1.1.1** (the grid script refuses anything older).
- **(g)** Grid script dry run: it may now also plan `publish <table>` (the JSON lags the XML); `-Apply` publishes and reads back.
- **(h)(n)** In the `sprk_event` and `sprk_analysis` home grids, add with "Edit columns" the four roots AND `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordtype` and one non-root regarding lookup (for example `sprk_regardingcommunication`): each of the edit paths of §12.5 (n) leaves the value unchanged on read-back; another column still edits inline; the console shows `[SpaarkeGridCustomizer v1.1.1] customizer registered (filing columns not editable)`; a regarding name cell still renders as a link.
- Picker `-Verify` now also fails on a business rule or process flow that references a raw filing column; it exits 0 only with none.

```
pwsh src/client/pcf/SpaarkeGridCustomizer/Solution/pack.ps1
pac solution import --path src/client/pcf/SpaarkeGridCustomizer/Solution/bin/SpaarkeGridCustomizerSolution_v1.1.1.zip --publish-changes
pwsh scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1
pwsh scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1 -Apply -SnapshotPath .\grid-customizer-snapshot-dev.json
pwsh scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1 -Verify
```

Trigger 9 (re-checked 2026-10-05): `9544c3b01` (156) is still not an ancestor of `work/unified-access-control-r2` (`59014238d`); a `merge-tree --write-tree` of work with this branch is conflict-free. Merge 156 first, then 168.

### 13.6 `.claude/**` edits needed

None (no `.claude/**` file names SpaarkeGridCustomizer, the picker script or the shared list).
