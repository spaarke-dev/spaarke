/**
 * Shared Playbook type definitions.
 *
 * These types are used by both the Analysis Builder code page and the
 * Quick Start Playbook Wizards. They map directly to existing Dataverse
 * entities — no schema changes required.
 */
// ---------------------------------------------------------------------------
// Dataverse field name constants (avoid magic strings)
// ---------------------------------------------------------------------------
export const ENTITY_NAMES = {
    playbook: 'sprk_analysisplaybook',
    action: 'sprk_analysisaction',
    skill: 'sprk_analysisskill',
    knowledge: 'sprk_analysisknowledge',
    tool: 'sprk_analysistool',
    analysis: 'sprk_analysis',
    document: 'sprk_document',
};
export const RELATIONSHIP_NAMES = {
    /** Playbook → Skills (N:N) */
    playbookSkill: 'sprk_playbook_skill',
    /** Playbook → Knowledge (N:N) */
    playbookKnowledge: 'sprk_playbook_knowledge',
    /** Playbook → Tools (N:N) */
    playbookTool: 'sprk_playbook_tool',
    /** Playbook → Actions (N:N) — note different naming pattern */
    playbookAction: 'sprk_analysisplaybook_action',
    /** Analysis → Skills (N:N) */
    analysisSkill: 'sprk_analysis_skill',
    /** Analysis → Knowledge (N:N) */
    analysisKnowledge: 'sprk_analysis_knowledge',
    /** Analysis → Tools (N:N) */
    analysisTool: 'sprk_analysis_tool',
};
export const ID_FIELDS = {
    playbook: 'sprk_analysisplaybookid',
    action: 'sprk_analysisactionid',
    skill: 'sprk_analysisskillid',
    knowledge: 'sprk_analysisknowledgeid',
    tool: 'sprk_analysistoolid',
    analysis: 'sprk_analysisid',
};
//# sourceMappingURL=types.js.map