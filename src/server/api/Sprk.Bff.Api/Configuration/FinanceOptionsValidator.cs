using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Start-up validator for the two eBilling SPE container ids on <see cref="FinanceOptions"/> (ebilling-r1 task 028, FR-16, D-55):
/// the Procedure Library and E-Billing Output Files containers must be distinct from each other and from the two
/// containers other features already index into (<c>EmailProcessing:DefaultContainerId</c>, <c>Communication:ArchiveContainerId</c>).
/// </summary>
/// <remarks>
/// A pasted default id is otherwise silent: <c>FileIndexingService</c> skips every file whose drive equals
/// <c>Finance:EbillingOutputContainerId</c>, so reusing the email or archive container there stops all indexing for that
/// container with no error. Empty values stay allowed (the keys are optional at start-up), and an empty value never
/// counts as a collision. The comparison is ordinal (container ids are case-sensitive) on trimmed values. The other
/// sections are read from <see cref="IConfiguration"/> rather than bound, because neither options class is validated at start
/// (precedent: <see cref="CustomerOptionsValidator"/>).
/// <para>ebilling-r1 task 090 adds the <c>Finance:FileStore</c> rules (<see cref="ValidateFileStore"/>).</para>
/// </remarks>
public sealed class FinanceOptionsValidator : IValidateOptions<FinanceOptions>
{
    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";
    private const string OutputKey = "Finance:EbillingOutputContainerId";
    private const string EmailDefaultKey = "EmailProcessing:DefaultContainerId";
    private const string ArchiveKey = "Communication:ArchiveContainerId";

    private readonly IConfiguration _configuration;

    public FinanceOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, FinanceOptions options)
    {
        var procedureLibrary = options.ProcedureLibraryContainerId?.Trim();
        var output = options.EbillingOutputContainerId?.Trim();

        var failures = new List<string>();

        if (!string.IsNullOrEmpty(procedureLibrary) && string.Equals(procedureLibrary, output, StringComparison.Ordinal))
        {
            failures.Add($"{ProcedureLibraryKey} and {OutputKey} must be different containers, but both are '{procedureLibrary}'. "
                + "The output container is never AI-indexed, so sharing it would stop indexing of the Procedure Library.");
        }

        foreach (var (key, value) in new[] { (ProcedureLibraryKey, procedureLibrary), (OutputKey, output) })
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            foreach (var otherKey in new[] { EmailDefaultKey, ArchiveKey })
            {
                var other = _configuration[otherKey]?.Trim();
                if (!string.IsNullOrEmpty(other) && string.Equals(value, other, StringComparison.Ordinal))
                {
                    failures.Add($"{key} must not equal {otherKey} ('{value}'): that container already holds other documents, "
                        + "and the eBilling container settings must name containers of their own.");
                }
            }
        }

        ValidateFileStore(options.FileStore ?? new FinanceFileStoreOptions(), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// <c>Finance:FileStore</c> (ebilling-r1 task 090): a configured endpoint must be a bare managed-identity <c>https</c> endpoint
    /// (the same rule the session store applies, <see cref="Sprk.Bff.Api.Infrastructure.Storage.BlobEndpointRules"/>) with a GUID
    /// tenant; the orphan grace must cover the invoice submission job's longest retry horizon, so the reconcile job can never delete
    /// the bytes of a submission that is still being retried. An empty endpoint is valid (not configured).
    /// </summary>
    private static void ValidateFileStore(FinanceFileStoreOptions fileStore, List<string> failures)
    {
        const string endpointKey = FinanceFileStoreOptions.SectionPath + ":BlobEndpoint";
        const string tenantKey = FinanceFileStoreOptions.SectionPath + ":TenantId";
        const string graceKey = FinanceFileStoreOptions.SectionPath + ":OrphanGraceMinutes";
        const string alertKey = FinanceFileStoreOptions.SectionPath + ":NotServableAlertMinutes";

        if (fileStore.IsConfigured)
        {
            try
            {
                Sprk.Bff.Api.Infrastructure.Storage.BlobEndpointRules.ParseManagedIdentityEndpoint(fileStore.BlobEndpoint, endpointKey);
            }
            catch (InvalidOperationException ex)
            {
                failures.Add(ex.Message);
            }

            if (!Guid.TryParse(fileStore.TenantId?.Trim(), out var tenant) || tenant == Guid.Empty)
            {
                failures.Add($"{tenantKey} must be a non-empty GUID when {endpointKey} is set: it is the first segment of every stored file's "
                    + "blob name, and every name is checked against it. Set it to this Dataverse environment's OrganizationId (checked at "
                    + "the store's first operation).");
            }
        }

        var horizonMinutes = (int)Math.Ceiling(Sprk.Bff.Api.Services.Finance.Files.StoredFileRetention.SubmissionRetryHorizon.TotalMinutes);
        if (fileStore.OrphanGraceMinutes < horizonMinutes || fileStore.OrphanGraceMinutes > FinanceFileStoreOptions.MaxOrphanGraceMinutes)
        {
            failures.Add($"{graceKey} must be between {horizonMinutes} and {FinanceFileStoreOptions.MaxOrphanGraceMinutes} minutes, but is "
                + $"{fileStore.OrphanGraceMinutes}. The lower bound is the invoice submission job's longest retry horizon: a shorter grace "
                + "could delete the bytes of a submission that is still being retried.");
        }

        if (fileStore.NotServableAlertMinutes < FinanceFileStoreOptions.MinNotServableAlertMinutes
            || fileStore.NotServableAlertMinutes > FinanceFileStoreOptions.MaxNotServableAlertMinutes)
        {
            failures.Add($"{alertKey} must be between {FinanceFileStoreOptions.MinNotServableAlertMinutes} and "
                + $"{FinanceFileStoreOptions.MaxNotServableAlertMinutes} minutes, but is {fileStore.NotServableAlertMinutes}.");
        }
    }
}
