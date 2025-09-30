# Task 1.1: Dataverse Entity Validation & Completion

**PHASE:** Foundation Setup (Days 1-5)
**STATUS:** ⚠️ VALIDATION REQUIRED - HIGH PRIORITY
**DEPENDENCIES:** None - Foundation validation task
**ESTIMATED TIME:** 0.5-4 hours (depending on validation results)
**PRIORITY:** CRITICAL - Validates foundation for all other development

---

## 📋 TASK OVERVIEW

### **Objective**
Validate existing Dataverse entity setup and complete any missing components for document management with SharePoint Embedded integration. This task confirms the data foundation is ready for the document management system.

### **Context: Previous Work Completed**
- ✅ **Dataverse Environment**: Already set up and accessible
- ✅ **sprk_documentdescription Field**: Successfully added via Power Platform CLI
- ✅ **DataverseService**: Complete implementation ready for entities
- ⚠️ **Entity Validation**: Need to confirm all required entities and fields exist

### **Business Context**
- Building file management system with SharePoint Embedded integration
- Need 1:1 relationship between Document records and SPE files
- Must support future one-file-multiple-documents scenario
- Document access control determines file access permissions

### **Architecture Impact**
This task creates the data layer foundation that enables:
- Document CRUD operations through DataverseService
- File metadata storage and tracking
- Container-based organization of documents
- Status tracking for async processing workflows

## 🔍 VALIDATION-FIRST APPROACH

### **STEP 1: Environment Validation**
Before any entity work, validate the current Dataverse setup:

```bash
# Run the validation script to check entity status
cd C:\code_files\spaarke
dotnet run --project test-dataverse-connection.cs
```

### **STEP 2: Determine Required Actions**
Based on validation results:

| Validation Result | Required Action | Estimated Time |
|-------------------|----------------|----------------|
| **✅ All tests pass** | Proceed to Task 1.3 (API Endpoints) | 0.5 hours (documentation only) |
| **⚠️ Entities exist, missing fields** | Complete missing fields via Power Platform | 1-2 hours |
| **❌ Connection fails** | Fix configuration, then re-validate | 2-3 hours |
| **❌ Entities missing** | Full entity creation (original plan) | 4-6 hours |

### **STEP 3: Execute Based on Results**
Follow the appropriate path based on validation outcome.

---

## 🎯 AI AGENT INSTRUCTIONS

### **CONTEXT FOR AI AGENT**
You are validating and completing the data foundation for a document management system that integrates Power Platform with SharePoint Embedded. Start with validation to determine the actual work needed.

### **VALIDATION PREREQUISITES**
Before starting validation:
1. Access to Power Platform environment
2. DataverseService configuration in `dataverse-config.local.json`
3. Managed identity or authentication credentials configured
4. Test script available: `test-dataverse-connection.cs`

### **ENTITY CREATION PREREQUISITES** (if needed after validation)
If entities need to be created:
1. Access to Power Platform admin center or maker portal
2. Appropriate permissions to create custom entities
3. Understanding of the publisher prefix "sprk_"
4. Knowledge of SharePoint Embedded field requirements

### **TECHNICAL REQUIREMENTS**

#### **Entity 1: sprk_document**
Create a new custom entity with these exact field specifications:

| Field Name | Type | Length | Required | Description |
|------------|------|--------|----------|-------------|
| sprk_name | String | 255 | Yes | Document display name |
| sprk_containerid | Lookup | - | Yes | Reference to sprk_container entity |
| sprk_documentdescription | String | 2000 | No | Document description/notes |
| sprk_hasfile | Boolean | - | No (default: false) | Whether document has associated file |
| sprk_filename | String | 255 | No | Name of file in SPE |
| sprk_filesize | BigInt | - | No | File size in bytes |
| sprk_mimetype | String | 100 | No | File MIME type |
| sprk_graphitemid | String | 100 | No | SPE item identifier |
| sprk_graphdriveid | String | 100 | No | SPE drive identifier |
| sprk_status | Choice/OptionSet | - | Yes (default: Draft) | Processing status |

#### **Choice Values for sprk_status:**
- Draft = 1
- Active = 2
- Processing = 3
- Error = 4

#### **Entity 2: sprk_container (Update Existing)**
If the container entity already exists, add this field:

| Field Name | Type | Length | Required | Description |
|------------|------|--------|----------|-------------|
| sprk_documentcount | Integer | - | No (default: 0) | Count of associated documents |

### **RELATIONSHIP CONFIGURATION**
- **Type**: 1:N relationship (Container to Documents)
- **Parent Entity**: sprk_container
- **Child Entity**: sprk_document
- **Lookup Field**: sprk_containerid
- **Cascade Behavior**: Restrict Delete (prevent container deletion if documents exist)

### **SECURITY CONFIGURATION**
- Create security roles for document management:
  - **Document Manager**: Full access to all document operations
  - **Document User**: Read/Write access to documents they own
  - **Document Reader**: Read-only access to documents they can view

### **FORM CONFIGURATION**
Create a main form for sprk_document with these sections:
1. **Basic Information**: Name, Container, Description, Status
2. **File Information**: HasFile, FileName, FileSize, MimeType
3. **Technical Details**: GraphItemId, GraphDriveId (hidden from users)

### **VIEW CONFIGURATION**
Create these views for sprk_document:
1. **Active Documents** (default): Shows only Active status documents
2. **All Documents**: Shows all documents with status filtering
3. **Documents by Container**: Grouped by container with counts
4. **Recent Documents**: Sorted by modified date, last 30 days

---

## ✅ VALIDATION PROTOCOL

Execute these validation steps to determine the current entity state:

### **Step 1: Entity Existence Check**
1. **Power Platform Portal Check**:
   ```
   - Navigate to https://make.powerapps.com
   - Select the correct environment
   - Go to Tables > All tables
   - Search for "sprk_document" and "sprk_container"
   - Document which entities exist and which are missing
   ```

2. **DataverseService Connection Test**:
   ```
   cd C:\code_files\spaarke
   dotnet run --project test-dataverse-connection.cs
   # Document connection results and any errors
   ```

### **Step 2: Field Completeness Validation**
1. **sprk_document Entity Fields Check**:
   ```
   - Verify presence of: sprk_name, sprk_containerid, sprk_documentdescription
   - Check for: sprk_hasfile, sprk_filename, sprk_filesize, sprk_mimetype
   - Validate: sprk_graphitemid, sprk_graphdriveid, sprk_status
   - Document any missing fields with exact specifications needed
   ```

2. **sprk_container Entity Fields Check**:
   ```
   - Verify sprk_documentcount field exists
   - Check field type is Integer with default value 0
   - Document if field needs to be added
   ```

3. **Relationship Validation**:
   ```
   - Verify 1:N relationship exists between sprk_container and sprk_document
   - Check cascade behavior settings
   - Test relationship navigation in Power Platform
   ```

### **Step 3: Security and Configuration Check**
1. **Security Roles Status**:
   ```
   - Check if Document Manager, Document User, Document Reader roles exist
   - Verify permissions are configured correctly
   - Document any missing security configurations
   ```

2. **Forms and Views Status**:
   ```
   - Check if main form exists for sprk_document
   - Verify required views: Active Documents, All Documents, etc.
   - Document any missing UI configurations
   ```

### **Step 4: Integration Readiness Check**
1. **DataverseService Compatibility**:
   ```
   - Test entity access via existing DataverseService methods
   - Verify field names match model class expectations
   - Check relationship navigation works through service
   - Document any model/entity mismatches
   ```

2. **API Readiness Assessment**:
   ```
   - Confirm entities are accessible via Web API
   - Test basic CRUD operations programmatically
   - Verify authentication works correctly
   - Document baseline performance metrics
   ```

---

## 🔍 TROUBLESHOOTING GUIDE

### **Common Issues and Solutions**

#### **Issue: Entity Creation Fails**
**Symptoms**: Error creating custom entity
**Solutions**:
1. Check user permissions in Power Platform admin center
2. Verify publisher prefix is available and registered
3. Ensure entity name doesn't conflict with existing entities
4. Check solution context and dependencies

#### **Issue: Lookup Relationship Not Working**
**Symptoms**: Cannot create relationship between entities
**Solutions**:
1. Verify both entities exist and are in same solution
2. Check relationship configuration settings
3. Ensure cascade behavior is set correctly
4. Verify lookup field is created properly

#### **Issue: Field Length or Type Errors**
**Symptoms**: Field creation fails with validation errors
**Solutions**:
1. Double-check field type specifications
2. Verify length limits match requirements
3. Ensure choice values are properly configured
4. Check for reserved field names

#### **Issue: Security Roles Not Working**
**Symptoms**: Users cannot access entities as expected
**Solutions**:
1. Verify security roles are assigned to users
2. Check entity-level permissions in security roles
3. Confirm field-level security is configured correctly
4. Test with different permission combinations

---

## 📚 KNOWLEDGE REFERENCES

### **Technical Documentation**
- Power Platform maker portal: https://make.powerapps.com
- Dataverse entity creation guide
- Publisher prefix management
- Security role configuration
- Relationship management in Dataverse

### **Project Context Files**
- `src/shared/Spaarke.Dataverse/DataverseService.cs` - Service implementation expecting these entities
- `src/shared/Spaarke.Dataverse/Models.cs` - Model classes defining entity structure
- `docs/CONFIGURATION_REQUIREMENTS.md` - Configuration context

### **Validation References**
- `test-dataverse-connection.cs` - Testing script to validate entity operations
- Existing entity patterns in the solution
- Power Platform security best practices

---

## 🎯 SUCCESS CRITERIA

This task is complete when:

### **Validation Completion Criteria**
- ✅ All entities and fields validated as existing or creation plan documented
- ✅ Entity-to-model mappings confirmed compatible with DataverseService
- ✅ Missing components identified with specific creation requirements
- ✅ test-dataverse-connection.cs script executes successfully
- ✅ Security and UI configurations assessed and documented

### **Creation Completion Criteria** (if required after validation)
- ✅ All missing entities created per specifications
- ✅ All missing fields added with correct types and lengths
- ✅ Relationships configured with proper cascade behavior
- ✅ Security roles and permissions implemented
- ✅ Forms and views configured for user scenarios

### **Integration Readiness Criteria**
- ✅ DataverseService successfully connects and operates on entities
- ✅ All field mappings work with existing model classes
- ✅ API endpoints ready for implementation (Task 1.3)
- ✅ Performance baselines established and documented

---

## 🔄 CONCLUSION AND NEXT STEP

### **Impact of Completion**
Completing this task unlocks:
1. **Full testing** of the existing DataverseService implementation
2. **API endpoint development** for document CRUD operations
3. **Background service integration** for async processing
4. **Power Platform UI development** for user interface

### **Quality Validation**
Before moving to the next task:
1. Run the test-dataverse-connection.cs script to validate entity operations
2. Verify all field mappings work with the DataverseService
3. Confirm relationship navigation works correctly
4. Test security roles with actual user accounts

### **Immediate Next Action**
Upon successful completion of this task:

**🎯 PROCEED TO: [Task-1.3-Document-CRUD-API-Endpoints.md](./Task-1.3-Document-CRUD-API-Endpoints.md)**

The entity foundation is now complete and ready for API layer development. The DataverseService implementation already exists and should now work seamlessly with your created entities.

### **Handoff Information**
Provide this information to the next task:
- Entity logical names and field names created
- Security role names and permissions configured
- Any customizations or deviations from the specifications
- Test results and performance baseline measurements

---

**📋 TASK COMPLETION CHECKLIST**
- [ ] sprk_document entity created with all fields
- [ ] sprk_container entity updated with document count
- [ ] Relationship configured and tested
- [ ] Security roles created and assigned
- [ ] Forms and views configured
- [ ] Validation tests completed successfully
- [ ] DataverseService integration confirmed
- [ ] Performance baselines established
- [ ] Next task team briefed on entity structure