// Global using aliases for the test project.
// This file is intentionally minimal; add project-wide aliases here as needed.

// R6 Pillar 2 task D-A-06 (FR-06): IAnalysisToolHandler -> IToolHandler rename
// with source-compat alias. `global using` does NOT cross assembly boundaries, so the
// alias must be duplicated in this test project for existing tests that reference the
// old name (ToolHandlerRegistryTests, AiAnalysisNodeExecutorTests, EmailAnalysisIntegrationTests,
// AnalysisOrchestrationServiceTests) to continue to compile unchanged.
global using IAnalysisToolHandler = Sprk.Bff.Api.Services.Ai.IToolHandler;
