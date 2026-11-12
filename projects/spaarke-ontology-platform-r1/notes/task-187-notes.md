# Task 187 notes - other pickers use the side-pane lookup (D-219 Q5)

Checked against master a43a65fb2f (includes task 178's `DataverseLookupField` with `filters` / `hint`).

## Picker inventory

| Picker (file:line at a43a65fb2f) | What it lists | Dataverse table? | Host has `INavigationService`? | Result |
|---|---|---|---|---|
| `OntologyAdmin/RuleWizard/steps/ScopeNoiseStep.tsx:80` "Limit to one matter" | Matters by name through `searchLookupTarget('sprk_matter', q)` (`Xrm.WebApi`, as the caller) | Yes, `sprk_matter` (the rule `subject` the host passes; the BFF refuses any other subject) | Host is `OntologyAdminWorkspaceWidget` (Console, `Spaarke.AI.Widgets`); it had none. Now built at the widget root with `createXrmNavigationService()` when `getXrm('lookupObjects')` is available | Converted to `DataverseLookupField`; `MatterPicker.navigationService` is the new optional carrier. No Xrm: inline search as before (no dead Select button) |
| `RuleReview/src/components/NewRuleSetModal.tsx:109` | n/a | n/a | n/a | File does not exist on master: removed by ebilling-116 (997f45faba, "RuleReview drops rule-set create and draft"). Nothing to convert |
| `RuleReview/src/components/AssignmentPanel.tsx:77` "Firm" | Firms by name through `searchOrganizationsAsLookup` (`Xrm.WebApi`, name filter only) | Yes, `sprk_organization` (not a contact, so no active-contacts filter) | Code page `RuleReview/src/main.tsx` had none. Now `createXrmNavigationService()` built in `Root` when `getXrm('lookupObjects')`, carried on `FirmLookup.navigationService` | Converted to `DataverseLookupField` (entity `sprk_organization`, inline `onSearch` kept as the no-navigation fallback) |
| `SpeAdminApp/src/components/layout/BuContextPicker.tsx:646` Business Unit | `GET /api/spe/businessunits` filtered in memory | Reads `businessunit`, but see reason below | Page has no Xrm use and no navigation service | Left unchanged |
| `SpeAdminApp/.../BuContextPicker.tsx:687` Container Type Config | `GET /api/spe/configs?businessUnitId=` (`sprk_specontainertypeconfig`), filtered in memory | Table is Dataverse; list is BFF-scoped | none | Left unchanged |
| `SpeAdminApp/.../settings/ContainerTypeConfig.tsx:605` Business Unit | same BFF business-unit list | as above | none | Left unchanged |
| `SpeAdminApp/.../ContainerTypeConfig.tsx:620` Environment | `GET /api/spe/environments` (`sprk_speenvironment`) | Table is Dataverse; list is BFF-scoped | none | Left unchanged |

### Why the four SPE Admin pickers are left unchanged

They are not caller-readable Dataverse lookups. Each list is a BFF route that computes the caller's reach server-side and fails closed:

- `BusinessUnitEndpoints.cs`: only the caller's own business unit and its descendants (a leaf customer admin must not read other customers' unit names; platform operators see all).
- `ConfigEndpoints.ListConfigsAsync`: configs in the caller's accessible business units only.
- `EnvironmentEndpoints.ListEnvironmentsAsync`: the caller's environment reach (`GetEnvironmentReachAsync`).

The side pane (`Xrm.Utility.lookupObjects`) lists every row the user can read in Dataverse, so converting would re-expose the rows those routes were built to hide, and a pick outside the BFF list breaks `BuContextPicker` (`allBus.find(...)` returns null and clears the selection). The pane's `filters` cannot express the caller's server-computed reach. This is a decision for the owner or the SPE Admin owner (for example a BFF-backed side-pane substitute), not a wiring change. Not filed as an issue by this task.

## Deviations from the POML

- `NewRuleSetModal.tsx` no longer exists (above); `<outputs>` names it, but it is not modified.
- Step 3 / acceptance criterion 4 dev check (Console in-app wizard and a code-page dialog wizard, light and dark, pane above the wizard, cancel keeps values) cannot run from the implementer session; it is still to do on dev.
- `RulesArea` / `RuleWizard` were not changed: `navigationService` rides on the existing `MatterPicker` prop.
- The ontology matter limit uses `matterPicker.subject` as the entity type (the host sets the subject to the matter table; documented on `MatterPicker`).

## Tests

- `RuleWizard.test.tsx`: 2 new (pane opens for the subject table and no inline input, only the normalised id is sent; cancel and rejected pane leave scope as it was). Both fail on the old `ScopeNoiseStep`.
- `OntologyAdminWorkspaceWidget.test.tsx`: 2 new (navigation service passed when Xrm can look up; none without Xrm).
- `RuleReview/src/__tests__/assignmentPanelFirmPicker.test.ts`: 4 new (entity `sprk_organization` + navigation service passed, inline fallback, no picker for client default, `createFirmLookup` carries the service). Two fail on the old `AssignmentPanel`.
