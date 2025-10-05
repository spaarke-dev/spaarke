# NPM Package Decision - Reassessment
**Date:** October 4, 2025
**Context:** User will build OTHER PCF controls (not just Universal Grid variations)
**Status:** 🔄 DECISION REQUIRES RECONSIDERATION

---

## Critical Clarification

**User Statement:**
> "But to be clear--we will build other PCF controls in our environment (not Universal Grid but others)"

**This changes the analysis!**

---

## Key Questions to Determine NPM Package Need

### Question 1: Will Other PCF Controls Need SDAP Integration?

**Scenarios:**

**Scenario A: Other Controls DON'T Need SDAP**
```
Control #1: Universal Dataset Grid
├── Purpose: Document/file management
└── Needs: SDAP integration ✅

Control #2: Custom Activity Timeline
├── Purpose: Show activity history
└── Needs: SDAP integration? ❌ No

Control #3: Sales Dashboard Widget
├── Purpose: Show metrics/charts
└── Needs: SDAP integration? ❌ No

Control #4: Address Map Viewer
├── Purpose: Show locations on map
└── Needs: SDAP integration? ❌ No
```

**Conclusion:** Only Universal Grid needs SDAP → **No NPM package needed** ✅

---

**Scenario B: Other Controls DO Need SDAP**
```
Control #1: Universal Dataset Grid
├── Purpose: Document/file management (grid view)
└── Needs: SDAP integration ✅

Control #2: Document Preview Widget
├── Purpose: Single document with preview
└── Needs: SDAP integration ✅ (download file for preview)

Control #3: Contract Signature Control
├── Purpose: Contract workflow with e-signature
└── Needs: SDAP integration ✅ (download contract PDF)

Control #4: Invoice Upload Widget
├── Purpose: Upload invoices with OCR
└── Needs: SDAP integration ✅ (upload file)
```

**Conclusion:** Multiple controls need SDAP → **NPM package IS valuable** ✅

---

### Question 2: What Other PCF Controls Are Planned?

**Please clarify:**

**A. Planned PCF Controls in Next 6-12 Months**
- What controls are on your roadmap?
- What is their primary purpose?
- Will they interact with documents/files?

**Examples to help you think through this:**

**Entity-Specific Controls:**
- Timeline/activity controls (no SDAP needed)
- Dashboard/reporting controls (no SDAP needed)
- Custom form controls (might need SDAP)
- Data visualization controls (no SDAP needed)

**Document/File-Related Controls:**
- Document preview/viewer (✅ NEEDS SDAP)
- File upload widgets (✅ NEEDS SDAP)
- Document comparison (✅ NEEDS SDAP)
- Signature/approval controls (✅ NEEDS SDAP)

**Workflow/Process Controls:**
- Approval workflows (might need SDAP if documents involved)
- Custom wizards (might need SDAP if file upload)
- Data entry forms (probably doesn't need SDAP)

---

### Question 3: Document/File Management in Other Scenarios?

**Will any other controls need to:**
- ✅ Upload files to SharePoint Embedded?
- ✅ Download files from SharePoint Embedded?
- ✅ Delete files from SharePoint Embedded?
- ✅ List files in a container?

**If YES to any:** NPM package is valuable
**If NO to all:** NPM package not needed

---

## Decision Matrix

### Path A: Other Controls DON'T Need SDAP

**Characteristics:**
- Other PCF controls are for different purposes (dashboards, timelines, maps, etc.)
- Only Universal Dataset Grid manages documents/files
- No other controls interact with SharePoint Embedded

**Recommendation:** ✅ **Option 2 (Direct PCF Integration)**
- Build SDAP client directly in Universal Dataset Grid
- No NPM package
- Other controls don't use it anyway

**Effort:** Sprint 6 as planned (90 hours with Fluent UI + chunked upload)

---

### Path B: Other Controls DO Need SDAP

**Characteristics:**
- 2+ controls need document/file operations
- Multiple controls will upload/download files
- Different controls for different document workflows

**Recommendation:** ⚠️ **Reconsider Option 3 (NPM Package)**

**Two Sub-Options:**

**B1: Build NPM Package Now (Sprint 6)**
```
Pros:
✅ Do it right from the start
✅ Other controls can use immediately
✅ No future migration needed
✅ Consistent SDAP integration across all controls

Cons:
⚠️ +8 hours initial setup
⚠️ Sprint 6 extended (90 → 98 hours)

Effort: 98 hours total
Timeline: 2.5 weeks
```

**B2: Build with Direct PCF Now, Migrate Later**
```
Pros:
✅ Faster Sprint 6 delivery
✅ Learn requirements before abstracting

Cons:
⚠️ Migration effort later (6 hours)
⚠️ Duplicate code temporarily
⚠️ Two deployment cycles

Effort: 90 hours (Sprint 6) + 6 hours (migration)
Timeline: 2.25 weeks + future migration
```

---

## Recommended Analysis Process

### Step 1: List All Planned PCF Controls

**Please provide:**

| Control Name | Primary Purpose | Needs Files? | Needs SDAP? |
|--------------|-----------------|--------------|-------------|
| Universal Dataset Grid | Document management grid | Yes | Yes |
| [Your Control #2] | [Purpose?] | [Yes/No?] | [Yes/No?] |
| [Your Control #3] | [Purpose?] | [Yes/No?] | [Yes/No?] |
| [Your Control #4] | [Purpose?] | [Yes/No?] | [Yes/No?] |

**Example filled in:**

| Control Name | Primary Purpose | Needs Files? | Needs SDAP? |
|--------------|-----------------|--------------|-------------|
| Universal Dataset Grid | Document management grid | Yes | ✅ Yes |
| Sales Activity Timeline | Show activity history | No | ❌ No |
| Revenue Dashboard | Show charts/metrics | No | ❌ No |
| Contract Preview Widget | View contract with preview | Yes | ✅ Yes |
| Invoice Upload Form | Upload invoices for processing | Yes | ✅ Yes |

**Result:** 3 out of 5 controls need SDAP → **NPM package recommended** ✅

---

### Step 2: Count SDAP-Needing Controls

**If 0-1 controls need SDAP:**
→ Use Option 2 (Direct PCF)

**If 2+ controls need SDAP:**
→ Use Option 3 (NPM Package)

---

## What We Need from You

### Critical Information:

**Question 1:** What other PCF controls are you planning to build?
- List control names
- Brief description of each

**Question 2:** Will any of these controls need to:
- Upload files to SharePoint Embedded?
- Download files from SharePoint Embedded?
- Delete/manage files?

**Question 3:** Timeline for these controls?
- Sprint 7?
- Sprint 8?
- Next quarter?
- Next year?

---

## Likely Scenarios (Our Guess)

### Scenario 1: Document-Focused Environment

**If your organization is heavily document-focused:**

```
Likely Controls:
✅ Universal Dataset Grid (Sprint 6)
✅ Contract Preview Widget (Sprint 7)
✅ Document Comparison Tool (Sprint 8)
✅ Invoice Upload Widget (Sprint 9)
✅ Approval Workflow Control (Sprint 10)

SDAP Usage: 5 out of 5 controls
```

**Recommendation:** ✅ **Build NPM package now (Option 3)**
- Worth 8-hour upfront investment
- Saves 300 lines × 4 = 1,200 lines duplication
- Consistent implementation
- Single source of truth

**Updated Sprint 6:** 98 hours (add 8 hours for NPM package setup)

---

### Scenario 2: Mixed Environment

**If your organization has diverse needs:**

```
Likely Controls:
✅ Universal Dataset Grid (Sprint 6) - SDAP
❌ Sales Pipeline Dashboard (Sprint 7) - No SDAP
❌ Customer Timeline Widget (Sprint 8) - No SDAP
✅ Document Preview Widget (Sprint 9) - SDAP
❌ Territory Map Viewer (Sprint 10) - No SDAP

SDAP Usage: 2 out of 5 controls
```

**Recommendation:** ⚠️ **Could go either way**

**Option A:** Build NPM package now (8 hours upfront)
- Pro: Ready for Control #2 in Sprint 9
- Con: 8-hour investment for future use

**Option B:** Direct PCF now, migrate before Sprint 9 (6 hours later)
- Pro: Faster Sprint 6
- Con: Migration effort later

**Lean toward:** Option A if Sprint 9 is within 3 months

---

### Scenario 3: Mostly Non-Document Controls

**If most controls are for other purposes:**

```
Likely Controls:
✅ Universal Dataset Grid (Sprint 6) - SDAP
❌ KPI Dashboard (Sprint 7) - No SDAP
❌ Process Flow Diagram (Sprint 8) - No SDAP
❌ Data Import Wizard (Sprint 9) - No SDAP
❌ Custom Report Viewer (Sprint 10) - No SDAP

SDAP Usage: 1 out of 5 controls
```

**Recommendation:** ✅ **Use Option 2 (Direct PCF)**
- Only one control needs SDAP
- No duplication
- NPM package unnecessary

**Sprint 6:** 90 hours (no NPM package)

---

## Updated Decision Framework

### Use NPM Package (Option 3) If:

**At least ONE of these is true:**

1. ✅ Planning **2+ controls** that need SDAP integration
2. ✅ Second SDAP control planned within **3 months**
3. ✅ Building **document management suite** of controls
4. ✅ Multiple teams will build controls needing SDAP
5. ✅ Want to publish reusable component library

**Effort:** +8 hours to Sprint 6 (90 → 98 hours)

---

### Use Direct PCF (Option 2) If:

**ALL of these are true:**

1. ✅ Only **Universal Grid** needs SDAP (in next 3-6 months)
2. ✅ Other controls are for different purposes (no file operations)
3. ✅ Prefer faster Sprint 6 delivery
4. ✅ Okay with 6-hour migration later if needed

**Effort:** Sprint 6 as planned (90 hours)

---

## Recommendation: Need Your Input

**We can't make this decision without knowing:**

1. What other PCF controls are you planning?
2. Will they need document/file operations?
3. When will you build them?

**Please provide:**
- List of planned PCF controls (even high-level)
- Rough timeline
- Whether they interact with files/documents

**Then we can make the right call:** Option 2 or Option 3

---

## Interim Recommendation (Conservative Approach)

**If you're unsure about future controls:**

### Use Option 3 (NPM Package) - "Future-Proof" Approach

**Rationale:**
- You confirmed building other PCF controls
- Document management is often needed across controls
- 8-hour investment is cheap insurance
- Better to have it and not need it than vice versa
- Easy to NOT use if other controls don't need SDAP

**Implementation:**
- Build `@spaarke/sdap-client` package in Sprint 6
- Universal Grid uses it
- Other controls can use it if needed
- If they don't need it, no harm done

**Downside:** 8 hours longer Sprint 6 (90 → 98 hours)

**Upside:** Ready for any future SDAP-needing control

---

## What Do You Think?

**Please clarify:**

1. **What other PCF controls are on your roadmap?**
   - Names and purposes
   - Rough timeline

2. **Will any of them need file/document operations?**
   - Upload files
   - Download files
   - Manage documents

3. **Your preference:**
   - A) Build NPM package now (+8 hours, future-proof)
   - B) Direct PCF now, migrate later if needed (faster now, 6 hours later)

**Once we know this, we can finalize the approach!**

---

**Status:** ⏸️ **AWAITING CLARIFICATION**
**Next Step:** User provides info on planned PCF controls and file operation needs
