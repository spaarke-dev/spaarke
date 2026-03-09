/**
 * DocumentDetailsStep.tsx
 * Step 2 of the Create Document wizard — form with document metadata fields.
 *
 * Fields:
 *   - Name (Input, required)
 *   - Document Type (Combobox)
 *   - Description (Textarea)
 *
 * canAdvance: requires name to be non-empty.
 *
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Input,
    Combobox,
    Option,
    Textarea,
    Label,
    Field,
} from "@fluentui/react-components";
import type { IDocumentFormValues, DocumentType } from "../types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentDetailsStepProps {
    /** Current form values. */
    formValues: IDocumentFormValues;
    /** Called when any form field changes. */
    onFormChange: (values: IDocumentFormValues) => void;
}

// ---------------------------------------------------------------------------
// Document type options
// ---------------------------------------------------------------------------

const DOCUMENT_TYPE_OPTIONS: { value: DocumentType; label: string }[] = [
    { value: "contract", label: "Contract" },
    { value: "agreement", label: "Agreement" },
    { value: "memo", label: "Memo" },
    { value: "brief", label: "Brief" },
    { value: "correspondence", label: "Correspondence" },
    { value: "report", label: "Report" },
    { value: "other", label: "Other" },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalL,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    formFields: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        maxWidth: "500px",
    },
});

// ---------------------------------------------------------------------------
// DocumentDetailsStep component
// ---------------------------------------------------------------------------

export function DocumentDetailsStep({
    formValues,
    onFormChange,
}: IDocumentDetailsStepProps): JSX.Element {
    const styles = useStyles();

    const handleNameChange = useCallback(
        (_e: unknown, data: { value: string }) => {
            onFormChange({ ...formValues, name: data.value });
        },
        [formValues, onFormChange],
    );

    const handleDocumentTypeChange = useCallback(
        (_e: unknown, data: { selectedOptions: string[] }) => {
            const selected = (data.selectedOptions[0] ?? "") as DocumentType | "";
            onFormChange({ ...formValues, documentType: selected });
        },
        [formValues, onFormChange],
    );

    const handleDescriptionChange = useCallback(
        (_e: unknown, data: { value: string }) => {
            onFormChange({ ...formValues, description: data.value });
        },
        [formValues, onFormChange],
    );

    return (
        <div className={styles.root}>
            <div>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Document details
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Provide a name and optional metadata for the document record.
                </Text>
            </div>

            <div className={styles.formFields}>
                <Field
                    label={<Label required>Document name</Label>}
                    validationState={formValues.name.trim() ? "none" : "error"}
                    validationMessage={formValues.name.trim() ? undefined : "Name is required"}
                >
                    <Input
                        value={formValues.name}
                        onChange={handleNameChange}
                        placeholder="Enter document name"
                        data-testid="document-name-input"
                    />
                </Field>

                <Field label="Document type">
                    <Combobox
                        value={
                            DOCUMENT_TYPE_OPTIONS.find((o) => o.value === formValues.documentType)
                                ?.label ?? ""
                        }
                        onOptionSelect={handleDocumentTypeChange}
                        placeholder="Select document type"
                        data-testid="document-type-combobox"
                    >
                        {DOCUMENT_TYPE_OPTIONS.map((opt) => (
                            <Option key={opt.value} value={opt.value}>
                                {opt.label}
                            </Option>
                        ))}
                    </Combobox>
                </Field>

                <Field label="Description">
                    <Textarea
                        value={formValues.description}
                        onChange={handleDescriptionChange}
                        placeholder="Optional description"
                        resize="vertical"
                        rows={3}
                        data-testid="document-description-textarea"
                    />
                </Field>
            </div>
        </div>
    );
}
