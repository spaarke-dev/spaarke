namespace Sprk.Bff.Api.Services.Ontology;

// The Ontology admin Classification area's wire shapes (ontology platform R1 task 103; D-22, FR-59, D-49). Booleans, counts and the
// category's own columns only: there is NO classifier confidence anywhere in these shapes, because none exists (#5).

/// <summary>The recall floor a measurement is judged against (D-10, D-49): at least <see cref="Percent"/> percent on a labelled set of at least <see cref="MinItems"/> items.</summary>
public sealed record ClassificationRecallFloor(int Percent, int MinItems);

/// <summary>How a category's stored recall measurement compares with the floor.</summary>
public static class ClassificationRecallStatus
{
    /// <summary>No recall has been stored for the category (task 074 has not measured it).</summary>
    public const string NotMeasured = "NotMeasured";

    /// <summary>Recall is at least the floor percent AND the labelled set is at least the floor size.</summary>
    public const string MeetsFloor = "MeetsFloor";

    /// <summary>Recall is below the floor percent.</summary>
    public const string BelowFloor = "BelowFloor";

    /// <summary>Recall reaches the floor percent but the labelled set is smaller than the floor size (or its size is unknown): the figure is not evidence.</summary>
    public const string SetTooSmall = "SetTooSmall";
}

/// <summary>
/// The measured recall of one category, read from the three <c>sprk_triagecategory</c> columns task 074 fills (D-49). <see cref="Status"/>
/// is one of <see cref="ClassificationRecallStatus"/>; a category never measured has null figures and <c>NotMeasured</c>.
/// </summary>
/// <param name="Status">One of <see cref="ClassificationRecallStatus"/>.</param>
/// <param name="Recall">Percent, 0 to 100 (<c>sprk_measuredrecall</c>); null when not measured.</param>
/// <param name="LabelledSetSize">Items in the labelled set (<c>sprk_labelledsetsize</c>); null when unknown.</param>
/// <param name="MeasuredOn">The date the measurement was taken, <c>yyyy-MM-dd</c> (<c>sprk_recallmeasuredon</c>); null when unknown.</param>
/// <param name="MeasuredBeforeLastEdit">True when the category row was modified on a later day than <paramref name="MeasuredOn"/>: the figure may describe the category as it was
/// before its last edit (K3, task 103 fix round). False when not measured, when either date is unknown, or when the edit and the measurement share a day (a measurement
/// writes the row too, so a same-day edit cannot be told apart from it).</param>
public sealed record ClassificationRecall(string Status, decimal? Recall, int? LabelledSetSize, string? MeasuredOn, bool MeasuredBeforeLastEdit = false);

/// <summary>Where a policy version stands: the policy's current version, a version not yet published, or a superseded one.</summary>
public static class ClassificationVersionState
{
    /// <summary>The version the policy's <c>sprk_currentversion</c> points at.</summary>
    public const string InForce = "InForce";

    /// <summary>Not the current version and not stamped out (<c>sprk_inforceto</c> empty): a saved draft.</summary>
    public const string Draft = "Draft";

    /// <summary>Superseded: <c>sprk_inforceto</c> is stamped and it is not the current version. History, but a rename is still refused while it names the category.</summary>
    public const string Earlier = "Earlier";
}

/// <summary>One policy version whose rule body names a category by its id.</summary>
/// <param name="PolicyId">The policy.</param>
/// <param name="PolicyCode">The policy's code, for example <c>POL-COMMIT-BUDGET</c>.</param>
/// <param name="PolicyName">The policy's display name; null when none is stored.</param>
/// <param name="PolicyEnabled">Whether the policy is On.</param>
/// <param name="VersionId">The version whose body names the category.</param>
/// <param name="VersionNumber">The version number; null when none is stored.</param>
/// <param name="VersionState">One of <see cref="ClassificationVersionState"/>.</param>
public sealed record ClassificationRuleReference(
    Guid PolicyId,
    string PolicyCode,
    string? PolicyName,
    bool PolicyEnabled,
    Guid VersionId,
    int? VersionNumber,
    string VersionState);

/// <summary>One triage category as the Classification list shows it.</summary>
/// <param name="Id">The category row.</param>
/// <param name="Name">The category name: also the constrained-decoding enum value the classifier is held to (NFR-09).</param>
/// <param name="Guidance">The text injected into the classifier prompt (task 072); null when none.</param>
/// <param name="Enabled">On: the classifier may choose it. A new category saves Off.</param>
/// <param name="Version">The row's <c>versionnumber</c>; the edit form sends it back so a concurrent change is detected.</param>
/// <param name="Volume30Days">Communications classified into the category in the last 30 days, among those the CALLER can read; null when the count could not be read.</param>
/// <param name="Recall">The stored recall measurement against the floor (D-49).</param>
/// <param name="ReadBy">Policy versions whose rule body names the category; empty when none (or see <see cref="ClassificationList.ReferencesAvailable"/>).</param>
public sealed record ClassificationCategory(
    Guid Id,
    string Name,
    string? Guidance,
    bool Enabled,
    long Version,
    int? Volume30Days,
    ClassificationRecall Recall,
    IReadOnlyList<ClassificationRuleReference> ReadBy);

/// <summary>The Classification list: every active category, with the floor the recall is judged against.</summary>
/// <param name="Categories">By name.</param>
/// <param name="RecallFloor">The floor (80 percent on 50 items).</param>
/// <param name="VolumeWindowDays">The window of <see cref="ClassificationCategory.Volume30Days"/> (30).</param>
/// <param name="VolumeAvailable">False when the volume could not be read: every <c>Volume30Days</c> is then null, not zero.</param>
/// <param name="ReferencesAvailable">False when the policy versions could not be read: every <c>ReadBy</c> is then empty, which does NOT mean "no rule reads it".</param>
public sealed record ClassificationList(
    IReadOnlyList<ClassificationCategory> Categories,
    ClassificationRecallFloor RecallFloor,
    int VolumeWindowDays,
    bool VolumeAvailable,
    bool ReferencesAvailable);

/// <summary>A category row as a write returns it (the list is re-read after a save).</summary>
public sealed record ClassificationRow(Guid Id, string Name, string? Guidance, bool Enabled, long Version);

/// <summary>Body of <c>POST /categories</c>. There is no <c>enabled</c> member: a new category always saves Off.</summary>
public sealed record CreateClassificationCategoryRequest(string? Name, string? Guidance);

/// <summary>
/// Body of <c>PATCH /categories/{id}</c>. A member left out is left alone; an empty <c>guidance</c> clears it. <c>Version</c> is the
/// <see cref="ClassificationCategory.Version"/> the form loaded; when sent and the row has moved on, nothing is written (409).
/// </summary>
public sealed record UpdateClassificationCategoryRequest(string? Name, string? Guidance, long? Version);

/// <summary>Body of <c>PUT /categories/{id}/enabled</c>.</summary>
public sealed record SetClassificationCategoryEnabledRequest(bool? Enabled, long? Version);

/// <summary>How a Classification operation ended.</summary>
internal enum ClassificationOutcome
{
    Ok,
    Created,

    /// <summary>The caller could not be resolved to a Dataverse user (no user context, OBO not configured or refused, Dataverse 401): the single 403.</summary>
    CallerUnresolved,

    /// <summary>Dataverse refused the caller the right (403): a Console User, or a user without the Spaarke Ontology Administrator role.</summary>
    Forbidden,

    NotFound,

    /// <summary>The request failed validation; see the field errors.</summary>
    Invalid,

    /// <summary>Another category already has the name.</summary>
    NameTaken,

    /// <summary>The rename was refused because a policy version reads the category; see the blockers.</summary>
    RenameBlocked,

    /// <summary>
    /// The rename was refused because the caller's read of the policies does not cover every business unit (their depth is below Global, or
    /// Deep outside the root business unit), so "no rule reads this category" cannot be established from what they can see.
    /// </summary>
    RenameUnverifiable,

    /// <summary>The rename was refused because the routing configuration (<c>Communication:CategoryRouting</c>) is keyed by the category's name; see the detail.</summary>
    RenameRouted,

    /// <summary>The row changed since the form loaded it (or since the BFF read it): nothing was written.</summary>
    Stale,

    /// <summary>A fault: Dataverse failed, or the references that decide a rename could not be read. Nothing was written.</summary>
    Unavailable,
}

/// <summary>The result of one Classification operation.</summary>
internal sealed record ClassificationOperationResult<T>(
    ClassificationOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyList<ClassificationRuleReference>? Blockers = null,
    string? Detail = null);
