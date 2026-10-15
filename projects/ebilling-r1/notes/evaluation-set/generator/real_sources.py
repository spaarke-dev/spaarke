"""Registry of the real (set C) invoices: one row per invoice cut from a public regulatory filing.

All are law-firm invoices that a utility filed with a state public service commission in a rate-case docket (public record).
No licence is stated on the filings; they are used only as public records for internal evaluation and are not redistributed
(the PDFs stay in local/, which is git-ignored; only labels with aliased timekeepers and narrative hashes are committed).
`raw` is the downloaded filing in local/real-invoices-raw/; `pages` is the 1-based inclusive page range of the invoice in it.
"""
KY = "https://psc.ky.gov/pscecf/"
FILINGS = {
    "gs": {"raw": "ky3.pdf", "url": KY + "2019-00053/kristen@gosssamfordlaw.com/06072019112328/Combined_Response.pdf",
           "title": "Jackson Purchase Energy Corporation, KY PSC Case 2019-00053, response with Goss Samford PLLC invoices (Item 30)",
           "firm": "Goss Samford, PLLC", "client_type": "electric cooperative", "docket": "KY PSC 2019-00053"},
    "sko": {"raw": "ky2.pdf", "url": KY + "2018-00358/monica.braun@skofirm.com/06142019042023/KAW_SR_LFUCGPHDR_NUM005_061419.pdf",
            "title": "Kentucky-American Water, KY PSC Case 2018-00358, rate case expense with Stoll Keenon Ogden PLLC invoices",
            "firm": "Stoll Keenon Ogden PLLC", "client_type": "water utility", "docket": "KY PSC 2018-00358"},
    "ds": {"raw": "ky2025.pdf", "url": KY + "2025-00332/evan.buckley%40dinsmore.com/06012026085737/2025-00332_DR2-51_Part_2_supplement.pdf",
           "title": "Navitas KY NG, KY PSC Case 2025-00332, data request 2-51 supplement with Dinsmore & Shohl LLP invoices",
           "firm": "Dinsmore & Shohl LLP", "client_type": "gas utility", "docket": "KY PSC 2025-00332"},
}
# id, filing key, first page, last page, invoice number as printed
INVOICES = [
    ("C-01", "gs", 4, 6, "4115"),
    ("C-02", "gs", 7, 10, "4141"),
    ("C-03", "gs", 11, 18, "4240"),
    ("C-04", "gs", 19, 23, "4246"),
    ("C-05", "gs", 24, 29, "4294"),
    ("C-06", "sko", 2, 5, "881085"),
    ("C-07", "sko", 6, 8, "883230"),
    ("C-08", "sko", 9, 12, "887263"),
    ("C-09", "sko", 13, 16, "888722"),
    ("C-10", "sko", 17, 22, "890701"),
    ("C-11", "ds", 1, 4, "6011094"),
    ("C-12", "ds", 5, 9, "6019250"),
]
