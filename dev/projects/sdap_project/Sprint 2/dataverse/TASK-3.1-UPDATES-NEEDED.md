# Task 3.1 Updates Based on Actual Schema

**Date:** 2025-09-30
**Purpose:** Document required updates to Task 3.1 based on verified Dataverse schema

---

## ✅ VERIFIED: Field Names are CORRECT

**Good News:** The field names in Task 3.1 need NO changes! The actual Dataverse entity uses:
- `sprk_documentname` ✅
- `sprk_documentdescription` ✅
- `sprk_containerid` ✅
- All other field names match exactly

**Previous concern about `sprk_name` vs `sprk_documentname` was unfounded** - the actual entity is correct.

---

## 🔧 REQUIRED UPDATES

### 1. Status Code Values (CRITICAL)

**Current Task 3.1 Shows:**
```xml
<condition attribute="statuscode" operator="eq" value="421500001" />
```

**✅ THIS IS CORRECT** - Actual Dataverse values per CONFIGURATION_REQUIREMENTS.md:
- Draft = 1
- Error = 2
- Active = 421500001
- Processing = 421500002

**Updates Made:**
- ✅ C# DocumentStatus enum updated to match (Models.cs:52-57)
- ✅ DocumentEventHandler updated to use Processing and Error instead of Inactive
- ✅ Build verified successful

### 2. Container Entity Scope

**Task 3.1 includes:** Full container management UI

**Recommendation:** Add note that container management is **read-only** in Sprint 2:
- Container forms/views for display only
- No container CRUD APIs implemented yet
- Focus Task 3.1 on Document entity

### 3. Field Security

**Task 3.1 mentions field security** but should explicitly note:
- `sprk_filename` field has `IsSecured=1` in Dataverse
- Field Security Profiles MUST be configured for this to work
- Document Manager vs Document User profiles need different access

### 4. Matter Field

**Discovered:** `sprk_matter` lookup field exists in Dataverse but:
- NOT in C# code
- NOT documented in prior tasks
- Appears on Document entity

**Recommendation:** Add note in Task 3.1 that sprk_matter field exists but is out of scope for Sprint 2

### 5. Ribbon Commands

**Task 3.1 includes:** Extensive ribbon customization with JavaScript functions

**Issue:** These depend on Task 3.2 JavaScript implementation

**Recommendation:** Note that ribbon commands will be **non-functional until Task 3.2 completes**

---

## 📝 SPECIFIC SECTION UPDATES NEEDED

### Update 1: Add "Known Limitations" Section

Insert before "VALIDATION STEPS":

```markdown
## ⚠️ KNOWN LIMITATIONS FOR SPRINT 2

### **Container Management**
- Container entity is **read-only** in this sprint
- No container CRUD APIs available
- Container views/forms are for display only
- Full container management will be added in future sprint

### **Ribbon Commands**
- File operation buttons (Upload, Download, Replace, Delete) will appear but be **non-functional**
- JavaScript implementation is Task 3.2 dependency
- Buttons can be hidden until Task 3.2 completes

### **Matter Field**
- `sprk_matter` lookup field exists but is not implemented
- Field is out of scope for Sprint 2
- Can be hidden on forms or left visible for future use

### **Field Security**
- `sprk_filename` field requires Field Security Profile configuration
- Profiles must be manually configured in Dataverse admin UI
- Users without profile assignment cannot see filename field
```

### Update 2: Clarify Status Code Section

Update the view FetchXML examples to use actual values and add comments:

```xml
<!-- Active Documents View - Uses actual statuscode value -->
<filter type="and">
  <condition attribute="statuscode" operator="eq" value="421500001" /> <!-- Active -->
</filter>

<!-- Draft Documents View -->
<filter type="and">
  <condition attribute="statuscode" operator="eq" value="1" /> <!-- Draft -->
</filter>

<!-- Processing Documents View -->
<filter type="and">
  <condition attribute="statuscode" operator="eq" value="421500002" /> <!-- Processing -->
</filter>

<!-- Error Documents View -->
<filter type="and">
  <condition attribute="statuscode" operator="eq" value="2" /> <!-- Error -->
</filter>
```

### Update 3: Add Chart for All Status Values

Add a new chart configuration:

```xml
<chart>
  <name>DocumentsByStatus</name>
  <displayname>Documents by Status</displayname>
  <description>Distribution of documents by status</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" aggregate="true">
      <entity name="sprk_document">
        <attribute name="statuscode" groupby="true" alias="status" />
        <attribute name="sprk_documentid" aggregate="count" alias="count" />
      </entity>
    </fetch>
  </fetchxml>
  <presentationdescription>
    <chart type="pie">
      <category>status</category>
      <series>count</series>
      <!-- Legend will show: Draft (1), Error (2), Active (421500001), Processing (421500002) -->
    </chart>
  </presentationdescription>
</chart>
```

### Update 4: Success Criteria Addition

Add to SUCCESS CRITERIA section:

```markdown
### **Technical Criteria**
- ✅ Status code values match Dataverse (1, 2, 421500001, 421500002)
- ✅ Field Security Profile configured for sprk_filename
- ✅ All field names use correct logical names (sprk_documentname, etc.)
- ⚠️ Ribbon commands acknowledged as non-functional until Task 3.2
- ⚠️ Container management acknowledged as read-only
```

---

## 🎯 DECISION: Proceed with Updates?

**Summary of Changes:**
1. ✅ Add "Known Limitations" section documenting Sprint 2 scope
2. ✅ Add comments to status code FetchXML clarifying values
3. ✅ Add chart showing all 4 status values
4. ✅ Update success criteria to reflect actual status values
5. ✅ Note that ribbon commands depend on Task 3.2

**No field name changes needed** - schema is already correct!

**Recommendation:** Proceed with these updates to Task 3.1, then mark it as READY TO START with accurate information.
