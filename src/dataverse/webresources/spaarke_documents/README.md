# Spaarke Documents - Web Resources

Web resources for the `spaarke_documents` Dataverse solution.

## Files

### DocumentOperations.js
**Purpose**: File management operations for Document entity forms
**Version**: 1.0.0
**Namespace**: `Spaarke.Documents`

Provides ribbon button commands for individual Document records:
- ✅ **Upload File** - Upload file to new document
- ✅ **Download File** - Download file from document
- ✅ **Replace File** - Replace existing file
- ✅ **Delete File** - Delete file from document

#### Architecture
```
Document Form (sprk_document)
  ↓ Ribbon Button (Upload/Download/Replace/Delete)
DocumentOperations.js
  ↓ Gets Container ID from sprk_containerid lookup
  ↓ Calls BFF API endpoints
SDAP BFF API
  ↓ App-only authentication
  ↓ Microsoft Graph API
SharePoint Embedded
```

#### Key Features
- Environment auto-detection (DEV/UAT/PROD)
- File validation (type whitelist, 4MB max)
- Correlation ID tracking for all API calls
- Permission checks based on form state
- Cookie-based authentication (`credentials: 'include'`)

#### Supported File Types
`.pdf`, `.docx`, `.doc`, `.xlsx`, `.xls`, `.pptx`, `.ppt`, `.txt`, `.csv`, `.xml`, `.json`, `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.zip`, `.msg`, `.eml`

#### Usage

**Form OnLoad Event:**
```javascript
// Function: Spaarke.Documents.onFormLoad
// Pass execution context: Yes
```

**Ribbon Button Commands:**
```javascript
// Upload: Spaarke.Documents.uploadFile(primaryControl)
// Download: Spaarke.Documents.downloadFile(primaryControl)
// Replace: Spaarke.Documents.replaceFile(primaryControl)
// Delete: Spaarke.Documents.deleteFile(primaryControl)
```

#### Dependencies
- Xrm API (native Dataverse)
- SDAP BFF API endpoints
- Document entity fields:
  - `sprk_hasfile` (Boolean)
  - `sprk_filename` (Text)
  - `sprk_filesize` (Integer)
  - `sprk_mimetype` (Text)
  - `sprk_graphitemid` (Text)
  - `sprk_graphdriveid` (Text)
  - `sprk_containerid` (Lookup to sprk_container)

#### Deployment

**Name in Dataverse**: `sprk_DocumentOperations.js`
**Display Name**: "Document Operations - File Management"
**Solution**: spaarke_documents

**Deploy via Power Apps Portal:**
1. Navigate to make.powerapps.com
2. Select environment
3. Solutions → spaarke_documents
4. New → Web resource → Script (JScript)
5. Upload `DocumentOperations.js`
6. Publish

**Configure Ribbon Buttons:**
- Add to Document entity main form ribbon
- Set commands to call appropriate functions
- Pass `primaryControl` parameter

## Status

✅ Production-Ready | Last Updated: 2025-12-03
