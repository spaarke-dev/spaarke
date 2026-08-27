using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// auth-v4 task 051 (FR-E2) — structural fitness function: every <c>ServiceBusClient</c> in the BFF
/// must be built in one place, so the credential decision stays fixable in one place.
/// </summary>
/// <remarks>
/// <para><b>Why this guard exists.</b> Before this task the BFF constructed <c>ServiceBusClient</c>
/// in four places and registered it as a singleton three times — in <c>WorkersModule</c>
/// (Program.cs:75), <c>OfficeWorkersModule</c> (:124) and <c>JobProcessingModule</c> (:196). .NET DI
/// resolves last-registration-wins, so only the third was ever used and the first two were dead code
/// that read a <i>different config key</i> than the one actually in effect. The SAS credential was
/// therefore live under two spellings simultaneously — <c>ConnectionStrings:ServiceBus</c> from the
/// Bicep stacks and <c>ServiceBus:ConnectionString</c> from
/// <c>scripts/Configure-ProductionAppSettings.ps1</c>.</para>
/// <para>That is the same shape ADR-028 A4 identifies as what made <c>BFF-API-ClientSecret</c>
/// unfixable: <i>"seven call sites each rolling their own credential handling"</i>. A migration that
/// fixes today's copies without preventing tomorrow's leaves the project exactly where the three
/// prior audits left it.</para>
/// <para><b>No allowlist.</b> Deliberately. An allowlist with one entry is a census waiting to
/// regrow — every credential inventory in this project started as a short list someone was sure was
/// complete. <c>MembershipJunctionUpdaterHost</c>, the one legitimate second caller, routes through
/// <c>ServiceBusClientFactory.CreateForNamespace</c> instead of being excepted.</para>
/// <para>Companion to <see cref="CredentialGuardTests"/> / <see cref="CredentialCensusTests"/> /
/// <see cref="FabricatedResultGuardTests"/>. Per <c>tests/CLAUDE.md</c> "Structural fitness
/// functions" and task 063, this file is MAINTAIN-class: it is the mechanism, not scaffolding.</para>
/// </remarks>
public class ServiceBusClientGuardTests
{
    private const string FactoryRelativePath =
        "src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ServiceBusClientFactory.cs";

    /// <summary>
    /// <c>new ServiceBusClient(...)</c> may appear only inside <see cref="FactoryRelativePath"/>.
    /// </summary>
    [Fact]
    public void ServiceBusClient_IsConstructedOnlyInTheFactory()
    {
        var offenders = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (relative.EndsWith(FactoryRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // SCOPE, NOT AN ALLOWLIST (added 2026-08-27, task 042 of sdap-SPE-admin-app-r2).
            //
            // This rule exists so the production choice between namespace+managed-identity and a SAS
            // connection string stays fixable in ONE place. A test that constructs a ServiceBusClient
            // over a NeverInvokedTokenCredential or a fake FQDN is not making that choice — it is
            // building a double. Scanning test projects made the guard report three such doubles in
            // `Sprk.Provisioning.ControlPlane.Tests/**` alongside the real finding, which is how a
            // guard gets read as noisy and then disabled.
            //
            // This is deliberately NOT the allowlist the class doc rules out: it does not exempt any
            // named production site. Every file under src/server/**, including every ControlPlane
            // service project, is still scanned. Only `*.Tests` projects are out of scope.
            if (relative.Contains(".Tests/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = SourceScan.StripLineComment(lines[i]);
                if (code.Contains("new ServiceBusClient(", StringComparison.Ordinal) ||
                    code.Contains("new Azure.Messaging.ServiceBus.ServiceBusClient(", StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}:{i + 1}: {code.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "ServiceBusClient must be constructed only in ServiceBusClientFactory, so the choice " +
            "between namespace+managed-identity and a SAS connection string stays fixable in one " +
            "place. Each extra construction site is an independent copy of that decision — the " +
            "shape that made BFF-API-ClientSecret unfixable (ADR-028 A4). Route the call through " +
            "ServiceBusClientFactory.Create or .CreateForNamespace instead. Found: " +
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// Registering a service only when its credential happens to be configured is the ADR-032
    /// asymmetric-registration anti-pattern (CLAUDE.md §10 F.1): clearing the credential silently
    /// un-registers the service and everything that injects it, so the failure surfaces as an
    /// unresolvable-dependency error naming a type nobody configured.
    /// </summary>
    [Fact]
    public void ServiceBusClient_RegistrationIsNotGatedOnCredentialPresence()
    {
        // Look BACKWARD from each registration for an enclosing null/empty guard, rather than
        // matching a config key on the guard line itself. The first version of this test did the
        // latter and its own negative control walked straight past it: a guard written as
        // `var probe = configuration.GetValue<string>("ServiceBus:ConnectionString");
        //  if (!string.IsNullOrWhiteSpace(probe)) { services.AddSingleton(... ); }`
        // puts the key and the condition on different lines, so nothing on the `if` line named
        // Service Bus at all. Any check that can be evaded by renaming a local is not a check.
        const int LookBackLines = 12;

        var offenders = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (!relative.Contains("/DI/", StringComparison.OrdinalIgnoreCase) &&
                !relative.EndsWith("Module.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = SourceScan.StripLineComment(lines[i]);

                var registersClient =
                    code.Contains("AddSingleton", StringComparison.Ordinal) &&
                    (code.Contains("ServiceBusClient", StringComparison.Ordinal) ||
                     code.Contains("ServiceBusClientFactory", StringComparison.Ordinal));

                if (!registersClient)
                {
                    continue;
                }

                for (var back = i; back >= Math.Max(0, i - LookBackLines); back--)
                {
                    var candidate = SourceScan.StripLineComment(lines[back]);
                    var isNullEmptyGuard =
                        candidate.Contains("if (", StringComparison.Ordinal) &&
                        (candidate.Contains("IsNullOrWhiteSpace", StringComparison.Ordinal) ||
                         candidate.Contains("IsNullOrEmpty", StringComparison.Ordinal));

                    if (isNullEmptyGuard)
                    {
                        offenders.Add(
                            $"{relative}:{i + 1}: registration `{code.Trim()}` is gated by " +
                            $"line {back + 1} `{candidate.Trim()}`");
                        break;
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A DI module must not branch on whether the Service Bus credential is configured. " +
            "Register the client unconditionally and let ServiceBusClientFactory.Create decide the " +
            "credential — it throws a message an operator can act on when neither a namespace nor a " +
            "connection string is set (ADR-032 / CLAUDE.md §10 F.1). Found: " +
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// The factory must keep offering both credential paths while the SAS string is still the
    /// rollback (NFR-06: rollback is config-only at every phase).
    /// </summary>
    [Fact]
    public void Factory_SupportsBothNamespaceAndConnectionStringPaths()
    {
        var path = Path.Combine(SourceScan.RepoRoot, FactoryRelativePath);
        var code = SourceScan.CodeText(File.ReadAllLines(path));

        Assert.Contains("new ServiceBusClient(options.FullyQualifiedNamespace, credential)", code, StringComparison.Ordinal);
        Assert.Contains("new ServiceBusClient(options.ConnectionString)", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The factory must never resolve a credential itself — it takes the DI-injected, ClientId-pinned
    /// one. Five user-assigned identities exist in the dev subscription and one is named like the
    /// BFF's without being attached to it, so an unpinned <c>DefaultAzureCredential</c> can
    /// authenticate as the wrong principal; the symptom is a permissions error, which sends the
    /// investigation to RBAC rather than to the credential.
    /// </summary>
    /// <remarks>
    /// Asserted structurally rather than by passing null and catching <c>ArgumentNullException</c> —
    /// that shape is ADR-038 ban <b>B4</b> (do not test <c>ArgumentNullException.ThrowIfNull</c>),
    /// and it would only prove a guard ran, not that no fallback exists.
    /// </remarks>
    [Fact]
    public void Factory_NeverConstructsItsOwnCredential()
    {
        var path = Path.Combine(SourceScan.RepoRoot, FactoryRelativePath);
        var code = SourceScan.CodeText(File.ReadAllLines(path));

        Assert.DoesNotContain("new DefaultAzureCredential", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new ManagedIdentityCredential", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new ClientSecretCredential", code, StringComparison.Ordinal);
    }
}
