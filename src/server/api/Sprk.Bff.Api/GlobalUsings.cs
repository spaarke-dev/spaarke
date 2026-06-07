// Global type aliases for source compatibility across the project.
// Added by R6 Pillar 2 task D-A-06 (FR-06): renames IAnalysisToolHandler -> IToolHandler.
// The alias below preserves the old name so existing handler implementations
// (GenericAnalysisHandler, DocumentClassifierHandler, SummaryHandler,
// SemanticSearchToolHandler), tests, and DI registrations compile unchanged.
//
// When new code is written, prefer the new name `IToolHandler` directly.
// The alias is retained indefinitely for source-compat; removal is a separate decision
// that would require touching every consumer (currently ~16 files in production + tests).

global using IAnalysisToolHandler = Sprk.Bff.Api.Services.Ai.IToolHandler;
