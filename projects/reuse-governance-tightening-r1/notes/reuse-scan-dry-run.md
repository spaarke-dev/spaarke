# Reuse-scan dry-run (task 010, FR-03)

> Status: **complete** (2026-11-12). Answer keys were written before any agent ran (POML step 1).
> Decision rule (owner, 2026-11-10): both cases caught at `low` → go. One missed → the owner sees the measured token cost per scan and the projected monthly cost at `medium`, and decides. Both missed → no-go, and tasks 011 and 012 are cancelled.

## Method

- One `code-mapper` agent per case (model `sonnet`, effort `low` from its definition). Each gets the capability list (concept, synonyms, interface shape), `.claude/patterns/CAPABILITY-INDEX.md` and the design.md §4.1 return schema.
- The search excludes `projects/`. That folder holds the later projects' hindsight notes, which name the duplicate outright; a real design-time scan would not have them.
- Hindsight in the index: rows 5 and 30 were written in task 001 knowing these histories. Each candidate's `match` field is therefore recorded. A catch that comes **only** through an index row counts as **partial**: it shows the index works, not that the search works.

## Case 2: rich-text editor (`spaarkeai-compose-r1`)

**Answer key (established from history).**
- `spaarkeai-compose-r1` (design committed 2026-06-29, `82bf67152f`) introduced a TipTap rich-text and document editor: `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (`0881e60979`, 2026-06-29).
- An editor already existed: `src/client/shared/Spaarke.UI.Components/src/components/RichTextEditor/RichTextEditor.tsx` (Lexical), present since 2025-12-14 (`71e709b97e`).
- Compose-r1's `design.md` and `spec.md` never mention `RichTextEditor`. Its design Q2 chose TipTap without weighing the existing editor.
- The repo still has two answers (CAPABILITY-INDEX row 30).
- **Caught** means the scan names `RichTextEditor.tsx` (or its `RichTextEditor` export) as an existing candidate for the editor capability.

**Capabilities to pass (from compose-r1 `design.md` §3 / Q2):** a rich-text document editor mounted in a workspace pane that loads and saves DOCX. Synonyms: rich text, WYSIWYG, editor, Lexical, TipTap, ProseMirror, contenteditable, document editing, formatting toolbar. Interface: a React component with a value/content prop and an onChange-style callback.

## Case 1: set-regarding area, nav-prop discovery (owner decision 2026-11-12)

The owner chose nav-prop discovery as the scored case 1, and asked that field mapping be looked at too. Field mapping runs as **case 1b**, a supplementary positive control that the FR-03 rule does not score (it is not a cross-project duplication; see the history below).

**Case 1 answer key.**
- A create wizard that writes a Dataverse record with lookups through the Web API needs each lookup's navigation property name to build `@odata.bind` keys. The shared answer is `discoverNavProps` / `findNavProp` in `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` (`findNavProp` added 2026-03-12, `7eb93ac916`; both exported today, lines 422 and 477).
- Later wizard services wrote their own private copies instead (consolidated by set-regarding r2 tasks 011 and 016).
- **Caught** means the scan names `discoverNavProps` or `findNavProp` in `PolymorphicResolverService.ts`. Naming only a private copy elsewhere is **partial**: it found a duplicate, not the shared component.
- **Capabilities to pass:** find the single-valued navigation property name for each lookup column of a Dataverse table from entity metadata, so a client can write `<navprop>@odata.bind` when creating a record. Synonyms: navigation property, nav prop, `@odata.bind`, lookup binding, `ManyToOneRelationships`, `ReferencingEntityNavigationPropertyName`, `EntityDefinitions`, schema name casing. Interface: an async function from a table's logical name to a list or map of lookup column → navigation property.

**Case 1b answer key (supplementary).**
- **Caught** means the scan names the BFF field-mapping API `src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs` or the `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` tables.
- **Capabilities to pass:** copy configured field values from a parent record into a new child record when it is created (rules per source/target table, field-to-field mappings). Synonyms: field mapping, mapping profile, mapping rule, inherit fields, default values from parent, prefill. Interface: given a source record and a target table, return or apply the mapped field values.

**History (why field mapping is not the scored case).**
- From history, the stubbed shared-lib `FieldMappingService.ts` and the real BFF engine (`src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs`) were created in the **same** commit (`b3f1681840`, 2026-02-03, events-and-workflow-automation-r1). That is not a later design duplicating an earlier component.
- `set-regarding-and-field-mapping-resolver-r1` retired a capability (the creation-time cascade in `AssociationResolver`). It did not duplicate one. `set-regarding-and-field-mapping-resolver-r2` `design.md` §1 then reused the BFF API correctly.
- A real cross-project duplication in this area: nav-prop discovery. `_discoverNavProps` was first written in the Create Matter wizard (`950bd162d0`, 2026-02-20). The shared `findNavProp` was added to `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` on 2026-03-12 (`7eb93ac916`). Six more wizard services later carried their own copies, which r2 consolidated (r2 tasks 011 and 016).

## Results (2026-11-12)

All three runs: `code-mapper`, model `sonnet`, effort `low` (from its definition), one agent per case, `projects/` excluded.

| Case | Tokens | Tool calls | Time | Verdict | Basis |
|---|---|---|---|---|---|
| 1 nav-prop (scored) | 39,299 | 6 | 27 s | **Caught** | Named `discoverNavProps` (`PolymorphicResolverService.ts:477`, 13 caller files) and `findNavProp` (`:422`). `discoverNavProps` was reached first via index row 5, `findNavProp` independently by a name search. |
| 2 editor (scored) | 34,346 | 5 | 38 s | **Caught** | Named `RichTextEditor` (`RichTextEditor.tsx:353`, Lexical, `value` + `onChange(html)`) and flagged that it is the closest match to the requested interface. The agent labelled it `index-row`. Checked here: its own synonym search (`LexicalComposer`, which it ran) matches only the `RichTextEditor` folder in `src/`, so the search alone finds it too. |
| 1b field mapping (not scored) | 33,249 | 8 | 31 s | Caught | `FieldMappingEndpoints.cs` came via index row 5. The engine itself, `CreateTimeFieldMapping.cs` and `MappingSourceResolver.cs`, plus `FieldMappingTypes.ts`, were found by name search. |

Mean cost per scan: about 36k sub-agent tokens at `sonnet`/`low`, well under the 200k stop line.

**Limits of this test.**
- The scans run on today's repo, not the repo as it was at each design's date. For case 2 that cuts against the scan: `ComposeEditor` now exists and competes as a candidate, and the scan still named `RichTextEditor`. For case 1, the shared function is better known today than in March.
- Index rows 5 and 30 were written knowing these histories. The verdicts above rest on hits the search makes without the index, so they do not depend on those rows.

**Found in passing (case 1).** Private copies of the same metadata query are still in the code: `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts:94`, `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts:152`, `src/solutions/Notepad/src/hooks/discoverMemoNavProps.ts:88`, `discoverHostNavProps` in `Spaarke.Communication.Components/.../ConnectionsWriteHandler.ts:141` and in the RegardingResolver PCF `ResolverWriteHandler.ts:295`, `discoverTodoNavProps` in `TodoRegardingUpdateBuilder.ts:315`, and inline in `sprk_secure_child_ribbon.js:225` (plus its solution copy). The three read here behave the same as the shared function; no wrong result was found. The Notepad file's header says the shared library does not export `discoverNavProps`, which is no longer true, and that steers the next author to copy it again. These are input for the task 020 duplication sweep.

## Decision

By the owner's rule (2026-11-10), both scored cases were caught at `low`, so the result is **go**: tasks 011 (reuse scan in `/design-to-spec`) and 012 (`project-pipeline` fallback) proceed, at effort `low`. **Owner answer, 2026-11-12: "Go."**
