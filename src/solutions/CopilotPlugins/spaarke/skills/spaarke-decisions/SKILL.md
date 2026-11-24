---
name: spaarke-decisions
description: "Show the Work Items waiting for the signed-in user's decision in Spaarke (the Decide lane), with the count per category and a decisions widget where the person reviews and completes them. Use when the user directly asks what decisions or Work Items are waiting on them in Spaarke, for example 'what decisions are waiting for me in Spaarke', 'show my Spaarke Work Items to decide' or 'any invoice decisions in Spaarke?'. Not for searching documents, matters or email, not for the Do lane or decided history, and never for completing a decision in chat: that happens only in the widget or in Spaarke."
---

# Spaarke decisions

Use this skill to show a person the Work Items that are waiting for their decision in Spaarke. It calls one tool, `spaarke_show_decisions`, which comes from the connector "Spaarke MCP". The tool reads the signed-in person's Decide lane as that person, so it shows only what their own Spaarke account may see.

## When to call spaarke_show_decisions

Call it when the person asks directly about decisions, or Work Items waiting on them, in Spaarke. Examples:

- "What decisions are waiting for me in Spaarke?"
- "Show my Spaarke Work Items to decide."
- "Do I have any invoice decisions in Spaarke?"
- "What do I need to ask outside counsel about?"
- "Is anything waiting on a rebudget approval?"

The tool takes one optional argument, `category`. Leave it out to show everything. Pass it only when the person names one of these categories, and use the exact value:

| Value | Category card in Spaarke |
|---|---|
| `all` | All (the default) |
| `invoiceDecisions` | Invoice decisions |
| `askOutsideCounsel` | Ask outside counsel |
| `approveOrRebudget` | Approve or rebudget |
| `chaseReply` | Chase a reply |
| `other` | Other |

Do not invent any other value or argument.

## What the person sees

- In a host that shows widgets, the decisions widget lists the whole lane. The person reviews each Work Item there and completes the decision there. Tell them to use the widget.
- In a host that does not show widgets, the text result gives the count per category and the top Work Items, each with a link to open it in Spaarke. Tell them to open the item in Spaarke to review it.
- A Work Item that is privileged is only counted, as "N privileged items — open to review". Repeat that line as it is. The detail is not available to you; the person opens it in Spaarke.
- A billing Work Item (category `invoiceDecisions`) links out to Invoice Review in Spaarke, where it is called a Finding. Offer the link; the decision is made in Invoice Review.

## What not to do

- Never complete, approve, dismiss, reject or otherwise decide a Work Item yourself, and never offer to. Completion is only by the person, through the widget or in Spaarke. A completed decision becomes a Decision Record in Spaarke; you do not write one.
- Do not suggest answers to a decision, guess what the person should choose, or summarise the detail of an item you were not shown.
- Do not use this skill to search documents, matters, email or the Do lane, or to list decisions already made. Say that this skill only shows decisions that are waiting.
- Do not call the tool again to "refresh" it in the same answer unless the person asks.
- If the tool is not available, tell the person that the "Spaarke MCP" connector is not connected for them, and that they need to connect it or ask their administrator. Do not try another way to read Spaarke data.
- If the tool returns an error, tell the person in one sentence what it says. Do not guess a cause and do not retry in a loop.
