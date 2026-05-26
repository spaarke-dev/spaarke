# Pattern Spec: Summary / Intelligence-centered

> **Status**: Draft v0.1
> **Pattern slug**: summary-intelligence
> **Closest analogous product:** Power BI (with reference to Tableau for chart variety and Dynamics dashboards for record-aware summaries)
> **Primary user intent:** *"Help me understand what's going on."*

---

## 1. Optimizes For

The Summary pattern optimizes for **situational awareness across many things at once** — what's the current state of the portfolio, what's trending, what stands out, what needs attention. It turns volume into shape.

The user is not asking about a specific thing (Entity) or making a per-item decision (Queue). They're answering "how are we doing" or "what's interesting right now." Summary is where users start their day, where they brief upward, and where they decide whether to drill in.

---

## 2. When To Use / When Not To Use

### 2.1 Use this pattern when

- The user needs an aggregate view across many records (spend across all matters, document throughput across the quarter, average review time across counsel).
- The relevant signal is in the *shape of the data*, not in any one item.
- The user is in an exploratory or briefing mode — gathering picture, not making per-item decisions.
- The user might drill from an aggregate into a record (Summary → Entity) or into a list of items (Summary → Queue), so the pattern needs hand-off mechanics.

### 2.2 Do not use this pattern when

- The user has identified one specific thing to work on (Entity).
- The user must make per-item decisions on many items (Queue).
- The data isn't large enough to need aggregation (5 matters don't need a dashboard).
- The aggregation hides decisions that need to happen at the item level. A bar chart of "47 contracts in negotiation" tells the user a fact; it doesn't help them disposition any of the 47. If disposition is what's needed, this is Queue, not Summary.

### 2.3 Pattern overlaps

- **Queue / Inbox-centered**: Queue is per-item triage. Summary is aggregate state. A Summary widget often links to a Queue ("12 contracts pending → open the pending queue").
- **Entity-centered**: Entity is one record. Summary aggregates across records. Drill-down from a chart segment to a single entity is a common transition.
- **Generative / Conversational**: The user can *ask* for a summary in chat. The result is a generated summary, which often manifests as a Summary widget rendered in Workspace. The patterns interlock; the chat is the prompt, the widget is the answer.

---

## 3. Mechanics

### 3.1 Core mechanics

1. **A single screen showing multiple aggregate views.** Charts, counts, trend lines, leaderboards, status breakdowns — composed so the user can see the picture without scrolling through panels. Power BI's grid layout is the convention.

2. **A primary number or chart that anchors attention.** The most important single signal — "$2.4M spend YTD," "82% on-time review rate," "12 critical-risk matters open." Without an anchor, the user doesn't know where to look first.

3. **Supporting context around the primary number.** Comparison ("up 12% from last quarter"), benchmark ("industry median is 78%"), or trend ("steady decline since March"). A number alone is data; a number in context is information.

4. **Drill-down from any aggregate.** Clicking a chart segment, a bar, or a count opens the underlying records — either as a filtered list (Queue) or a single record (Entity). The user should never reach a summary and not be able to get to the items.

5. **Filter / time-range / scope controls that affect all views simultaneously.** Change "this quarter" to "last quarter" and the whole dashboard updates. Power BI's slicer pattern is the standard; users expect filters to be persistent and scoped to the view.

6. **Refresh state and last-updated indicator.** The user knows whether they're looking at fresh data or a cached view from earlier. Stale dashboards that look live are a trust failure.

### 3.2 Supporting mechanics

1. **Export / share affordances.** Summaries get sent to colleagues and shown in meetings; the pattern must support "send this view" without requiring screenshot workarounds.

2. **Annotations on charts.** "Spike here was the Acme acquisition." "Drop in March was during the freeze." Without annotations, users have to remember context that has to be communicated separately.

3. **Saved views.** A user who builds a useful filter combination should be able to save it ("My open critical matters this quarter") and return to it without rebuilding.

### 3.3 AI augmentation mechanics

This is where the Summary pattern has evolved most in the last 24 months. Power BI Copilot, Tableau Pulse, ThoughtSpot Sage, and others have converged on a set of behaviors.

1. **AI-generated narrative caption.** A short paragraph at the top of the dashboard explaining what the data shows: "Total spend is up 14% this quarter, driven primarily by litigation matters in Q3. Three matters account for 60% of the increase." This is what makes a chart-rich dashboard scannable for users who don't want to interpret it themselves.

2. **AI-surfaced anomalies and insights.** Flags on specific data points that warrant attention — "this counterparty's contract count doubled in the last 30 days," "spend on outside counsel for matter X exceeds budget by 40%." The AI is doing what a careful analyst would do during a manual review.

3. **Conversational drill-in.** "Why is spend up?" answered from the same data the dashboard renders. The chat is in the Conversation pane; the answer often includes pointers back into the dashboard or new chart renders in Workspace.

4. **AI-suggested next views.** "You usually look at outside-counsel spend after the overall view — open that?" or "This anomaly is similar to one from Q2 — see the comparison?" Subtle, not nagging.

---

## 4. Expectations to Honor (Closest Analogous Product: Power BI)

Spaarke users will arrive at the Summary pattern with Power BI muscle memory most often, given the Microsoft tenancy. Some will be coming from Dynamics dashboards, which are simpler. Tableau is rarer in legal-departmental contexts but its conventions cross over.

### 4.1 What Power BI does that Spaarke must match

- **Grid layout of cards / charts / counts** that resizes for screen width.
- **Slicer-style filters** that affect multiple visuals at once and persist across navigation within the dashboard.
- **Drill-down by click** on chart elements (segments, bars, points).
- **Tooltip-on-hover** showing the exact value of a chart element.
- **Cross-filtering** — clicking one chart filters the others to that segment.
- **Color conventions** for status (red/yellow/green for risk-style indicators; sequential color ramps for magnitude; categorical palettes for type).
- **Sort / pivot on tabular views** within the dashboard.
- **A clear loading state** so the user knows the dashboard is fetching, not broken.

### 4.2 Where Spaarke legitimately diverges

| Spaarke divergence | Why |
|---|---|
| AI narrative caption is default-on, not a separate widget | Legal users are not data analysts; a sentence interpreting the chart removes the cognitive step of "what does this mean for me" |
| Anomaly flags appear inline on the chart, not in a separate insights panel | Power BI Copilot puts insights in a side panel; for legal use, putting them on the relevant chart element shortens the "find the thing" path |
| Drill-down from a chart segment may open Queue or Entity in another Workspace tab, not just filter the dashboard | Spaarke's three-pane shell means drill-down can be cross-pattern (Summary → Queue, Summary → Entity), not just within-pattern (Summary filtered) |
| The Conversation pane can ask questions about the dashboard data and get answers that re-render the dashboard | Power BI Copilot does this within the dashboard panel; in Spaarke, the chat is in its own pane, which is structurally different and may need different mechanics for syncing chat-question to dashboard-update |

---

## 5. Common Failure Modes

| Failure mode | Symptom | Fix |
|---|---|---|
| **Dashboard as data dump.** 14 charts, no anchor, no narrative. | Users glance, don't know where to look, close the page. | Identify the primary signal. Make it visually dominant. Everything else is supporting. |
| **Aggregates without drill-down.** A chart of "47 contracts pending" with no way to see the 47. | Users screenshot the chart and email it; the dashboard becomes a static report. | Every aggregate must drill. If the data isn't drillable for technical reasons, label the chart as a report view, not an interactive surface. |
| **Filters that look like data.** A user filters to "last quarter" and forgets; later returns and thinks the data is wrong. | Confusion and loss of trust. | Filter state is always visible and prominent. "Showing: Last quarter • Litigation matters • Active only." |
| **AI narrative that's vague or wrong.** "Performance is steady" when there's a 30% spike on a key metric. | Worse than no narrative — users lose trust in the AI overlay. | Narrative must be specific (numbers, named drivers) and grounded in the data the dashboard shows. If the AI can't produce a specific narrative, no narrative is better than vague. |
| **Cross-filtering that's hard to undo.** A click filters; the user can't figure out how to get back to the unfiltered view. | Users avoid clicking the dashboard because they can't recover from misclicks. | A clear "reset filters" affordance; clicking an empty area of the same chart undoes the filter. |
| **Stale data presented as live.** Last-updated indicator absent or hard to find. | Users make decisions on old data; trust collapses when they find out. | Last-updated is mandatory and visible without scrolling. |

---

## 6. Spaarke Component Mapping

### 6.1 Current design positions

| Pattern element | Current Spaarke component |
|---|---|
| Summary widgets | R1's **StatusSummary**, **BudgetDashboard**, **FindingsWidget** are all Summary-pattern widgets. The pattern is well-represented in the current widget set. |
| Pane assignment | Workspace pane (Summary is primary work surface for briefing/exploration modes). |
| AI augmentation | Not currently specified as a default Summary behavior. The pattern would benefit from a standard "narrative caption" convention. |
| Conversation pane integration | spec.md mentions the SSE `workspace_widget` event renders summary widgets from chat queries (FR-203). The mechanic exists; the user-facing behavior conventions need specification. |

### 6.2 Design-challenge findings

| Finding | Current design | Pattern requires |
|---|---|---|
| **AI narrative caption is not a default Summary feature.** | Summary widgets render data; AI narration is implicit but not specified as a uniform convention. | Define a standard narrative-caption convention — placement, length, regeneration cadence, and how user feedback ("this caption is wrong") flows back to the model. Belongs in the AI Augmentation cross-cutting spec, not just this pattern. |
| **Anomaly flags within charts are not a defined component.** | Findings can appear in FindingsWidget but as a separate widget, not as inline marks on related summary charts. | A convention for inline anomaly marks on chart elements (a different visual treatment from selection or filter). |
| **Drill-down from Summary to other patterns is not specified in detail.** | Cross-pattern transitions are mentioned in design.md §2.2 but the specific "click a chart segment → open Queue in a new Workspace tab" mechanic isn't defined. | The Composition Guide (Phase 5) is where this gets specified per transition. Summary → Queue and Summary → Entity are both high-frequency. |
| **3-tab Workspace ceiling may pinch.** | Workspace max 3 tabs (design.md §12 D-03). | Drilling from a Summary opens another tab (Queue or Entity). If Summary itself is one tab, the user can only drill to two destinations before hitting the ceiling. This may be the right constraint or may need revisiting once we see use cases. Flag for the design-challenge journal. |

### 6.3 New components / events / widgets proposed

- **Anomaly mark convention** — visual treatment for "AI flagged this element" within existing summary widgets.
- **Standard narrative caption** — applied uniformly to StatusSummary, BudgetDashboard, FindingsWidget. Belongs in AI Augmentation cross-cutting.
- **Drill-down transitions** — Summary → Queue, Summary → Entity, Summary → Canvas. Belong in Composition Guide.

---

## 7. Open Questions

| Question | Why it matters | Validation |
|---|---|---|
| What's the right default narrative length? One sentence? Three? A paragraph? | Too short and it's marketing fluff; too long and users skip it. | Prototype testing with primary personas |
| How does the user correct an AI narrative they think is wrong? Inline edit? Flag and regenerate? Override that persists? | Trust over time. If users can't correct, they stop trusting the captions. | Designer's call + pilot instrumentation on override rates |
| When Summary lives in Workspace and the user drills, does the Summary tab close or stay open? Power BI keeps it; Tableau closes it. | UX preference but consequential for the 3-tab ceiling. | Prototype testing |
| Does the Conversation pane drive Summary widgets directly, or does it create a new widget that *includes* the chart? | Architecture and consistency question. The user-perceived difference is whether their old dashboard transforms or a new one appears. | Engineering call + prototype testing |

---

*Draft v0.1 — 2026-05-18.*
