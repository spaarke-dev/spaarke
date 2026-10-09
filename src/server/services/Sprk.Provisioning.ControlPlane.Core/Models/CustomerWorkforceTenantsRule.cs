// -----------------------------------------------------------------------------
// CustomerWorkforceTenantsRule.cs
//
// Task 255 (INCOMING-141 from unified-access-control-r2 task 141, owner decision I1 = (b)). The ONE rule for
// the intake value `customerWorkforceTenantIds`: the Entra tenant id(s) of the CUSTOMER whose employees use
// the stamp. H4b writes them as the BFF's WorkforceIdentity__CustomerTenantIds__N on both slots; the BFF's
// member test admits a first-sign-in email bind or contact creation only for a member (acct = 0) of a listed
// tenant, and an empty list DENIES everyone (workforce_tenant_list_empty).
//
// Applied by POST /api/runs (400 before the run guard, the registry, Cosmos or the enqueue), again by H4b
// before it writes (defence in depth) and by H13's T7 to know what both slots must carry.
//
// RULES — a value the BFF would refuse at startup is refused here, and so is one that would admit the wrong
// people:
//   - REQUIRED for every tenancy model. Model 1: it is the customer's tenant, a DIFFERENT GUID from the run's
//     tenantId (Spaarke's). Model 2: the customer's tenant, normally the run's tenantId — written anyway,
//     nothing is inferred (hand-off §3). The hand-off allows Model 2 to default it from H0.5's consent tid;
//     Model 2 is out of scope (plan D3), so it is simply required like Model 1.
//   - A JSON array of 1..MaxTenants tenant ids (GUID strings); stored canonical (lowercase "D", input order).
//   - Never the all-zero GUID, a duplicate or an unparseable value (the BFF's ValidateOnStart refuses them).
//   - Never a CIAM tenant (ReservedTenantsOptions.CiamTenantIds — the BFF's startup refuses Ciam:TenantId).
//   - Never Spaarke's own tenant (ReservedTenantsOptions.SpaarkeTenantId) and, on Model 1, never the run's
//     tenantId (= AzureAd:TenantId, the stamp registration's tenant): either would email-bind Spaarke's own
//     staff into this customer's environment (hand-off §3 🔴).
// Reject, never repair. Never a fallback to the run's tenantId (AzureAd:TenantId) or to TenantRouting.
//
// §11 justification — Existing: UserProvisioningIntake / OpenAiMonthlyLimitRule are the same "one intake rule,
// shared by endpoint and handler" shape for other values. Extension: neither carries a tenant concept; this is
// a sibling, not a variant. Cost of doing nothing: every new stamp denies every Type-2 first sign-in, or (with a
// wrong value) refuses to start / admits Spaarke's staff.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>The intake rule for <c>customerWorkforceTenantIds</c> (task 255).</summary>
public static class CustomerWorkforceTenantsRule
{
    /// <summary>Rejection code: the value is absent or blank.</summary>
    public const string RequiredRejectionCode = "workforce-tenants-required";

    /// <summary>Rejection code: not a JSON array of 1..<see cref="MaxTenants"/> distinct, non-zero tenant ids.</summary>
    public const string InvalidRejectionCode = "workforce-tenants-invalid";

    /// <summary>Rejection code: a listed tenant is a CIAM (Entra External ID) tenant.</summary>
    public const string CiamTenantRejectionCode = "workforce-tenants-ciam-tenant";

    /// <summary>Rejection code: a listed tenant is Spaarke's own tenant (or, on Model 1, the run's tenantId).</summary>
    public const string SpaarkeTenantRejectionCode = "workforce-tenants-spaarke-tenant";

    /// <summary>Most tenants one stamp may list (a customer after acquisitions; far above any real case, bounds the value).</summary>
    public const int MaxTenants = 10;

    /// <summary>Longest accepted raw value — <see cref="MaxTenants"/> braced GUIDs with JSON punctuation fit well inside it.</summary>
    public const int MaxValueLength = 1024;

    private const int MaxEchoedLength = 64;

    /// <summary>The app-setting base name H4b writes (<c>{base}__0</c>, <c>__1</c>, …) — the BFF's <c>WorkforceIdentity:CustomerTenantIds</c>.</summary>
    public const string AppSettingBaseName = "WorkforceIdentity__CustomerTenantIds";

    /// <summary>
    /// Validates the run's value. <paramref name="tenancyModel"/> and <paramref name="runTenantId"/> are the run's
    /// <c>tenancyModel</c> and intake <c>tenantId</c>; <paramref name="reserved"/> the L2-owned reserved tenants.
    /// </summary>
    public static CustomerWorkforceTenantsOutcome Validate(
        string? tenancyModel, string? runTenantId, string? value, ReservedTenants reserved)
    {
        ArgumentNullException.ThrowIfNull(reserved);

        var key = IntakeParameterCatalog.CustomerWorkforceTenantIds;
        if (string.IsNullOrWhiteSpace(value))
        {
            return new CustomerWorkforceTenantsOutcome.Invalid(RequiredRejectionCode,
                $"'{key}' is required: a JSON array of the CUSTOMER's Entra tenant id(s) whose employees use this stamp " +
                "(e.g. [\"<customer-tenant-guid>\"]). Model 1: the customer's own tenant — never Spaarke's, never the run's " +
                "tenantId. Model 2: the customer's tenant. Without it the stamp's BFF denies every first sign-in of a " +
                "customer employee (sdap.access.deny.workforce_tenant_list_empty).");
        }

        if (!TryParse(value, out var tenantIds, out var shapeError))
        {
            return new CustomerWorkforceTenantsOutcome.Invalid(InvalidRejectionCode, $"'{key}' {shapeError}");
        }

        Guid.TryParse(runTenantId?.Trim(), out var runTenant);
        var isModel1 = TenancyModelParser.TryParse(tenancyModel, out var model) && model == TenancyModel.Model1;
        for (var i = 0; i < tenantIds.Count; i++)
        {
            var tenant = tenantIds[i];
            if (reserved.CiamTenantIds.Contains(tenant))
            {
                return new CustomerWorkforceTenantsOutcome.Invalid(CiamTenantRejectionCode,
                    $"'{key}'[{i}] = {tenant:D} is Spaarke's Entra External ID (CIAM) tenant. External contacts are never " +
                    "workforce members, and the stamp's BFF refuses to start with it listed. Give the customer's own " +
                    "workforce tenant.");
            }
            if (tenant == reserved.SpaarkeTenantId || (isModel1 && runTenant != Guid.Empty && tenant == runTenant))
            {
                return new CustomerWorkforceTenantsOutcome.Invalid(SpaarkeTenantRejectionCode,
                    $"'{key}'[{i}] = {tenant:D} is Spaarke's own tenant" +
                    (isModel1 ? " (a Model 1 stamp's tenantId)" : string.Empty) +
                    ". Listed, it would let the BFF bind Spaarke's own staff into this customer's environment. Give the " +
                    "CUSTOMER's tenant — the tenant its employees sign in from (Model 1 customer staff are B2B guests " +
                    "from their HOME tenant).");
            }
        }

        return new CustomerWorkforceTenantsOutcome.Valid(
            tenantIds.Select(t => t.ToString("D")).ToList(),
            Canonical(tenantIds));
    }

    /// <summary>
    /// Shape-only read of a stored value (POST /api/runs stores the canonical form) — the tenant ids in order, or
    /// null when absent or malformed. For resolution after <see cref="Validate"/> has passed; never a substitute.
    /// </summary>
    public static IReadOnlyList<string>? ParseStored(string? value)
        => !string.IsNullOrWhiteSpace(value) && TryParse(value, out var ids, out _)
            ? ids.Select(t => t.ToString("D")).ToList()
            : null;

    /// <summary>The canonical stored form: a JSON array of lowercase "D" GUIDs, in the given order.</summary>
    public static string Canonical(IEnumerable<Guid> tenantIds)
        => JsonSerializer.Serialize(tenantIds.Select(t => t.ToString("D")).ToArray());

    private static bool TryParse(string value, out List<Guid> tenantIds, out string error)
    {
        tenantIds = [];
        error = string.Empty;
        if (value.Length > MaxValueLength)
        {
            error = $"is longer than {MaxValueLength} characters; it must be a JSON array of at most {MaxTenants} tenant ids.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 2 });
        }
        catch (JsonException)
        {
            error = $"value '{Echo(value)}' is not a JSON array of tenant ids (e.g. [\"<customer-tenant-guid>\"]).";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"value '{Echo(value)}' is not a JSON array of tenant ids (e.g. [\"<customer-tenant-guid>\"]).";
                return false;
            }

            var count = document.RootElement.GetArrayLength();
            if (count < 1 || count > MaxTenants)
            {
                error = $"lists {count} tenant ids; between 1 and {MaxTenants} are accepted.";
                return false;
            }

            var i = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(element.GetString()?.Trim(), out var tenant)
                    || tenant == Guid.Empty)
                {
                    error = $"[{i}] is not a tenant id (a non-zero GUID string).";
                    return false;
                }
                if (tenantIds.Contains(tenant))
                {
                    error = $"[{i}] = {tenant:D} is listed twice.";
                    return false;
                }
                tenantIds.Add(tenant);
                i++;
            }
        }

        return true;
    }

    private static string Echo(string value)
    {
        var head = value.Length > MaxEchoedLength ? value[..MaxEchoedLength] + "…" : value;
        return new string(head.Select(c => char.IsControl(c) ? '?' : c).ToArray());
    }
}

/// <summary>Result of <see cref="CustomerWorkforceTenantsRule.Validate"/>.</summary>
public abstract record CustomerWorkforceTenantsOutcome
{
    private CustomerWorkforceTenantsOutcome()
    {
    }

    /// <summary>Usable: the tenant ids (canonical "D", input order) and the canonical stored value.</summary>
    public sealed record Valid(IReadOnlyList<string> TenantIds, string CanonicalValue) : CustomerWorkforceTenantsOutcome;

    /// <summary>Refused, with the rejection code and an operator-facing diagnostic.</summary>
    public sealed record Invalid(string RejectionCode, string Diagnostic) : CustomerWorkforceTenantsOutcome;
}
