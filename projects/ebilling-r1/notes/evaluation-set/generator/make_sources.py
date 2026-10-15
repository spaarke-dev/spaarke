"""Writes generator/sources.json - the registry of real-document metadata (URL, licence, local path).
Run from the evaluation-set folder:  python -I generator/make_sources.py"""
import json

GOV = ("No licence stated. Government/public-institution publication posted on the open web; used only as a "
       "public document for internal evaluation, not redistributed.")
REC = ("No licence stated. Court record filed on PACER and hosted by the claims agent; used only as a "
       "public record for internal evaluation, not redistributed.")

ocg = [
    ("berks", "County of Berks (PA) Outside Counsel Guidelines (2016)", "https://berkspa.gov/getmedia/5f301be1-b62d-43fe-b439-6a3c869d321b/Outside_Counsel_Guidelines.pdf"),
    ("delaware", "Delaware Department of Justice Outside Counsel Billing Policy (bid JUD-DOJ25001)", "https://bidcondocs.delaware.gov/JUD/JUD-DOJ25001-OUTSIDE_COUNSEL-billing.pdf"),
    ("maricopa-opds", "Maricopa County Office of Public Defense Services Billing Guidelines", "https://listos.maricopa.gov/DocumentCenter/View/58252/OPDS-Billing-Guidelines"),
    ("ms-dfa", "Mississippi DFA Billing Guidelines for Defense Attorneys (amended 2022-10-17)", "https://www.dfa.ms.gov/sites/default/files/Tort%20Home/Contact%20-%20Services%20and%20Resources%20-%20Claims/Billing%20Guidelines%20for%20Defense%20Attorneys%20-%20amended%2010172022.pdf"),
    ("nmsu", "New Mexico State University Outside Counsel Guidelines (Exhibit B)", "https://legal.nmsu.edu/wp-content/uploads/sites/28/2021/11/NMSU-Outside-Counsel-Guidelines.pdf"),
    ("nv-dids", "Nevada DIDS Attorney Fee Billing Guidelines (2025-03-04)", "https://dids.nv.gov/uploadedFiles/didsnvgov/content/Resources/Forms/DIDS%20Billing%20Guidelines%20-%20Attorney%20Fees%2003042025.pdf"),
    ("santabarbara-courts", "Santa Barbara Superior Court appointed-counsel billing guidelines (FC-3150, 2026-04-22)", "https://www.santabarbara.courts.ca.gov/system/files/general/fc-3150-mc-billing-guidelines-042226.pdf"),
    ("solano-courts", "Solano Superior Court appointed-counsel billing guidelines (rev 2025-11-18)", "https://solano.courts.ca.gov/system/files/general/billing-guidelines-cac-fc3150-rev11-18-25.pdf"),
    ("texas-oag", "Texas Attorney General rules on outside counsel contracts, 1 TAC ch. 57 (via UT System)", "https://www.utsystem.edu/sites/default/files/offices/general-counsel/OutsideCounsel/OAGRulesreOutsideCounsel.pdf"),
    ("ucop", "University of California standard core legal Outside Counsel Guidelines", "https://ucop.edu/uc-legal/_files/standard-core-legal-outside-counsel-guidelines.pdf"),
    ("wyandotte", "Unified Government of Wyandotte County / Kansas City KS Outside Counsel Guide (bid N33053 Exhibit B)", "https://purchasing.wycokck.org/eprocurement/bids/N33053/Exhibit%20B%20-%20Outside%20counsel%20guide.pdf"),
    ("la-orm-attorney-billing", "Louisiana Office of Risk Management attorney billing guidelines", "https://www.doa.la.gov/media/bu1jwq0w/attorney-billing-guidelines.pdf"),
    ("ohio-ag-retention", "Ohio Attorney General Special Counsel Retention Agreement FY20-FY21", "https://ohioattorneygeneral.gov/Files/Legal/Outside-Counsel/General-Appointments/FY20-FY21-Special-Counsel-Retention-Agreement.aspx"),
    ("nj-oag", "State of New Jersey Division of Law Outside Counsel Guidelines", "https://www.nj.gov/oag/law/pdf/rfqs/oag-dol-Outside-Counsel-Guidelines.pdf"),
    ("cook-forest-preserve", "Forest Preserve District of Cook County Billing Guidelines for Outside Counsel (2023-11-16)", "https://fpdcc.com/downloads/policies/FPCC-Outside-Counsel-Guidelines-111623.pdf"),
    ("gatech", "Georgia Institute of Technology Outside Counsel Guidelines (Feb 2023)", "https://generalcounsel.gatech.edu/sites/default/files/2024-01/GT%20OGC%20Outside%20Counsel%20Guidelines%20(Final%20February%202023).pdf"),
]

rc = [
    ("uk-ag-panel.html", "Attorney General's civil panel counsel hourly rates (UK, from 2025-04-01)", "https://www.gov.uk/guidance/attorney-generals-panel-counsel-practical-information", "Open Government Licence v3.0", "open"),
    ("la-memo-legal-services-2024.docx", "Louisiana Maximum Hourly Fee Schedule for professional legal services (memo 2024-02-22)", "https://doa.louisiana.gov/media/kzvd34i0/memo-legal-services-contract-changes-effective-02-22-2024-ada.docx", GOV, "public-record-no-licence"),
    ("cja-2026-ohsd.pdf", "Criminal Justice Act panel attorney rate chart as of 2026-01-01 (S.D. Ohio)", "https://www.ohsd.uscourts.gov/sites/ohsd/files/_2026_CJA%20Rate%20Chart.pdf", "No licence stated. US federal judiciary publication (US government work, 17 USC 105: no copyright).", "open"),
    ("fl-rule-2-37-030.doc", "Florida Administrative Code 2-37.030 standard fee schedule for outside attorneys", "https://flrules.org/gateway/notice_Files.asp?ID=5583337", GOV, "public-record-no-licence"),
    ("youngconaway-lugano-exhibit-b.pdf", "Young Conaway timekeeper rate schedule (UST Appendix B), In re Lugano Diamonds & Jewelry, Doc 681", "https://casedocs.omniagentsolutions.com/cmsvol2/pub_47571/6868038c-8c01-43d4-8650-ef1785e91dd6_681.pdf", REC, "public-record-no-licence"),
]

fa = [
    ("klgates-akoustis", "K&L Gates LLP first interim fee application, In re Akoustis Technologies (Bankr. D. Del. 24-12796, Doc 518)", "https://cases.stretto.com/public/x368/13098/PLEADINGS/1309804302580000000256.pdf", "Stretto"),
    ("foleyhoag-pear", "Foley Hoag LLP second monthly application, In re Pear Therapeutics (Bankr. D. Del. 23-10429, Doc 314)", "https://cases.stretto.com/public/X237/12136/PLEADINGS/1213607142380000000188.pdf", "Stretto"),
    ("polsinelli-n23-07541", "Polsinelli PC twelfth monthly fee statement (Bankr. N.D. Ill. 23-07541, Doc 782)", "https://cases.stretto.com/public/x243/12204/PLEADINGS/1220407182480000000283.pdf", "Stretto"),
    ("covington-mmar", "Covington & Burling LLP second interim and final application, Montreal Maine & Atlantic Railway (Bankr. D. Me. 13-10670, Doc 1931)", "https://www.richter.ca/wp-content/uploads/Insolvency-Cases/en/M/Montreal-Maine-and-Atlantic-Canada-Co/Chapter-11---Montreal-Maine-and-Atlantic-Railway-Ltd/Motions/429-DE1931-Covington-final-fee-appl-20160120.pdf", "Richter (monitor)"),
    ("whburkley-crownboiler", "WH Burkley LLP monthly fee statement, In re Crown Boiler Co. (Bankr. W.D. Pa. 26-20515, Doc 462)", "https://casedocs.omniagentsolutions.com/cmsvol2/pub_47595/c5876153-e1c2-42b0-bb1a-4f28ae263c0d_462.pdf", "Omni"),
    ("ellis-redriver", "Randi S. Ellis second monthly fee statement, In re Red River Talc (Bankr. S.D. Tex. 24-90505, Doc 1042)", "https://document.epiq11.com/document/getdocumentsbydocket/?docketId=1140755&projectCode=RRI&docketNumber=1042", "Epiq"),
    ("consolidatedburger-final", "Berger Singerman LLP third and final application, In re Consolidated Burger Holdings (Bankr. N.D. Fla. 25-40162, Doc 530)", "https://casedocs.omniagentsolutions.com/cmsvol2/pub_47550/f21a1370-4747-430a-85f2-961b34c67e86_530.pdf", "Omni"),
    ("zettajet-final", "WongPartnership LLP final fee application, In re Zetta Jet USA (Bankr. C.D. Cal. 2:17-bk-21386)", "https://casedocs.omniagentsolutions.com/cmsvol2/pub_47213/be10e655-5202-4772-9cd4-5045e4a8308b_2854.pdf", "Omni"),
]

out = {"retrieved": "2026-10-15", "ocg": [], "rate_cards": [], "fee_applications": []}
for i, (f, t, u) in enumerate(ocg, 1):
    out["ocg"].append({"id": f"E-OCG-{i:02d}", "title": t, "source_url": u, "licence": GOV,
                       "licence_status": "public-record-no-licence", "local_path": f"local/ocg/{f}.pdf"})
for i, (f, t, u, lic, st) in enumerate(rc, 1):
    out["rate_cards"].append({"id": f"E-RC-{i:02d}", "title": t, "source_url": u, "licence": lic,
                              "licence_status": st, "local_path": f"local/rate-cards/{f}"})
for i, (f, t, u, a) in enumerate(fa, 1):
    out["fee_applications"].append({"id": f"A-{i:02d}", "title": t, "source_url": u, "claims_agent": a, "licence": REC,
                                    "licence_status": "public-record-no-licence", "local_path": f"local/fee-applications/{f}.pdf"})
with open("generator/sources.json", "w", encoding="utf-8") as fh:
    json.dump(out, fh, indent=1)
print(len(out["ocg"]), len(out["rate_cards"]), len(out["fee_applications"]))
