# Task 050 — Refresh Matter "Create To Do" ribbon icon (FR-19)

> **Date**: 2026-08-16 · STANDARD/sonnet/high. **Status: XML edit DONE + validated; solution deploy PENDING (batched).**

## Done
- Edited `src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml`, button `sprk.Wizard.Matter.CreateTodo.Button` (only): `Image32by32="$webresource:sprk_ToDoCheckmark32.svg"`, `Image16by16="$webresource:sprk_ToDoCheckmark16.svg"`, added `ModernImage="$webresource:sprk_ToDoCheckmark32.svg"`. All other attributes (Alt/Command/Id/LabelText/Sequence/TemplateAlias/ToolTips) unchanged; no other button touched.
- **XML well-formed** ([xml] parse OK); XPath confirms exactly 1 CreateTodo button with the 3 new icon attrs, Command/LabelText intact.
- **Escalation gate passed**: both `sprk_ToDoCheckmark32.svg` (`e5de459a-…`) and `sprk_ToDoCheckmark16.svg` (`be028483-…`) confirmed deployed in spaarkedev1 (type 11 SVG).

## Pending (deploy — batched with 025/035/052)
- Export `spaarke_insights` → apply the same RibbonDiff edit to the exported `customizations.xml` RibbonDiffXml section → repack → `pac solution import --publish-changes` (per ribbon-edit skill; use the form-deploy roundtrip mechanism proven in task 014).
- Browser visual verify (step 6/7): open a Matter → Create To Do button shows the checkmark icon (not OOB), click → CreateTodo wizard still opens with Matter context. **Operator-UAT.**

Deferred deliberately: `spaarke_insights` is a large shared solution; batching its import with the other Phase-3/4/6 deploys (rather than a concurrent one-off) keeps blast radius contained.
