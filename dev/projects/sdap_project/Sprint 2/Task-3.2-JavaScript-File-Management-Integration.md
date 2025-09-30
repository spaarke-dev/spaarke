# Task 3.2: JavaScript File Management Integration

**PHASE:** Power Platform Integration (Days 11-16)
**STATUS:** 🔴 READY TO START
**DEPENDENCIES:** Task 3.1 (Model-Driven App Configuration), Task 1.3 (API Endpoints)
**ESTIMATED TIME:** 10-14 hours
**PRIORITY:** HIGH - Completes user experience

---

## 📋 TASK OVERVIEW

### **Objective**
Create JavaScript web resources for file management operations in the model-driven app. This task bridges the Power Platform UI with the BFF API to provide seamless file upload, download, replace, and delete functionality directly from document forms.

### **Business Context**
- Need to integrate Power Platform forms with BFF API for file operations
- Must handle file upload, download, replace, and delete operations
- Should provide proper user feedback and error handling
- Must respect user permissions and show/hide buttons accordingly
- Integrates with existing authentication and CORS configuration

### **Architecture Impact**
This task delivers:
- Complete file management functionality within Power Platform
- Seamless integration between UI and backend services
- User-friendly file operations with progress feedback
- Comprehensive error handling and user guidance
- Production-ready JavaScript that follows enterprise patterns

---

## 🔍 PRIOR TASK REVIEW AND VALIDATION

### **Power Platform UI Review**
Before starting this task, verify the following from Task 3.1:

#### **Model-Driven App Validation**
- [ ] **App published successfully** and accessible to users
- [ ] **Document forms working** with all required fields visible
- [ ] **Security roles functional** with appropriate field restrictions
- [ ] **Ribbon commands configured** for file operations (Upload, Download, Replace, Delete)
- [ ] **Form performance acceptable** for JavaScript integration

#### **API Endpoints Validation**
From Task 1.3, confirm:
- [ ] **Document CRUD endpoints operational** and tested
- [ ] **File management endpoints working** for upload/download operations
- [ ] **Authentication working** for API calls from Power Platform
- [ ] **CORS configured** to allow Power Platform domain access
- [ ] **Error responses structured** for JavaScript consumption

#### **Integration Points Confirmation**
- [ ] **Ribbon command IDs available** for JavaScript function binding
- [ ] **Form field names confirmed** for data access and updates
- [ ] **API base URL accessible** from Power Platform environment
- [ ] **Authentication token acquisition** method identified

### **Gaps and Corrections**
If any issues found in prior tasks:

1. **UI Issues**: Ensure forms and ribbons are properly configured before adding JavaScript
2. **API Problems**: Resolve any API connectivity or authentication issues
3. **Security Issues**: Fix any CORS or authentication problems
4. **Performance Problems**: Address slow API responses that will impact user experience

---

## 🎯 AI AGENT INSTRUCTIONS

### **CONTEXT FOR AI AGENT**
You are implementing the final integration layer that provides users with seamless file management capabilities within their familiar Power Platform interface. The JavaScript must be enterprise-grade, secure, and provide excellent user experience.

### **POWER PLATFORM JAVASCRIPT PRINCIPLES**

#### **Enterprise JavaScript Standards**
- **Namespace Organization**: Use consistent namespace patterns to avoid conflicts
- **Error Handling**: Comprehensive error handling with user-friendly messages
- **Security First**: Never expose sensitive data or API keys
- **Performance Optimized**: Minimize API calls and optimize for responsiveness
- **Browser Compatibility**: Support Edge, Chrome, Firefox (modern browsers)

#### **Power Platform Integration Patterns**
- **Xrm.WebApi**: Use for Dataverse operations when possible
- **Xrm.Utility**: Leverage for dialogs, progress indicators, and user feedback
- **Xrm.Navigation**: Use for page navigation and URL generation
- **Form Context**: Properly handle form context for field access and updates
- **Security Context**: Respect user permissions and security roles

### **TECHNICAL REQUIREMENTS**

#### **1. JavaScript Web Resource Structure**

**Main Web Resource: sprk_DocumentOperations.js**
```javascript
"use strict";

// Namespace declaration
var Spaarke = Spaarke || {};
Spaarke.Documents = Spaarke.Documents || {};

// Configuration
Spaarke.Documents.Config = {
    apiBaseUrl: null, // Will be set dynamically
    maxFileSize: 100 * 1024 * 1024, // 100MB
    allowedFileTypes: ['.pdf', '.docx', '.xlsx', '.pptx', '.txt', '.jpg', '.png', '.gif'],
    uploadTimeout: 300000, // 5 minutes
    version: "1.0.0"
};

// Initialization
Spaarke.Documents.init = function() {
    try {
        // Get API base URL from environment
        Spaarke.Documents.Config.apiBaseUrl = Spaarke.Documents.getApiBaseUrl();

        // Log initialization
        console.log("Spaarke Documents v" + Spaarke.Documents.Config.version + " initialized");

        return true;
    } catch (error) {
        console.error("Failed to initialize Spaarke Documents:", error);
        return false;
    }
};

// Get API base URL based on environment
Spaarke.Documents.getApiBaseUrl = function() {
    var globalContext = Xrm.Utility.getGlobalContext();
    var clientUrl = globalContext.getClientUrl();

    // Determine API URL based on environment
    if (clientUrl.includes('crm.dynamics.com')) {
        // Production or test environment
        return "https://api.spaarke.com"; // Replace with actual production API URL
    } else {
        // Development environment
        return "https://localhost:7034"; // Local development API
    }
};

// Authentication helper
Spaarke.Documents.getAuthToken = async function() {
    try {
        // Get current user context
        var globalContext = Xrm.Utility.getGlobalContext();
        var userSettings = globalContext.userSettings;

        // For Power Platform integration, we'll use the current user's token
        // This is a simplified example - actual implementation may vary based on auth setup
        var token = await Spaarke.Documents.acquireToken();

        if (!token) {
            throw new Error("Failed to acquire authentication token");
        }

        return token;
    } catch (error) {
        console.error("Authentication failed:", error);
        throw new Error("Authentication failed. Please refresh and try again.");
    }
};

// Token acquisition (implementation depends on your auth setup)
Spaarke.Documents.acquireToken = async function() {
    // This is a placeholder - actual implementation depends on your authentication setup
    // Options include:
    // 1. Using ADAL.js or MSAL.js for Azure AD authentication
    // 2. Using Power Platform's built-in authentication
    // 3. Custom token endpoint

    try {
        // Example using fetch to get token from your auth endpoint
        var response = await fetch(Spaarke.Documents.Config.apiBaseUrl + "/api/auth/token", {
            method: "GET",
            credentials: "include"
        });

        if (response.ok) {
            var tokenData = await response.json();
            return tokenData.accessToken;
        } else {
            throw new Error("Token acquisition failed");
        }
    } catch (error) {
        console.error("Token acquisition error:", error);
        return null;
    }
};

// Utility functions
Spaarke.Documents.Utils = {
    // Format file size for display
    formatFileSize: function(bytes) {
        if (!bytes) return "0 Bytes";

        var k = 1024;
        var sizes = ['Bytes', 'KB', 'MB', 'GB'];
        var i = Math.floor(Math.log(bytes) / Math.log(k));

        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    },

    // Get file extension
    getFileExtension: function(filename) {
        return filename.slice((filename.lastIndexOf(".") - 1 >>> 0) + 2).toLowerCase();
    },

    // Validate file type
    isValidFileType: function(filename) {
        var extension = "." + Spaarke.Documents.Utils.getFileExtension(filename);
        return Spaarke.Documents.Config.allowedFileTypes.includes(extension);
    },

    // Validate file size
    isValidFileSize: function(fileSize) {
        return fileSize <= Spaarke.Documents.Config.maxFileSize;
    },

    // Generate correlation ID for tracking
    generateCorrelationId: function() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            var r = Math.random() * 16 | 0;
            var v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    },

    // Show loading indicator
    showLoading: function(message) {
        Xrm.Utility.showProgressIndicator(message || "Processing...");
    },

    // Hide loading indicator
    hideLoading: function() {
        Xrm.Utility.closeProgressIndicator();
    },

    // Show error message
    showError: function(title, message, details) {
        var alertStrings = {
            confirmButtonLabel: "OK",
            text: message
        };

        if (details) {
            alertStrings.text += "\n\nDetails: " + details;
        }

        var alertOptions = {
            height: 200,
            width: 450
        };

        Xrm.Navigation.openAlertDialog(alertStrings, alertOptions);
    },

    // Show success message
    showSuccess: function(title, message) {
        var alertStrings = {
            confirmButtonLabel: "OK",
            text: message
        };

        var alertOptions = {
            height: 150,
            width: 400
        };

        Xrm.Navigation.openAlertDialog(alertStrings, alertOptions);
    },

    // Show confirmation dialog
    showConfirmation: function(title, message) {
        var confirmStrings = {
            title: title,
            subtitle: message,
            text: "Do you want to continue?",
            confirmButtonLabel: "Yes",
            cancelButtonLabel: "No"
        };

        var confirmOptions = {
            height: 200,
            width: 450
        };

        return Xrm.Navigation.openConfirmDialog(confirmStrings, confirmOptions);
    }
};
```

#### **2. File Upload Implementation**

```javascript
// File Upload Function
Spaarke.Documents.UploadFile = async function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Check if user has permission to upload files
        if (!Spaarke.Documents.canPerformOperation(formContext, "upload")) {
            Spaarke.Documents.Utils.showError("Permission Denied", "You don't have permission to upload files.");
            return;
        }

        // Check if document already has a file
        var hasFile = formContext.getAttribute("sprk_hasfile").getValue();
        if (hasFile) {
            Spaarke.Documents.Utils.showError("File Already Exists", "This document already has a file. Use 'Replace File' to update it.");
            return;
        }

        // Create file input element
        var fileInput = document.createElement("input");
        fileInput.type = "file";
        fileInput.accept = Spaarke.Documents.Config.allowedFileTypes.join(",");
        fileInput.style.display = "none";

        // Handle file selection
        fileInput.onchange = async function(event) {
            var file = event.target.files[0];
            if (!file) return;

            try {
                await Spaarke.Documents.processFileUpload(formContext, file);
            } catch (error) {
                console.error("File upload error:", error);
                Spaarke.Documents.Utils.hideLoading();
                Spaarke.Documents.Utils.showError("Upload Failed", error.message);
            }
        };

        // Trigger file selection
        document.body.appendChild(fileInput);
        fileInput.click();
        document.body.removeChild(fileInput);

    } catch (error) {
        console.error("Upload file error:", error);
        Spaarke.Documents.Utils.showError("Upload Error", "An unexpected error occurred during file upload.");
    }
};

// Process file upload
Spaarke.Documents.processFileUpload = async function(formContext, file) {
    var correlationId = Spaarke.Documents.Utils.generateCorrelationId();

    try {
        // Validate file
        if (!Spaarke.Documents.Utils.isValidFileType(file.name)) {
            throw new Error("Invalid file type. Allowed types: " + Spaarke.Documents.Config.allowedFileTypes.join(", "));
        }

        if (!Spaarke.Documents.Utils.isValidFileSize(file.size)) {
            throw new Error("File size exceeds maximum allowed size of " +
                           Spaarke.Documents.Utils.formatFileSize(Spaarke.Documents.Config.maxFileSize));
        }

        // Show progress
        Spaarke.Documents.Utils.showLoading("Uploading file: " + file.name);

        // Get document ID
        var documentId = formContext.data.entity.getId().replace("{", "").replace("}", "");

        // Get authentication token
        var token = await Spaarke.Documents.getAuthToken();

        // Create form data
        var formData = new FormData();
        formData.append("file", file);
        formData.append("correlationId", correlationId);

        // Upload file
        var uploadResponse = await fetch(
            Spaarke.Documents.Config.apiBaseUrl + "/api/v1/documents/" + documentId + "/files",
            {
                method: "POST",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-ID": correlationId
                },
                body: formData
            }
        );

        if (!uploadResponse.ok) {
            var errorData = await uploadResponse.json();
            throw new Error(errorData.detail || "File upload failed");
        }

        var uploadResult = await uploadResponse.json();

        // Update form fields
        await Spaarke.Documents.updateFormAfterUpload(formContext, file, uploadResult);

        // Show success message
        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showSuccess("Upload Successful", "File '" + file.name + "' has been uploaded successfully.");

        // Refresh form to show updated data
        formContext.data.refresh(true);

    } catch (error) {
        console.error("File upload processing error:", error);
        throw error;
    }
};

// Update form after successful upload
Spaarke.Documents.updateFormAfterUpload = async function(formContext, file, uploadResult) {
    try {
        // Update form fields using Xrm.WebApi
        var documentId = formContext.data.entity.getId().replace("{", "").replace("}", "");

        var updateData = {
            "sprk_hasfile": true,
            "sprk_filename": file.name,
            "sprk_filesize": file.size,
            "sprk_mimetype": file.type || "application/octet-stream"
        };

        // Update via Web API
        await Xrm.WebApi.updateRecord("sprk_document", documentId, updateData);

        // Update form attributes
        formContext.getAttribute("sprk_hasfile").setValue(true);
        formContext.getAttribute("sprk_filename").setValue(file.name);
        formContext.getAttribute("sprk_filesize").setValue(file.size);
        formContext.getAttribute("sprk_mimetype").setValue(file.type || "application/octet-stream");

        // Update ribbon buttons
        Spaarke.Documents.refreshRibbonButtons(formContext);

    } catch (error) {
        console.error("Form update error:", error);
        // Don't throw here - upload was successful, form update is secondary
    }
};
```

#### **3. File Download Implementation**

```javascript
// File Download Function
Spaarke.Documents.DownloadFile = async function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Check if user has permission to download files
        if (!Spaarke.Documents.canPerformOperation(formContext, "download")) {
            Spaarke.Documents.Utils.showError("Permission Denied", "You don't have permission to download files.");
            return;
        }

        // Check if document has a file
        var hasFile = formContext.getAttribute("sprk_hasfile").getValue();
        if (!hasFile) {
            Spaarke.Documents.Utils.showError("No File", "This document does not have an associated file.");
            return;
        }

        var correlationId = Spaarke.Documents.Utils.generateCorrelationId();

        // Show progress
        Spaarke.Documents.Utils.showLoading("Preparing download...");

        // Get document ID
        var documentId = formContext.data.entity.getId().replace("{", "").replace("}", "");

        // Get authentication token
        var token = await Spaarke.Documents.getAuthToken();

        // Get download URL
        var downloadResponse = await fetch(
            Spaarke.Documents.Config.apiBaseUrl + "/api/v1/documents/" + documentId + "/files",
            {
                method: "GET",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-ID": correlationId
                }
            }
        );

        if (!downloadResponse.ok) {
            var errorData = await downloadResponse.json();
            throw new Error(errorData.detail || "Failed to generate download URL");
        }

        var downloadData = await downloadResponse.json();

        // Hide loading
        Spaarke.Documents.Utils.hideLoading();

        // Initiate download
        var link = document.createElement("a");
        link.href = downloadData.downloadUrl;
        link.download = downloadData.fileName || "document";
        link.style.display = "none";

        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        console.log("File download initiated for document:", documentId);

    } catch (error) {
        console.error("Download file error:", error);
        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showError("Download Failed", error.message);
    }
};
```

#### **4. File Replace Implementation**

```javascript
// File Replace Function
Spaarke.Documents.ReplaceFile = async function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Check if user has permission to replace files
        if (!Spaarke.Documents.canPerformOperation(formContext, "replace")) {
            Spaarke.Documents.Utils.showError("Permission Denied", "You don't have permission to replace files.");
            return;
        }

        // Check if document has a file to replace
        var hasFile = formContext.getAttribute("sprk_hasfile").getValue();
        if (!hasFile) {
            Spaarke.Documents.Utils.showError("No File", "This document does not have a file to replace.");
            return;
        }

        // Get current file name for confirmation
        var currentFileName = formContext.getAttribute("sprk_filename").getValue();

        // Show confirmation
        var confirmed = await Spaarke.Documents.Utils.showConfirmation(
            "Replace File",
            "This will replace the current file '" + currentFileName + "' with a new file. This action cannot be undone."
        );

        if (!confirmed.confirmed) {
            return;
        }

        // Create file input element
        var fileInput = document.createElement("input");
        fileInput.type = "file";
        fileInput.accept = Spaarke.Documents.Config.allowedFileTypes.join(",");
        fileInput.style.display = "none";

        // Handle file selection
        fileInput.onchange = async function(event) {
            var file = event.target.files[0];
            if (!file) return;

            try {
                await Spaarke.Documents.processFileReplace(formContext, file);
            } catch (error) {
                console.error("File replace error:", error);
                Spaarke.Documents.Utils.hideLoading();
                Spaarke.Documents.Utils.showError("Replace Failed", error.message);
            }
        };

        // Trigger file selection
        document.body.appendChild(fileInput);
        fileInput.click();
        document.body.removeChild(fileInput);

    } catch (error) {
        console.error("Replace file error:", error);
        Spaarke.Documents.Utils.showError("Replace Error", "An unexpected error occurred during file replacement.");
    }
};

// Process file replacement
Spaarke.Documents.processFileReplace = async function(formContext, file) {
    var correlationId = Spaarke.Documents.Utils.generateCorrelationId();

    try {
        // Validate file
        if (!Spaarke.Documents.Utils.isValidFileType(file.name)) {
            throw new Error("Invalid file type. Allowed types: " + Spaarke.Documents.Config.allowedFileTypes.join(", "));
        }

        if (!Spaarke.Documents.Utils.isValidFileSize(file.size)) {
            throw new Error("File size exceeds maximum allowed size of " +
                           Spaarke.Documents.Utils.formatFileSize(Spaarke.Documents.Config.maxFileSize));
        }

        // Show progress
        Spaarke.Documents.Utils.showLoading("Replacing file: " + file.name);

        // Get document ID
        var documentId = formContext.data.entity.getId().replace("{", "").replace("}", "");

        // Get authentication token
        var token = await Spaarke.Documents.getAuthToken();

        // Create form data
        var formData = new FormData();
        formData.append("file", file);
        formData.append("replaceExisting", "true");
        formData.append("correlationId", correlationId);

        // Replace file
        var replaceResponse = await fetch(
            Spaarke.Documents.Config.apiBaseUrl + "/api/v1/documents/" + documentId + "/files",
            {
                method: "PUT",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-ID": correlationId
                },
                body: formData
            }
        );

        if (!replaceResponse.ok) {
            var errorData = await replaceResponse.json();
            throw new Error(errorData.detail || "File replacement failed");
        }

        var replaceResult = await replaceResponse.json();

        // Update form fields
        await Spaarke.Documents.updateFormAfterUpload(formContext, file, replaceResult);

        // Show success message
        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showSuccess("Replace Successful", "File has been replaced with '" + file.name + "'.");

        // Refresh form to show updated data
        formContext.data.refresh(true);

    } catch (error) {
        console.error("File replacement processing error:", error);
        throw error;
    }
};
```

#### **5. File Delete Implementation**

```javascript
// File Delete Function
Spaarke.Documents.DeleteFile = async function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Check if user has permission to delete files
        if (!Spaarke.Documents.canPerformOperation(formContext, "delete")) {
            Spaarke.Documents.Utils.showError("Permission Denied", "You don't have permission to delete files.");
            return;
        }

        // Check if document has a file to delete
        var hasFile = formContext.getAttribute("sprk_hasfile").getValue();
        if (!hasFile) {
            Spaarke.Documents.Utils.showError("No File", "This document does not have a file to delete.");
            return;
        }

        // Get current file name for confirmation
        var currentFileName = formContext.getAttribute("sprk_filename").getValue();

        // Show confirmation
        var confirmed = await Spaarke.Documents.Utils.showConfirmation(
            "Delete File",
            "This will permanently delete the file '" + currentFileName + "'. This action cannot be undone.\n\nThe document record will remain, but the associated file will be removed."
        );

        if (!confirmed.confirmed) {
            return;
        }

        var correlationId = Spaarke.Documents.Utils.generateCorrelationId();

        // Show progress
        Spaarke.Documents.Utils.showLoading("Deleting file...");

        // Get document ID
        var documentId = formContext.data.entity.getId().replace("{", "").replace("}", "");

        // Get authentication token
        var token = await Spaarke.Documents.getAuthToken();

        // Delete file
        var deleteResponse = await fetch(
            Spaarke.Documents.Config.apiBaseUrl + "/api/v1/documents/" + documentId + "/files",
            {
                method: "DELETE",
                headers: {
                    "Authorization": "Bearer " + token,
                    "X-Correlation-ID": correlationId
                }
            }
        );

        if (!deleteResponse.ok) {
            var errorData = await deleteResponse.json();
            throw new Error(errorData.detail || "File deletion failed");
        }

        // Update form fields
        await Spaarke.Documents.updateFormAfterDelete(formContext);

        // Show success message
        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showSuccess("Delete Successful", "File '" + currentFileName + "' has been deleted successfully.");

        // Refresh form to show updated data
        formContext.data.refresh(true);

    } catch (error) {
        console.error("Delete file error:", error);
        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showError("Delete Failed", error.message);
    }
};

// Update form after successful file deletion
Spaarke.Documents.updateFormAfterDelete = async function(formContext) {
    try {
        // Update form fields using Xrm.WebApi
        var documentId = formContext.data.entity.getId().replace("{", "").replace("}", "");

        var updateData = {
            "sprk_hasfile": false,
            "sprk_filename": null,
            "sprk_filesize": null,
            "sprk_mimetype": null,
            "sprk_graphitemid": null,
            "sprk_graphdriveid": null
        };

        // Update via Web API
        await Xrm.WebApi.updateRecord("sprk_document", documentId, updateData);

        // Update form attributes
        formContext.getAttribute("sprk_hasfile").setValue(false);
        formContext.getAttribute("sprk_filename").setValue(null);
        formContext.getAttribute("sprk_filesize").setValue(null);
        formContext.getAttribute("sprk_mimetype").setValue(null);

        // Clear technical fields if they exist
        var graphItemIdAttr = formContext.getAttribute("sprk_graphitemid");
        if (graphItemIdAttr) graphItemIdAttr.setValue(null);

        var graphDriveIdAttr = formContext.getAttribute("sprk_graphdriveid");
        if (graphDriveIdAttr) graphDriveIdAttr.setValue(null);

        // Update ribbon buttons
        Spaarke.Documents.refreshRibbonButtons(formContext);

    } catch (error) {
        console.error("Form update after delete error:", error);
        // Don't throw here - deletion was successful, form update is secondary
    }
};
```

#### **6. Permission and Button Management**

```javascript
// Check if user can perform specific operations
Spaarke.Documents.canPerformOperation = function(formContext, operation) {
    try {
        // Get user security roles
        var userRoles = Xrm.Utility.getGlobalContext().userSettings.roles;

        // Check basic entity permissions
        var hasWritePermission = true; // Simplified - implement proper permission check
        var hasDeletePermission = true; // Simplified - implement proper permission check

        switch (operation) {
            case "upload":
                return hasWritePermission && !formContext.getAttribute("sprk_hasfile").getValue();

            case "download":
                return formContext.getAttribute("sprk_hasfile").getValue();

            case "replace":
                return hasWritePermission && formContext.getAttribute("sprk_hasfile").getValue();

            case "delete":
                return hasDeletePermission && formContext.getAttribute("sprk_hasfile").getValue();

            default:
                return false;
        }
    } catch (error) {
        console.error("Permission check error:", error);
        return false;
    }
};

// Configure file operation buttons based on form state and permissions
Spaarke.Documents.ConfigureFileButtons = function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // This function is called on form load and when relevant fields change
        Spaarke.Documents.refreshRibbonButtons(formContext);

        // Set up field change handlers
        var hasFileAttr = formContext.getAttribute("sprk_hasfile");
        if (hasFileAttr) {
            hasFileAttr.addOnChange(function() {
                Spaarke.Documents.refreshRibbonButtons(formContext);
            });
        }

    } catch (error) {
        console.error("Configure file buttons error:", error);
    }
};

// Refresh ribbon buttons based on current state
Spaarke.Documents.refreshRibbonButtons = function(formContext) {
    try {
        // Force ribbon refresh to update button states
        // This will cause the ribbon enable rules to be re-evaluated
        formContext.ui.refreshRibbon();
    } catch (error) {
        console.error("Refresh ribbon error:", error);
    }
};

// Form load event handler
Spaarke.Documents.onFormLoad = function(executionContext) {
    try {
        // Initialize the module
        if (!Spaarke.Documents.init()) {
            console.error("Failed to initialize Spaarke Documents module");
            return;
        }

        // Configure file operation buttons
        Spaarke.Documents.ConfigureFileButtons(executionContext);

        // Set up any additional form-specific configurations
        Spaarke.Documents.setupFormConfiguration(executionContext);

    } catch (error) {
        console.error("Form load error:", error);
    }
};

// Setup additional form configuration
Spaarke.Documents.setupFormConfiguration = function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Make file size field read-only and format it
        var fileSizeControl = formContext.getControl("sprk_filesize");
        if (fileSizeControl) {
            fileSizeControl.setDisabled(true);

            // Format file size display
            var fileSizeAttr = formContext.getAttribute("sprk_filesize");
            if (fileSizeAttr) {
                fileSizeAttr.addOnChange(function() {
                    var sizeValue = fileSizeAttr.getValue();
                    if (sizeValue) {
                        var formattedSize = Spaarke.Documents.Utils.formatFileSize(sizeValue);
                        // You might want to update a separate display field here
                    }
                });
            }
        }

        // Make other file-related fields read-only
        var fieldsToDisable = ["sprk_filename", "sprk_mimetype", "sprk_graphitemid", "sprk_graphdriveid"];
        fieldsToDisable.forEach(function(fieldName) {
            var control = formContext.getControl(fieldName);
            if (control) {
                control.setDisabled(true);
            }
        });

    } catch (error) {
        console.error("Setup form configuration error:", error);
    }
};

// Field change event handlers
Spaarke.Documents.onHasFileChange = function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Refresh ribbon when file status changes
        Spaarke.Documents.refreshRibbonButtons(formContext);

        // You might want to show/hide related fields or sections
        var hasFile = formContext.getAttribute("sprk_hasfile").getValue();

        // Show/hide file information section
        var fileInfoSection = formContext.ui.tabs.get("general").sections.get("file_info");
        if (fileInfoSection) {
            fileInfoSection.setVisible(hasFile);
        }

    } catch (error) {
        console.error("Has file change error:", error);
    }
};
```

#### **7. Error Handling and Logging**

```javascript
// Enhanced error handling
Spaarke.Documents.ErrorHandler = {
    // Log error with context
    logError: function(error, context, correlationId) {
        var errorInfo = {
            timestamp: new Date().toISOString(),
            error: error.message || error,
            stack: error.stack,
            context: context,
            correlationId: correlationId,
            userAgent: navigator.userAgent,
            url: window.location.href
        };

        console.error("Spaarke Documents Error:", errorInfo);

        // In production, you might want to send this to a logging service
        // this.sendToLoggingService(errorInfo);
    },

    // Handle API errors specifically
    handleApiError: function(response, context) {
        var errorMessage = "An unexpected error occurred";

        if (response.status === 401) {
            errorMessage = "Authentication failed. Please refresh the page and try again.";
        } else if (response.status === 403) {
            errorMessage = "You don't have permission to perform this action.";
        } else if (response.status === 404) {
            errorMessage = "The requested resource was not found.";
        } else if (response.status === 413) {
            errorMessage = "The file is too large. Please select a smaller file.";
        } else if (response.status === 415) {
            errorMessage = "The file type is not supported.";
        } else if (response.status >= 500) {
            errorMessage = "A server error occurred. Please try again later.";
        }

        return new Error(errorMessage);
    },

    // Send error to logging service (placeholder)
    sendToLoggingService: function(errorInfo) {
        // Implementation would depend on your logging service
        // Example: Application Insights, custom logging endpoint, etc.
    }
};

// Wrap async functions with error handling
Spaarke.Documents.withErrorHandling = function(asyncFunction, context) {
    return async function(...args) {
        try {
            return await asyncFunction.apply(this, args);
        } catch (error) {
            var correlationId = Spaarke.Documents.Utils.generateCorrelationId();
            Spaarke.Documents.ErrorHandler.logError(error, context, correlationId);
            throw error;
        }
    };
};
```

#### **8. Performance Optimization**

```javascript
// Performance monitoring and optimization
Spaarke.Documents.Performance = {
    // Track operation timing
    startTimer: function(operationName) {
        var startTime = performance.now();
        return {
            operationName: operationName,
            startTime: startTime,
            end: function() {
                var endTime = performance.now();
                var duration = endTime - startTime;

                console.log("Operation '" + operationName + "' took " + duration.toFixed(2) + " ms");

                // Track slow operations
                if (duration > 5000) { // 5 seconds
                    console.warn("Slow operation detected: " + operationName + " took " + duration + "ms");
                }

                return duration;
            }
        };
    },

    // Debounce function for frequent operations
    debounce: function(func, wait) {
        var timeout;
        return function executedFunction(...args) {
            var later = function() {
                clearTimeout(timeout);
                func(...args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    },

    // Cache for API responses
    cache: {
        store: new Map(),

        get: function(key) {
            var item = this.store.get(key);
            if (item && item.expiry > Date.now()) {
                return item.value;
            }
            this.store.delete(key);
            return null;
        },

        set: function(key, value, ttlMs) {
            ttlMs = ttlMs || 300000; // 5 minutes default
            this.store.set(key, {
                value: value,
                expiry: Date.now() + ttlMs
            });
        },

        clear: function() {
            this.store.clear();
        }
    }
};
```

### **WEB RESOURCE CONFIGURATION**

#### **Web Resource Properties**
```json
{
    "name": "sprk_DocumentOperations",
    "displayname": "Spaarke Document Operations",
    "description": "JavaScript library for document file management operations",
    "webresourcetype": "Script (JScript)",
    "content": "[Base64 encoded JavaScript file content]",
    "ishidden": false,
    "iscustomizable": true,
    "canbedeleted": true
}
```

#### **Form Event Configuration**
```xml
<!-- Form Events Configuration -->
<events>
    <event name="onload" attribute="" active="true">
        <handler functionname="Spaarke.Documents.onFormLoad" libraryname="sprk_DocumentOperations" handlertype="1" passenabled="true" />
    </event>

    <event name="onchange" attribute="sprk_hasfile" active="true">
        <handler functionname="Spaarke.Documents.onHasFileChange" libraryname="sprk_DocumentOperations" handlertype="1" passenabled="true" />
    </event>
</events>
```

---

## ✅ VALIDATION STEPS

### **JavaScript Functionality Testing**

#### **1. File Upload Testing**
```javascript
// Test file upload functionality
async function testFileUpload() {
    try {
        // Create test file
        var testContent = "This is a test file for upload validation";
        var testFile = new File([testContent], "test-upload.txt", { type: "text/plain" });

        // Create mock form context
        var mockFormContext = createMockFormContext();

        // Test file validation
        console.log("File type valid:", Spaarke.Documents.Utils.isValidFileType(testFile.name));
        console.log("File size valid:", Spaarke.Documents.Utils.isValidFileSize(testFile.size));

        // Test upload process (in controlled environment)
        // Note: This would typically be done through manual testing

    } catch (error) {
        console.error("File upload test failed:", error);
    }
}

// Helper function to create mock form context for testing
function createMockFormContext() {
    // This is a simplified mock - actual implementation would be more comprehensive
    return {
        data: {
            entity: {
                getId: function() { return "{test-document-id}"; }
            }
        },
        getAttribute: function(attributeName) {
            return {
                getValue: function() {
                    switch (attributeName) {
                        case "sprk_hasfile": return false;
                        case "sprk_filename": return null;
                        case "sprk_filesize": return null;
                        default: return null;
                    }
                },
                setValue: function(value) { /* Mock implementation */ }
            };
        }
    };
}
```

#### **2. API Integration Testing**
```javascript
// Test API connectivity and authentication
async function testApiIntegration() {
    try {
        // Test base URL configuration
        console.log("API Base URL:", Spaarke.Documents.Config.apiBaseUrl);

        // Test authentication
        var token = await Spaarke.Documents.getAuthToken();
        console.log("Authentication successful:", !!token);

        // Test API health check (if available)
        var healthResponse = await fetch(Spaarke.Documents.Config.apiBaseUrl + "/api/v1/health", {
            method: "GET",
            headers: {
                "Authorization": "Bearer " + token
            }
        });

        console.log("API health check:", healthResponse.ok ? "PASS" : "FAIL");

    } catch (error) {
        console.error("API integration test failed:", error);
    }
}
```

#### **3. Permission and Security Testing**
```javascript
// Test permission checking
function testPermissions() {
    try {
        var mockFormContext = createMockFormContext();

        // Test different operations
        console.log("Can upload:", Spaarke.Documents.canPerformOperation(mockFormContext, "upload"));
        console.log("Can download:", Spaarke.Documents.canPerformOperation(mockFormContext, "download"));
        console.log("Can replace:", Spaarke.Documents.canPerformOperation(mockFormContext, "replace"));
        console.log("Can delete:", Spaarke.Documents.canPerformOperation(mockFormContext, "delete"));

    } catch (error) {
        console.error("Permission test failed:", error);
    }
}
```

### **End-to-End User Testing**

#### **1. Complete File Operations Flow**
```bash
# Manual testing steps
1. Navigate to Document form in Power Platform app
2. Verify ribbon buttons are visible and appropriate states
3. Test file upload:
   - Click "Upload File" button
   - Select valid file type and size
   - Verify upload progress and completion
   - Check form fields are updated correctly
4. Test file download:
   - Click "Download File" button
   - Verify download initiates correctly
   - Check downloaded file integrity
5. Test file replacement:
   - Click "Replace File" button
   - Confirm replacement dialog
   - Select new file and verify replacement
6. Test file deletion:
   - Click "Delete File" button
   - Confirm deletion dialog
   - Verify file is deleted and form updated
```

#### **2. Error Scenario Testing**
```bash
# Test various error conditions
1. Upload oversized file (> 100MB)
2. Upload invalid file type
3. Attempt operations without permissions
4. Test with network connectivity issues
5. Test with invalid authentication tokens
6. Verify error messages are user-friendly
```

#### **3. Performance Testing**
```javascript
// Test performance characteristics
function testPerformance() {
    // Test initialization time
    var initTimer = Spaarke.Documents.Performance.startTimer("Initialization");
    Spaarke.Documents.init();
    initTimer.end();

    // Test button configuration time
    var buttonTimer = Spaarke.Documents.Performance.startTimer("Button Configuration");
    Spaarke.Documents.ConfigureFileButtons(mockExecutionContext);
    buttonTimer.end();

    // Performance targets:
    // - Initialization: < 100ms
    // - Button configuration: < 50ms
    // - File operations: < 30 seconds for reasonable file sizes
}
```

### **Cross-Browser Testing**

#### **1. Browser Compatibility**
```bash
# Test in different browsers
1. Microsoft Edge (primary)
2. Google Chrome
3. Mozilla Firefox
4. Safari (if applicable)

# Verify functionality:
- File upload/download works
- Progress indicators display correctly
- Error messages appear properly
- Authentication flows work
```

#### **2. Mobile Testing**
```bash
# Test mobile experience
1. Access Power Platform app on mobile browser
2. Test file operations on touch interface
3. Verify responsive design elements
4. Check file upload from mobile device
```

---

## 🔍 TROUBLESHOOTING GUIDE

### **Common Issues and Solutions**

#### **Issue: JavaScript Functions Not Available**
**Symptoms**: Ribbon buttons show "Function not found" errors
**Diagnosis Steps**:
1. Check web resource is published and available
2. Verify function names in ribbon configuration match JavaScript
3. Check browser console for JavaScript errors
4. Verify web resource is included in solution

**Solutions**:
```javascript
// Add function existence check
if (typeof Spaarke === 'undefined' || !Spaarke.Documents) {
    console.error("Spaarke Documents library not loaded");
    // Show user-friendly error
}

// Defensive programming for ribbon functions
window.Spaarke = window.Spaarke || {};
window.Spaarke.Documents = window.Spaarke.Documents || {};
```

#### **Issue: Authentication Failures**
**Symptoms**: API calls return 401 Unauthorized errors
**Diagnosis Steps**:
1. Check authentication token acquisition logic
2. Verify API endpoint authentication requirements
3. Check CORS configuration
4. Review token expiration handling

**Solutions**:
```javascript
// Add token validation
Spaarke.Documents.validateToken = async function(token) {
    try {
        var response = await fetch(Spaarke.Documents.Config.apiBaseUrl + "/api/auth/validate", {
            method: "GET",
            headers: { "Authorization": "Bearer " + token }
        });
        return response.ok;
    } catch (error) {
        return false;
    }
};

// Implement token refresh
Spaarke.Documents.refreshToken = async function() {
    // Implementation depends on your auth setup
};
```

#### **Issue: File Upload Failures**
**Symptoms**: File uploads fail with various errors
**Diagnosis Steps**:
1. Check file size and type validation
2. Verify API endpoint is accessible
3. Check network connectivity
4. Review server-side error logs

**Solutions**:
```javascript
// Enhanced file validation
Spaarke.Documents.validateFile = function(file) {
    var errors = [];

    if (!file) {
        errors.push("No file selected");
    }

    if (file && file.size > Spaarke.Documents.Config.maxFileSize) {
        errors.push("File size exceeds maximum allowed size");
    }

    if (file && !Spaarke.Documents.Utils.isValidFileType(file.name)) {
        errors.push("Invalid file type");
    }

    return {
        isValid: errors.length === 0,
        errors: errors
    };
};
```

#### **Issue: CORS Errors**
**Symptoms**: Browser blocks API calls due to CORS policy
**Diagnosis Steps**:
1. Check browser developer tools for CORS errors
2. Verify API CORS configuration includes Power Platform domain
3. Check request headers and methods

**Solutions**:
```javascript
// Verify CORS configuration on API side includes:
// - Power Platform environment domain
// - Required HTTP methods (GET, POST, PUT, DELETE)
// - Required headers (Authorization, Content-Type, X-Correlation-ID)
```

#### **Issue: Ribbon Buttons Not Updating**
**Symptoms**: Buttons don't enable/disable based on form state
**Diagnosis Steps**:
1. Check ribbon enable rules configuration
2. Verify ribbon refresh logic
3. Check field change event handlers

**Solutions**:
```javascript
// Force ribbon refresh with error handling
Spaarke.Documents.forceRibbonRefresh = function(formContext) {
    try {
        formContext.ui.refreshRibbon();
    } catch (error) {
        console.error("Ribbon refresh failed:", error);
        // Fallback: Manual button state management
        Spaarke.Documents.manualButtonStateUpdate(formContext);
    }
};
```

#### **Issue: Performance Problems**
**Symptoms**: Slow response times or browser freezing
**Diagnosis Steps**:
1. Monitor browser performance tools
2. Check for memory leaks
3. Review API response times
4. Check for blocking operations

**Solutions**:
```javascript
// Implement timeout for long operations
Spaarke.Documents.withTimeout = function(promise, timeoutMs) {
    return Promise.race([
        promise,
        new Promise((_, reject) =>
            setTimeout(() => reject(new Error('Operation timed out')), timeoutMs)
        )
    ]);
};

// Use timeout for API calls
var uploadPromise = fetch(uploadUrl, uploadOptions);
var uploadResult = await Spaarke.Documents.withTimeout(uploadPromise, 300000); // 5 minutes
```

---

## 📚 KNOWLEDGE REFERENCES

### **Power Platform JavaScript References**
- Xrm object model documentation
- Web resource development guide
- Form scripting best practices
- Ribbon customization documentation

### **API Integration References**
- BFF API endpoint documentation (from Task 1.3)
- Authentication and authorization patterns
- CORS configuration requirements
- Error response format specifications

### **Browser and JavaScript References**
- File API documentation for file handling
- Fetch API for HTTP requests
- Promise and async/await patterns
- Modern JavaScript (ES6+) features

---

## 🎯 SUCCESS CRITERIA

This task is complete when:

### **Functional Criteria**
- ✅ All file operations (upload, download, replace, delete) work correctly
- ✅ JavaScript functions are properly bound to ribbon commands
- ✅ User permissions are respected and enforced
- ✅ Form fields update correctly after file operations
- ✅ Error handling provides clear user guidance

### **Integration Criteria**
- ✅ Authentication works between Power Platform and API
- ✅ CORS configuration allows necessary API calls
- ✅ API responses are handled correctly in JavaScript
- ✅ Progress indicators and user feedback work properly

### **User Experience Criteria**
- ✅ File operations are intuitive and user-friendly
- ✅ Error messages are clear and actionable
- ✅ Progress indicators show operation status
- ✅ Confirmation dialogs prevent accidental actions
- ✅ Mobile experience is acceptable

### **Performance Criteria**
- ✅ JavaScript loads quickly and doesn't impact form performance
- ✅ File operations complete within reasonable timeframes
- ✅ Large file uploads show progress appropriately
- ✅ No memory leaks or performance degradation

### **Security Criteria**
- ✅ Authentication tokens are handled securely
- ✅ No sensitive data exposed in client-side code
- ✅ User permissions properly enforced
- ✅ API calls use secure protocols (HTTPS)

---

## 🔄 CONCLUSION AND NEXT STEP

### **Impact of Completion**
Completing this task delivers:
1. **Complete end-to-end user experience** for document and file management
2. **Seamless integration** between Power Platform UI and backend services
3. **Enterprise-grade file operations** with proper error handling and security
4. **User-friendly interface** that follows Power Platform best practices
5. **Production-ready solution** for document management workflows

### **Quality Validation**
Before considering the project complete:
1. Execute comprehensive end-to-end testing of all file operations
2. Validate user experience across different roles and scenarios
3. Confirm performance meets established targets
4. Test error scenarios and recovery procedures
5. Verify security and permission enforcement works correctly

### **System Integration Validation**
Ensure the complete solution works:
1. Document CRUD operations trigger appropriate backend processing
2. File operations integrate correctly with SharePoint Embedded
3. User interface reflects backend state accurately
4. Error handling provides appropriate user guidance
5. Performance is acceptable for expected user loads

### **Project Completion**
Upon successful completion of this task:

**🎉 PROJECT COMPLETE: Document Management CRUD System**

The complete document management system is now operational with:
- Dataverse entities for data storage
- REST API for backend operations
- Async processing for scalable operations
- Power Platform UI for user interaction
- JavaScript integration for file management

### **Handoff Information**
For production deployment and ongoing support:
- Complete documentation of all components and integrations
- Performance baselines and monitoring requirements
- Security configuration and compliance requirements
- User training materials and support procedures
- Troubleshooting guides and operational runbooks

---

**📋 TASK COMPLETION CHECKLIST**
- [ ] JavaScript web resource created and published
- [ ] All file operations implemented and tested
- [ ] Authentication and API integration working
- [ ] User permissions and security enforced
- [ ] Error handling comprehensive and user-friendly
- [ ] Performance targets met consistently
- [ ] Cross-browser compatibility verified
- [ ] End-to-end testing completed successfully
- [ ] Production deployment readiness confirmed