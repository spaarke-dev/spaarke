# Test Scenarios: Account Briefing Generator Skill

## Overview
These test scenarios validate the **Account Briefing Generator** skill's ability to create comprehensive pre-meeting briefings by aggregating account information, recent activities, opportunity status, stakeholder details, and relevant context for effective customer conversations.

---

## What You Need

To test this skill, your environment should have:
- At least one **account** with a complete profile (industry, employees, revenue, address, website, description)
- Multiple **contacts** linked to the account with roles (`jobtitle`), contact details, and description fields containing relationship notes
- At least one **open opportunity** linked to the account at a mid-to-late stage
- **Recent activity records** (phone calls, emails, appointments) linked to the account or opportunity from the last 30 days
- **Annotations** (notes) with strategic context — competitive intelligence, relationship insights, account strategy
- Optionally, **historical won opportunities** for the account (showing customer lifetime value)
- Optionally, **support cases** linked to the account

> **Tip:** The richer the account's data, the more comprehensive the briefing. Accounts with contacts that have detailed `description` fields (personality notes, communication preferences) produce the best stakeholder sections.

---

## Test Scenario 1: Standard Pre-Meeting Briefing

### Prompt
```
I have a meeting with [account name] tomorrow. Give me a briefing
```
Use an account with active opportunities and recent engagement.

### What to Look For
- Skill aggregates account profile, contacts, opportunities, activities, notes, and cases
- Generates a structured meeting-ready briefing
- Includes talking points tailored to the current deal status
- Surfaces relationship intelligence and communication preferences from notes
- Highlights open issues or risks that could come up in the meeting

### Expected Output Should Include
- **Quick reference** — company name, location, industry, size, revenue, customer-since date
- **Relationship status** — health indicator based on engagement and case data
- **Active opportunities** — stage, value, close date, current status, recent developments, talking points
- **Key stakeholders** — name, title, contact info, role in the deal, relationship notes, communication preferences
- **Recent activity summary** — last 7–14 days of interactions in chronological order
- **Open issues** — unresolved support cases or pending items
- **Strategic context** — notes about competitive landscape, long-term goals, sensitivities

### Pass Criteria
- [ ] Account profile accurately summarised
- [ ] All open opportunities included with current status
- [ ] Talking points generated per opportunity
- [ ] Key contacts listed with relationship intelligence
- [ ] Recent activities summarised
- [ ] Open issues and risks surfaced
- [ ] Strategic context from notes included
- [ ] Communication preferences highlighted (if in the data)

---

## Test Scenario 2: Briefing for a Specific Contact Meeting

### Prompt
```
I'm meeting [contact name] from [account name] tomorrow. What should I know?
```
Use a contact name linked to an account in your environment.

### What to Look For
- Skill focuses the briefing on the specific contact while providing account context
- Includes the contact's role, title, communication style, and past interactions
- Surfaces what was last discussed with this specific person
- Provides talking points tailored to this stakeholder's interests and role

### Pass Criteria
- [ ] Briefing focused on the specified contact
- [ ] Contact's role, preferences, and history highlighted
- [ ] Last interactions with this contact summarised
- [ ] Talking points tailored to this stakeholder's interests
- [ ] Account context provided as background

---

## Test Scenario 3: Briefing with Historical Context

### Prompt
```
Give me a full briefing on [account name] including our history with them
```

### What to Look For
- Skill includes historical data: previous won/lost opportunities, total customer value, how long they've been a customer
- Summarises the evolution of the relationship over time
- Includes strategic notes and long-term account plans from annotations

### Expected Output Should Include
- Everything from the standard briefing PLUS:
- **Relationship timeline** — key milestones (first deal, expansions, support incidents)
- **Total customer value** — sum of won opportunity values
- **Historical deals** — previous wins and losses with values and dates
- **Strategic notes** — long-term plans, competitive threats, growth opportunities

### Pass Criteria
- [ ] Historical deals summarised (won and lost)
- [ ] Total customer value calculated
- [ ] Relationship timeline or milestones included
- [ ] Strategic context from notes integrated
- [ ] Briefing useful for someone unfamiliar with the account

---

## Test Scenario 4: Briefing for a New Account (First Meeting)

### Prompt
```
I'm meeting [account name] for the first time. What do we know about them?
```
Use an account with limited or no prior activity.

### What to Look For
- Skill handles sparse data gracefully
- Presents whatever firmographic data is available (industry, size, location)
- Identifies contacts if any exist
- Flags gaps — "No prior interactions on record"
- Suggests preparation steps (research company, prepare discovery questions)

### Pass Criteria
- [ ] Available data presented accurately
- [ ] Gaps clearly noted (no prior meetings, no notes on file)
- [ ] Preparation suggestions provided
- [ ] No fabricated history or context

---

## Test Scenario 5: Competitor-Aware Briefing

### Prompt
```
Brief me on [account name] — I hear they're also talking to competitors
```

### What to Look For
- Skill searches for competitor mentions in notes, annotations, and opportunity descriptions
- Surfaces any competitive intelligence found in the data
- Provides talking points to address competitive concerns
- Flags if no competitive data is found and suggests gathering it

### Pass Criteria
- [ ] Competitor mentions extracted from notes and descriptions
- [ ] Competitive talking points included if data exists
- [ ] "No competitive intelligence found" returned cleanly if none exists
- [ ] Recommendation to gather competitive context during the meeting

---

## Test Scenario 6: Briefing Across Multiple Opportunities

### Prompt
```
Brief me on [account name] — they have several active deals
```
Use an account with 2+ open opportunities.

### What to Look For
- Skill covers ALL open opportunities for the account, not just one
- Each opportunity summarised with its own status and talking points
- Cross-opportunity context provided (e.g., total pipeline value, dependencies between deals)

### Pass Criteria
- [ ] All open opportunities covered
- [ ] Per-opportunity status and talking points
- [ ] Total pipeline value for the account calculated
- [ ] Relationships between deals noted if applicable

---

## Edge Cases

| Scenario | What to Try | Expected Behavior |
|----------|-------------|-------------------|
| Account with no contacts | Account record exists but no linked contacts | Briefing generated from account data; flags "no contacts on record" |
| Account with many contacts (10+) | Large stakeholder group | Contacts prioritised by role and recency; key decision-makers featured prominently |
| Account with no opportunities | Customer with no active deals | Briefing focuses on relationship status, support history, and potential |
| Account with only closed deals | No open opportunities, only historical | Includes historical summary; notes no active deals |
| Meeting with unknown contact | "I'm meeting someone new at [account]" | Provides account context; suggests discovery about the new person |
| Same-day urgent briefing | "I have a call with [account] in 30 minutes" | Provides a concise quick-reference format prioritising the most critical info |
