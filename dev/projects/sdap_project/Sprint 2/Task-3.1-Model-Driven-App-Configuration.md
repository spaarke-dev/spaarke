# Task 3.1: Model-Driven App Configuration

**PHASE:** Power Platform Integration (Days 11-16)
**STATUS:** ✅ READY TO START (Schema Verified 2025-09-30)
**DEPENDENCIES:** Task 1.1 (Entity Creation), Task 2.2 (Background Service)
**ESTIMATED TIME:** 6-8 hours
**PRIORITY:** HIGH - User interface foundation
**SCHEMA REFERENCE:** [docs/dataverse/ACTUAL-ENTITY-SCHEMA.md](../../../docs/dataverse/ACTUAL-ENTITY-SCHEMA.md)

---

## 📋 TASK OVERVIEW

### **Objective**
Create a comprehensive model-driven app for document management that provides users with an intuitive interface for document and file operations. This task builds the Power Platform UI that connects to the backend infrastructure.

### **Business Context**
- Building user interface for document and file management
- Need to integrate with BFF API for file operations
- Must show/hide functionality based on user permissions
- Should provide excellent user experience for CRUD operations
- Integrates with existing Power Platform solution components

### **Architecture Impact**
This task delivers:
- Complete user interface for document management
- Integration points for custom JavaScript functionality
- Security-based UI customization
- Familiar Power Platform experience for end users
- Foundation for advanced features and customizations

---

## 🔍 PRIOR TASK REVIEW AND VALIDATION

### **Backend Infrastructure Review**
Before starting this task, verify the following from previous tasks:

#### **Task 1.1: Entity Structure Validation**
- [ ] **sprk_document entity operational** with all required fields
- [ ] **sprk_container entity available** for lookup relationships
- [ ] **Entity relationships working** correctly in Dataverse
- [ ] **Security roles configured** for different user types
- [ ] **Forms and views baseline created** during entity setup

#### **Task 2.2: Backend Services Validation**
- [ ] **Document CRUD API endpoints operational** and tested
- [ ] **Background services processing events** correctly
- [ ] **File operations functional** through API integration
- [ ] **Authentication and authorization working** for API calls
- [ ] **Health checks confirming** all services operational

#### **Integration Points Confirmation**
- [ ] **API endpoints accessible** from Power Platform environment
- [ ] **CORS configuration allowing** Power Platform domain
- [ ] **Authentication tokens available** for API integration
- [ ] **Error handling providing** user-friendly messages

### **Gaps and Corrections**
If any issues found in prior tasks:

1. **Entity Issues**: Ensure all entity fields and relationships work before building UI
2. **API Problems**: Resolve API connectivity and authentication issues
3. **Performance Issues**: Address slow backend operations that will impact UI
4. **Security Problems**: Fix authorization issues before implementing UI security

---

## 🎯 AI AGENT INSTRUCTIONS

### **CONTEXT FOR AI AGENT**
You are implementing the user interface layer that provides business users with access to the document management system. The app must be intuitive, secure, and integrate seamlessly with the backend services.

### **POWER PLATFORM DESIGN PRINCIPLES**

#### **User Experience Design**
- **Familiar Interface**: Follow standard Power Platform UI patterns
- **Progressive Disclosure**: Show basic information first, details on demand
- **Context-Aware Actions**: Show relevant actions based on user role and data state
- **Responsive Design**: Work effectively on desktop and mobile devices
- **Accessibility Compliance**: Meet accessibility standards for enterprise use

#### **Security and Permissions**
- **Role-Based UI**: Show/hide features based on user security roles
- **Field-Level Security**: Respect entity field security settings
- **Operation-Specific Access**: Enable/disable actions based on user permissions
- **Audit-Friendly**: Support audit requirements for enterprise compliance

### **TECHNICAL REQUIREMENTS**

#### **1. Model-Driven App Structure**

**App Configuration:**
```json
{
  "name": "Spaarke Document Management",
  "description": "Document and file management for SharePoint Embedded integration",
  "urlSuffix": "spaarke-documents",
  "enableMobileClient": true,
  "enableCanvasApps": true,
  "welcomePageEnabled": true,
  "siteMap": {
    "areas": [
      {
        "id": "DocumentManagement",
        "title": "Document Management",
        "groups": [
          {
            "id": "Documents",
            "title": "Documents",
            "subAreas": [
              {
                "entity": "sprk_document",
                "title": "Documents"
              },
              {
                "entity": "sprk_container",
                "title": "Containers"
              }
            ]
          },
          {
            "id": "Administration",
            "title": "Administration",
            "subAreas": [
              {
                "type": "dashboard",
                "title": "Dashboard"
              },
              {
                "type": "webresource",
                "title": "System Health",
                "url": "/webresources/sprk_SystemHealthDashboard"
              }
            ]
          }
        ]
      }
    ]
  }
}
```

#### **2. Document Entity Configuration**

**Main Form Design:**
```xml
<!-- Document Main Form Layout -->
<form>
  <tabs>
    <tab name="general" label="General Information">
      <sections>
        <section name="basic_info" label="Basic Information" columns="2">
          <fields>
            <field name="sprk_documentname" required="true" />
            <field name="sprk_containerid" required="true" />
            <field name="statuscode" />
            <field name="statecode" />
            <field name="sprk_documentdescription" />
          </fields>
        </section>

        <section name="file_info" label="File Information" columns="2">
          <fields>
            <field name="sprk_hasfile" />
            <field name="sprk_filename" />
            <field name="sprk_filesize" />
            <field name="sprk_mimetype" />
          </fields>
        </section>
      </sections>
    </tab>

    <tab name="technical" label="Technical Details" visible="false">
      <sections>
        <section name="spe_details" label="SharePoint Embedded Details" columns="1">
          <fields>
            <field name="sprk_graphitemid" />
            <field name="sprk_graphdriveid" />
          </fields>
        </section>
      </sections>
    </tab>

    <tab name="audit" label="Audit Information">
      <sections>
        <section name="audit_info" label="Audit Information" columns="2">
          <fields>
            <field name="createdon" />
            <field name="createdby" />
            <field name="modifiedon" />
            <field name="modifiedby" />
          </fields>
        </section>
      </sections>
    </tab>
  </tabs>
</form>
```

**View Configurations:**

1. **Active Documents View (Default)**
```xml
<view name="ActiveDocuments" type="public" default="true">
  <displayname>Active Documents</displayname>
  <description>Shows all active documents</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
      <entity name="sprk_document">
        <attribute name="sprk_documentname" />
        <attribute name="sprk_containerid" />
        <attribute name="sprk_hasfile" />
        <attribute name="sprk_filename" />
        <attribute name="sprk_filesize" />
        <attribute name="modifiedon" />
        <attribute name="sprk_documentid" />
        <order attribute="modifiedon" descending="true" />
        <filter type="and">
          <condition attribute="statuscode" operator="eq" value="421500001" />
        </filter>
      </entity>
    </fetch>
  </fetchxml>
  <layoutxml>
    <grid name="resultset" object="sprk_document" jump="sprk_documentname" select="1" icon="1" preview="1">
      <row name="result" id="sprk_documentid">
        <cell name="sprk_documentname" width="200" />
        <cell name="sprk_containerid" width="150" />
        <cell name="sprk_hasfile" width="100" />
        <cell name="sprk_filename" width="200" />
        <cell name="sprk_filesize" width="100" />
        <cell name="modifiedon" width="150" />
      </row>
    </grid>
  </layoutxml>
</view>
```

2. **All Documents View**
```xml
<view name="AllDocuments" type="public">
  <displayname>All Documents</displayname>
  <description>Shows all documents regardless of status</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
      <entity name="sprk_document">
        <attribute name="sprk_documentname" />
        <attribute name="sprk_containerid" />
        <attribute name="statuscode" />
        <attribute name="statecode" />
        <attribute name="sprk_hasfile" />
        <attribute name="modifiedon" />
        <attribute name="sprk_documentid" />
        <order attribute="modifiedon" descending="true" />
      </entity>
    </fetch>
  </fetchxml>
  <layoutxml>
    <grid name="resultset" object="sprk_document" jump="sprk_documentname" select="1" icon="1" preview="1">
      <row name="result" id="sprk_documentid">
        <cell name="sprk_documentname" width="200" />
        <cell name="sprk_containerid" width="150" />
        <cell name="statuscode" width="100" />
        <cell name="sprk_hasfile" width="100" />
        <cell name="modifiedon" width="150" />
      </row>
    </grid>
  </layoutxml>
</view>
```

3. **Documents by Container View**
```xml
<view name="DocumentsByContainer" type="public">
  <displayname>Documents by Container</displayname>
  <description>Shows documents grouped by container</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
      <entity name="sprk_document">
        <attribute name="sprk_name" />
        <attribute name="sprk_containerid" />
        <attribute name="sprk_status" />
        <attribute name="sprk_hasfile" />
        <attribute name="modifiedon" />
        <attribute name="sprk_documentid" />
        <order attribute="sprk_containerid" descending="false" />
        <order attribute="sprk_name" descending="false" />
      </entity>
    </fetch>
  </fetchxml>
  <layoutxml>
    <grid name="resultset" object="sprk_document" jump="sprk_name" select="1" icon="1" preview="1">
      <row name="result" id="sprk_documentid">
        <cell name="sprk_containerid" width="200" />
        <cell name="sprk_name" width="200" />
        <cell name="sprk_status" width="100" />
        <cell name="sprk_hasfile" width="100" />
        <cell name="modifiedon" width="150" />
      </row>
    </grid>
  </layoutxml>
</view>
```

4. **Recent Documents View**
```xml
<view name="RecentDocuments" type="public">
  <displayname>Recent Documents</displayname>
  <description>Shows documents modified in the last 30 days</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
      <entity name="sprk_document">
        <attribute name="sprk_name" />
        <attribute name="sprk_containerid" />
        <attribute name="sprk_status" />
        <attribute name="sprk_hasfile" />
        <attribute name="modifiedon" />
        <attribute name="sprk_documentid" />
        <order attribute="modifiedon" descending="true" />
        <filter type="and">
          <condition attribute="modifiedon" operator="last-x-days" value="30" />
        </filter>
      </entity>
    </fetch>
  </fetchxml>
  <layoutxml>
    <grid name="resultset" object="sprk_document" jump="sprk_name" select="1" icon="1" preview="1">
      <row name="result" id="sprk_documentid">
        <cell name="sprk_name" width="200" />
        <cell name="sprk_containerid" width="150" />
        <cell name="sprk_status" width="100" />
        <cell name="sprk_hasfile" width="100" />
        <cell name="modifiedon" width="150" />
      </row>
    </grid>
  </layoutxml>
</view>
```

#### **3. Container Entity Configuration**

**Container Main Form:**
```xml
<form>
  <tabs>
    <tab name="general" label="General Information">
      <sections>
        <section name="basic_info" label="Basic Information" columns="2">
          <fields>
            <field name="sprk_name" required="true" />
            <field name="sprk_specontainerid" />
            <field name="sprk_documentcount" />
          </fields>
        </section>
      </sections>
    </tab>

    <tab name="documents" label="Related Documents">
      <sections>
        <section name="documents_subgrid" label="Documents" columns="1">
          <subgrid name="documents_subgrid" entity="sprk_document" viewid="DocumentsByContainer" />
        </section>
      </sections>
    </tab>
  </tabs>
</form>
```

**Container Views:**

1. **Active Containers View**
```xml
<view name="ActiveContainers" type="public" default="true">
  <displayname>Active Containers</displayname>
  <description>Shows all active containers with document counts</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
      <entity name="sprk_container">
        <attribute name="sprk_name" />
        <attribute name="sprk_specontainerid" />
        <attribute name="sprk_documentcount" />
        <attribute name="modifiedon" />
        <attribute name="sprk_containerid" />
        <order attribute="sprk_name" descending="false" />
      </entity>
    </fetch>
  </fetchxml>
  <layoutxml>
    <grid name="resultset" object="sprk_container" jump="sprk_name" select="1" icon="1" preview="1">
      <row name="result" id="sprk_containerid">
        <cell name="sprk_name" width="250" />
        <cell name="sprk_documentcount" width="150" />
        <cell name="modifiedon" width="150" />
      </row>
    </grid>
  </layoutxml>
</view>
```

#### **4. Dashboard Configuration**

**Document Management Dashboard:**
```xml
<dashboard>
  <name>DocumentManagementDashboard</name>
  <displayname>Document Management Dashboard</displayname>
  <description>Overview of document management activities</description>
  <layout>
    <row height="33%">
      <cell width="33%">
        <chart name="DocumentsByStatus" />
      </cell>
      <cell width="33%">
        <chart name="DocumentsByContainer" />
      </cell>
      <cell width="34%">
        <chart name="DocumentsOverTime" />
      </cell>
    </row>
    <row height="33%">
      <cell width="50%">
        <list name="RecentDocuments" />
      </cell>
      <cell width="50%">
        <list name="TopContainers" />
      </cell>
    </row>
    <row height="34%">
      <cell width="100%">
        <list name="DocumentsNeedingAttention" />
      </cell>
    </row>
  </layout>
</dashboard>
```

**Chart Configurations:**

1. **Documents by Status Chart**
```xml
<chart>
  <name>DocumentsByStatus</name>
  <displayname>Documents by Status</displayname>
  <description>Distribution of documents by status</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" aggregate="true">
      <entity name="sprk_document">
        <attribute name="sprk_status" groupby="true" alias="status" />
        <attribute name="sprk_documentid" aggregate="count" alias="count" />
      </entity>
    </fetch>
  </fetchxml>
  <presentationdescription>
    <chart type="pie">
      <category>status</category>
      <series>count</series>
    </chart>
  </presentationdescription>
</chart>
```

2. **Documents by Container Chart**
```xml
<chart>
  <name>DocumentsByContainer</name>
  <displayname>Documents by Container</displayname>
  <description>Document count by container</description>
  <fetchxml>
    <fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" aggregate="true">
      <entity name="sprk_document">
        <attribute name="sprk_containerid" groupby="true" alias="container" />
        <attribute name="sprk_documentid" aggregate="count" alias="count" />
      </entity>
    </fetch>
  </fetchxml>
  <presentationdescription>
    <chart type="column">
      <category>container</category>
      <series>count</series>
    </chart>
  </presentationdescription>
</chart>
```

#### **5. Security Role Configuration**

**Document Manager Role:**
```xml
<role name="SpaarkeDocumentManager">
  <displayname>Spaarke Document Manager</displayname>
  <description>Full access to document management functions</description>
  <privileges>
    <!-- Document Entity -->
    <privilege name="sprk_document" level="Global" access="Create,Read,Update,Delete" />

    <!-- Container Entity -->
    <privilege name="sprk_container" level="Global" access="Create,Read,Update,Delete" />

    <!-- System Privileges -->
    <privilege name="prvReadWorkflow" level="Global" />
    <privilege name="prvCreateWorkflow" level="Global" />

    <!-- App Access -->
    <privilege name="prvUseSpaarkeDocumentManagement" level="Global" />
  </privileges>

  <fieldSecurityProfiles>
    <profile name="DocumentManagerFieldSecurity" />
  </fieldSecurityProfiles>
</role>
```

**Document User Role:**
```xml
<role name="SpaarkeDocumentUser">
  <displayname>Spaarke Document User</displayname>
  <description>Standard user access to document management</description>
  <privileges>
    <!-- Document Entity -->
    <privilege name="sprk_document" level="User" access="Create,Read,Update" />
    <privilege name="sprk_document" level="None" access="Delete" />

    <!-- Container Entity -->
    <privilege name="sprk_container" level="Global" access="Read" />

    <!-- App Access -->
    <privilege name="prvUseSpaarkeDocumentManagement" level="Global" />
  </privileges>

  <fieldSecurityProfiles>
    <profile name="DocumentUserFieldSecurity" />
  </fieldSecurityProfiles>
</role>
```

**Document Reader Role:**
```xml
<role name="SpaarkeDocumentReader">
  <displayname>Spaarke Document Reader</displayname>
  <description>Read-only access to document management</description>
  <privileges>
    <!-- Document Entity -->
    <privilege name="sprk_document" level="Global" access="Read" />

    <!-- Container Entity -->
    <privilege name="sprk_container" level="Global" access="Read" />

    <!-- App Access -->
    <privilege name="prvUseSpaarkeDocumentManagement" level="Global" />
  </privileges>

  <fieldSecurityProfiles>
    <profile name="DocumentReaderFieldSecurity" />
  </fieldSecurityProfiles>
</role>
```

#### **6. Field Security Profiles**

**Document Manager Field Security:**
```xml
<fieldSecurityProfile name="DocumentManagerFieldSecurity">
  <displayname>Document Manager Field Security</displayname>
  <description>Full access to all document fields</description>
  <fieldPermissions>
    <field entity="sprk_document" name="sprk_graphitemid" canRead="true" canUpdate="true" />
    <field entity="sprk_document" name="sprk_graphdriveid" canRead="true" canUpdate="true" />
    <field entity="sprk_container" name="sprk_specontainerid" canRead="true" canUpdate="true" />
  </fieldPermissions>
</fieldSecurityProfile>
```

**Document User Field Security:**
```xml
<fieldSecurityProfile name="DocumentUserFieldSecurity">
  <displayname>Document User Field Security</displayname>
  <description>Limited access to technical fields</description>
  <fieldPermissions>
    <field entity="sprk_document" name="sprk_graphitemid" canRead="false" canUpdate="false" />
    <field entity="sprk_document" name="sprk_graphdriveid" canRead="false" canUpdate="false" />
    <field entity="sprk_container" name="sprk_specontainerid" canRead="false" canUpdate="false" />
  </fieldPermissions>
</fieldSecurityProfile>
```

#### **7. Custom Ribbon Commands**

**Document Form Ribbon:**
```xml
<ribbon>
  <tabs>
    <tab id="sprk_document.Form.Tab">
      <groups>
        <group id="sprk_document.FileOperations">
          <displayname>File Operations</displayname>
          <controls>
            <button id="sprk_document.UploadFile"
                    command="Spaarke.UploadFile"
                    sequence="10"
                    labeltext="Upload File"
                    tooltiptext="Upload a file to this document"
                    image16="/_imgs/ribbon/uploadfile_16.png"
                    image32="/_imgs/ribbon/uploadfile_32.png" />

            <button id="sprk_document.DownloadFile"
                    command="Spaarke.DownloadFile"
                    sequence="20"
                    labeltext="Download File"
                    tooltiptext="Download the file associated with this document"
                    image16="/_imgs/ribbon/downloadfile_16.png"
                    image32="/_imgs/ribbon/downloadfile_32.png" />

            <button id="sprk_document.ReplaceFile"
                    command="Spaarke.ReplaceFile"
                    sequence="30"
                    labeltext="Replace File"
                    tooltiptext="Replace the existing file with a new version"
                    image16="/_imgs/ribbon/replacefile_16.png"
                    image32="/_imgs/ribbon/replacefile_32.png" />

            <button id="sprk_document.DeleteFile"
                    command="Spaarke.DeleteFile"
                    sequence="40"
                    labeltext="Delete File"
                    tooltiptext="Delete the file associated with this document"
                    image16="/_imgs/ribbon/deletefile_16.png"
                    image32="/_imgs/ribbon/deletefile_32.png" />
          </controls>
        </group>
      </groups>
    </tab>
  </tabs>

  <commands>
    <command id="Spaarke.UploadFile">
      <enableRule>
        <rule id="Spaarke.CanUploadFile" />
      </enableRule>
      <displayRule>
        <rule id="Spaarke.ShowFileOperations" />
      </displayRule>
      <actions>
        <action functionName="Spaarke.Documents.UploadFile" library="sprk_DocumentOperations" />
      </actions>
    </command>

    <command id="Spaarke.DownloadFile">
      <enableRule>
        <rule id="Spaarke.HasFile" />
      </enableRule>
      <displayRule>
        <rule id="Spaarke.ShowFileOperations" />
      </displayRule>
      <actions>
        <action functionName="Spaarke.Documents.DownloadFile" library="sprk_DocumentOperations" />
      </actions>
    </command>

    <command id="Spaarke.ReplaceFile">
      <enableRule>
        <rule id="Spaarke.CanReplaceFile" />
      </enableRule>
      <displayRule>
        <rule id="Spaarke.ShowFileOperations" />
      </displayRule>
      <actions>
        <action functionName="Spaarke.Documents.ReplaceFile" library="sprk_DocumentOperations" />
      </actions>
    </command>

    <command id="Spaarke.DeleteFile">
      <enableRule>
        <rule id="Spaarke.CanDeleteFile" />
      </enableRule>
      <displayRule>
        <rule id="Spaarke.ShowFileOperations" />
      </displayRule>
      <actions>
        <action functionName="Spaarke.Documents.DeleteFile" library="sprk_DocumentOperations" />
      </actions>
    </command>
  </commands>

  <rules>
    <rule id="Spaarke.ShowFileOperations">
      <condition>
        <and>
          <entityRule entity="sprk_document" />
          <formRule form="Information" />
        </and>
      </condition>
    </rule>

    <rule id="Spaarke.CanUploadFile">
      <condition>
        <and>
          <valueRule field="sprk_hasfile" value="false" />
          <privilegeRule privilege="prvWritesprk_document" />
        </and>
      </condition>
    </rule>

    <rule id="Spaarke.HasFile">
      <condition>
        <valueRule field="sprk_hasfile" value="true" />
      </condition>
    </rule>

    <rule id="Spaarke.CanReplaceFile">
      <condition>
        <and>
          <valueRule field="sprk_hasfile" value="true" />
          <privilegeRule privilege="prvWritesprk_document" />
        </and>
      </condition>
    </rule>

    <rule id="Spaarke.CanDeleteFile">
      <condition>
        <and>
          <valueRule field="sprk_hasfile" value="true" />
          <privilegeRule privilege="prvDeletesprk_document" />
        </and>
      </condition>
    </rule>
  </rules>
</ribbon>
```

### **ADVANCED UI CONFIGURATIONS**

#### **1. Business Process Flows**

**Document Lifecycle Process:**
```xml
<process name="DocumentLifecycleProcess" category="Business Process Flow">
  <displayname>Document Lifecycle</displayname>
  <description>Guides users through the document lifecycle</description>
  <stages>
    <stage id="Draft" name="Draft" entity="sprk_document">
      <steps>
        <step name="EnterBasicInfo" required="true" attribute="sprk_name" />
        <step name="SelectContainer" required="true" attribute="sprk_containerid" />
        <step name="AddDescription" required="false" attribute="sprk_documentdescription" />
      </steps>
    </stage>

    <stage id="FileUpload" name="File Upload" entity="sprk_document">
      <steps>
        <step name="UploadFile" required="false" attribute="sprk_hasfile" />
        <step name="VerifyFileInfo" required="false" attribute="sprk_filename" />
      </steps>
    </stage>

    <stage id="Active" name="Active" entity="sprk_document">
      <steps>
        <step name="SetActive" required="true" attribute="sprk_status" />
        <step name="FinalReview" required="false" />
      </steps>
    </stage>
  </stages>
</process>
```

#### **2. Quick Create Forms**

**Document Quick Create:**
```xml
<form type="quick">
  <displayname>Quick Create: Document</displayname>
  <sections>
    <section name="quick_info" columns="1">
      <fields>
        <field name="sprk_name" required="true" />
        <field name="sprk_containerid" required="true" />
        <field name="sprk_documentdescription" />
      </fields>
    </section>
  </sections>
</form>
```

#### **3. Mobile-Specific Configurations**

**Mobile Form Layout:**
```xml
<form type="mobile">
  <displayname>Document Mobile Form</displayname>
  <tabs>
    <tab name="general" label="Document">
      <sections>
        <section name="basic" columns="1">
          <fields>
            <field name="sprk_name" />
            <field name="sprk_containerid" />
            <field name="sprk_status" />
            <field name="sprk_hasfile" />
            <field name="sprk_filename" />
          </fields>
        </section>
      </sections>
    </tab>
  </tabs>
</form>
```

---

## ⚠️ KNOWN LIMITATIONS FOR SPRINT 2

**IMPORTANT:** This section documents Sprint 2 scope boundaries and dependencies.

### **Container Management**
- ✅ Container entity exists in Dataverse with full schema
- ⚠️ **Container management is READ-ONLY in Sprint 2**
- ❌ No container CRUD APIs available in BFF
- ✅ Container views/forms are for display and reference only
- 📋 Full container management will be added in future sprint

**Impact on UI:**
- Users can view containers and their document counts
- Users cannot create/update/delete containers via the app
- Document creation requires selecting from existing containers

### **Ribbon Commands and File Operations**
- ✅ Ribbon button definitions included in Task 3.1
- ⚠️ **File operation buttons will be NON-FUNCTIONAL until Task 3.2**
- 📋 JavaScript implementation is Task 3.2 dependency
- ⚠️ Buttons can appear but will show "Not Implemented" messages

**Affected Commands:**
- Upload File button
- Download File button
- Replace File button
- Delete File button

**Recommendation:** Either hide these buttons initially OR show them with disabled state until Task 3.2 completes.

### **Matter Field**
- ✅ `sprk_matter` lookup field exists in Dataverse entity
- ❌ **Matter field is NOT implemented in API or documented in CONFIGURATION_REQUIREMENTS.md**
- ⚠️ Field is out of scope for Sprint 2
- 📋 Can be hidden on forms OR left visible for future use

**Impact:** If visible, users can set the matter relationship but no matter-specific functionality exists yet.

### **Field-Level Security**
- ✅ `sprk_filename` field has `IsSecured=1` in Dataverse
- ⚠️ **Field Security Profiles MUST be manually configured**
- 📋 Profiles are NOT automatically created by this task

**Required Manual Steps:**
1. Create "Document Manager Field Security" profile in Dataverse
2. Grant Read/Update access to `sprk_filename` field
3. Create "Document User Field Security" profile
4. Deny access to `sprk_filename` field
5. Assign profiles to appropriate users

**Impact:** Without field security profile configuration, either all users see the field OR no users see it (depending on field's default behavior).

### **Status Code Values**

**VERIFIED:** Task 3.1 uses correct Dataverse status code values per CONFIGURATION_REQUIREMENTS.md:

| Value | Label | Usage in Views/Charts |
|-------|-------|----------------------|
| 1 | Draft | `<condition attribute="statuscode" operator="eq" value="1" />` |
| 2 | Error | `<condition attribute="statuscode" operator="eq" value="2" />` |
| 421500001 | Active | `<condition attribute="statuscode" operator="eq" value="421500001" />` |
| 421500002 | Processing | `<condition attribute="statuscode" operator="eq" value="421500002" />` |

**Code Alignment:**
- ✅ C# DocumentStatus enum updated to match these values (Models.cs:52-57)
- ✅ Background service handlers updated (Processing, Error, Draft, Active)
- ✅ DataverseWebApiService uses correct status codes

---

## ✅ VALIDATION STEPS

### **App Publication and Access Testing**

#### **1. App Publication Validation**
```powershell
# Export and validate app configuration
pac solution export --name "SpaarkeDocumentManagement" --path "app-export.zip"

# Check for validation errors
pac solution check --path "app-export.zip"

# Import to test environment
pac solution import --path "app-export.zip" --environment [TEST_ENV_URL]

# Publish all customizations
pac solution publish --environment [TEST_ENV_URL]
```

#### **2. User Access Testing**
```bash
# Test app access with different user roles
# 1. Document Manager - should see all features
# 2. Document User - should see limited features
# 3. Document Reader - should see read-only features

# Navigate to: https://[environment].crm.dynamics.com/main.aspx?appid=[APP_ID]
```

#### **3. Form and View Testing**
```bash
# Test document form functionality
# 1. Create new document record
# 2. Verify all fields are accessible
# 3. Test field validation and requirements
# 4. Check security role field restrictions

# Test view functionality
# 1. Navigate to each view (Active, All, By Container, Recent)
# 2. Verify data loads correctly
# 3. Test filtering and sorting
# 4. Check view performance with data
```

### **Security Role Validation**

#### **1. Permission Testing**
```javascript
// Test user permissions programmatically
function testUserPermissions() {
    // Check document entity permissions
    var userPrivileges = Xrm.Utility.getGlobalContext().userSettings.roles;

    // Test create permission
    var canCreate = hasPrivilege("prvCreatesprk_document");

    // Test read permission
    var canRead = hasPrivilege("prvReadsprk_document");

    // Test update permission
    var canUpdate = hasPrivilege("prvWritesprk_document");

    // Test delete permission
    var canDelete = hasPrivilege("prvDeletesprk_document");

    console.log("Permissions - Create:", canCreate, "Read:", canRead, "Update:", canUpdate, "Delete:", canDelete);
}

function hasPrivilege(privilegeName) {
    // Implementation depends on available APIs
    // This is a conceptual example
    return true; // Replace with actual privilege check
}
```

#### **2. Field Security Testing**
```javascript
// Test field-level security
function testFieldSecurity() {
    var formContext = Xrm.Page; // or executionContext.getFormContext()

    // Check if technical fields are visible based on user role
    var graphItemIdControl = formContext.getControl("sprk_graphitemid");
    var graphDriveIdControl = formContext.getControl("sprk_graphdriveid");

    if (graphItemIdControl) {
        console.log("GraphItemId visible:", graphItemIdControl.getVisible());
        console.log("GraphItemId disabled:", graphItemIdControl.getDisabled());
    }

    if (graphDriveIdControl) {
        console.log("GraphDriveId visible:", graphDriveIdControl.getVisible());
        console.log("GraphDriveId disabled:", graphDriveIdControl.getDisabled());
    }
}
```

### **Performance Testing**

#### **1. View Performance Testing**
```javascript
// Test view loading performance
function testViewPerformance() {
    var startTime = performance.now();

    // Navigate to different views and measure load times
    // Target: < 3 seconds for views with reasonable data volumes (< 1000 records)

    window.addEventListener('load', function() {
        var endTime = performance.now();
        var loadTime = endTime - startTime;
        console.log("View load time:", loadTime, "ms");

        // Log if performance target not met
        if (loadTime > 3000) {
            console.warn("View load time exceeds 3 second target");
        }
    });
}
```

#### **2. Form Performance Testing**
```javascript
// Test form loading and interaction performance
function testFormPerformance() {
    var formStartTime = performance.now();

    // Monitor form load event
    Xrm.Page.data.entity.addOnLoad(function() {
        var formEndTime = performance.now();
        var formLoadTime = formEndTime - formStartTime;
        console.log("Form load time:", formLoadTime, "ms");

        // Target: < 2 seconds for form loading
        if (formLoadTime > 2000) {
            console.warn("Form load time exceeds 2 second target");
        }
    });

    // Test field interaction responsiveness
    Xrm.Page.getAttribute("sprk_name").addOnChange(function() {
        console.log("Field change response time:", performance.now());
    });
}
```

### **Dashboard and Chart Testing**

#### **1. Dashboard Functionality**
```bash
# Test dashboard components
# 1. Navigate to Document Management Dashboard
# 2. Verify all charts load with data
# 3. Test chart drill-down functionality
# 4. Check list components show relevant data
# 5. Test dashboard refresh functionality
```

#### **2. Chart Data Validation**
```javascript
// Validate chart data accuracy
function validateChartData() {
    // This would typically be done through UI testing
    // Verify chart data matches expected database queries

    // Documents by Status chart should match status distribution
    // Documents by Container chart should match container counts
    // Ensure charts update when underlying data changes
}
```

### **Mobile App Testing**

#### **1. Mobile Interface Validation**
```bash
# Test mobile app functionality
# 1. Access app on mobile device or browser mobile view
# 2. Verify responsive design works correctly
# 3. Test touch interactions and navigation
# 4. Check performance on mobile networks
# 5. Validate offline capability (if configured)
```

#### **2. Mobile-Specific Features**
```bash
# Test mobile-specific features
# 1. Test mobile form layouts
# 2. Verify mobile navigation works
# 3. Check mobile-optimized views
# 4. Test any mobile-specific customizations
```

---

## 🔍 TROUBLESHOOTING GUIDE

### **Common Issues and Solutions**

#### **Issue: App Not Appearing for Users**
**Symptoms**: Users cannot see the app in their app list
**Diagnosis Steps**:
1. Check user security role assignments
2. Verify app sharing configuration
3. Check environment access permissions
4. Review app publication status

**Solutions**:
```powershell
# Check app sharing
pac app list --environment [ENV_URL]
pac app share --app-id [APP_ID] --user [USER_ID] --environment [ENV_URL]

# Verify user roles
pac user list --environment [ENV_URL]
```

#### **Issue: Forms Not Loading or Displaying Errors**
**Symptoms**: Forms show errors or fail to load
**Diagnosis Steps**:
1. Check entity and form configuration
2. Verify field security profiles
3. Review user permissions
4. Check for JavaScript errors

**Solutions**:
- Verify all required fields are configured correctly
- Check field security profile assignments
- Review entity relationship configurations
- Test with different user roles

#### **Issue: Views Showing No Data**
**Symptoms**: Views load but show no records
**Diagnosis Steps**:
1. Check view FetchXML configuration
2. Verify user permissions on entity
3. Check filter conditions in views
4. Review security role privileges

**Solutions**:
```xml
<!-- Debug view FetchXML -->
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_document">
    <!-- Remove complex filters temporarily for testing -->
    <attribute name="sprk_documentid" />
    <attribute name="sprk_name" />
  </entity>
</fetch>
```

#### **Issue: Security Roles Not Working**
**Symptoms**: Users see fields/functions they shouldn't or can't access what they should
**Diagnosis Steps**:
1. Review security role configuration
2. Check field security profile assignments
3. Verify user role assignments
4. Test with system administrator role

**Solutions**:
- Verify security role privileges are correctly configured
- Check field security profile assignments to users
- Ensure business unit hierarchy is correct
- Test role assignment with different users

#### **Issue: Performance Problems**
**Symptoms**: Views and forms load slowly
**Diagnosis Steps**:
1. Check data volume in entities
2. Review view FetchXML for efficiency
3. Monitor browser network activity
4. Check for custom JavaScript performance issues

**Solutions**:
- Optimize view FetchXML queries
- Add appropriate indexes to custom entities
- Implement view paging for large datasets
- Optimize custom JavaScript code

#### **Issue: Charts Not Displaying Data**
**Symptoms**: Dashboard charts show no data or incorrect data
**Diagnosis Steps**:
1. Check chart FetchXML configuration
2. Verify underlying data exists
3. Review chart aggregation logic
4. Check user permissions on chart data

**Solutions**:
```xml
<!-- Simplified chart FetchXML for testing -->
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false" aggregate="true">
  <entity name="sprk_document">
    <attribute name="sprk_status" groupby="true" alias="status" />
    <attribute name="sprk_documentid" aggregate="count" alias="count" />
  </entity>
</fetch>
```

---

## 📚 KNOWLEDGE REFERENCES

### **Power Platform Configuration References**
- Power Platform maker portal: https://make.powerapps.com
- Model-driven app configuration guide
- Security role and field security configuration
- Dashboard and chart configuration documentation

### **Existing Solution Components**
- Current entity configurations and relationships
- Existing security role patterns
- Power Platform solution structure
- Custom web resource patterns

### **Integration References**
- API endpoint documentation for JavaScript integration
- Authentication patterns for Power Platform to API calls
- CORS configuration for external API access
- Error handling patterns for UI integration

---

## 🎯 SUCCESS CRITERIA

This task is complete when:

### **Functional Criteria**
- ✅ Model-driven app publishes successfully without errors
- ✅ All users can access the app based on their assigned roles
- ✅ Document and container forms display correctly with all fields
- ✅ All configured views load and display data appropriately
- ✅ Dashboard shows meaningful charts and data visualizations
- ✅ Navigation between entities and views works smoothly

### **Security Criteria**
- ✅ Security roles restrict access appropriately by user type
- ✅ Field-level security hides technical fields from end users
- ✅ Users can only perform operations allowed by their role
- ✅ Business units and hierarchical security work correctly (if applicable)

### **Performance Criteria**
- ✅ Views load within 3 seconds for reasonable data volumes
- ✅ Forms load within 2 seconds for standard use cases
- ✅ Dashboard components load and refresh efficiently
- ✅ Mobile interface performs acceptably on standard devices

### **User Experience Criteria**
- ✅ Interface is intuitive for business users
- ✅ Error messages are clear and actionable
- ✅ Mobile interface provides good user experience
- ✅ Business process flows guide users effectively (if implemented)

---

## 🔄 CONCLUSION AND NEXT STEP

### **Impact of Completion**
Completing this task delivers:
1. **Complete user interface** for document management operations
2. **Role-based access control** with appropriate security restrictions
3. **Intuitive user experience** following Power Platform best practices
4. **Mobile-ready interface** for modern workplace scenarios
5. **Foundation for custom functionality** through JavaScript integration

### **Quality Validation**
Before moving to the next task:
1. Test app functionality with multiple user roles
2. Validate performance meets established targets
3. Confirm security restrictions work correctly
4. Test mobile interface on actual devices
5. Verify dashboard provides meaningful business insights

### **Integration Readiness**
Ensure the UI foundation is solid:
1. All forms and views display data correctly
2. Security model aligns with business requirements
3. Performance is acceptable for expected user load
4. Navigation and user experience meet usability standards
5. App is ready for custom JavaScript integration

### **Immediate Next Action**
Upon successful completion of this task:

**🎯 PROCEED TO: [Task-3.2-JavaScript-File-Management-Integration.md](./Task-3.2-JavaScript-File-Management-Integration.md)**

The Power Platform UI foundation is now complete and ready for file management integration. The JavaScript task will add the custom functionality needed for file upload, download, and management operations.

### **Handoff Information**
Provide this information to the next task:
- App ID and entity configuration details
- Security role names and permission structure
- Form and view IDs for JavaScript integration
- Ribbon command IDs that need JavaScript functions
- Performance requirements and user experience standards

---

**📋 TASK COMPLETION CHECKLIST**
- [ ] Model-driven app published successfully
- [ ] All security roles configured and tested
- [ ] Forms and views working correctly
- [ ] Dashboard showing meaningful data
- [ ] Performance targets met
- [ ] Mobile interface functional
- [ ] User access testing completed
- [ ] Next task team briefed on UI capabilities