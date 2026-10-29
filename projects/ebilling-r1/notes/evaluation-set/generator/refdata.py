"""Reference data for the synthetic evaluation set: fictional firms, people, UTBMS codes and narrative banks.

Everything here is invented (names are random combinations; no real person or firm is intended).
UTBMS code names are the ABA litigation task codes, the ABA/LOC activity codes and the ABA expense codes
(decisions-log D-32: freely usable). Evaluation tooling only - no runtime dependency on the BFF.
"""

FIRST = ["Jordan", "Priya", "Marcus", "Elena", "Tomas", "Aisha", "Gregory", "Sofia", "Daniel", "Nadia", "Oliver", "Mei",
         "Rafael", "Hannah", "Victor", "Imani", "Louis", "Katya", "Samuel", "Leila", "Peter", "Yusuf", "Clara", "Dmitri",
         "Naomi", "Hector", "Ingrid", "Anders", "Beatrix", "Cyrus", "Dalia", "Emeka", "Farah", "Gideon", "Helena", "Ivan"]
LAST = ["Whitfield", "Ramanathan", "Okafor", "Lindqvist", "Brennan", "Castellanos", "Hargrove", "Nakamura", "Delacroix",
        "Abernathy", "Voss", "Pemberton", "Yilmaz", "Kowalczyk", "Thornbury", "Oyelaran", "Fairweather", "Sandoval",
        "Marchetti", "Halloran", "Eriksen", "Nwosu", "Blackwood", "Tavares", "Quillen", "Rosenthal", "Ashdown", "Mbeki",
        "Lockhart", "Santoro", "Wickham", "Zielinski", "Carmichael", "Dunleavy", "Esposito", "Galbraith"]
PARTIES = ["Northwind Logistics", "Meridian Foods", "Caldera Energy", "Bluepine Holdings", "Harbor Point Capital",
           "Larkspur Medical", "Ironbridge Steel", "Solano Freight", "Quarry Lane Partners", "Tidewater Retail",
           "Alder & Finch Ltd", "Redstone Mining", "Pacifica Telecom", "Summit Ridge Homes", "Orchard Row Brands"]
CITIES = ["Chicago", "Denver", "Atlanta", "Houston", "Boston", "Seattle", "Phoenix", "Minneapolis", "Dallas", "Portland"]
TOPICS = ["the supply agreement", "the lease dispute", "the employment claim", "the warranty claim", "the licensing dispute",
          "the construction contract", "the vendor agreement", "the distribution agreement", "the insurance coverage issue",
          "the data incident", "the regulatory inquiry", "the customs audit"]
DOCS = ["complaint", "answer and affirmative defenses", "motion to dismiss", "motion for summary judgment",
        "motion to compel", "protective order", "notice of deposition", "interrogatory responses",
        "requests for production", "discovery plan", "settlement proposal", "mediation statement", "pretrial brief",
        "witness list", "exhibit list", "reply brief"]

FIRM_A = ["Hartwell", "Pryce", "Aldridge", "Kestrel", "Montclair", "Fenwick", "Ravenscroft", "Ostrander", "Sutherland",
          "Calloway", "Draycott", "Eastlake", "Garrison", "Holloway", "Ingersoll", "Jarrow", "Kingsley", "Lindenmeyer",
          "Maddox", "Northcott", "Oakhurst", "Penhallow", "Quimby", "Rutherford", "Stanhope", "Tennant"]
FIRM_B = ["Vance", "Whitcombe", "Yardley", "Zeller", "Ambrose", "Beckett", "Cranmer", "Dunmore", "Ellison", "Forsythe"]
STREETS = ["Market Street", "Commerce Avenue", "Lakeshore Drive", "Federal Plaza", "Peachtree Way", "Congress Boulevard",
           "Elm Court", "Union Square", "Harbor Road", "Pine Street"]

# (title, billed abbreviation, rate factor vs partner rate)
TITLES = [("Partner", "Ptr", 1.00), ("Of Counsel", "O/C", 0.88), ("Senior Associate", "Sr Assoc", 0.70),
          ("Associate", "Assoc", 0.55), ("Paralegal", "Para", 0.30), ("Legal Assistant", "LA", 0.24)]
TITLE_NAMES = [t[0] for t in TITLES]
ATTORNEY_TITLES = {"Partner", "Of Counsel", "Senior Associate", "Associate"}

TASK = {  # ABA litigation task codes
    "L110": "Fact Investigation/Development", "L120": "Analysis/Strategy", "L130": "Experts/Consultants",
    "L140": "Document/File Management", "L150": "Budgeting", "L160": "Settlement/Non-Binding ADR",
    "L190": "Other Case Assessment, Development and Administration", "L210": "Pleadings",
    "L220": "Preliminary Injunctions/Provisional Remedies", "L230": "Court Mandated Conferences",
    "L240": "Dispositive Motions", "L250": "Other Written Motions and Submissions",
    "L310": "Written Discovery", "L320": "Document Production", "L330": "Depositions", "L340": "Expert Discovery",
    "L350": "Discovery Motions", "L410": "Fact Witnesses", "L420": "Expert Witnesses",
    "L430": "Written Motions and Submissions", "L440": "Other Trial Preparation and Support",
    "L450": "Trial and Hearing Attendance", "L460": "Post-Trial Motions and Submissions", "L470": "Enforcement",
    "L510": "Appellate Motions and Submissions", "L520": "Appellate Briefs", "L530": "Oral Argument",
}
ACTIVITY = {"A101": "Plan and prepare for", "A102": "Research", "A103": "Draft/Revise", "A104": "Review/Analyze",
            "A105": "Communicate (within legal team)", "A106": "Communicate (with client)",
            "A107": "Communicate (opponents/other outside counsel)", "A108": "Communicate (other external)",
            "A109": "Appear For/Attend", "A110": "Manage Data/Files/Documentation", "A111": "Other",
            "A112": "Billable Travel Time"}
EXPENSE = {"E101": "Copying", "E102": "Outside printing", "E103": "Word processing", "E104": "Facsimile",
           "E105": "Telephone", "E106": "Online research", "E107": "Delivery services/messengers", "E108": "Postage",
           "E109": "Local travel", "E110": "Out-of-town travel", "E111": "Meals", "E112": "Court fees",
           "E113": "Subpoena fees", "E114": "Witness fees", "E115": "Deposition transcripts",
           "E116": "Trial transcripts", "E117": "Trial exhibits", "E118": "Litigation support vendors",
           "E119": "Experts", "E120": "Private investigators", "E121": "Arbitrators/mediators", "E122": "Local counsel",
           "E123": "Other professionals", "E124": "Other"}

# Clear narratives by task code: (activity, text). Placeholders: {topic} {party} {doc} {city}.
# None of these may contain a keyword used by NonBillableActivity rules (checked in rules.py).
CLEAR = {
    "L110": [("A104", "Review and analyze client documents concerning {topic} to identify key facts"),
             ("A106", "Telephone conference with client representative regarding factual background of {topic}"),
             ("A102", "Research public records concerning {party} and related entities")],
    "L120": [("A103", "Prepare case assessment memorandum regarding {topic} and recommended strategy"),
             ("A101", "Plan strategy for upcoming phase of {topic} with team"),
             ("A104", "Analyze exposure and damages model for {topic}")],
    "L130": [("A106", "Telephone conference with client regarding selection of expert for {topic}"),
             ("A104", "Review expert qualifications and prior testimony for {topic}"),
             ("A108", "Correspondence with consulting expert regarding scope of analysis")],
    "L140": [("A104", "Review and organize incoming production from {party} for matter file"),
             ("A110", "Update document review protocol and coding panel for {topic}"),
             ("A104", "Review privilege log entries before service")],
    "L150": [("A103", "Prepare updated litigation budget for {topic}"),
             ("A106", "Conference with client regarding budget variance for {topic}")],
    "L160": [("A106", "Conference with client regarding settlement authority for {topic}"),
             ("A107", "Telephone conference with opposing counsel regarding settlement terms"),
             ("A103", "Draft settlement term sheet for {topic}")],
    "L190": [("A111", "Prepare monthly status report to client regarding {topic}"),
             ("A104", "Review court docket and calendar of pending deadlines for {topic}")],
    "L210": [("A103", "Draft {doc} in {topic}"), ("A104", "Review and revise {doc} based on client comments"),
             ("A102", "Research pleading standards applicable to {topic}")],
    "L220": [("A103", "Draft motion for preliminary injunction in {topic}"),
             ("A102", "Research standard for provisional remedies concerning {topic}")],
    "L230": [("A101", "Prepare for case management conference in {topic}"),
             ("A109", "Attend case management conference in {topic}")],
    "L240": [("A103", "Draft {doc} and supporting memorandum"), ("A102", "Research case law supporting {doc}"),
             ("A104", "Review opposing brief in response to {doc}")],
    "L250": [("A103", "Draft {doc} in {topic}"), ("A102", "Research authority for {doc}")],
    "L310": [("A103", "Draft {doc} to {party}"), ("A104", "Review and analyze {doc} received from {party}"),
             ("A103", "Prepare responses and objections to {doc}")],
    "L320": [("A104", "Review documents produced by {party} for responsiveness"),
             ("A110", "Prepare production set and cover correspondence for service on {party}"),
             ("A104", "Analyze key documents for deposition preparation")],
    "L330": [("A101", "Prepare outline for deposition of {party} witness"),
             ("A109", "Attend deposition of {party} corporate representative"),
             ("A104", "Review and summarize deposition transcript of {party} witness")],
    "L340": [("A104", "Review expert report served by {party}"), ("A106", "Conference with client expert regarding rebuttal report")],
    "L350": [("A103", "Draft {doc} regarding deficiencies in {party} responses"),
             ("A107", "Meet and confer with counsel for {party} regarding discovery dispute")],
    "L410": [("A101", "Prepare fact witness for testimony regarding {topic}"),
             ("A106", "Telephone conference with witness regarding {topic}")],
    "L420": [("A101", "Prepare expert witness for testimony in {topic}"), ("A104", "Review draft expert disclosure for {topic}")],
    "L430": [("A103", "Draft {doc} for trial in {topic}"), ("A104", "Revise {doc} after review by lead counsel")],
    "L440": [("A101", "Prepare trial notebook and demonstrative exhibits for {topic}"),
             ("A101", "Prepare examination outlines for trial in {topic}")],
    "L450": [("A109", "Attend hearing on {doc} in {topic}"), ("A109", "Attend trial day in {topic}")],
    "L460": [("A103", "Draft post-trial motion in {topic}"), ("A102", "Research standard for new trial in {topic}")],
    "L470": [("A103", "Draft application to enforce judgment against {party}"), ("A108", "Correspondence with garnishee regarding judgment")],
    "L510": [("A103", "Draft notice of appeal and docketing statement for {topic}"), ("A102", "Research appealability issues in {topic}")],
    "L520": [("A103", "Draft appellate brief in {topic}"), ("A104", "Revise appellate brief after cite check")],
    "L530": [("A101", "Prepare for oral argument in {topic}"), ("A109", "Attend and present oral argument in {topic}")],
}
CODES_BY_PHASE = {  # task-code mix used when generating a matter
    "early": ["L110", "L120", "L130", "L140", "L150", "L160", "L190", "L210", "L220", "L230"],
    "discovery": ["L310", "L320", "L330", "L340", "L350", "L140", "L120", "L190", "L240", "L250"],
    "trial": ["L410", "L420", "L430", "L440", "L450", "L120", "L460", "L190"],
    "appeal": ["L510", "L520", "L530", "L120", "L190"],
}
VAGUE = ["Attention to file.", "Review documents.", "Work on case.", "Correspondence.", "Legal research.",
         "Various tasks regarding matter.", "Prepare for hearing.", "Telephone calls.", "Review and respond to emails.",
         "Continue work on project."]
CLERICAL = [("A110", "Scheduled conference call and updated calendar entries"),
            ("A110", "Organized and indexed file materials"),
            ("A110", "Photocopied exhibits and assembled binders"),
            ("A110", "Docketed deadlines in case management system"),
            ("A110", "Ran conflict check for new party"),
            ("A111", "Invoice preparation and review of billing entries")]
INTERNAL = "Internal conference with {tk} regarding strategy for {topic}"
NON_ENGLISH = ["Conferencia telefonica con el cliente sobre {topic}", "Revision des pieces transmises par {party}",
               "Besprechung mit dem Mandanten zu {topic}"]

EXPENSE_BANK = {  # code -> (description template, unit-cost range, quantity range)
    "E101": ("Photocopies - {n} pages", (0.08, 0.15), (30, 900)),
    "E105": ("Long distance conference call charges", (4.0, 40.0), (1, 1)),
    "E107": ("Overnight courier to {party} counsel", (18.0, 65.0), (1, 3)),
    "E108": ("Postage - certified mail", (5.0, 14.0), (1, 4)),
    "E109": ("Mileage to courthouse", (0.60, 0.70), (12, 60)),
    "E110": ("Airfare to {city} for deposition", (310.0, 780.0), (1, 1)),
    "E111": ("Working meal during deposition preparation", (22.0, 60.0), (1, 4)),
    "E112": ("Court filing fee", (65.0, 435.0), (1, 1)),
    "E115": ("Court reporter and transcript - deposition", (480.0, 1950.0), (1, 1)),
    "E118": ("Document hosting and processing vendor", (220.0, 1800.0), (1, 1)),
    "E119": ("Consulting expert fees", (1200.0, 6500.0), (1, 1)),
}
# Expense codes that rule sets may declare non-reimbursable. They never appear in clean invoices of those sets.
EXPENSE_OVERHEAD = {"E103": ("Word processing and document formatting", (25.0, 90.0), (1, 1)),
                    "E104": ("Facsimile charges", (1.0, 4.0), (3, 20)),
                    "E106": ("Online legal research charges", (45.0, 320.0), (1, 1))}
