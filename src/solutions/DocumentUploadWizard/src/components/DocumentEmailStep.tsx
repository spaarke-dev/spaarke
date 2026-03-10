/**
 * DocumentEmailStep.tsx
 * Document-upload-specific wrapper around the shared SendEmailStep component.
 *
 * Pre-fills email subject and body with uploaded document names and parent
 * entity context. Provides onSearchUsers callback that queries the Dataverse
 * systemuser table via OData.
 *
 * Layout delegates entirely to the shared SendEmailStep — this component
 * manages domain state and Dataverse integration only.
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - Document access through BFF API (SpeFileStore facade)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useState, useCallback, useMemo } from "react";
import { SendEmailStep } from "@spaarke/ui-components/components/EmailStep";
import type { ILookupItem } from "@spaarke/ui-components/components/EmailStep";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentEmailStepProps {
    /** Display names of uploaded files. */
    uploadedFileNames: string[];
    /** Display name of the parent entity (e.g., matter name). */
    parentEntityName: string;
    /** Dataverse logical name of the parent entity (e.g., "sprk_matter"). */
    parentEntityType: string;
    /** GUID of the parent entity record. */
    parentEntityId: string;
}

// ---------------------------------------------------------------------------
// Email template helpers
// ---------------------------------------------------------------------------

/**
 * Builds the default email subject line.
 * Example: "Documents uploaded - Anderson v. Smith"
 */
function buildDefaultSubject(parentEntityName: string): string {
    const entityLabel = parentEntityName || "document record";
    return `Documents uploaded - ${entityLabel}`;
}

/**
 * Builds the default email body with uploaded file names and parent entity context.
 */
function buildDefaultBody(
    uploadedFileNames: string[],
    parentEntityName: string,
    parentEntityType: string
): string {
    const entityLabel = parentEntityName || "the document record";
    const entityTypeLabel = formatEntityTypeLabel(parentEntityType);

    const fileList = uploadedFileNames.length > 0
        ? uploadedFileNames.map((name) => `  - ${name}`).join("\n")
        : "  (no files)";

    return [
        `The following documents have been uploaded to ${entityTypeLabel} "${entityLabel}":`,
        "",
        fileList,
        "",
        "Please review the uploaded documents at your earliest convenience.",
        "",
        "---",
        "This email was sent from the Spaarke Document Upload Wizard.",
    ].join("\n");
}

/**
 * Converts a Dataverse logical name like "sprk_matter" into a
 * human-readable label like "Matter".
 */
function formatEntityTypeLabel(entityType: string): string {
    if (!entityType) return "record";
    // Strip prefix (e.g., "sprk_") and capitalize
    const stripped = entityType.replace(/^[a-z]+_/, "");
    return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

// ---------------------------------------------------------------------------
// Xrm.WebApi: search systemuser
// ---------------------------------------------------------------------------

/**
 * Resolve Xrm.WebApi from the frame hierarchy.
 */
function resolveXrmWebApi(): { retrieveMultipleRecords: (entity: string, options: string) => Promise<{ entities: Record<string, unknown>[] }> } | null {
    const frames: Window[] = [window];
    try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
    try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

    for (const frame of frames) {
        try {
            /* eslint-disable @typescript-eslint/no-explicit-any */
            const xrm = (frame as any).Xrm;
            if (xrm?.WebApi?.retrieveMultipleRecords) {
                return xrm.WebApi;
            }
            /* eslint-enable @typescript-eslint/no-explicit-any */
        } catch {
            // Cross-origin frame — skip
        }
    }
    return null;
}

/**
 * Searches the Dataverse systemuser table via Xrm.WebApi for user lookup.
 * Uses Xrm.WebApi (authenticated automatically) instead of direct OData fetch.
 * Returns ILookupItem[] with id (systemuserid) and name ("FullName (email)").
 */
async function searchSystemUsers(query: string): Promise<ILookupItem[]> {
    if (!query || query.trim().length < 2) return [];

    const webApi = resolveXrmWebApi();
    if (!webApi) {
        console.error("[DocumentEmailStep] Xrm.WebApi not available for user search");
        return [];
    }

    const escaped = query.replace(/'/g, "''");
    const filter = `contains(fullname, '${escaped}') or contains(internalemailaddress, '${escaped}')`;
    const options = `?$select=systemuserid,fullname,internalemailaddress&$filter=${filter}&$top=10&$orderby=fullname asc`;

    try {
        const result = await webApi.retrieveMultipleRecords("systemuser", options);

        return result.entities.map((user) => ({
            id: user.systemuserid as string,
            name: user.internalemailaddress
                ? `${user.fullname} (${user.internalemailaddress})`
                : user.fullname as string,
        }));
    } catch (err) {
        console.error("[DocumentEmailStep] systemuser search failed:", err);
        return [];
    }
}

// ---------------------------------------------------------------------------
// DocumentEmailStep (exported)
// ---------------------------------------------------------------------------

export function DocumentEmailStep({
    uploadedFileNames,
    parentEntityName,
    parentEntityType,
    parentEntityId,
}: IDocumentEmailStepProps): JSX.Element {
    // Memoize default values so they only recompute when inputs change
    const defaultSubject = useMemo(
        () => buildDefaultSubject(parentEntityName),
        [parentEntityName]
    );
    const defaultBody = useMemo(
        () => buildDefaultBody(uploadedFileNames, parentEntityName, parentEntityType),
        [uploadedFileNames, parentEntityName, parentEntityType]
    );

    // Controlled email form state
    const [emailTo, setEmailTo] = useState("");
    const [emailSubject, setEmailSubject] = useState(defaultSubject);
    const [emailBody, setEmailBody] = useState(defaultBody);

    // User search callback (stable reference via useCallback)
    const handleSearchUsers = useCallback(
        (query: string): Promise<ILookupItem[]> => {
            return searchSystemUsers(query);
        },
        []
    );

    return (
        <SendEmailStep
            title="Send Email"
            subtitle="Share the uploaded documents with a colleague via email."
            emailTo={emailTo}
            onEmailToChange={setEmailTo}
            emailSubject={emailSubject}
            onEmailSubjectChange={setEmailSubject}
            emailBody={emailBody}
            onEmailBodyChange={setEmailBody}
            onSearchUsers={handleSearchUsers}
            regardingEntityType={parentEntityType}
            regardingId={parentEntityId}
            infoNote="This email will be saved as a draft activity on the parent record."
            messageRows={12}
        />
    );
}

DocumentEmailStep.displayName = "DocumentEmailStep";
