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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
