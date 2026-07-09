/**
 * Field Mapping Framework Types (client engine contract)
 *
 * These types mirror the BFF field-mapping DTO surface exactly, so the
 * context-agnostic engine (`FieldMappingService.ts`) and the per-type apply
 * engines that land in tasks 012 (Copy/lookup) and 013 (Default/Concat/Template)
 * can be authored against the same shape the server emits.
 *
 * Source of truth (BFF, task 002):
 *   - `Sprk.Bff.Api/Models/FieldMapping/FieldMappingProfileWithRulesDto.cs`
 *   - `Sprk.Bff.Api/Models/FieldMapping/FieldMappingRuleDto.cs`
 *
 * The single read path is:
 *   GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}
 * which returns a `FieldMappingProfileWithRulesDto` (rules `$expand`-ed).
 * ASP.NET Core Minimal API serializes with the default camelCase policy, so the
 * JSON keys match the camelCase interface members below verbatim.
 *
 * @see spec.md FR-01, FR-02, FR-09
 * @see design.md §4.1
 * @see FieldMappingService.ts for the engine that consumes these types
 */

// ---------------------------------------------------------------------------
// String-literal discriminators (mirror the BFF string projections)
// ---------------------------------------------------------------------------

/**
 * Mapping type discriminator carried by each rule (BFF `MappingType`).
 *
 * Discriminates which apply engine handles the rule:
 *   - `Copy`     → verbatim source→target copy, incl. lookups (task 012)
 *   - `Default`  → literal from `defaultValue` when the source is empty (task 013)
 *   - `Concat`   → format string from `expression` (task 013)
 *   - `Template` → format string from `expression` (task 013)
 *
 * Modeled as a widened union (`| string`) so an unrecognized value coming from
 * a newer server never breaks JSON parsing — the engine's dispatch `default`
 * branch records a warning and skips (graceful degradation, never throws).
 */
export type FieldMappingType = 'Copy' | 'Default' | 'Concat' | 'Template' | (string & {});

/** Known `FieldMappingType` string constants (parse/compare without magic strings). */
export const FieldMappingTypes = {
  Copy: 'Copy',
  Default: 'Default',
  Concat: 'Concat',
  Template: 'Template',
} as const;

/**
 * Field data type as projected by the BFF (`SourceFieldType` / `TargetFieldType`):
 * one of "Text" | "Lookup" | "OptionSet" | "Number" | "DateTime" | "Boolean" | "Memo".
 * Widened with `| string` for forward compatibility.
 */
export type FieldMappingFieldType =
  | 'Text'
  | 'Lookup'
  | 'OptionSet'
  | 'Number'
  | 'DateTime'
  | 'Boolean'
  | 'Memo'
  | (string & {});

/**
 * Type-coercion strictness (BFF `CompatibilityMode`): "Strict" (0) or "Resolve" (1).
 * Widened with `| string` for forward compatibility.
 */
export type FieldMappingCompatibilityMode = 'Strict' | 'Resolve' | (string & {});

/**
 * Profile sync mode (BFF `SyncMode`): "OneTime" | "ManualRefresh" | "UpdateRelated".
 * Widened with `| string` for forward compatibility.
 */
export type FieldMappingSyncMode = 'OneTime' | 'ManualRefresh' | 'UpdateRelated' | (string & {});

// ---------------------------------------------------------------------------
// Rule + Profile (mirror the BFF DTOs)
// ---------------------------------------------------------------------------

/**
 * A single field-to-field mapping rule.
 *
 * 1:1 mirror of `FieldMappingRuleDto` (BFF, task 002). The five fields added in
 * task 002 — `mappingType`, `defaultValue`, `expression`, `isRequired`,
 * `compatibilityMode` — are all present so the apply engines (012/013) have the
 * complete rule contract.
 */
export interface IFieldMappingRule {
  /** Unique identifier of the rule (BFF `Id`). */
  id: string;
  /** Logical name of the source field to read from (BFF `SourceField`). */
  sourceField: string;
  /** Logical name of the target field to write to (BFF `TargetField`). */
  targetField: string;
  /** Source field data type (BFF `SourceFieldType`). */
  sourceFieldType: FieldMappingFieldType;
  /** Target field data type (BFF `TargetFieldType`). */
  targetFieldType: FieldMappingFieldType;
  /** Apply order (BFF `Priority`); lower runs first. */
  priority: number;
  /** Which apply engine handles this rule (BFF `MappingType`; default "Copy"). */
  mappingType: FieldMappingType;
  /** Literal value used by the Default engine when the source is empty (BFF `DefaultValue`). Nullable. */
  defaultValue?: string | null;
  /** Format string used by the Concat/Template engines (BFF `Expression`). Nullable; unused for Copy/Default. */
  expression?: string | null;
  /** When true, an empty resolved source value is a warning-worthy condition (BFF `IsRequired`). */
  isRequired: boolean;
  /** Type-coercion strictness (BFF `CompatibilityMode`; default "Strict"). */
  compatibilityMode: FieldMappingCompatibilityMode;
}

/**
 * A field-mapping profile for a source/target entity pair, with its rules.
 *
 * 1:1 mirror of `FieldMappingProfileWithRulesDto` (BFF). `rules` is the
 * `$expand`-ed collection returned in the single profile fetch.
 */
export interface IFieldMappingProfile {
  /** Unique identifier of the profile (BFF `Id`). */
  id: string;
  /** Display name of the profile (BFF `Name`). */
  name: string;
  /** Source entity logical name (BFF `SourceEntity`). */
  sourceEntity: string;
  /** Target entity logical name (BFF `TargetEntity`). */
  targetEntity: string;
  /** When to apply mappings (BFF `SyncMode`). */
  syncMode: FieldMappingSyncMode;
  /** Whether the profile is active (BFF `IsActive`). */
  isActive: boolean;
  /** The profile's rules, `$expand`-ed in the profile fetch (BFF `Rules`). */
  rules: IFieldMappingRule[];
}

// ---------------------------------------------------------------------------
// Engine result
// ---------------------------------------------------------------------------

/**
 * Result of a single `applyFieldMappings` invocation.
 *
 * Mirrors the never-throw, warnings-based shape of
 * `PolymorphicResolverService.applyResolverFields` (NFR-06 graceful-blank): a
 * missing profile or any per-rule failure is reported as data, never thrown.
 */
export interface IMappingResult {
  /**
   * `true` when an active profile was found for the entity pair; `false` on a
   * BFF 404 / no-profile (graceful no-op).
   */
  profileFound: boolean;
  /** Target field logical names that were written onto the payload. */
  fieldsMapped: string[];
  /** Non-fatal diagnostics accumulated while resolving/applying the profile. */
  warnings: string[];
}
