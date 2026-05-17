---
name: log-call-transcripts
description: Analyzes sales call transcripts to extract key information and automatically logs the call as a Phone Call activity in Dataverse. Use when user says "log this call", "process this transcript", "save this call to CRM", "create a phone call record from this transcript", uploads a call recording transcript, or pastes meeting/call notes to be logged.
metadata:
  author: Dataverse
  version: 1.0.0
  category: sales-productivity
---

# Log Call Transcripts

When a sales representative uploads or provides a call transcript, this skill extracts actionable information from the conversation and creates a properly structured Phone Call activity record in Dataverse. This eliminates manual data entry and ensures consistent activity logging across the sales team.

## Instructions

### Step 1: Receive and Parse Transcript
When the user provides a call transcript:
1. Accept the transcript content (text or file upload)
2. Identify the transcript format and parse it appropriately
3. Extract speaker labels if available (e.g., "Sales Rep:", "Customer:")

#### Step 2: Extract Key Information from Transcript
Analyze the transcript to identify and extract:

**Participant Information:**
- Customer name(s) mentioned
- Company/Account name mentioned
- Customer's role or title if mentioned
- Phone numbers discussed

**Call Metadata:**
- Call duration (if mentioned or calculable)
- Date/time of call (if mentioned, otherwise use current datetime)
- Call direction (inbound/outbound based on context)

**Content Analysis:**
- **Subject/Topic:** Main purpose of the call (e.g., "Product demo follow-up", "Pricing discussion", "Support inquiry")
- **Key Discussion Points:** Summarize 3-5 main topics discussed
- **Action Items:** Any commitments or next steps mentioned
- **Customer Pain Points:** Problems or challenges the customer mentioned
- **Competitor Mentions:** Any competitor products or companies referenced
- **Budget Signals:** Any pricing or budget discussions
- **Decision Maker Signals:** References to decision-making process or stakeholders
- **Timeline Signals:** Any urgency or timing requirements mentioned

#### Step 3: Match to Existing Records
Query Dataverse to find matching records:

**Find Account:**
```
Use read_query to search the account table:
- Search by company name mentioned in transcript
- Match on account.name field
- If multiple matches, list them for user confirmation
```

**Find Contact:**
```
Use read_query to search the contact table:
- Search by participant name(s) from transcript
- Match on contact.fullname or contact.firstname + contact.lastname
- Filter by accountid if account was identified
- If multiple matches, list them for user confirmation
```

**Find Related Opportunity (if applicable):**
```
Use read_query to search the opportunity table:
- Filter by customerid matching the identified Account or Contact
- Filter by statecode = 0 (Open)
- Present open opportunities for user selection if multiple exist
```

**Find Related Lead (if no Account/Contact found):**
```
Use read_query to search the lead table:
- Search by companyname or fullname
- Filter by statecode = 0 (Open)
```

#### Step 4: Prepare Phone Call Activity Record
Construct the Phone Call record with extracted information:

**Required Fields:**
- `subject`: Generated from call topic (max 200 characters)
- `description`: Detailed call summary including:
  - Key discussion points
  - Customer pain points identified
  - Action items and next steps
  - Any competitor or budget mentions
- `phonenumber`: Customer phone if extracted
- `directioncode`: true for outgoing, false for incoming
- `actualdurationminutes`: Call duration in minutes

**Regarding Object (Link to primary record):**
- Set `regardingobjectid` to the most relevant record:
  - If opportunity was discussed → link to Opportunity
  - If no opportunity but account identified → link to Account
  - If only contact identified → link to Contact
  - If only lead identified → link to Lead

**Activity Party Fields:**
- `from`: The sales rep (current user/systemuser)
- `to`: The customer contact(s) identified

**Additional Fields:**
- `statecode`: 1 (Completed)
- `statuscode`: 2 (Made) for outgoing or 4 (Received) for incoming
- `category`: "Sales Call" or appropriate category
- `actualstart`: Call start time
- `actualend`: Call end time

#### Step 5: Create the Phone Call Record
```
Use create_record tool with tablename: phonecall
Include all prepared fields in the item parameter as JSON
```

#### Step 6: Create Follow-up Task (if action items identified)
If action items were extracted from the transcript:
```
Use create_record tool with tablename: task
- subject: First action item or "Follow-up from call with [Customer Name]"
- description: List of all action items from the call
- regardingobjectid: Same as the phone call record
- scheduledend: Based on timeline mentioned or 3 business days from now
- prioritycode: Based on urgency signals (1=High, 2=Normal, 3=Low)
```

#### Step 7: Add Detailed Notes (if needed)
If transcript contains detailed technical or strategic information:
```
Use create_record tool with tablename: annotation
- subject: "Call Transcript - [Date]"
- notetext: Full transcript or detailed notes
- objectid: Link to the created Phone Call activity
- objecttypecode: "phonecall"
```

### Step 8: Update Related Records (if applicable)
Based on transcript content, suggest updates to related records:

**Update Opportunity (if linked):**
- If competitor mentioned → suggest updating opportunity competitive information
- If budget discussed → suggest updating budgetamount
- If timeline clarified → suggest updating estimatedclosedate

**Update Lead (if linked):**
- If qualification signals found → suggest updating leadqualitycode
- If budget discussed → suggest updating budgetstatus
- If decision maker confirmed → suggest updating decisionmaker field

## Examples

### Example 1: Standard Sales Call Logging

**User says:** "Log this call transcript:

Sales Rep: Hi John, thanks for taking my call today.
John: No problem Sarah. We're really interested in learning more about your Enterprise solution.
Sales Rep: Great! I know Contoso has been evaluating options. What's driving the timeline?
John: We need to make a decision by end of Q1. Our current vendor Acme Corp has been having reliability issues.
Sales Rep: I understand. What's your budget range for this project?
John: We're looking at around $50,000 for the initial implementation.
Sales Rep: Perfect. Let me send you a proposal by Friday and we can schedule a demo for your team next week.
John: Sounds good. Make sure to include our CTO, Mike Chen, on that invite."

**Actions:**
1. Parse transcript and extract participant names (John, Sarah), company (Contoso)
2. Query Dataverse for matching Account "Contoso" and Contact "John"
3. Extract key signals: budget ($50K), timeline (Q1), competitor (Acme Corp), stakeholder (Mike Chen, CTO)
4. Create Phone Call record linked to Contoso account
5. Create follow-up Task "Send proposal to Contoso" due Friday

**Result:**
- Phone Call created: "Enterprise Solution Discussion with Contoso"
- Follow-up Task created with Friday deadline
- Suggested updates: Add $50K budget to opportunity, add Acme Corp as competitor

### Example 2: Support Call with No Existing Records

**User says:** "Process this support call - customer called about integration issues"

**Actions:**
1. Parse transcript for customer details
2. Search Dataverse - no matching account/contact found
3. Create Phone Call with extracted details
4. Prompt user: "No matching account found. Would you like to create a new Lead?"

**Result:** Phone Call logged with suggestion to create new Lead record

## Troubleshooting

### Error: No matching Account or Contact found
**Cause:** Company or person name in transcript doesn't match any Dataverse records
**Solution:** 
- Check for spelling variations or abbreviations
- Ask user to confirm the correct account/contact
- Offer to create a new Lead record if appropriate

### Error: Multiple matching records found
**Cause:** Common names or company names match multiple Dataverse records
**Solution:**
- Present the list of potential matches to the user
- Ask user to select the correct record before proceeding
- Use additional context (phone number, email) to narrow matches

### Error: Phone Call creation failed
**Cause:** Missing required fields or invalid field values
**Solution:**
- Ensure `subject` is under 200 characters
- Verify `regardingobjectid` points to a valid record
- Check that `directioncode` is a valid boolean value

## Output Format
After processing, provide the user with:
1. **Summary:** Brief confirmation of what was logged
2. **Phone Call Record:** Link/ID of created phone call activity
3. **Key Extracted Information:** Bullet points of main findings
4. **Follow-up Task:** Link/ID if created
5. **Suggested Updates:** Any recommended updates to related records

### Example Interaction

**User Input:**
"Log this call transcript:

Sales Rep: Hi John, thanks for taking my call today.
John: No problem Sarah. We're really interested in learning more about your Enterprise solution.
Sales Rep: Great! I know Contoso has been evaluating options. What's driving the timeline?
John: We need to make a decision by end of Q1. Our current vendor Acme Corp has been having reliability issues.
Sales Rep: I understand. What's your budget range for this project?
John: We're looking at around $50,000 for the initial implementation.
Sales Rep: Perfect. Let me send you a proposal by Friday and we can schedule a demo for your team next week.
John: Sounds good. Make sure to include our CTO, Mike Chen, on that invite."

**Skill Output:**
✅ **Phone Call Logged Successfully**

**Record Created:** Phone Call - "Enterprise Solution Discussion with Contoso"

**Key Information Extracted:**
- **Account:** Contoso (matched in Dataverse)
- **Contact:** John (linked to Contoso)
- **Topic:** Enterprise solution evaluation
- **Competitor Mentioned:** Acme Corp (reliability issues)
- **Budget Signal:** $50,000 for implementation
- **Timeline:** Decision by end of Q1
- **Decision Maker:** Mike Chen (CTO) to be included

**Follow-up Task Created:** "Send proposal to Contoso" - Due: Friday

**Suggested Updates:**
- Update Opportunity "Contoso Enterprise Deal" with budget amount $50,000
- Add competitor "Acme Corp" to opportunity
- Update estimated close date to Q1 end

### Dataverse Tables Used
| Table | Purpose |
|-------|---------|
| `phonecall` | Primary record created for the call activity |
| `account` | Query to match company mentioned in transcript |
| `contact` | Query to match participant names |
| `opportunity` | Query for related open deals, link as regarding object |
| `lead` | Fallback if no account/contact found |
| `task` | Create follow-up tasks for action items |
| `annotation` | Store full transcript as attachment |

### Key Fields Reference
**phonecall:**
- `subject` (NVARCHAR 200) - Call title
- `description` (MULTILINE TEXT) - Call summary and notes
- `phonenumber` (PHONE) - Customer phone number
- `directioncode` (BIT) - true=Outgoing, false=Incoming
- `actualdurationminutes` (DURATION) - Call length in minutes
- `actualstart` (DATETIME) - When call started
- `actualend` (DATETIME) - When call ended
- `regardingobjectid` (LOOKUP) - Polymorphic link to account, contact, lead, or opportunity
- `from` (ACTIVITY PARTY) - Caller (systemuser, contact, account, lead)
- `to` (ACTIVITY PARTY) - Call recipient (contact, account, lead)
- `leftvoicemail` (BIT) - Whether voicemail was left
- `statecode` (STATE) - Open(0), Completed(1), Canceled(2)
- `statuscode` (STATUS) - Open(1), Made(2), Canceled(3), Received(4)

**activityparty (for from/to fields):**
- `partyid` (LOOKUP) - Polymorphic reference to participant (contact, account, lead, systemuser)
- `participationtypemask` (CHOICE) - Sender(1), To Recipient(2), etc.

**annotation (for transcript storage):**
- `subject` (NVARCHAR 500) - Note title
- `notetext` (MULTILINE TEXT) - Full transcript content
- `objectid` (LOOKUP) - Link to phonecall record
- `objecttypecode` (NVARCHAR) - "phonecall"
