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

import { useState, useCallback, useRef, useMemo } from "react";
import { SendEmailStep } from "@spaarke/ui-components/components/EmailStep";
import type { ILookupItem } from "@spaarke/ui-components/components/EmailStep";
import { resolveDataverseUrl, createDataverseTokenProvider } from "../services/codePageTokenProvider";

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
// Dataverse OData: search systemuser
// ---------------------------------------------------------------------------

/**
 * Searches the Dataverse systemuser table via OData for user lookup.
 * Returns ILookupItem[] with id (systemuserid) and name ("FullName (email)").
 */
async function searchSystemUsers(
    query: string,
    getToken: () => Promise<string>,
    dataverseUrl: string
): Promise<ILookupItem[]> {
    if (!query || query.trim().length < 2) return [];

    const token = await getToken();
    const filter = [
        `contains(fullname, '${escapeODataString(query)}')`,
        `or contains(internalemailaddress, '${escapeODataString(query)}')`,
    ].join(" ");

    const url =
        `${dataverseUrl}/api/data/v9.2/systemusers` +
        `?$select=systemuserid,fullname,internalemailaddress` +
        `&$filter=${encodeURIComponent(filter)}` +
        `&$top=10` +
        `&$orderby=fullname asc`;

    const response = await fetch(url, {
        headers: {
            Authorization: `Bearer ${token}`,
            Accept: "application/json",
            "OData-MaxVersion": "4.0",
            "OData-Version": "4.0",
        },
    });

    if (!response.ok) {
        console.error(
            "[DocumentEmailStep] systemuser search failed:",
            response.status,
            response.statusText
        );
        return [];
    }

    interface SystemUserRecord {
        systemuserid: string;
        fullname: string;
        internalemailaddress?: string;
    }

    const data: { value: SystemUserRecord[] } = await response.json();

    return data.value.map((user) => ({
        id: user.systemuserid,
        name: user.internalemailaddress
            ? `${user.fullname} (${user.internalemailaddress})`
            : user.fullname,
    }));
}

/**
 * Escapes single quotes for OData string literals.
 */
function escapeODataString(value: string): string {
    return value.replace(/'/g, "''");
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
    // Resolve Dataverse URL and token provider once
    const dataverseUrl = useRef(resolveDataverseUrl()).current;
    const getToken = useRef(createDataverseTokenProvider()).current;

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
            return searchSystemUsers(query, getToken, dataverseUrl);
        },
        [getToken, dataverseUrl]
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
