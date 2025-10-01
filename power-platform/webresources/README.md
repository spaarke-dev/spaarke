# Power Platform Web Resources

This directory contains JavaScript web resources for the Spaarke Document Management System.

## 📁 Structure

```
webresources/
├── scripts/
│   └── sprk_DocumentOperations.js    # Main file management operations
├── DEPLOYMENT-GUIDE.md               # Deployment instructions
└── README.md                         # This file
```

## 🎯 Purpose

These web resources enable file management operations within the Power Platform model-driven app:
- Upload files to SharePoint Embedded (SPE)
- Download files from SPE
- Replace existing files
- Delete files
- Automatically update Dataverse document metadata

## 🚀 Quick Start

1. **Deploy web resource** to Power Platform (see [DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md))
2. **Configure form events** to call initialization
3. **Add ribbon buttons** for file operations
4. **Test** with small files (< 4MB for Sprint 2)

## 📦 Web Resources

### sprk_DocumentOperations.js

**Version:** 1.0.0
**Size:** ~30KB

**Functions:**
- `Spaarke.Documents.init()` - Initialize library
- `Spaarke.Documents.uploadFile(primaryControl)` - Upload file
- `Spaarke.Documents.downloadFile(primaryControl)` - Download file
- `Spaarke.Documents.replaceFile(primaryControl)` - Replace file
- `Spaarke.Documents.deleteFile(primaryControl)` - Delete file
- `Spaarke.Documents.onFormLoad(executionContext)` - Form OnLoad handler

**Dependencies:**
- None (uses native Xrm API)

**Browser Support:**
- Edge (Chromium)
- Chrome
- Firefox
- Safari

## 🔧 Configuration

### API Endpoints

The JavaScript automatically detects the environment and uses the appropriate API URL:

| Environment | Dataverse URL | API URL |
|-------------|---------------|---------|
| DEV | spaarkedev1.crm.dynamics.com | spaarke-bff-dev.azurewebsites.net |
| UAT | spaarkeuat.crm.dynamics.com | spaarke-bff-uat.azurewebsites.net |
| PROD | spaarkeprod.crm.dynamics.com | spaarke-bff-prod.azurewebsites.net |
| Local | localhost | localhost:5073 |

### File Constraints

- **Max file size:** 4MB (Sprint 2 limitation)
- **Allowed types:** .pdf, .docx, .xlsx, .pptx, .txt, .jpg, .png, .gif, .zip, .msg, .eml, etc.
- **Upload timeout:** 2 minutes
- **Download timeout:** 2 minutes

## 🔐 Security

### Authentication
- Uses user context (no secrets in JavaScript)
- Leverages Power Platform authentication
- API calls include correlation IDs for tracking

### Permissions
- Operations check user permissions
- Field-level security on `sprk_filename`
- Role-based access control

## 🧪 Testing

### Browser Console Testing

```javascript
// Check initialization
// Should see: "Spaarke Documents v1.0.0 initialized"

// Check API URL
console.log(Spaarke.Documents.Config.apiBaseUrl);
// Should show correct API URL for environment

// Test file validation
Spaarke.Documents.Utils.isValidFileType("test.pdf"); // Should return true
Spaarke.Documents.Utils.isValidFileSize(1024 * 1024); // Should return true (1MB)
```

### Manual Testing

1. Open document record
2. Click "Upload File" button
3. Select small file (< 4MB)
4. Verify success message
5. Click "Download File" button
6. Verify file downloads correctly

## 📝 Development

### Modifying the JavaScript

1. Edit `scripts/sprk_DocumentOperations.js`
2. Test locally in browser
3. Upload to Power Platform
4. Publish web resource
5. Clear browser cache
6. Test in Power Platform

### Adding New Functions

```javascript
// Add to Spaarke.Documents namespace
Spaarke.Documents.myNewFunction = function(primaryControl) {
    var formContext = primaryControl;

    try {
        // Your logic here
    } catch (error) {
        console.error("Error:", error);
        Spaarke.Documents.Utils.showError("Error", error.message);
    }
};
```

### Code Standards

- Use strict mode (`"use strict";`)
- Namespace all functions under `Spaarke.Documents`
- Include JSDoc comments
- Handle all errors with try/catch
- Show user-friendly error messages
- Log operations to console
- Use correlation IDs for tracking

## 🐛 Troubleshooting

### Common Issues

1. **CORS errors**
   - Check API CORS configuration
   - Verify Power Platform domain in allowed origins

2. **Authentication errors**
   - Verify user is logged in
   - Check API authentication configuration

3. **Upload failures**
   - Verify file size < 4MB
   - Check file type is allowed
   - Ensure document has container reference

4. **Button not visible**
   - Publish ribbon customizations
   - Clear browser cache
   - Refresh form

### Debug Mode

Open browser console (F12) to see:
- Initialization messages
- API calls with correlation IDs
- Error messages
- Network requests

## 📚 Documentation

- [Deployment Guide](DEPLOYMENT-GUIDE.md) - Step-by-step deployment instructions
- [Task 3.2 Specification](../../dev/projects/sdap_project/Sprint 2/Task-3.2-JavaScript-File-Management-Integration.md) - Complete requirements
- [CORS Configuration](../../docs/configuration/CORS-Configuration-Strategy.md) - Multi-environment CORS setup
- [Authentication Guide](../../docs/configuration/Certificate-Authentication-JavaScript.md) - Authentication approach

## 🔄 Version History

### 1.0.0 (2025-09-30)
- Initial release
- Upload, download, replace, delete operations
- Automatic environment detection
- Error handling and user feedback
- Correlation ID tracking

## 📞 Support

For issues or questions:
1. Check [DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md) troubleshooting section
2. Review browser console for errors
3. Check API logs for failures
4. Contact development team

---

**Maintained by:** Spaarke Development Team
**Last Updated:** 2025-09-30
