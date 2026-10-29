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

    /// <summary>The first value of the global option set <c>sprk_fieldmappingtypes</c> (Text = 100000000).</summary>
    internal const int FieldTypeOptionSetBase = 100000000;

    /// <summary>
    /// Reads a stored <c>sprk_sourcefieldtype</c> / <c>sprk_targetfieldtype</c> as the engine's field type, 0..6 (Text,
    /// Lookup, OptionSet, Number, DateTime, Boolean, Memo). <c>sprk_targetfieldtype</c> uses the global option set
    /// <c>sprk_fieldmappingtypes</c> (100000000 + n, plus Currency 100000007), while older rows and the local
    /// <c>sprk_sourcefieldtype</c> set hold the bare n. Both encodings are live on dev (#2281: 24 rules store 0/1, one
    /// stores 100000001), so both are read here, once, for every consumer. Currency reads as Number: the engine already
    /// writes a Money for a currency column from the column's metadata. Any other value is returned unchanged.
    /// </summary>
    internal static int NormalizeFieldType(int stored)
    {
        var type = stored >= FieldTypeOptionSetBase ? stored - FieldTypeOptionSetBase : stored;
        return type == FieldTypeCurrency ? FieldTypeNumber : type;
    }

    private const int FieldTypeNumber = 3;
    private const int FieldTypeCurrency = 7;

    /// <summary>Columns selected from <c>sprk_fieldmappingrule</c>, including <c>sprk_syncmode</c> (task 005, FR-05d).</summary>
    internal const string RuleSelect =
        "sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_sourcefield,sprk_sourcefieldtype,"
        + "sprk_targetfield,sprk_targetfieldtype,sprk_mapping_type,sprk_compatibilitymode,sprk_isrequired,"
        + "sprk_defaultvalue,sprk_expression,sprk_iscascadingsource,sprk_executionorder,sprk_isactive,sprk_syncmode";
}
