/**
 * DocumentEmailStep.tsx
 * Document-upload-specific wrapper around the shared SendEmailStep component.
 *
 * Pre-fills email subject and body with uploaded document names and parent
 * entity context, and — when the send dependencies are supplied at the dynamic
 * "Send Email" step injection point (NextStepsStep) — actually SENDS the email
 * via the shared `POST /api/communications/send` service, attaching the
 * uploaded documents.
 *
 * The send action lives on an explicit "Send Email" button inside the step,
 * mirroring the sibling dynamic next-steps ("Work on Analysis" → Create
 * Analysis, "Find Similar" → Find Similar Documents) and the standalone
 * DocumentEmailWizard.handleFinish send path. Before GitHub #919's sibling UAT
 * this step was a dead form — it rendered To/Subject/Body but was never wired
 * to send (the composed values were trapped in local state).
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - Document access through BFF API (SpeFileStore facade)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useState, useCallback, useMemo } from "react";
import {
    Button,
    MessageBar,
    MessageBarBody,
    Spinner,
    Text,
    makeStyles,
    tokens,
} from "@fluentui/react-components";
import { CheckmarkCircleRegular, MailRegular } from "@fluentui/react-icons";
import { SendEmailStep, extractEmailFromUserName } from "@spaarke/ui-components/components/EmailStep";
import type { ILookupItem } from "@spaarke/ui-components/components/EmailStep";
import { sendCommunication } from "@spaarke/ui-components/services/communicationApi";
import type { ICommunicationAssociation } from "@spaarke/ui-components/services/communicationApi";
import type { AuthenticatedFetchFn } from "@spaarke/ui-components/services/EntityCreationService";

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

    // ── Send wiring (supplied at the dynamic-step injection point) ──────────
    // These are optional so the display-only props the dialog memoizes still
    // satisfy the type; NextStepsStep supplies them when it renders the step.

    /** `sprk_document` GUIDs of the uploaded files, attached to the email. */
    attachmentDocumentIds?: string[];
    /**
     * Authenticated fetch (Bearer-attached) for `POST /api/communications/send`.
     * When omitted the step renders display-only (no Send button) — a defensive
     * fallback; in the wizard it is always supplied.
     */
    authenticatedFetch?: AuthenticatedFetchFn;
    /** BFF base URL for the send call. */
    bffBaseUrl?: string;
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
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalL,
    },
    footer: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    sendRow: {
        display: "flex",
        justifyContent: "flex-end",
    },
    successBlock: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        color: tokens.colorPaletteGreenForeground1,
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
    attachmentDocumentIds,
    authenticatedFetch,
    bffBaseUrl,
}: IDocumentEmailStepProps): JSX.Element {
    const styles = useStyles();

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

    // Send state
    const [isSending, setIsSending] = useState(false);
    const [sendError, setSendError] = useState<string | null>(null);
    const [isSent, setIsSent] = useState(false);

    // User search callback (stable reference via useCallback)
    const handleSearchUsers = useCallback(
        (query: string): Promise<ILookupItem[]> => {
            return searchSystemUsers(query);
        },
        []
    );

    // Send handler — mirrors DocumentEmailWizard.handleFinish: parse recipients,
    // validate, build SendCommunicationOptions (attaching the uploaded documents
    // and associating to the parent), and POST /api/communications/send.
    const handleSend = useCallback(async () => {
        setSendError(null);

        const recipients = emailTo
            .split(/[;,]/)
            .map((s) => s.trim())
            .map((s) => extractEmailFromUserName(s) || s)
            .filter(Boolean);

        if (recipients.length === 0) {
            setSendError("At least one recipient is required.");
            return;
        }
        if (!emailSubject.trim()) {
            setSendError("Subject is required.");
            return;
        }
        if (!emailBody.trim()) {
            setSendError("Message body is required.");
            return;
        }
        if (!authenticatedFetch) {
            setSendError("Email sending is unavailable in this context.");
            return;
        }

        const associations: ICommunicationAssociation[] = [];
        if (parentEntityType && parentEntityId) {
            associations.push({ entityType: parentEntityType, entityId: parentEntityId });
        }

        setIsSending(true);
        try {
            await sendCommunication(
                {
                    to: recipients,
                    subject: emailSubject.trim(),
                    body: emailBody,
                    bodyFormat: "text",
                    attachmentDocumentIds:
                        attachmentDocumentIds && attachmentDocumentIds.length > 0
                            ? attachmentDocumentIds
                            : undefined,
                    associations: associations.length > 0 ? associations : undefined,
                    sendMode: "sharedMailbox",
                },
                { authenticatedFetch, bffBaseUrl }
            );
            setIsSent(true);
        } catch (err) {
            setSendError(err instanceof Error ? err.message : "Failed to send email.");
        } finally {
            setIsSending(false);
        }
    }, [emailTo, emailSubject, emailBody, authenticatedFetch, bffBaseUrl, attachmentDocumentIds, parentEntityType, parentEntityId]);

    const canSend = !!authenticatedFetch;
    const attachmentCount = attachmentDocumentIds?.length ?? 0;

    return (
        <div className={styles.root}>
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
                infoNote={
                    attachmentCount > 0
                        ? `The email is sent via the Spaarke shared mailbox with ${attachmentCount} attached document${attachmentCount === 1 ? "" : "s"}, and saved as a Communication${parentEntityType ? " on the parent record" : ""}.`
                        : `The email is sent via the Spaarke shared mailbox and saved as a Communication${parentEntityType ? " on the parent record" : ""}.`
                }
                messageRows={12}
            />

            {canSend && (
                <div className={styles.footer}>
                    {sendError && (
                        <MessageBar intent="error">
                            <MessageBarBody>{sendError}</MessageBarBody>
                        </MessageBar>
                    )}
                    {isSent ? (
                        <div className={styles.successBlock}>
                            <CheckmarkCircleRegular fontSize={20} />
                            <Text size={300} weight="semibold">Email sent.</Text>
                        </div>
                    ) : (
                        <div className={styles.sendRow}>
                            <Button
                                appearance="primary"
                                icon={isSending ? <Spinner size="tiny" /> : <MailRegular />}
                                onClick={handleSend}
                                disabled={isSending || emailTo.trim() === ""}
                            >
                                {isSending ? "Sending…" : "Send Email"}
                            </Button>
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

DocumentEmailStep.displayName = "DocumentEmailStep";
