/**
 * ProfileEditor Component
 *
 * Provides UI for editing Field Mapping Profile settings:
 * - Source entity selection dropdown
 * - Target entity selection dropdown
 * - Mapping direction selector
 * - Sync mode configuration
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with design tokens (no hard-coded colors)
 * - ADR-022: React 16 APIs
 */

import * as React from "react";
import {
    Dropdown,
    Option,
    Input,
    Label,
    Text,
    Switch,
    makeStyles,
    tokens,
    Field,
    Card,
    CardHeader,
    Tooltip,
    InfoLabel,
} from "@fluentui/react-components";
import { Info16Regular } from "@fluentui/react-icons";
import {
    IFieldMappingProfile,
    SyncMode,
    SyncModeLabels,
    MappingDirection,
    MappingDirectionLabels,
} from "../types/FieldMappingTypes";

/**
 * Entity configuration for supported entity types
 */
export interface EntityConfig {
    logicalName: string;
    displayName: string;
}

/**
 * Supported entity types for field mapping
 */
export const SUPPORTED_ENTITIES: EntityConfig[] = [
    { logicalName: "sprk_matter", displayName: "Matter" },
    { logicalName: "sprk_project", displayName: "Project" },
    { logicalName: "sprk_invoice", displayName: "Invoice" },
    { logicalName: "sprk_analysis", displayName: "Analysis" },
    { logicalName: "account", displayName: "Account" },
    { logicalName: "contact", displayName: "Contact" },
    { logicalName: "sprk_workassignment", displayName: "Work Assignment" },
    { logicalName: "sprk_budget", displayName: "Budget" },
    { logicalName: "sprk_event", displayName: "Event" },
];

interface ProfileEditorProps {
    /** Current profile data */
    profile: Partial<IFieldMappingProfile>;
    /** Callback when profile changes */
    onProfileChange: (profile: Partial<IFieldMappingProfile>) => void;
    /** Whether the form is disabled */
    disabled?: boolean;
    /** Whether this is creating a new profile */
    isNew?: boolean;
}

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    card: {
        padding: tokens.spacingHorizontalM,
    },
    formGrid: {
        display: "grid",
        gridTemplateColumns: "1fr 1fr",
        gap: tokens.spacingHorizontalM,
        "@media (max-width: 600px)": {
            gridTemplateColumns: "1fr",
        },
    },
    fullWidth: {
        gridColumn: "1 / -1",
    },
    fieldGroup: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    switchRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    infoIcon: {
        color: tokens.colorNeutralForeground3,
    },
    sectionTitle: {
        marginBottom: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground1,
    },
});

export const ProfileEditor: React.FC<ProfileEditorProps> = ({
    profile,
    onProfileChange,
    disabled = false,
    isNew = false,
}) => {
    const styles = useStyles();

    const handleNameChange = React.useCallback(
        (value: string) => {
            onProfileChange({ ...profile, name: value });
        },
        [profile, onProfileChange]
    );

    const handleSourceEntityChange = React.useCallback(
        (logicalName: string) => {
            onProfileChange({ ...profile, sourceEntity: logicalName });
        },
        [profile, onProfileChange]
    );

    const handleTargetEntityChange = React.useCallback(
        (logicalName: string) => {
            onProfileChange({ ...profile, targetEntity: logicalName });
        },
        [profile, onProfileChange]
    );

    const handleDirectionChange = React.useCallback(
        (direction: MappingDirection) => {
            onProfileChange({ ...profile, mappingDirection: direction });
        },
        [profile, onProfileChange]
    );

    const handleSyncModeChange = React.useCallback(
        (syncMode: SyncMode) => {
            onProfileChange({ ...profile, syncMode });
        },
        [profile, onProfileChange]
    );

    const handleActiveChange = React.useCallback(
        (checked: boolean) => {
            onProfileChange({ ...profile, isActive: checked });
        },
        [profile, onProfileChange]
    );

    const handleDescriptionChange = React.useCallback(
        (value: string) => {
            onProfileChange({ ...profile, description: value });
        },
        [profile, onProfileChange]
    );

    const getEntityDisplayName = (logicalName: string): string => {
        return SUPPORTED_ENTITIES.find((e) => e.logicalName === logicalName)?.displayName || logicalName;
    };

    return (
        <Card className={styles.card}>
            <CardHeader
                header={<Text weight="semibold" size={400}>Profile Settings</Text>}
            />

            <div className={styles.container}>
                <div className={styles.formGrid}>
                    {/* Profile Name */}
                    <Field
                        label={
                            <InfoLabel
                                info="A descriptive name for this field mapping profile"
                            >
                                Profile Name
                            </InfoLabel>
                        }
                        required
                    >
                        <Input
                            value={profile.name || ""}
                            onChange={(_, data) => handleNameChange(data.value || "")}
                            placeholder="e.g., Matter to Event Mapping"
                            disabled={disabled}
                        />
                    </Field>

                    {/* Active Toggle */}
                    <Field label="Status">
                        <div className={styles.switchRow}>
                            <Switch
                                checked={profile.isActive ?? true}
                                onChange={(_, data) => handleActiveChange(data.checked)}
                                disabled={disabled}
                            />
                            <Text>{profile.isActive !== false ? "Active" : "Inactive"}</Text>
                        </div>
                    </Field>

                    {/* Source Entity */}
                    <Field
                        label={
                            <InfoLabel
                                info="The parent entity whose field values will be copied"
                            >
                                Source Entity
                            </InfoLabel>
                        }
                        required
                    >
                        <Dropdown
                            value={getEntityDisplayName(profile.sourceEntity || "")}
                            selectedOptions={profile.sourceEntity ? [profile.sourceEntity] : []}
                            onOptionSelect={(_, data) => {
                                if (data.optionValue) {
                                    handleSourceEntityChange(data.optionValue);
                                }
                            }}
                            disabled={disabled || !isNew}
                            placeholder="Select source entity"
                        >
                            {SUPPORTED_ENTITIES.map((entity) => (
                                <Option key={entity.logicalName} value={entity.logicalName}>
                                    {entity.displayName}
                                </Option>
                            ))}
                        </Dropdown>
                    </Field>

                    {/* Target Entity */}
                    <Field
                        label={
                            <InfoLabel
                                info="The child entity that will receive the copied field values"
                            >
                                Target Entity
                            </InfoLabel>
                        }
                        required
                    >
                        <Dropdown
                            value={getEntityDisplayName(profile.targetEntity || "")}
                            selectedOptions={profile.targetEntity ? [profile.targetEntity] : []}
                            onOptionSelect={(_, data) => {
                                if (data.optionValue) {
                                    handleTargetEntityChange(data.optionValue);
                                }
                            }}
                            disabled={disabled || !isNew}
                            placeholder="Select target entity"
                        >
                            {SUPPORTED_ENTITIES.map((entity) => (
                                <Option key={entity.logicalName} value={entity.logicalName}>
                                    {entity.displayName}
                                </Option>
                            ))}
                        </Dropdown>
                    </Field>

                    {/* Mapping Direction */}
                    <Field
                        label={
                            <InfoLabel
                                info="Direction of field value flow between source and target"
                            >
                                Mapping Direction
                            </InfoLabel>
                        }
                    >
                        <Dropdown
                            value={MappingDirectionLabels[profile.mappingDirection ?? MappingDirection.ParentToChild]}
                            selectedOptions={[(profile.mappingDirection ?? MappingDirection.ParentToChild).toString()]}
                            onOptionSelect={(_, data) => {
                                if (data.optionValue !== undefined) {
                                    handleDirectionChange(parseInt(data.optionValue, 10) as MappingDirection);
                                }
                            }}
                            disabled={disabled}
                        >
                            {Object.entries(MappingDirectionLabels).map(([value, label]) => (
                                <Option key={value} value={value}>
                                    {label}
                                </Option>
                            ))}
                        </Dropdown>
                    </Field>

                    {/* Sync Mode */}
                    <Field
                        label={
                            <InfoLabel
                                info={`One-time: Apply at creation only. Manual Refresh: User can re-apply mappings from child form.`}
                            >
                                Sync Mode
                            </InfoLabel>
                        }
                    >
                        <Dropdown
                            value={SyncModeLabels[profile.syncMode ?? SyncMode.OneTime]}
                            selectedOptions={[(profile.syncMode ?? SyncMode.OneTime).toString()]}
                            onOptionSelect={(_, data) => {
                                if (data.optionValue !== undefined) {
                                    handleSyncModeChange(parseInt(data.optionValue, 10) as SyncMode);
                                }
                            }}
                            disabled={disabled}
                        >
                            {Object.entries(SyncModeLabels).map(([value, label]) => (
                                <Option key={value} value={value}>
                                    {label}
                                </Option>
                            ))}
                        </Dropdown>
                    </Field>

                    {/* Description */}
                    <Field
                        label="Description"
                        className={styles.fullWidth}
                    >
                        <Input
                            value={profile.description || ""}
                            onChange={(_, data) => handleDescriptionChange(data.value || "")}
                            placeholder="Optional notes about this mapping profile"
                            disabled={disabled}
                        />
                    </Field>
                </div>
            </div>
        </Card>
    );
};

export default ProfileEditor;
