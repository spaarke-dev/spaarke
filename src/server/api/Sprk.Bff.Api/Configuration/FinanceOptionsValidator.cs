using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Start-up validator for <see cref="FinanceOptions"/>: the <c>Finance:FileStore</c> rules (<see cref="ValidateFileStore"/>,
/// ebilling-r1 task 090).
/// </summary>
/// <remarks>
/// ebilling-r1 task 092 retired <c>Finance:EbillingOutputContainerId</c> and task 124 retired
/// <c>Finance:ProcedureLibraryContainerId</c> (EB-D-163): eBilling files are Stored Files in the Finance file store, so no eBilling
/// SharePoint Embedded container is configured and this validator reads no container id. A setting left behind under either key binds
/// to nothing and is never validated.
/// </remarks>
public sealed class FinanceOptionsValidator : IValidateOptions<FinanceOptions>
{
    public ValidateOptionsResult Validate(string? name, FinanceOptions options)
    {
        var failures = new List<string>();

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
