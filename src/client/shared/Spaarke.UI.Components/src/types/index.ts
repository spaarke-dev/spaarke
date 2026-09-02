export * from './DatasetTypes';
export * from './CommandTypes';
export * from './ColumnRendererTypes';
export * from './EntityConfigurationTypes';
// ChartDefinitionTypes + DrillInteractionTypes removed (VHVU-042): the canonical
// VisualType / IChartDefinition / IChartData / IAggregatedDataPoint / DrillInteraction
// now live in @spaarke/visuals. These were a stale, unused fork (0 consumers).
export * from './FieldMappingTypes';
export * from './EventTypeConfig';
export * from './WebApiLike';
export * from './FetchXmlTypes';
export * from './ConfigurationTypes';
export * from './LookupTypes';
export * from './MiniGraphTypes';
export * from './serviceInterfaces';
// DataGridConfiguration - Spaarke DataGrid Framework R1 schema (task 001)
export * from './DataGridConfiguration';
// RecordHeaderConfiguration - Spaarke.Records.RecordHeader v1.0 schema (record-header-and-notepad-r2 task 030)
export * from './RecordHeaderConfiguration';
// sprk_analysis record type + option-set enums + regarding catalog. Hoisted from
// the SpaarkeAi solution (2026-07-29 P2) so shared-lib widgets + the solution share ONE source.
export * from './sprkAnalysis';
export { PrivilegeService } from '../services/PrivilegeService';
export { FieldSecurityService } from '../services/FieldSecurityService';
export { ColumnRendererService } from '../services/ColumnRendererService';
export { EntityConfigurationService } from '../services/EntityConfigurationService';
export { CustomCommandFactory } from '../services/CustomCommandFactory';
// (FieldMappingService class removed in task 010 — the engine is now the
// `applyFieldMappings` function exported from the services barrel. Field-mapping
// TYPES are exported above via `export * from './FieldMappingTypes'`.)
export {
  EventTypeService,
  eventTypeService,
  DEFAULT_EVENT_FIELD_STATES,
  ALL_EVENT_FIELDS,
  DEFAULT_SECTION_STATES,
  getEventTypeFieldConfig,
} from '../services/EventTypeService';
export type { IGetEventTypeFieldConfigResult } from '../services/EventTypeService';
