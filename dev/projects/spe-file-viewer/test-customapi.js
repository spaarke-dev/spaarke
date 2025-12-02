// Test Custom API - Copy and paste this entire block into browser console
// STEP 1: Replace YOUR_DOCUMENT_GUID with the actual document record ID from the URL

const documentId = "YOUR_DOCUMENT_GUID";
const entityName = "sprk_document";

Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": {
                    typeName: entityName,
                    structuralProperty: 5
                }
            },
            operationType: 1,
            operationName: "sprk_GetFilePreviewUrl"
        };
    },
    entity: {
        entityType: entityName,
        id: documentId
    }
}).then(
    function(result) {
        console.log("=== Custom API Success ===");
        console.log("PreviewUrl:", result.PreviewUrl);
        console.log("FileName:", result.FileName);
        console.log("FileSize:", result.FileSize);
        console.log("ContentType:", result.ContentType);
        console.log("ExpiresAt:", result.ExpiresAt);
        console.log("CorrelationId:", result.CorrelationId);
        console.log("Full Response:", result);

        if (result.PreviewUrl) {
            console.log("Opening preview in new tab...");
            window.open(result.PreviewUrl, "_blank");
        }
    },
    function(error) {
        console.error("=== Custom API Error ===");
        console.error("Message:", error.message);
        console.error("Full Error:", error);
    }
);
