# Task 168: lock the four root columns on the child forms

> **Task**: 168 (#1107) · owner round 8 item 3 · amendments: owner rounds 9-10 (round 10 item 2 = add the picker to the analysis form, then lock)
> **Branch**: `task/uac-r2-168`, from `task/uac-r2-156-c1-r2` with `work/unified-access-control-r2` merged in (clean merge, no conflicts). Base `144b36b51`.
> **Rigor**: FULL (security edge, live form definitions, TEST-MODIFYING). Model tier sonnet @ high (run on Opus).
> **Status**: **completed-with-escalation.** The code, fixtures, presave, tests and docs are done and green. Three escalation triggers fired on live dev and one blocker was found in the analysis amendment (below). The live `-Apply` is the main session's manual gate. As the script stands it REFUSES the full four-table apply until the owner decides on trigger 1.

---

## 0. Outcome in one screen

| Item | State |
|---|---|
| `scripts/Lock-CoreAncestorStampColumnsOnForms.ps1`: dry run, `-Apply` (snapshot-before-write, re-read before each PATCH), `-Verify`, `-RestoreFrom` (two refusals), `-SelfTest` | ✅ written; `-SelfTest` exit 0; dry run and `-Verify` run read-only on dev |
| `tests/fixtures/form-lock-core-ancestor/`: the ten POML cases + 11 (trigger 1) + 12 (trigger 3) | ✅ 12/12 + 5 inline checks |
| Presave v1.4.0: `setSubmitMode("always")` after every staged or cleared lookup, guarded | ✅ 21/21 presave tests; full RegardingResolver suite 114/114; PCF `build:prod` OK |
| Seeds S1-S11, each red then restored byte-identical | ✅ §6 |
| I-1 row + 156 note "Resolved" line | ✅ |
| **Trigger 1 FIRED**: three forms show the roots as visible, editable controls and host no filing picker | 🔔 owner decision. The script refuses them (`NO_FILING_PICKER`). |
| **Trigger 3 FIRED (literally)**: the Event main form hosts the RegardingResolver without the presave | 🔔 reported. Moot for the lock, because that form has no root control. It points to a defect that predates this task. |
| **Trigger 5 FIRED**: the `sprk_event` and `sprk_analysis` home grids are EDITABLE Power Apps grids | 🔔 owner decision. Reported by the script; not changed. |
| **Round 10 item 2 (add the RegardingResolver to the analysis form) BLOCKED**: `sprk_analysis` has no `sprk_regardingrecordurl`, and the picker writes it unconditionally | 🔔 needs a decision: a schema add or a shared-code change |
| Live `-Apply` | ⏳ manual gate (§8). Only `-Tables sprk_todo` applies cleanly today. |

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
- **Writes and their line numbers.** No PATCH, POST or DELETE runs outside the `-RestoreFrom` branch (from :519) or the `-Apply` section (after `if (-not $Apply) { … exit 0 }` at :718):
  - **:560** `PATCH systemforms(formid)`: restore of `formXmlBefore`. Runs inside `if ($RestoreFrom)`, after the environment-URL refusal, the snapshot self-hash check and the "current hash = stored/written hash" refusal (:551).
  - **:564** `Publish-Table` → **:512** `POST PublishXml`: restore.
  - **:780** `PATCH systemforms(formid)`: apply. Runs after every refusal check, after the snapshot is written, read back and re-hashed (refused at :761), after a re-read of all forms (:765), and after a re-read of THIS form (:776-777).
  - **:785** `Publish-Table` → **:512** `POST PublishXml`: apply, once per changed table.

  `Publish-Table` (:510) is only a definition; it is called at :564 and :785 only.
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
