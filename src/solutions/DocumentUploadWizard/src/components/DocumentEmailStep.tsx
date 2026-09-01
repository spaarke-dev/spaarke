/**
 * DocumentEmailStep.tsx
 * Document-upload "Send Email" step — embeds the canonical shared EmailComposer
 * INLINE (the standard Spaarke compose form: From / To / Cc / Bcc, rich-text body,
 * Attachments, Related-to), with the uploaded documents pre-attached and the parent
 * record as the association.
 *
 * Design (owner decisions, 2026-09-01):
 *   - INLINE mount → the composer renders its own native Send button (top "From:" row,
 *     Outlook-style) and NO Save Draft / Cancel bar (that chrome only exists in the
 *     dialog/page mounts). No modal — this stays a wizard step.
 *   - `sendMode="sharedMailbox"` fixes the send mode + hides the From switcher, so Send
 *     is a plain button (no "From" dropdown to fuss with).
 *   - "Don't send" = just finish the wizard (the wizard's own Finish/Back/Cancel handle it).
 *
 * Supersedes the earlier basic `EmailStep/SendEmailStep` + custom Send button (the
 * dead-form fix). The composer owns all send mechanics (`sendCommunication`), so there
 * is no hand-rolled send here.
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-012  - Shared, context-agnostic components (auth + Xrm handlers injected)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useState, useCallback, useMemo } from "react";
import { MessageBar, MessageBarBody, Text, makeStyles, tokens } from "@fluentui/react-components";
import { CheckmarkCircleRegular } from "@fluentui/react-icons";
import { EmailComposer, createXrmEmailComposeHandlers } from "@spaarke/ui-components/components/EmailComposer";
import type { IWizardContext } from "@spaarke/ui-components/components/EmailComposer";
import type { ILookupItem } from "@spaarke/ui-components/types/LookupTypes";
import type { ICommunicationAssociation } from "@spaarke/ui-components/services/communicationApi";
import type { AuthenticatedFetchFn } from "@spaarke/ui-components/services/EntityCreationService";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentEmailStepProps {
    /** Display names of uploaded files (for the default body). */
    uploadedFileNames: string[];
    /** Display name of the parent entity (e.g., matter name). */
    parentEntityName: string;
    /** Dataverse logical name of the parent entity (e.g., "sprk_matter"). */
    parentEntityType: string;
    /** GUID of the parent entity record. */
    parentEntityId: string;

    // ── Send wiring (supplied at the dynamic-step injection point) ──────────
    // Optional so the display-only props the dialog memoizes still satisfy the type;
    // NextStepsStep supplies these when it renders the step.

    /** Uploaded documents — surfaced as the composer's `'wizard'` attachment source. */
    uploadedFiles?: IWizardContext["uploadedFiles"];
    /** Authenticated fetch (Bearer-attached) for the composer's send + BFF calls. */
    authenticatedFetch?: AuthenticatedFetchFn;
    /** BFF base URL (no `/api` suffix). */
    bffBaseUrl?: string;
}

// ---------------------------------------------------------------------------
// Email template helpers
// ---------------------------------------------------------------------------

/** Builds the default email subject line. */
function buildDefaultSubject(parentEntityName: string): string {
    const entityLabel = parentEntityName || "document record";
    return `Documents uploaded - ${entityLabel}`;
}

/** Builds the default email body with uploaded file names and parent entity context. */
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

/** Converts a Dataverse logical name like "sprk_matter" into a label like "Matter". */
function formatEntityTypeLabel(entityType: string): string {
    if (!entityType) return "record";
    const stripped = entityType.replace(/^[a-z]+_/, "");
    return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

// ---------------------------------------------------------------------------
// Xrm.WebApi: recipient typeahead (systemuser search)
// ---------------------------------------------------------------------------

/** Resolve Xrm.WebApi from the frame hierarchy. */
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
 * Recipient typeahead: searches the Dataverse systemuser table via Xrm.WebApi and
 * returns ILookupItem[] for the composer's `onSearchRecipients`. The composer's
 * advanced people picker (`onLookupRecipients`, from the Xrm factory) additionally
 * covers contacts.
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
                : (user.fullname as string),
        }));
    } catch (err) {
        console.error("[DocumentEmailStep] systemuser search failed:", err);
        return [];
    }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        flexGrow: 1,
        // The composer's BodyEditor flex-grows and owns the scroll region; give it room.
        minHeight: "520px",
    },
    sentBlock: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalS,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
        color: tokens.colorPaletteGreenForeground1,
    },
    unavailable: {
        color: tokens.colorNeutralForeground3,
        paddingTop: tokens.spacingVerticalL,
    },
    errorBar: {
        marginBottom: tokens.spacingVerticalS,
    },
});

// ---------------------------------------------------------------------------
// DocumentEmailStep (exported)
// ---------------------------------------------------------------------------

export function DocumentEmailStep({
    uploadedFileNames,
    parentEntityName,
    parentEntityType,
    parentEntityId,
    uploadedFiles,
    authenticatedFetch,
    bffBaseUrl,
}: IDocumentEmailStepProps): JSX.Element {
    const styles = useStyles();

    const defaultSubject = useMemo(
        () => buildDefaultSubject(parentEntityName),
        [parentEntityName]
    );
    const defaultBody = useMemo(
        () => buildDefaultBody(uploadedFileNames, parentEntityName, parentEntityType),
        [uploadedFileNames, parentEntityName, parentEntityType]
    );

    const [isSent, setIsSent] = useState(false);
    const [sendError, setSendError] = useState<string | null>(null);

    const handleSearchUsers = useCallback(
        (query: string): Promise<ILookupItem[]> => searchSystemUsers(query),
        []
    );

    // Xrm-backed advanced lookups (people picker, "Related to" record lookup, local-file
    // upload-to-Document, template picker, AI draft, share-link). Context-agnostic engine
    // (ADR-012) — the Code Page injects these via the shared factory.
    const composeHandlers = useMemo(
        () => createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl }),
        [authenticatedFetch, bffBaseUrl]
    );

    // Associate the sent email with the parent record (ADR-024 regarding family).
    const associations = useMemo<ICommunicationAssociation[] | undefined>(() => {
        if (parentEntityType && parentEntityId) {
            return [{ entityType: parentEntityType, entityId: parentEntityId, entityName: parentEntityName }];
        }
        return undefined;
    }, [parentEntityType, parentEntityId, parentEntityName]);

    // Uploaded docs → the composer's `'wizard'` attachment source (auto-included in
    // `attachmentSources` when `wizardContext` is present).
    const wizardContext = useMemo<IWizardContext | undefined>(
        () => (uploadedFiles && uploadedFiles.length > 0 ? { uploadedFiles } : undefined),
        [uploadedFiles]
    );

    // Defensive: without an authenticated fetch the composer cannot send (AI features off,
    // or a unit-render without the seam). Show a note rather than a broken form.
    if (!authenticatedFetch) {
        return (
            <div className={styles.root}>
                <Text className={styles.unavailable}>Email is unavailable in this context.</Text>
            </div>
        );
    }

    if (isSent) {
        return (
            <div className={styles.sentBlock}>
                <CheckmarkCircleRegular fontSize={48} />
                <Text size={500} weight="semibold">Email sent</Text>
                <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
                    You can finish the wizard.
                </Text>
            </div>
        );
    }

    return (
        <div className={styles.root}>
            {sendError && (
                <MessageBar intent="error" className={styles.errorBar}>
                    <MessageBarBody>{sendError}</MessageBarBody>
                </MessageBar>
            )}
            <EmailComposer
                mode="compose"
                mount="inline"
                authenticatedFetch={authenticatedFetch}
                bffBaseUrl={bffBaseUrl}
                initialSubject={defaultSubject}
                initialBody={defaultBody}
                initialBodyFormat="PlainText"
                associations={associations}
                wizardContext={wizardContext}
                // Fixed to the shared mailbox → plain Send button, no From switcher.
                sendMode="sharedMailbox"
                onSearchRecipients={handleSearchUsers}
                onLookupRecipients={composeHandlers.onLookupRecipients}
                recordLookupCatalog={composeHandlers.recordLookupCatalog}
                onLookupRecord={composeHandlers.onLookupRecord}
                onAddRelationship={composeHandlers.onAddRelationship}
                onUploadLocalAttachment={composeHandlers.onUploadLocalAttachment}
                onResolveShareLink={composeHandlers.onResolveShareLink}
                onListEmailTemplates={composeHandlers.onListEmailTemplates}
                onRenderEmailTemplate={composeHandlers.onRenderEmailTemplate}
                onDraftWithAi={composeHandlers.onDraftWithAi}
                onSent={() => setIsSent(true)}
                onError={(err) => setSendError(err?.detail || "Failed to send email.")}
            />
        </div>
    );
}

DocumentEmailStep.displayName = "DocumentEmailStep";
