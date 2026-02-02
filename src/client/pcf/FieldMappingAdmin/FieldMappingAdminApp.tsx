/**
 * FieldMappingAdmin React App Component
 *
 * Admin interface for managing field mapping profiles and rules.
 * Provides UI for:
 * - Editing profile settings (source/target entity, sync mode)
 * - Managing mapping rules (add, edit, delete)
 * - Real-time type compatibility validation
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with dark mode support (uses design tokens)
 * - ADR-022: React 16 APIs
 * - ADR-012: Uses patterns from @spaarke/ui-components
 *
 * @version 1.1.0
 */

import * as React from "react";
import {
    Button,
    Text,
    Spinner,
    makeStyles,
    tokens,
    MessageBar,
    MessageBarBody,
    MessageBarActions,
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Toast,
    ToastTitle,
    ToastBody,
    useToastController,
    Toaster,
    useId,
} from "@fluentui/react-components";
import {
    Save20Regular,
    ArrowSync20Regular,
    Dismiss20Regular,
} from "@fluentui/react-icons";
import { IInputs } from "./generated/ManifestTypes";
import { ProfileEditor, SUPPORTED_ENTITIES, EntityFieldInfo, RulesList, RuleEditor } from "./components";
import {
    IFieldMappingProfile,
    IFieldMappingRule,
    FieldType,
    CompatibilityMode,
    SyncMode,
    MappingDirection,
    CompatibilityLevel,
    STRICT_TYPE_COMPATIBILITY,
} from "./types/FieldMappingTypes";

interface FieldMappingAdminAppProps {
    context: ComponentFramework.Context<IInputs>;
    profileId: string;
    apiBaseUrl: string;
    version: string;
}

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingHorizontalM,
        height: "100%",
        overflow: "hidden",
    },
    header: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    toolbar: {
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        paddingBottom: tokens.spacingVerticalS,
    },
    content: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        flex: 1,
        overflow: "auto",
    },
    footer: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        paddingTop: tokens.spacingVerticalS,
        marginTop: "auto",
    },
    versionText: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4,
    },
    statusText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    loadingContainer: {
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        height: "200px",
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: tokens.spacingVerticalXXL,
        gap: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground3,
    },
});

/**
 * Validate type compatibility between source and target field types.
 */
const validateTypeCompatibility = (
    sourceType: FieldType,
    targetType: FieldType
): CompatibilityLevel => {
    // Exact match is always compatible
    if (sourceType === targetType) {
        return CompatibilityLevel.Exact;
    }

    // Check strict compatibility matrix
    const compatibleTypes = STRICT_TYPE_COMPATIBILITY[sourceType] ?? [];

    if (compatibleTypes.includes(targetType)) {
        if (sourceType === FieldType.Text && targetType === FieldType.Memo) {
            return CompatibilityLevel.Exact;
        }
        return CompatibilityLevel.SafeConversion;
    }

    return CompatibilityLevel.Incompatible;
};

/**
 * Map Dataverse field type to our FieldType enum
 */
const mapAttributeType = (attributeType: string): FieldType => {
    switch (attributeType?.toLowerCase()) {
        case "lookup":
        case "customer":
        case "owner":
            return FieldType.Lookup;
        case "string":
        case "uniqueidentifier":
            return FieldType.Text;
        case "memo":
            return FieldType.Memo;
        case "picklist":
        case "state":
        case "status":
            return FieldType.OptionSet;
        case "integer":
        case "bigint":
        case "decimal":
        case "double":
        case "money":
            return FieldType.Number;
        case "datetime":
            return FieldType.DateTime;
        case "boolean":
            return FieldType.Boolean;
        default:
            return FieldType.Text;
    }
};

export const FieldMappingAdminApp: React.FC<FieldMappingAdminAppProps> = ({
    context,
    profileId,
    apiBaseUrl,
    version,
}) => {
    const styles = useStyles();
    const toasterId = useId("toaster");
    const { dispatchToast } = useToastController(toasterId);

    // State
    const [isLoading, setIsLoading] = React.useState(false);
    const [isSaving, setIsSaving] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);
    const [hasChanges, setHasChanges] = React.useState(false);

    // Profile state
    const [profile, setProfile] = React.useState<Partial<IFieldMappingProfile>>({
        id: profileId || `new-${Date.now()}`,
        name: "",
        sourceEntity: "",
        targetEntity: "",
        mappingDirection: MappingDirection.ParentToChild,
        syncMode: SyncMode.OneTime,
        isActive: true,
        description: "",
    });

    // Rules state
    const [rules, setRules] = React.useState<IFieldMappingRule[]>([]);
    const [deletedRuleIds, setDeletedRuleIds] = React.useState<string[]>([]);

    // Rule editor state
    const [editingRule, setEditingRule] = React.useState<IFieldMappingRule | null>(null);
    const [isRuleEditorOpen, setIsRuleEditorOpen] = React.useState(false);

    // Entity fields state
    const [sourceFields, setSourceFields] = React.useState<EntityFieldInfo[]>([]);
    const [targetFields, setTargetFields] = React.useState<EntityFieldInfo[]>([]);
    const [isLoadingFields, setIsLoadingFields] = React.useState(false);

    // Compatibility results for rules
    const compatibilityResults = React.useMemo(() => {
        const results = new Map<string, CompatibilityLevel>();
        rules.forEach((rule) => {
            results.set(rule.id, validateTypeCompatibility(rule.sourceFieldType, rule.targetFieldType));
        });
        return results;
    }, [rules]);

    // Load profile when profileId changes
    React.useEffect(() => {
        if (profileId) {
            loadProfile();
        }
    }, [profileId]);

    // Load entity fields when source/target entity changes
    React.useEffect(() => {
        if (profile.sourceEntity) {
            loadEntityFields(profile.sourceEntity, "source");
        }
    }, [profile.sourceEntity]);

    React.useEffect(() => {
        if (profile.targetEntity) {
            loadEntityFields(profile.targetEntity, "target");
        }
    }, [profile.targetEntity]);

    /**
     * Load profile and rules from Dataverse
     */
    const loadProfile = async () => {
        setIsLoading(true);
        setError(null);

        try {
            // Load profile
            const profileResult = await context.webAPI.retrieveRecord(
                "sprk_fieldmappingprofile",
                profileId,
                "?$select=sprk_fieldmappingprofileid,sprk_name,sprk_sourceentity,sprk_targetentity,sprk_mappingdirection,sprk_syncmode,sprk_isactive,sprk_description"
            );

            setProfile({
                id: profileResult.sprk_fieldmappingprofileid,
                name: profileResult.sprk_name || "",
                sourceEntity: profileResult.sprk_sourceentity || "",
                targetEntity: profileResult.sprk_targetentity || "",
                mappingDirection: profileResult.sprk_mappingdirection ?? MappingDirection.ParentToChild,
                syncMode: profileResult.sprk_syncmode ?? SyncMode.OneTime,
                isActive: profileResult.sprk_isactive ?? true,
                description: profileResult.sprk_description || "",
            });

            // Load rules
            await loadRules();
        } catch (err) {
            console.error("[FieldMappingAdmin] Error loading profile:", err);
            setError(err instanceof Error ? err.message : "Failed to load profile");
        } finally {
            setIsLoading(false);
        }
    };

    /**
     * Load rules for the current profile
     */
    const loadRules = async () => {
        try {
            const rulesResult = await context.webAPI.retrieveMultipleRecords(
                "sprk_fieldmappingrule",
                `?$filter=_sprk_fieldmappingprofile_value eq '${profileId}'&$select=sprk_fieldmappingruleid,sprk_name,sprk_sourcefield,sprk_sourcefieldtype,sprk_targetfield,sprk_targetfieldtype,sprk_compatibilitymode,sprk_isrequired,sprk_defaultvalue,sprk_iscascadingsource,sprk_executionorder,sprk_isactive&$orderby=sprk_executionorder asc`
            );

            const loadedRules: IFieldMappingRule[] = rulesResult.entities.map((entity: Record<string, unknown>) => ({
                id: String(entity.sprk_fieldmappingruleid),
                profileId,
                name: entity.sprk_name as string | undefined,
                sourceField: String(entity.sprk_sourcefield || ""),
                sourceFieldType: (entity.sprk_sourcefieldtype as FieldType) ?? FieldType.Text,
                targetField: String(entity.sprk_targetfield || ""),
                targetFieldType: (entity.sprk_targetfieldtype as FieldType) ?? FieldType.Text,
                compatibilityMode: (entity.sprk_compatibilitymode as CompatibilityMode) ?? CompatibilityMode.Strict,
                isRequired: Boolean(entity.sprk_isrequired),
                defaultValue: entity.sprk_defaultvalue as string | undefined,
                isCascadingSource: Boolean(entity.sprk_iscascadingsource),
                executionOrder: (entity.sprk_executionorder as number) ?? 0,
                isActive: entity.sprk_isactive !== false,
            }));

            setRules(loadedRules);
            setDeletedRuleIds([]);
        } catch (err) {
            console.error("[FieldMappingAdmin] Error loading rules:", err);
            // Don't set error - profile may not have rules yet
        }
    };

    /**
     * Load entity field metadata for dropdowns
     */
    const loadEntityFields = async (entityName: string, type: "source" | "target") => {
        setIsLoadingFields(true);

        try {
            // Use EntityDefinitions API to get attribute metadata
            // STUB: [API] - S012-01: This uses Dataverse WebAPI EntityDefinitions endpoint
            // For now, use a simplified approach with common fields
            const commonFields: EntityFieldInfo[] = [
                { logicalName: `${entityName}id`, displayName: `${entityName} ID`, type: FieldType.Lookup },
                { logicalName: "statecode", displayName: "Status", type: FieldType.OptionSet },
                { logicalName: "statuscode", displayName: "Status Reason", type: FieldType.OptionSet },
                { logicalName: "ownerid", displayName: "Owner", type: FieldType.Lookup },
                { logicalName: "createdon", displayName: "Created On", type: FieldType.DateTime },
                { logicalName: "modifiedon", displayName: "Modified On", type: FieldType.DateTime },
            ];

            // Add entity-specific fields based on entity name
            const entityFields = getEntitySpecificFields(entityName);
            const allFields = [...commonFields, ...entityFields];

            if (type === "source") {
                setSourceFields(allFields);
            } else {
                setTargetFields(allFields);
            }
        } catch (err) {
            console.error(`[FieldMappingAdmin] Error loading ${type} fields:`, err);
        } finally {
            setIsLoadingFields(false);
        }
    };

    /**
     * Get entity-specific fields based on entity name
     * STUB: In production, this would query EntityDefinitions API
     */
    const getEntitySpecificFields = (entityName: string): EntityFieldInfo[] => {
        switch (entityName) {
            case "sprk_matter":
                return [
                    { logicalName: "sprk_mattername", displayName: "Matter Name", type: FieldType.Text },
                    { logicalName: "sprk_client", displayName: "Client", type: FieldType.Lookup },
                    { logicalName: "sprk_responsibleattorney", displayName: "Responsible Attorney", type: FieldType.Lookup },
                    { logicalName: "sprk_opendate", displayName: "Open Date", type: FieldType.DateTime },
                    { logicalName: "sprk_closedate", displayName: "Close Date", type: FieldType.DateTime },
                    { logicalName: "sprk_description", displayName: "Description", type: FieldType.Memo },
                ];
            case "sprk_event":
                return [
                    { logicalName: "sprk_eventname", displayName: "Event Name", type: FieldType.Text },
                    { logicalName: "sprk_eventtype_ref", displayName: "Event Type", type: FieldType.Lookup },
                    { logicalName: "sprk_basedate", displayName: "Base Date", type: FieldType.DateTime },
                    { logicalName: "sprk_duedate", displayName: "Due Date", type: FieldType.DateTime },
                    { logicalName: "sprk_priority", displayName: "Priority", type: FieldType.OptionSet },
                    { logicalName: "sprk_description", displayName: "Description", type: FieldType.Memo },
                    { logicalName: "sprk_regardingmatter", displayName: "Regarding Matter", type: FieldType.Lookup },
                    { logicalName: "sprk_regardingproject", displayName: "Regarding Project", type: FieldType.Lookup },
                    { logicalName: "sprk_regardingaccount", displayName: "Regarding Account", type: FieldType.Lookup },
                ];
            case "sprk_project":
                return [
                    { logicalName: "sprk_projectname", displayName: "Project Name", type: FieldType.Text },
                    { logicalName: "sprk_matter", displayName: "Matter", type: FieldType.Lookup },
                    { logicalName: "sprk_startdate", displayName: "Start Date", type: FieldType.DateTime },
                    { logicalName: "sprk_enddate", displayName: "End Date", type: FieldType.DateTime },
                ];
            case "account":
                return [
                    { logicalName: "name", displayName: "Account Name", type: FieldType.Text },
                    { logicalName: "accountnumber", displayName: "Account Number", type: FieldType.Text },
                    { logicalName: "primarycontactid", displayName: "Primary Contact", type: FieldType.Lookup },
                    { logicalName: "telephone1", displayName: "Phone", type: FieldType.Text },
                    { logicalName: "emailaddress1", displayName: "Email", type: FieldType.Text },
                ];
            case "contact":
                return [
                    { logicalName: "fullname", displayName: "Full Name", type: FieldType.Text },
                    { logicalName: "firstname", displayName: "First Name", type: FieldType.Text },
                    { logicalName: "lastname", displayName: "Last Name", type: FieldType.Text },
                    { logicalName: "emailaddress1", displayName: "Email", type: FieldType.Text },
                    { logicalName: "telephone1", displayName: "Phone", type: FieldType.Text },
                    { logicalName: "parentcustomerid", displayName: "Company", type: FieldType.Lookup },
                ];
            default:
                return [
                    { logicalName: `${entityName.replace("sprk_", "")}name`, displayName: "Name", type: FieldType.Text },
                    { logicalName: "sprk_description", displayName: "Description", type: FieldType.Memo },
                ];
        }
    };

    /**
     * Handle profile changes
     */
    const handleProfileChange = React.useCallback((updatedProfile: Partial<IFieldMappingProfile>) => {
        setProfile(updatedProfile);
        setHasChanges(true);
    }, []);

    /**
     * Handle adding a new rule
     */
    const handleAddRule = React.useCallback(() => {
        setEditingRule(null);
        setIsRuleEditorOpen(true);
    }, []);

    /**
     * Handle editing a rule
     */
    const handleEditRule = React.useCallback((rule: IFieldMappingRule) => {
        setEditingRule(rule);
        setIsRuleEditorOpen(true);
    }, []);

    /**
     * Handle deleting a rule
     */
    const handleDeleteRule = React.useCallback((ruleId: string) => {
        // If it's a saved rule (not new), track for deletion
        if (!ruleId.startsWith("new-")) {
            setDeletedRuleIds((prev) => [...prev, ruleId]);
        }
        setRules((prev) => prev.filter((r) => r.id !== ruleId));
        setHasChanges(true);
    }, []);

    /**
     * Handle rule reordering
     */
    const handleReorderRule = React.useCallback((ruleId: string, direction: "up" | "down") => {
        setRules((prev) => {
            const index = prev.findIndex((r) => r.id === ruleId);
            if (index === -1) return prev;

            const newRules = [...prev];
            const targetIndex = direction === "up" ? index - 1 : index + 1;

            if (targetIndex < 0 || targetIndex >= newRules.length) return prev;

            // Swap rules
            [newRules[index], newRules[targetIndex]] = [newRules[targetIndex], newRules[index]];

            // Update execution order
            return newRules.map((rule, i) => ({
                ...rule,
                executionOrder: i,
            }));
        });
        setHasChanges(true);
    }, []);

    /**
     * Handle saving a rule from editor
     */
    const handleSaveRule = React.useCallback((rule: IFieldMappingRule) => {
        setRules((prev) => {
            const existingIndex = prev.findIndex((r) => r.id === rule.id);
            if (existingIndex >= 0) {
                // Update existing
                const updated = [...prev];
                updated[existingIndex] = rule;
                return updated;
            } else {
                // Add new with next execution order
                const maxOrder = prev.reduce((max, r) => Math.max(max, r.executionOrder), -1);
                return [...prev, { ...rule, executionOrder: maxOrder + 1 }];
            }
        });
        setIsRuleEditorOpen(false);
        setEditingRule(null);
        setHasChanges(true);
    }, []);

    /**
     * Handle closing rule editor
     */
    const handleCloseRuleEditor = React.useCallback(() => {
        setIsRuleEditorOpen(false);
        setEditingRule(null);
    }, []);

    /**
     * Save all changes
     */
    const handleSave = async () => {
        setIsSaving(true);
        setError(null);

        try {
            // 1. Update profile
            const profileData: Record<string, unknown> = {
                sprk_name: profile.name,
                sprk_mappingdirection: profile.mappingDirection,
                sprk_syncmode: profile.syncMode,
                sprk_isactive: profile.isActive,
                sprk_description: profile.description,
            };

            // Only update entity fields on new profiles
            if (!profileId || profileId.startsWith("new-")) {
                profileData.sprk_sourceentity = profile.sourceEntity;
                profileData.sprk_targetentity = profile.targetEntity;
            }

            if (profileId && !profileId.startsWith("new-")) {
                await context.webAPI.updateRecord("sprk_fieldmappingprofile", profileId, profileData);
            } else {
                const newProfile = await context.webAPI.createRecord("sprk_fieldmappingprofile", profileData);
                setProfile((prev) => ({ ...prev, id: newProfile.id }));
            }

            // 2. Delete removed rules
            for (const ruleId of deletedRuleIds) {
                try {
                    await context.webAPI.deleteRecord("sprk_fieldmappingrule", ruleId);
                } catch (err) {
                    console.error(`[FieldMappingAdmin] Failed to delete rule ${ruleId}:`, err);
                }
            }

            // 3. Save/update rules
            for (const rule of rules) {
                const ruleData: Record<string, unknown> = {
                    sprk_name: rule.name || `${rule.sourceField} -> ${rule.targetField}`,
                    sprk_sourcefield: rule.sourceField,
                    sprk_sourcefieldtype: rule.sourceFieldType,
                    sprk_targetfield: rule.targetField,
                    sprk_targetfieldtype: rule.targetFieldType,
                    sprk_compatibilitymode: rule.compatibilityMode,
                    sprk_isrequired: rule.isRequired,
                    sprk_defaultvalue: rule.defaultValue,
                    sprk_iscascadingsource: rule.isCascadingSource,
                    sprk_executionorder: rule.executionOrder,
                    sprk_isactive: rule.isActive,
                    "sprk_fieldmappingprofile@odata.bind": `/sprk_fieldmappingprofiles(${profileId})`,
                };

                if (rule.id.startsWith("new-")) {
                    await context.webAPI.createRecord("sprk_fieldmappingrule", ruleData);
                } else {
                    await context.webAPI.updateRecord("sprk_fieldmappingrule", rule.id, ruleData);
                }
            }

            setHasChanges(false);
            setDeletedRuleIds([]);

            // Reload to get server-assigned IDs
            await loadRules();

            // Show success toast
            dispatchToast(
                <Toast>
                    <ToastTitle>Changes saved successfully</ToastTitle>
                </Toast>,
                { intent: "success" }
            );
        } catch (err) {
            console.error("[FieldMappingAdmin] Error saving:", err);
            setError(err instanceof Error ? err.message : "Failed to save changes");

            dispatchToast(
                <Toast>
                    <ToastTitle>Error saving changes</ToastTitle>
                    <ToastBody>{err instanceof Error ? err.message : "Unknown error"}</ToastBody>
                </Toast>,
                { intent: "error" }
            );
        } finally {
            setIsSaving(false);
        }
    };

    /**
     * Refresh data from server
     */
    const handleRefresh = async () => {
        if (profileId && !profileId.startsWith("new-")) {
            await loadProfile();
        }
        setHasChanges(false);
    };

    // Count invalid rules
    const invalidRulesCount = Array.from(compatibilityResults.values()).filter(
        (level) => level === CompatibilityLevel.Incompatible
    ).length;

    // If no profile ID provided, show instructions
    if (!profileId) {
        return (
            <div className={styles.container}>
                <div className={styles.emptyState}>
                    <Text size={400} weight="semibold">
                        Field Mapping Admin
                    </Text>
                    <Text>Create a new Field Mapping Profile record to begin configuring mappings.</Text>
                </div>
                <div className={styles.footer}>
                    <Text className={styles.versionText}>v{version}</Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <Toaster toasterId={toasterId} />

            {/* Header Toolbar */}
            <Toolbar className={styles.toolbar}>
                <ToolbarButton
                    appearance="primary"
                    icon={<Save20Regular />}
                    onClick={handleSave}
                    disabled={isSaving || !hasChanges || invalidRulesCount > 0}
                >
                    {isSaving ? "Saving..." : "Save"}
                </ToolbarButton>
                <ToolbarButton
                    icon={<ArrowSync20Regular />}
                    onClick={handleRefresh}
                    disabled={isLoading || isSaving}
                >
                    Refresh
                </ToolbarButton>
                <ToolbarDivider />
                <Text className={styles.statusText}>
                    {hasChanges ? "Unsaved changes" : "No changes"}
                    {invalidRulesCount > 0 && ` | ${invalidRulesCount} invalid rule(s)`}
                </Text>
            </Toolbar>

            {/* Error Message */}
            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                    <MessageBarActions>
                        <Button
                            appearance="transparent"
                            icon={<Dismiss20Regular />}
                            onClick={() => setError(null)}
                        />
                    </MessageBarActions>
                </MessageBar>
            )}

            {/* Main Content */}
            {isLoading ? (
                <div className={styles.loadingContainer}>
                    <Spinner label="Loading profile..." />
                </div>
            ) : (
                <div className={styles.content}>
                    {/* Profile Editor */}
                    <ProfileEditor
                        profile={profile}
                        onProfileChange={handleProfileChange}
                        disabled={isSaving}
                        isNew={!profileId || profileId.startsWith("new-")}
                    />

                    {/* Rules List */}
                    <RulesList
                        rules={rules}
                        onEditRule={handleEditRule}
                        onDeleteRule={handleDeleteRule}
                        onAddRule={handleAddRule}
                        onReorderRule={handleReorderRule}
                        disabled={isSaving}
                        compatibilityResults={compatibilityResults}
                    />
                </div>
            )}

            {/* Rule Editor Dialog */}
            <RuleEditor
                rule={editingRule}
                isOpen={isRuleEditorOpen}
                onClose={handleCloseRuleEditor}
                onSave={handleSaveRule}
                sourceFields={sourceFields}
                targetFields={targetFields}
                isLoadingFields={isLoadingFields}
                profileId={profileId}
            />

            {/* Footer */}
            <div className={styles.footer}>
                <Text className={styles.statusText}>
                    {rules.length} rule{rules.length !== 1 ? "s" : ""}
                </Text>
                <Text className={styles.versionText}>v{version}</Text>
            </div>
        </div>
    );
};
