# Task 028 — Analysis wizard + analysis-to-email mapping at send (FR-16, FR-17)

FULL rigor, sonnet @ xhigh, directional. Started 2026-11-25.

## Coordination (step 1)

- PR #1501: CLOSED, not merged. The project CLAUDE.md records it as superseded, so there is nothing to rebase onto.
- PR #2110: MERGED. `CommunicationService.cs` on master already carries it; the conflict check runs against master at edit time.

## Inventory (step 2, 2026-11-25)

There are two analysis creators in the client.

| Creator | Route | Client mapping |
|---|---|---|
| `Spaarke.AI.Widgets/.../CreateAnalysisWizardWidget.tsx` (the spec's "Analysis wizard"; hosted by the Console `WorkspacePane` and `InAppWizardHost`) | `POST /api/v1/child-records/sprk_analysis` via `withBffChildWrites(...).createRecord` (:912-913) | `applyFieldMappings(association parent -> sprk_analysis)` :894; `applyFieldMappings(sprk_analysis -> sprk_communication)` for the Send Email subject/body :958 |
| `Spaarke.UI.Components/.../Playbook/analysisService.ts` `createAndAssociate` (used by `PlaybookLibraryShell`) | `POST /api/ai/analysis/create` | none; the server maps Document -> Analysis (task 043) |

Widget payload (:807-907):
- user-entered: `sprk_name`, `sprk_description` (or null), the Attorney 1 / Paralegal 1 binds when picked, the agreement type bind when picked;
- the document bind (the uploaded or selected document; required);
- the regarding keys from `applyResolverFields` when an association is chosen;
- client-set: `sprk_worktype` (the user's work-type choice) and `sprk_analysisstatus = InProgress` (the wizard's own run state, not a mapped default).

`/api/ai/analysis/create` (`CreateAnalysisRequest`) takes only Name, DocumentId, skill/knowledge/tool ids, PlaybookId and ActionId. It has no description, work type, status, assignees, agreement type or regarding.

## Criteria conflict (escalated to the owner 2026-11-25)

The two criteria added from the task 043 review name `analysisService.ts` as "the Analysis wizard", and say "the wizard keeps creating the analysis through /api/ai/analysis/create". The wizard this task migrates (the widget) has never used that route; it creates through the child-records route. `analysisService.ts` belongs to the Playbook Library.

Recommendation:
- The widget stays on the child-records route and names the association parent as `mappingSource`, like the other wizards. With no association, no source is named.
- The 043 criteria apply to `analysisService.ts` / `PlaybookLibraryShell`: show the 201 `warnings`, and keep that path on `/api/ai/analysis/create`.
- Known limit: a Document -> Analysis profile is not applied to an analysis the widget creates. This is the same as today, since the widget maps only from the association parent.
- Moving the widget onto `/api/ai/analysis/create` would need a BFF contract change for six fields. That is out of scope.

**Owner decision (2026-11-25): as recommended.**

## Execution

- Master was merged into the work branch first: 83543888fe, `--no-verify`, a master merge. The BFF builds with 0 errors.
- Open PRs: none touch `CommunicationService.cs`, the widget, `createXrmEmailComposeHandlers.ts`, `analysisService.ts`, `PlaybookLibraryShell.tsx` or `composeOnlySend.ts` (checked 2026-11-25).
- The send path refuses an empty Subject or Body in `ValidateRequest`, in both the send-as-user and shared-mailbox branches. The fill step therefore runs in `SendAsync` before either branch.
- Dev has no `* -> Analysis` and no `Analysis -> Communication` profile. The live check needs temporary profiles, as task 024 did; each needs the owner's go.
- One implementer agent (Sonnet) works on `task/fm-028` from 83543888fe.
