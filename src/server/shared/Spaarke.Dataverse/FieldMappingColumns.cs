namespace Spaarke.Dataverse;

/// <summary>
/// The Dataverse columns the field-mapping read path selects, held once so the profile and rule queries cannot drift
/// (field-mapping-server-write-path-r1 tasks 004 and 005, spec FR-05).
/// </summary>
/// <remarks>
/// <para>
/// The two tables carry DIFFERENT mode columns, both deployed under exactly these names (verified with the Dataverse
/// <c>describe</c> tool on spaarkedev1, 2026-10-21): <c>sprk_fieldmappingprofile.sprk_capabilitymode</c> (Strict
/// 100000000 / Resolve 100000001) and <c>sprk_fieldmappingrule.sprk_compatibilitymode</c> (Strict 0 / Resolve 1). They
/// are not two spellings of one column, so neither is renamed. The profile has no <c>sprk_syncmode</c>; the rule does.
/// </para>
/// <para>
/// Internal (InternalsVisibleTo the BFF unit tests, ADR-038 Amendment A2): a wrong column name here is a Dataverse 400 at
/// runtime, and Mock&lt;HttpMessageHandler&gt; is banned, so the constant is the honest observation point.
/// </para>
/// </remarks>
internal static class FieldMappingColumns
{
    /// <summary>Columns selected from <c>sprk_fieldmappingprofile</c>.</summary>
    internal const string ProfileSelect =
        "sprk_fieldmappingprofileid,sprk_name,_sprk_sourcerecordtype_value,_sprk_targetrecordtype_value,"
        + "sprk_capabilitymode,sprk_defaultvalue,sprk_description,statecode";

    /// <summary>Columns selected from <c>sprk_fieldmappingrule</c>, including <c>sprk_syncmode</c> (task 005, FR-05d).</summary>
    internal const string RuleSelect =
        "sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_sourcefield,sprk_sourcefieldtype,"
        + "sprk_targetfield,sprk_targetfieldtype,sprk_mapping_type,sprk_compatibilitymode,sprk_isrequired,"
        + "sprk_defaultvalue,sprk_expression,sprk_iscascadingsource,sprk_executionorder,sprk_isactive,sprk_syncmode";
}
