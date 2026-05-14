---
name: account-briefing-generator
description: Generates meeting briefings by aggregating account info, contacts, opportunities, cases, and activity history into structured prep documents with talking points and discovery questions. Use when user says "prep me for my call", "brief me on this account", "meeting prep", "I have a meeting with [company]", "account briefing", "customer briefing", or "prepare for customer meeting".
metadata:
  author: Dataverse
  version: 1.0.0
  category: sales-productivity
---

# Account Briefing Generator

Sales reps often have meetings scheduled with little prep time. This skill rapidly synthesizes all available Dataverse information about an account into a concise, actionable meeting brief. In Quick Prep mode, it identifies the specific meeting and attendees and generates a targeted agenda with BANT-gap discovery questions. In Full Briefing mode, it delivers a comprehensive account view with relationship health scoring, stakeholder mapping, and deal history.

## Instructions

### Mode Detection
| Trigger | Mode | Focus |
|---------|------|-------|
| "Prep me for my call with Fabrikam at 2pm" | **Quick Prep** | Specific meeting — attendees, agenda, BANT gaps, discovery questions |
| "I have a meeting with Contoso in 30 minutes" | **Quick Prep** | Specific meeting — fast, scannable, action-oriented |
| "Brief me on Contoso" | **Full Briefing** | Full account — health score, stakeholder map, LTV, strategic context |
| "Give me a full briefing on Fabrikam" | **Full Briefing** | Full account — all sections |

If a specific appointment is identified, always include the Quick Prep section. Full Briefing sections are added when time permits or explicitly requested.

### Workflow

#### Step 1: Identify the Account and Meeting Context

**1.1 Find the Account:**
```
SELECT accountid, name, telephone1, emailaddress1, websiteurl,
       address1_line1, address1_city, address1_stateorprovince, address1_postalcode,
       industrycode, numberofemployees, revenue, ownershipcode,
       description, createdon, primarycontactid, ownerid
FROM account
WHERE name LIKE '%[account_name]%'
AND statecode = 0
```

Confirm account with user if multiple matches.

**1.2 Find Upcoming Appointment (Quick Prep mode or if available):**
```
SELECT appointmentid, subject, scheduledstart, scheduledend,
       location, description, regardingobjectid, regardingobjecttypecode, ownerid
FROM appointment
WHERE statecode = 0
AND scheduledstart >= '[today]'
ORDER BY scheduledstart ASC
```

Filter results where scheduledstart is within the next 7 days and regardingobjectid matches the account or a related opportunity. Present to user if multiple matches.

**1.3 Get Meeting Attendees via Activity Party:**

If an appointment is found, retrieve attendees from the activityparty table:
```
SELECT partyid, participationtypemask, addressused
FROM activityparty
WHERE activityid = '[appointmentid]'
```

**Participation type codes:**
- 1 = Organizer
- 2 = To Recipient / Required Attendee
- 5 = Owner
- 9 = Customer

For each external partyid (contact or lead), fetch details:
```
SELECT contactid, fullname, jobtitle, emailaddress1, telephone1,
       accountrolecode, msdyn_decisioninfluencetag, description
FROM contact
WHERE contactid = '[partyid]'
```

#### Step 2: Gather Account Intelligence

**2.1 Get All Contacts:**
```
SELECT contactid, fullname, firstname, lastname, jobtitle,
       emailaddress1, telephone1, mobilephone,
       accountrolecode, msdyn_decisioninfluencetag, description
FROM contact
WHERE accountid = '[accountid]'
AND statecode = 0
ORDER BY accountrolecode
```

**2.2 Map Contact Roles:**
Classify contacts by buying role based on jobtitle and role fields:
- **Economic Buyer:** C-level, VP, Director with budget authority
- **Technical Buyer:** IT, Engineering, Operations managers
- **End User:** Individual contributors, Analysts
- **Champion:** Internal advocate (any level)
- **Gatekeeper:** Executive Assistant, Procurement
- **Potential Blocker:** Legal, Security, Finance controllers

Use `msdyn_decisioninfluencetag` where available:
- 0 = Decision Maker
- 1 = Influencer
- 2 = Blocker
- 3 = Unknown

**2.3 Get Recent Activities (last 90 days):**

Calculate cutoff date programmatically: `[90_days_ago]` = today minus 90 days.
```
SELECT activityid, activitytypecode, subject, description,
       createdon, actualend, ownerid, statecode
FROM activitypointer
WHERE regardingobjectid = '[accountid]'
AND createdon >= '[90_days_ago]'
ORDER BY createdon DESC
```

**2.4 Analyze Activity Patterns:**
- Total activities (90 days)
- Activity breakdown by type (calls, meetings, emails, tasks)
- Most recent interaction date and type
- Most engaged contacts (by activity count)
- Sentiment indicators from description fields: positive ("happy", "pleased", "expanding"), negative ("frustrated", "issue", "problem", "competitor", "evaluating"), risk ("renewal", "cancel", "leaving")

#### Step 3: Gather Opportunity Intelligence

**3.1 Get Open Opportunities:**
```
SELECT opportunityid, name, estimatedvalue, estimatedclosedate,
       salesstage, stepname, closeprobability,
       budgetstatus, need, purchasetimeframe, decisionmaker, purchaseprocess,
       description, currentsituation, customerneed, customerpainpoints,
       msdyn_forecastcategory, msdyn_opportunityscore, createdon
FROM opportunity
WHERE accountid = '[accountid]'
AND statecode = 0
ORDER BY estimatedvalue DESC
```

**3.2 Get Opportunity History:**
```
SELECT opportunityid, name, estimatedvalue, actualclosedate, statecode
FROM opportunity
WHERE accountid = '[accountid]'
AND statecode IN (1, 2)
ORDER BY actualclosedate DESC
```

**3.3 Get Products Owned (from won opportunities):**
```
SELECT p.name as product, op.quantity, op.extendedamount, o.name as opportunity,
       o.actualclosedate
FROM opportunityproduct op
JOIN opportunity o ON op.opportunityid = o.opportunityid
JOIN product p ON op.productid = p.productid
WHERE o.accountid = '[accountid]'
AND o.statecode = 1
ORDER BY o.actualclosedate DESC
```

**3.4 BANT Gap Analysis (for primary open opportunity):**

For the highest-value or most recently active open opportunity, assess qualification status:

| Signal | Field | Weak | Moderate | Strong |
|--------|-------|------|----------|--------|
| Budget | `budgetstatus` | 0 (No Budget) | 1 (May Buy) | 2–3 (Can/Will Buy) |
| Authority | `decisionmaker` | NULL / false | — | true |
| Need | `need` | 3 (No need) | 2 (Good to have) | 0–1 (Must/Should have) |
| Timeline | `purchasetimeframe` | 4 (Unknown) | 2–3 (This/Next Year) | 0–1 (Immediate/This Qtr) |
| Process | `purchaseprocess` | 2 (Unknown) | 1 (Committee) | 0 (Individual) |

Identify all weak/unknown signals — these drive the discovery questions in Step 6.

#### Step 4: Gather Support Intelligence

**4.1 Get Recent Cases:**

Calculate cutoff: `[90_days_ago]` = today minus 90 days.
```
SELECT incidentid, title, ticketnumber, createdon, prioritycode,
       statecode, statuscode, caseorigincode, casetypecode,
       description, customersatisfactioncode, msdyn_casesentiment
FROM incident
WHERE accountid = '[accountid]'
AND createdon >= '[90_days_ago]'
ORDER BY createdon DESC
```

**4.2 Analyze Support Patterns:**
- Open cases count and severity
- Average satisfaction score (customersatisfactioncode: 1=Very Dissatisfied → 5=Very Satisfied)
- Common issue themes from title/description
- Any escalations or high-priority open cases

#### Step 5: Analyze Relationship Health (Full Briefing mode)

**5.1 Engagement Metrics:**
- Days since last meaningful contact (call or meeting)
- Total touchpoints in last 90 days
- Engagement trend: compare last 30 days vs prior 60 days
- Stakeholder coverage: how many distinct contacts engaged

**5.2 Relationship Health Score:**
| Factor | Weight | Scoring |
|--------|--------|---------|
| Recent engagement | 25% | Active <14 days = High; 14–45 days = Medium; >45 days = Low |
| Open opportunities | 20% | Active open deal = High; No open deals = Low |
| Support sentiment | 20% | No open cases = High; Open normal = Medium; Escalated = Low |
| Stakeholder coverage | 20% | 3+ contacts engaged = High; 1–2 = Medium; 0 = Low |
| Revenue trajectory | 15% | Growing = High; Stable = Medium; Declining = Low |

**Score interpretation:** High factors = Healthy | Mix = At Risk | Mostly Low = Critical

#### Step 6: Generate Talking Points, Agenda, and Discovery Questions

**6.1 Recommended Meeting Agenda (Quick Prep mode — when appointment found):**

Based on opportunity stage and open items, generate a 3–5 point agenda:
1. Opening: Reference last interaction, confirm agenda
2. Address any open support issues proactively
3. Business review: Validate current situation and priorities
4. Discovery: Fill BANT gaps identified in Step 3.4
5. Next steps: Define clear outcome before closing

**6.2 Discovery Questions by BANT Gap:**

Generate targeted questions only for weak/unknown signals identified in Step 3.4:

*Budget gaps (budgetstatus = 0 or 1):*
- "Have you allocated budget for this initiative in the current fiscal year?"
- "What's the approval process for a project of this scope?"
- "Are there competing priorities for this budget?"

*Authority gaps (decisionmaker = false/null):*
- "Who else will be involved in evaluating and approving this decision?"
- "What does the sign-off process look like on your end?"
- "Is [executive name] engaged in this initiative?"

*Need gaps (need = 2 or 3):*
- "What is the cost to the business if this problem isn't solved this year?"
- "How are you currently handling [pain point from customerpainpoints]?"
- "What would success look like 6 months after implementation?"

*Timeline gaps (purchasetimeframe = 3 or 4):*
- "Is there a hard deadline driving the urgency?"
- "What events or milestones is this decision tied to?"
- "What would cause you to delay this decision?"

*Process gaps (purchaseprocess = 2):*
- "Is this an individual decision or does it go through a committee?"
- "Who else needs to be comfortable before you can move forward?"

**6.3 Positive Talking Points:**
- Reference recent wins, product updates, or ROI data relevant to this account
- Address known pain points from opportunity fields (customerpainpoints, currentsituation)

**6.4 Issues to Address Proactively:**
- Open support cases — acknowledge before customer raises them
- Overdue tasks or commitments — address and reset expectations
- Competitor mentions in notes — prepare differentiation narrative
- Engagement gaps with key stakeholders (CTO, CFO not engaged)

**6.5 Contingency Responses:**
- Support frustration → "I know about [issue] — here's where we are and the resolution timeline..."
- Budget pushback → "We can phase the approach — let me show you what that looks like..."
- Competitor comparison → Focus on [integration, support quality, roadmap] differentiation
- Timeline delay → "Help me understand what's causing the delay so I can help navigate internally"

#### Step 7: Generate Brief

**Quick Prep Output** (when specific appointment identified):
```
════════════════════════════════════════════════════════════════
              QUICK PREP: [ACCOUNT NAME]
════════════════════════════════════════════════════════════════
Generated: [Date/Time]
Meeting: [Meeting subject]
Scheduled: [Time] | [Location/Teams link]
Attendees: [Name, Title] | [Name, Title]

📊 ACCOUNT SNAPSHOT
────────────────────────────────────────────────────────────────
[Industry] | [n] employees | Customer since [year] | LTV: $[value]

💼 ACTIVE DEAL
────────────────────────────────────────────────────────────────
[Deal Name] — $[Value] | [Stage] | Close: [Date]
BANT: Budget [✓/⚠️] | Authority [✓/⚠️] | Need [✓/⚠️] | Timeline [✓/⚠️]

📋 OPEN ITEMS
────────────────────────────────────────────────────────────────
⚠️ [Overdue task or commitment — due date]
🎫 [Open support case — priority and age]

📅 SUGGESTED AGENDA
────────────────────────────────────────────────────────────────
1. [Agenda point 1]
2. [Agenda point 2]
3. [Agenda point 3]
4. [Discovery: BANT gap]
5. Next steps and decision timeline

❓ DISCOVERY QUESTIONS
────────────────────────────────────────────────────────────────
• [Question targeting identified BANT gap]
• [Question targeting identified BANT gap]
• [Question targeting identified BANT gap]

⚠️ WATCH OUTS
────────────────────────────────────────────────────────────────
• [Risk 1]
• [Risk 2]
════════════════════════════════════════════════════════════════
```

**Full Briefing Output** (when account-level view requested):
```
════════════════════════════════════════════════════════════════
              MEETING BRIEF: [ACCOUNT NAME]
════════════════════════════════════════════════════════════════

Generated: [Date/Time]
Meeting: [Meeting subject if found]
Scheduled: [Meeting time if found]
Attendees: [Expected attendees if found]

════════════════════════════════════════════════════════════════
📊 COMPANY SNAPSHOT
════════════════════════════════════════════════════════════════

Company: [Account Name]
Industry: [Industry]
Size: [n] employees | Revenue: $[n]M
Location: [City, State]

Relationship Status: [PROSPECT / ACTIVE CUSTOMER / AT RISK]
Customer Since: [Date] ([n] years)
Account Owner: [Rep Name]
Total Lifetime Value: $[sum of won opportunities]

════════════════════════════════════════════════════════════════
👥 KEY STAKEHOLDERS
════════════════════════════════════════════════════════════════

[For each contact — name, title, role classification, last contact date, notes]

STAKEHOLDER MAP:
┌─────────────────────────────────────────────────────┐
│  [Economic Buyer]                                   │
│       │                                             │
│       ├── [Champion] ← CHAMPION ✓                  │
│       │       │                                     │
│       │       └── [Technical Buyer] ✓              │
│       │                                             │
│       └── [Blocker/Gap] ← Needs engagement ⚠️      │
└─────────────────────────────────────────────────────┘

════════════════════════════════════════════════════════════════
💼 CURRENT OPPORTUNITIES
════════════════════════════════════════════════════════════════

[For each open opportunity — value, stage, close date, BANT status, next steps, risks]

BANT STATUS:
• Budget: [✓ Confirmed / ⚠️ Unknown / ✗ No Budget]
• Authority: [✓ Decision maker confirmed / ⚠️ Not identified]
• Need: [✓ Strong / ⚠️ Moderate / ✗ Weak]
• Timeline: [✓ This quarter / ⚠️ This year / ✗ Unknown]

DEAL HISTORY (Won):
[List of won deals with value and date]
TOTAL WON: $[value] | EXPANSION POTENTIAL: [High/Medium/Low]

════════════════════════════════════════════════════════════════
📞 RECENT ACTIVITY SUMMARY
════════════════════════════════════════════════════════════════

LAST 90 DAYS: [n] activities | [n] calls | [n] meetings | [n] emails
Trend: [Stable / Increasing / Declining]

RECENT HIGHLIGHTS:
[Date] [Type] with [Contact] — [Subject] — [Outcome]
...

ENGAGEMENT GAPS:
⚠️ [Contact name]: Last contact [n] days ago — [context]

════════════════════════════════════════════════════════════════
🎫 SUPPORT CASE STATUS
════════════════════════════════════════════════════════════════

OPEN CASES: [n]
[Case title | Priority | Age | Status | Summary]

RECENT CLOSED (90 days): [n] | Avg satisfaction: [score]/5

════════════════════════════════════════════════════════════════
🎯 MEETING PREPARATION
════════════════════════════════════════════════════════════════

SUGGESTED AGENDA:
1–5 [Agenda points based on open items and opportunity stage]

DISCOVERY QUESTIONS (by BANT gap):
[Questions targeting only identified weak signals]

POSITIVE TALKING POINTS:
✅ [Value reinforcement, ROI, recent wins]

ISSUES TO ADDRESS:
⚠️ [Support cases, overdue commitments, competitive mentions]

CONTINGENCY RESPONSES:
[Specific responses for anticipated difficult topics]

════════════════════════════════════════════════════════════════
📋 PRE-MEETING ACTION ITEMS
════════════════════════════════════════════════════════════════

□ [Action 1 — e.g., check open case status before meeting]
□ [Action 2 — e.g., prepare ROI summary]
□ [Action 3 — e.g., re-send security docs]

════════════════════════════════════════════════════════════════
📚 QUICK REFERENCE
════════════════════════════════════════════════════════════════

Key Numbers: LTV $[n] | Current Deal $[n] | Employees [n] | Customer [n] yrs
Key Dates: Deal close [date] | Contract renewal [date] | Last purchase [date]
Key Relationships: Champion [name] | Technical [name] | Gap [name]
Products Owned: [list]

════════════════════════════════════════════════════════════════
```

#### Step 8: Create Post-Meeting Task
After delivering the brief, offer to create a follow-up task:
```
Use create_record with tablename: task
- subject: "Post-meeting: Log notes and next steps for [Account]"
- description: "Meeting with [Contact] on [Date]. Capture: key discussion points, decisions made, action items, next steps."
- regardingobjectid: [accountid or opportunityid]
- scheduledend: [meeting end time + 30 minutes]
- prioritycode: 2
```

### Output Format
- **Quick Prep:** Scannable in under 2 minutes. Meeting header → deal snapshot → open items → agenda → discovery questions → watch outs.
- **Full Briefing:** Digestible in under 10 minutes. All Quick Prep sections plus relationship health, stakeholder map, full activity timeline, deal history, contingency responses, and pre-meeting action checklist.

### Example Interaction

**User Input (Quick Prep):**
"Prep me for my call with Fabrikam tomorrow at 2pm."

**Skill Output:**
```
════════════════════════════════════════════════════════════════
              QUICK PREP: FABRIKAM
════════════════════════════════════════════════════════════════
Meeting: QBR — Cloud Migration
Tomorrow, 2:00–3:00 PM | Teams (link in calendar)
Attendees: Emily Torres (VP Operations) | James Liu (IT Director — new)

📊 ACCOUNT SNAPSHOT
Manufacturing | 1,200 employees | Customer since 2022 | LTV: $275,000

💼 ACTIVE DEAL
Fabrikam Cloud Migration — $85,000 | Propose | Close: Mar 31
BANT: Budget ⚠️ | Authority ⚠️ | Need ✓ | Timeline ✓

📋 OPEN ITEMS
⚠️ Overdue task: "Send updated SOW to Emily" — was due 5 days ago
🎫 Open case: Priority 2 — "API integration timeout errors" (12 days open)

📅 SUGGESTED AGENDA
1. Acknowledge API case — share resolution status
2. Present updated SOW (resolve overdue commitment)
3. Confirm budget approval process (budget gap)
4. Understand James Liu's role in the decision (authority gap)
5. Agree on next steps and decision timeline by Mar 31

❓ DISCOVERY QUESTIONS
• "Has budget been formally allocated for the cloud migration this quarter?"
• "James — what's your involvement in evaluating this solution?"
• "What needs to happen on your end to make a decision by March 31?"

⚠️ WATCH OUTS
• 🔴 Competitor mentioned in notes — be ready with differentiation narrative
• 🟡 Open support case — coordinate with support before call; acknowledge it first
════════════════════════════════════════════════════════════════
```

**User Input (Full Briefing):**
"Give me a full briefing on Contoso."

*(Full Briefing output — all sections as shown in the Full Briefing template above.)*

### Dataverse Tables Used
| Table | Purpose |
|-------|---------|
| `account` | Primary account information |
| `contact` | Stakeholder details and role mapping |
| `opportunity` | Current and historical deals, BANT fields |
| `opportunityproduct` | Products in deals |
| `activitypointer` | Activity history |
| `appointment` | Meeting context and scheduling |
| `activityparty` | Meeting attendees (required/optional) |
| `phonecall` | Call history |
| `email` | Email history |
| `incident` | Support case status |
| `annotation` | Notes and additional context |
| `task` | Create post-meeting tasks |
| `product` | Product names |

### Key Fields Reference
**account:**
- `industrycode` (CHOICE) - Industry classification
- `numberofemployees` (INT) - Company size
- `revenue` (MONEY) - Annual revenue
- `primarycontactid` (LOOKUP → contact) - Main contact
- `websiteurl` (URL) - Company website
- `statecode` (STATE) - Active(0), Inactive(1)

**contact:**
- `fullname` (NVARCHAR) - Contact name
- `jobtitle` (NVARCHAR) - Title for role mapping
- `emailaddress1` (EMAIL) - Primary email
- `telephone1`, `mobilephone` (PHONE) - Phone numbers
- `accountrolecode` (CHOICE) - Decision Maker(1), Employee(2), Influencer(3)
- `msdyn_decisioninfluencetag` (CHOICE) - Decision maker(0), Influencer(1), Blocker(2), Unknown(3)

**opportunity:**
- `salesstage` (CHOICE) - Qualify(0), Develop(1), Propose(2), Close(3)
- `estimatedvalue` (MONEY) - Deal value
- `closeprobability` (INT) - Win probability (0-100)
- `budgetstatus` (CHOICE) - No Committed Budget(0), May Buy(1), Can Buy(2), Will Buy(3)
- `need` (CHOICE) - Must have(0), Should have(1), Good to have(2), No need(3)
- `purchasetimeframe` (CHOICE) - Immediate(0), This Quarter(1), Next Quarter(2), This Year(3), Unknown(4)
- `purchaseprocess` (CHOICE) - Individual(0), Committee(1), Unknown(2)
- `decisionmaker` (BIT) - Decision maker confirmed (true/false)
- `msdyn_forecastcategory` (CHOICE) - Pipeline(100000001), Best case(100000002), Committed(100000003), Omitted(100000004)
- `customerpainpoints` (MULTILINE TEXT) - Customer pain points
- `currentsituation` (MULTILINE TEXT) - Current situation notes

**activityparty:**
- `activityid` (LOOKUP) - Links to appointment
- `partyid` (LOOKUP) - Contact, lead, or systemuser
- `participationtypemask` (CHOICE) - Organizer(1), To/Required(2), Owner(5), Customer(9)

**incident:**
- `statecode` (STATE) - Active(0), Resolved(1), Cancelled(2)
- `prioritycode` (CHOICE) - High(1), Normal(2), Low(3)
- `customersatisfactioncode` (CHOICE) - Very Dissatisfied(1), Dissatisfied(2), Neutral(3), Satisfied(4), Very Satisfied(5)

### Meeting Prep Best Practices
1. **Quick Prep** is designed to be generated 15 minutes before a call — keep it fast and focused
2. **Full Briefing** is for QBRs, executive meetings, and renewal discussions — generate the day before
3. Action-oriented: focus on what to do, not just what to know
4. Always address open support cases proactively — don't let the customer bring it up first
5. Discovery questions should only target actual gaps — don't ask about things already confirmed
6. Generate close to meeting time to ensure fresh data

## Examples

### Example 1: Quick Meeting Prep

**User says:** "I have a meeting with Contoso in 30 minutes"

**Actions:**
1. Find account "Contoso" and upcoming appointment
2. Retrieve meeting attendees from activityparty
3. Get contact details and roles for each attendee
4. Pull open opportunities and identify BANT gaps
5. Generate quick prep brief

**Result:**
```
QUICK PREP: Contoso - Call at 2:00pm

ATTENDEES:
- John Smith (CFO) - Economic Buyer, first meeting
- Mary Johnson (VP Ops) - Champion, met 3x before

OPEN DEAL: Enterprise Platform - $150K - Propose Stage
BANT Gaps: Timeline not confirmed, procurement not engaged

DISCOVERY QUESTIONS:
1. "What's your target go-live date?"
2. "When should we loop in procurement?"

CAUTION: 1 open support case (P2) - be prepared to address

OBJECTIVE: Confirm timeline, get procurement intro
```

### Example 2: Full Account Briefing

**User says:** "Give me a full briefing on Fabrikam"

**Actions:**
1. Retrieve complete account information
2. Map all stakeholders with roles and engagement history
3. Calculate lifetime value and relationship health
4. Summarize all opportunities (open and closed)
5. Review recent activities and support cases

**Result:**
```
ACCOUNT BRIEFING: FABRIKAM INC

OVERVIEW:
Industry: Manufacturing | Employees: 2,500 | Revenue: $500M
Customer Since: 2022 | Lifetime Value: $450K

RELATIONSHIP HEALTH: Good (78/100)
- Engagement Trend: Stable
- Last Contact: 3 days ago

STAKEHOLDER MAP:
- Tom CEO → Executive Sponsor (met 2x)
- Lisa VP IT → Technical Buyer (highly engaged)
- Mark Procurement → Gatekeeper (not yet engaged)

OPEN OPPORTUNITIES:
1. Analytics Expansion - $80K - Develop Stage

HISTORICAL WINS:
- Platform License (2022) - $200K
- Integration Services (2023) - $170K

OPEN CASES: None

STRATEGIC NOTES:
- Mentioned expansion to Europe in last QBR
- Budget cycle is Q4
```

### Example 3: Prep for Specific Meeting

**User says:** "Prep me for my call with the Alpine Ski House team at 10am tomorrow"

**Actions:**
1. Find tomorrow's 10am appointment with Alpine Ski House
2. Identify all attendees
3. Generate tailored prep based on meeting subject and attendees

**Result:**
```
MEETING PREP: Alpine Ski House - Demo Call
Tomorrow 10:00am | Teams Meeting

ATTENDEES & CONTEXT:
- Sarah Chen (IT Director) - Technical evaluator, driving this initiative
- James Wilson (CFO) - First time joining, likely budget discussion

MEETING OBJECTIVE: Demo + Budget Alignment

TALKING POINTS FOR CFO:
- ROI from similar implementations (include case study)
- Implementation timeline and resource requirements
- Pricing and payment terms

PREP: Have pricing sheet ready, expect budget questions
```

## Troubleshooting

### Error: Account not found
**Cause:** Name doesn't match exactly or account is inactive
**Solution:**
- Use partial match (LIKE '%name%')
- Check for common spelling variations
- Include inactive accounts if recently deactivated

### Error: No upcoming appointments found
**Cause:** Meeting not in Dynamics or linked to different record
**Solution:**
- Search by date range instead of regardingobjectid
- Check appointments on related contacts
- Fall back to Full Briefing mode if no meeting found

### Error: Attendee details unavailable
**Cause:** External attendees not in system or activityparty not populated
**Solution:**
- Use email domain to identify company
- Prompt user to add contact records
- Note external attendees separately