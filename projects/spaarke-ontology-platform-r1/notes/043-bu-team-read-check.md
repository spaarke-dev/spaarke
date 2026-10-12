# 043 F1 - do BU default teams hold Read on sprk_decisionrecord / sprk_signal? (read-only live check)

Run 2026-10-12 against `spaarkedev1` (read-only; no role, team or privilege was granted or changed).
Method: `teams?$filter=isdefault eq true`, `teamroles` for each, then the Web API function `RetrieveRolePrivilegesRole(RoleId=...)`
per distinct role (privileges `prvReadsprk_DecisionRecord`, `prvReadsprk_Signal`, `prvReadsprk_ToDo`, `prvReadsprk_Event`).
(`roleprivilegescollection` was tried first and under-reports for BU role copies - do not use it for this.)

Why it matters: `DecisionRecordWriter.cs:273` assigns an ordinary Decision Record to the business unit's default owner team.
Dataverse refuses the create (0x80040299) unless that team holds Read on the table through a role. A team role at any depth is enough
for a record the team itself owns (Basic depth on a team role covers team-owned rows).

## (a) + (b) Every business unit's default team, its roles, and Read depth

| Business unit | Default team | Roles held by the team | Read `sprk_decisionrecord` | Read `sprk_signal` | Read `sprk_todo` | Read `sprk_event` |
|---|---|---|---|---|---|---|
| Spaarke (root) | Spaarke | Spaarke Basic User; Spaarke Office Add In User; Spaarke Console User | Basic (Basic User); Deep (Console User) | Basic (Basic User); Deep (Console User) | Deep (Basic User) | Deep (Basic User, Office Add In User) |
| Spaarke Business Unit 1 | Spaarke Business Unit 1 | Spaarke Reporting Access Viewer; Spaarke AI Analysis User; Spaarke Basic User; Spaarke Office Add In User | Basic (Basic User) | Basic (Basic User) | Deep (Basic User) | Deep (Basic User, Office Add In User) |
| Spaarke Demo | Spaarke Demo | System Administrator | Global | Global | Global | Global |
| Spaarke Dev 1 | Spaarke Dev 1 | none | NONE | NONE | NONE | NONE |
| Spaarke Test 1 | Spaarke Test 1 | none | NONE | NONE | NONE | NONE |
| Secure Record | Secure Record | none | NONE | NONE | NONE | NONE |

Distinct roles seen on default teams (8 role rows, 5 role names), by Read depth on each table:

| Role (BU copy) | `sprk_decisionrecord` | `sprk_signal` | `sprk_todo` | `sprk_event` |
|---|---|---|---|---|
| Spaarke Basic User (root, BU 1) | Basic | Basic | Deep | Deep |
| Spaarke Console User (root only) | Deep | Deep | none | none |
| Spaarke Office Add In User (root, BU 1) | none | none | none | Deep |
| Spaarke Reporting Access Viewer (BU 1) | none | none | none | none |
| Spaarke AI Analysis User (BU 1) | none | none | none | none |
| System Administrator (Demo) | Global | Global | Global | Global |

## (c) Default teams with NO role granting Read

The environment has 6 business units and 6 default teams (15 teams in all).

| Table | Default teams with Read | Default teams with NO Read |
|---|---|---|
| `sprk_decisionrecord` | 3 of 6 (root, BU 1, Demo) | **3 of 6** (Spaarke Dev 1, Spaarke Test 1, Secure Record) |
| `sprk_signal` | 3 of 6 (root, BU 1, Demo) | **3 of 6** (same three) |

A decision on a matter whose owner team resolves to one of those three BU default teams would fail the Decision Record create with 0x80040299,
exactly as `sprk_signal` did live (`SignalWriter.cs:206-208`, `:1226`).

## (d) Which role gives BU default teams Read on `sprk_todo` and `sprk_event` today

- `sprk_todo`: **Spaarke Basic User** only (Deep), on the root and BU 1 default teams, plus System Administrator on the Demo team.
- `sprk_event`: **Spaarke Basic User** (Deep) and **Spaarke Office Add In User** (Deep), on the root and BU 1 default teams, plus System Administrator on Demo.
- The same three teams with no roles (Dev 1, Test 1, Secure Record) have no Read on `sprk_todo` or `sprk_event` either, so the existing
  BU-team-owned children already carry the same gap; the pattern works only where the team holds Spaarke Basic User.

## Observation for the decision (not acted on)

`prvReadsprk_DecisionRecord` and `prvReadsprk_Signal` are already in **Spaarke Basic User** (Basic depth), so any BU default team that holds
Spaarke Basic User already satisfies the Dataverse requirement for both tables. The three teams that fail hold no role at all. Whether to
grant Spaarke Basic User (or another role) to those teams, change the owner choice, or accept the gap is an owner decision; nothing was changed.
