# Incoming Communication Dataverse Views

These views should be created on the `sprk_communication` entity for admin visibility into incoming email processing.

---

## View 1: All Incoming Communications

- **Name**: All Incoming Communications
- **Entity**: sprk_communication
- **FetchXML Filter**: `sprk_direction eq 100000000` (Incoming)
- **Columns**:
  - sprk_name (Communication Name)
  - sprk_from (From Address)
  - sprk_to (To Address)
  - sprk_subject (Subject)
  - sprk_direction (Direction)
  - statuscode (Status)
  - sprk_receivedat (Received At)
  - sprk_associationstatus (Association Status)
  - createdon (Created On)
- **Sort**: createdon (descending)
- **Purpose**: Overview of all received emails

---

## View 2: Pending Association Review

- **Name**: Pending Association Review
- **Entity**: sprk_communication
- **FetchXML Filter**: `sprk_direction eq 100000000 AND sprk_associationstatus eq 100000001` (Incoming + Pending Review)
- **Columns**:
  - sprk_name (Communication Name)
  - sprk_from (From Address)
  - sprk_subject (Subject)
  - sprk_associationstatus (Association Status)
  - sprk_receivedat (Received At)
  - createdon (Created On)
- **Sort**: createdon (descending)
- **Purpose**: Emails needing manual association resolution

---

## View 3: Incoming by Mailbox

- **Name**: Incoming by Mailbox
- **Entity**: sprk_communication
- **FetchXML Filter**: `sprk_direction eq 100000000` (Incoming)
- **Columns**:
  - sprk_name (Communication Name)
  - sprk_to (Receiving Mailbox)
  - sprk_from (From Address)
  - sprk_subject (Subject)
  - sprk_receivedat (Received At)
  - sprk_associationstatus (Association Status)
  - statuscode (Status)
- **Sort**:
  - Primary: sprk_to (ascending)
  - Secondary: createdon (descending)
- **Purpose**: Group incoming emails by receiving mailbox for mailbox-specific monitoring

---

## View 4: Recent Incoming (7 Days)

- **Name**: Recent Incoming (7 Days)
- **Entity**: sprk_communication
- **FetchXML Filter**: `sprk_direction eq 100000000 AND createdon ge [last 7 days]`
- **Columns**:
  - sprk_name (Communication Name)
  - sprk_from (From Address)
  - sprk_to (To Address)
  - sprk_subject (Subject)
  - sprk_receivedat (Received At)
  - sprk_associationstatus (Association Status)
  - statuscode (Status)
- **Sort**: createdon (descending)
- **Purpose**: Quick view of recent incoming emails for daily monitoring

---

## Implementation Notes

### Creation Method
Views are created manually in Dataverse solution editor (Make.powerapps.com):
1. Navigate to Solutions → Email Communication Solution
2. Select sprk_communication entity
3. Create new View with specified name and FetchXML filter
4. Add columns in order specified
5. Configure sort criteria
6. Save and publish

Alternatively, views can be defined in solution XML under `savedqueries` node.

### Option Set Reference Values

**sprk_associationstatus** (Association Status):
- 100000000 = Resolved
- 100000001 = Pending Review

**sprk_direction** (Direction):
- 100000000 = Incoming
- 100000001 = Outgoing

**statuscode** (Status):
- Depends on communication lifecycle status values (e.g., Active, Resolved, etc.)

### View Access and Security

- Views are system views available to all users with read access to sprk_communication
- No additional permissions required beyond entity read access
- Filtering ensures users only see relevant communications per their needs

### Future Enhancements

- Add "Failed Association" view to surface communications with association errors
- Add "By Sender Domain" view for security/spam monitoring
- Implement personal views per mailbox for dedicated monitors

---

## Status

View definitions documented. To be created in Dataverse during deployment phase.

---

**Last updated**: 2026-03-09
**Task**: ECS-026 (Create Incoming Communication Dataverse Views)
