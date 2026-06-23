# Channel 7 — Work Assignments — Design Notes (DEFERRED)

> **Status**: DEFERRED on 2026-06-22 per user decision after Channels 1–5 producer-fix.
> **Why**: The deployed "New Work Assignments" playbook has only a "Notify" node (`c10a54bf-…`) with templates referencing undefined `{{task.*}}` variables. No Query node exists. Adding one requires CREATING a `sprk_playbooknode` record (not patching), which is heavier than the channel-patches we've validated; needs inspection of a working playbook's node-graph shape first.

---

## Semantic intent (captured from user)

When Channel 7 IS implemented, the Query node should surface NEW work assignments to a user with the following filter:

- **"Me" filter** (Option C — user's choice): `sprk_assignedto eq-userid` OR `ownerid eq-userid` (cross-entity OR via `entityname=` if needed)
- **"New" filter** — date must be in window via EITHER field:
  - `createdon` last-x-hours
  - `sprk_responseduedate` in window
  - (Use `<filter type="or">` between the two, mirroring Ch.1/Ch.2 OR-date pattern)
- **Exclude self-created**: `createdby ne-userid`
- **Display attributes**: `sprk_name`, `sprk_assignedto`, `sprk_regardingmatter`, plus joined matter name/number

## Confirmed `sprk_workassignment` schema (verified 2026-06-22)

| Field | Status |
|---|---|
| `sprk_workassignmentid` | exists |
| `sprk_name` | exists |
| `sprk_assignedto` | exists (lookup) |
| `sprk_regardingmatter` | exists (lookup) |
| `ownerid` | exists |
| `createdby` | exists |
| `createdon` | exists |
| `sprk_duedate` | DOES NOT EXIST |
| `sprk_status` | DOES NOT EXIST |
| `sprk_priority` | DOES NOT EXIST |
| `sprk_assignedby` | DOES NOT EXIST |
| `sprk_responseduedate` | TODO: verify exists |

## Draft fetchXml (NOT YET DEPLOYED)

```xml
<fetch top="50">
  <entity name="sprk_workassignment">
    <attribute name="sprk_name"/>
    <attribute name="createdon"/>
    <attribute name="sprk_responseduedate"/>
    <attribute name="sprk_workassignmentid"/>
    <attribute name="sprk_assignedto"/>
    <attribute name="sprk_regardingmatter"/>
    <attribute name="ownerid"/>
    <link-entity name="sprk_matter" from="sprk_matterid" to="sprk_regardingmatter" link-type="outer" alias="m">
      <attribute name="sprk_mattername"/>
      <attribute name="sprk_matternumber"/>
    </link-entity>
    <filter type="and">
      <condition attribute="createdby" operator="ne-userid"/>
      <filter type="or">
        <condition attribute="createdon" operator="last-x-hours" value="{{timeWindowHours}}"/>
        <filter type="and">
          <condition attribute="sprk_responseduedate" operator="ge" value="{{todayUtc}}"/>
          <condition attribute="sprk_responseduedate" operator="le" value="{{dueSoonWindowUtc}}"/>
        </filter>
      </filter>
      <filter type="or">
        <condition attribute="sprk_assignedto" operator="eq-userid"/>
        <condition attribute="ownerid" operator="eq-userid"/>
      </filter>
    </filter>
    <order attribute="createdon" descending="true"/>
  </entity>
</fetch>
```

## Also needed when Ch.7 is implemented

1. **Verify `sprk_responseduedate` exists** on `sprk_workassignment` before deploying.
2. **Fix the existing Notify node** (`c10a54bf-…`) — currently references undefined `{{task.subject}}` / `{{task.matterName}}` / `{{task.dueDate}}` / `{{task.id}}`. Should reference iteration-variable bindings like `{{item.sprk_name}}` / `{{item.m.sprk_mattername}}` / `{{item.sprk_responseduedate}}` / `{{item.sprk_workassignmentid}}` once the Query node feeds it.
3. **Confirm playbook node graph shape** by inspecting a working playbook (e.g., Tasks Overdue) — sprk_playbooknode records linked via sprk_playbookid, sequence ordering, and how the Query node's output feeds the Notify node.
4. **Create the new Query node** via either: (a) script that creates a sprk_playbooknode record with the right shape, or (b) Maker Designer UI.
