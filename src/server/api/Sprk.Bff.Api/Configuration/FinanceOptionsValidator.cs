using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Start-up validator for the eBilling SPE container id on <see cref="FinanceOptions"/> (ebilling-r1 task 028, FR-16, D-55): the
/// Procedure Library container must be distinct from the two containers other features already store into
/// (<c>EmailProcessing:DefaultContainerId</c>, <c>Communication:ArchiveContainerId</c>).
/// </summary>
/// <remarks>
/// <para>ebilling-r1 task 092 retired <c>Finance:EbillingOutputContainerId</c> (export files are Stored Files) and its checks. The
/// Procedure Library key stays while <c>RuleSetService</c> and <c>RateCardImportService</c> read it (the 092 escalation).</para>
/// Empty values stay allowed (the key is optional at start-up), and an empty value never counts as a collision. The comparison is ordinal (container ids are case-sensitive) on trimmed values. The other
/// sections are read from <see cref="IConfiguration"/> rather than bound, because neither options class is validated at start
/// (precedent: <see cref="CustomerOptionsValidator"/>).
/// <para>ebilling-r1 task 090 adds the <c>Finance:FileStore</c> rules (<see cref="ValidateFileStore"/>).</para>
/// </remarks>
public sealed class FinanceOptionsValidator : IValidateOptions<FinanceOptions>
{
    private const string ProcedureLibraryKey = "Finance:ProcedureLibraryContainerId";
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

        var failures = new List<string>();

        if (!string.IsNullOrEmpty(procedureLibrary))
        {
            foreach (var otherKey in new[] { EmailDefaultKey, ArchiveKey })
            {
                var other = _configuration[otherKey]?.Trim();
                if (!string.IsNullOrEmpty(other) && string.Equals(procedureLibrary, other, StringComparison.Ordinal))
                {
                    failures.Add($"{ProcedureLibraryKey} must not equal {otherKey} ('{procedureLibrary}'): that container already holds other documents, "
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
