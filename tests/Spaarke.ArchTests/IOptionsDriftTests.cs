using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Spaarke.ArchTests.Adr032;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// <b>Tier-1 IOptions inventory vs deployment channels</b> (customer-provisioning-orchestration-r1 task 204e, punch row B02;
/// deferred twice before: task 201 "Deferred #4" and the task 081.5 rollback).
///
/// <para><b>The failure this prevents.</b> A BFF options type registered with <c>.ValidateOnStart()</c> refuses to boot a stamp
/// whose settings do not satisfy it (the F20 / F20a SIGABRT chain, 2026-08-24). Every new such type is a new way for a freshly
/// provisioned stamp to die at <c>Host.StartAsync</c> — and nothing connected "this type demands key K" to "something
/// deploys K". <c>.claude/constraints/provisioning.md</c> states the rule in prose ("a new <c>ValidateOnStart</c> module MUST add
/// its entry to <c>per_env_settings</c>"); this test is the forcing function.</para>
///
/// <para><b>What it checks</b> (all deterministic, no network, &lt; 1 s):</para>
/// <list type="number">
///   <item>Every <c>AddOptions&lt;T&gt;()…ValidateOnStart()</c> chain in the BFF is inventoried (paren-aware, so a chain with a
///   lambda body is read whole).</item>
///   <item>For each <c>T</c>, the keys its startup validation can DEMAND: (a) every member that fails DataAnnotations on a
///   default-constructed instance — this is computed with the real <see cref="Validator"/>, not guessed from attributes; plus
///   (b) the keys listed for it in <see cref="Census"/> — needed for custom validators (<see cref="IValidateOptions{T}"/> or a
///   <c>.Validate(…)</c> lambda), which no reflection can read.</item>
///   <item>Each demanded key must be written by a <b>stamp deployment channel</b>: the canonical manifest
///   (<c>scripts/canonical-secret-catalog/manifest.yaml</c>: a secret's <c>app_settings</c> or a <c>per_env_settings</c> key) or the
///   customer stamp's Bicep app-settings (<c>infrastructure/bicep/customer.bicep</c>). <c>appsettings.template.json</c> is
///   deliberately NOT a channel: the BFF csproj sets it <c>CopyToPublishDirectory="Never"</c>, so a stamp never receives it.</item>
///   <item>A type with custom validation and no census entry fails — the author must say what it demands.</item>
///   <item>Census hygiene: an entry for a type that no longer has a chain, a <see cref="Disposition.KnownDrift"/> key that is now
///   supplied (remove the ledger line).</item>
/// </list>
///
/// <para><b>Deliberately not checked:</b> the manifest's <c>iOptionsModule</c> field. It is documentation (the manifest header says so), four
/// of its values already name no class (<c>AzureAdOptions</c>, <c>SpeOptions</c>, <c>AiSafetyOptions</c>, a free-text value), and a wrong
/// label breaks no stamp — checking it would be a rule with no failure behind it.</para>
///
/// <para><b>Why this is a per-PR ArchTest, not a nightly one</b> (the task asked for nightly): the whole
/// <c>tests/Spaarke.ArchTests</c> suite already runs BLOCKING on every PR (<c>ci-tier1-blocking.yml</c> job
/// <c>arch-tests</c>, filter deleted 2026-09-10). This test reads files and reflects over one assembly — sub-second — so a nightly
/// wiring would only delay the signal and need a second workflow edit. No workflow was changed.</para>
///
/// <para><b>Allowlist rule</b> (<c>tests/CLAUDE.md</c> "Structural fitness functions"): every <see cref="Census"/> entry carries a
/// written reason and an ADR / source citation; every <see cref="Disposition.KnownDrift"/> entry names a filed punch-list row.</para>
///
/// <para><b>KEEP path</b> (ADR-038 §7 / Amendment A1, eighth path): structural fitness function — survives <c>/test-diet</c>.</para>
///
/// <para><b>Maintenance procedure — you added or changed a <c>ValidateOnStart</c> options type and this test failed:</b></para>
/// <list type="bullet">
///   <item><c>NO CHANNEL</c> — the key is demanded at startup but nothing writes it for a stamp. Add it to
///   <c>per_env_settings</c> (or a secret's <c>app_settings</c>) in the manifest and regenerate (see
///   <c>.claude/patterns/provisioning/manifest-driven-secret-catalog.md</c>), or to <c>customer.bicep</c>'s app settings. Do NOT
///   weaken the validator to make this pass.</item>
///   <item><c>NO CENSUS ENTRY</c> — add the type to <see cref="Census"/> with the keys its validator demands, each marked
///   <see cref="Disposition.Supplied"/> (a channel writes it), <see cref="Disposition.Exempt"/> (a gate that is off for every stamp —
///   name it in <see cref="Guard.Gate"/>; the test fails if a stamp channel writes that gate) or <see cref="Disposition.KnownDrift"/> (demanded, not written, filed).</item>
///   <item><c>NOW SUPPLIED</c> — a channel now writes a key you had ledgered; change it to <see cref="Disposition.Supplied"/>.</item>
/// </list>
/// </summary>
public class IOptionsDriftTests
{
    // ───────────────────────────────────── census ─────────────────────────────────────

    internal enum Disposition
    {
        /// <summary>A stamp channel writes it — verified on every run.</summary>
        Supplied,
        /// <summary>Demanded only behind a gate that no stamp turns on — say which gate and who owns turning it on.</summary>
        Exempt,
        /// <summary>Demanded and NOT written by any channel: a confirmed deployment gap, filed. Fails the moment a channel supplies it.</summary>
        KnownDrift,
    }

    /// <param name="Gate">For <see cref="Disposition.Exempt"/> (required there): the boolean setting that turns the demand on. The
    /// check fails if a stamp channel writes it — an Exempt key whose gate a stamp sets is a demanded key nobody supplies.</param>
    internal sealed record Guard(string Key, Disposition Disposition, string Reason, string? Gate = null);

    /// <summary>
    /// The options types whose startup validation is CUSTOM (an <see cref="IValidateOptions{T}"/> or a <c>.Validate(…)</c> lambda)
    /// or whose demanded keys deserve an explicit disposition, with the keys they demand. A type validated only by DataAnnotations
    /// needs no entry unless one of its keys is not <see cref="Disposition.Supplied"/>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Guard[]> Census = new Dictionary<string, Guard[]>(StringComparer.Ordinal)
    {
        ["OnboardingOptions"] = new[]
        {
            new Guard("Onboarding:HmacSigningKey", Disposition.Exempt,
                "Lambda in OnboardingModule: required outside Development/Testing unless Onboarding:EnableDevBypass — and only when Onboarding:Enabled=true (task 258; default false, no stamp channel sets it). "
                + "The H0.5 consent callback is the Model 2 entry (a DAG root; Model 2 is out of scope, plan D3) and enqueues to L2's queue through the host's own Service Bus — a stamp's namespace has no such queue and L2 does not drain it — so no customer stamp can serve it. "
                + "A host that enables it supplies the key as a Key Vault reference. Gate: OnboardingModule.IsEnabled (registrations and route mapping).",
                Gate: "Onboarding:Enabled"),
        },
        ["PublicConfigOptions"] = new[]
        {
            new Guard("PublicConfig:BffUrl", Disposition.Supplied,
                "PublicConfigOptionsValidator (task 087 FR-36): required outside Development/Testing. Manifest per_env_settings PublicConfig__BffUrl (from-h2a-output:bff_url — StampBffUrl, the URL H9 records), task 258."),
            new Guard("PublicConfig:MsalClientId", Disposition.Supplied,
                "As above. Manifest per_env_settings PublicConfig__MsalClientId (from-h3-output:bff_app_client_id, = AzureAd__ClientId), task 258."),
            new Guard("PublicConfig:TenantId", Disposition.Supplied,
                "As above. Manifest per_env_settings PublicConfig__TenantId (from-intake-parameter:tenant_id, = AzureAd__TenantId), task 258."),
        },
        ["CredentialSelectionOptions"] = new[]
        {
            new Guard("Graph:Credentials:Order:0", Disposition.Supplied, "CredentialSelectionOptionsValidator + IdentityConfigurationValidator; ADR-028 A4. Manifest per_env_settings (auth-v4 §10.2 entry 1)."),
            new Guard("Graph:Credentials:RequireSecretFreeIdentity", Disposition.Supplied, "IdentityConfigurationValidator Rule 6; ADR-028 A4. Manifest per_env_settings (auth-v4 §10.2 entry 2)."),
        },
        ["GraphOptions"] = new[]
        {
            new Guard("Graph:ManagedIdentity:ClientId", Disposition.Supplied, "GraphOptionsValidator: required when Graph:ManagedIdentity:Enabled. Manifest per_env_settings (Graph__ManagedIdentity__ClientId)."),
            new Guard("Graph:Scopes", Disposition.Supplied,
                "[Required] + MinLength on string[]: empty by default, so startup fails without Graph__Scopes__0. Manifest per_env_settings literal Graph__Scopes__0 = https://graph.microsoft.com/.default, task 258."),
        },
        ["ServiceBusOptions"] = new[]
        {
            new Guard("ServiceBus:QueueName", Disposition.Supplied,
                "[Required] with an empty default. Manifest per_env_settings literal ServiceBus__QueueName = sdap-jobs, a queue customer.bicep creates (serviceBusQueues default; H4bBulkAppSettingsHandlerTests pins it), task 258."),
        },
        ["DocumentIntelligenceOptions"] = new[]
        {
            new Guard("DocumentIntelligence:OpenAiEndpoint", Disposition.Supplied, "DocumentIntelligenceOptionsValidator: required when DocumentIntelligence:Enabled. Manifest secret AzureOpenAI-Endpoint app_settings."),
            new Guard("DocumentIntelligence:AiSearchEndpoint", Disposition.Supplied, "DocumentIntelligenceOptionsValidator: required when RecordMatchingEnabled. Manifest secret AiSearch-Endpoint app_settings."),
            new Guard("DocumentIntelligence:AiSearchIndexName", Disposition.Exempt,
                "DocumentIntelligenceOptionsValidator: required only when DocumentIntelligence:RecordMatchingEnabled=true, which no stamp channel sets (default false). Whoever enables record matching for a stamp must add the key; the validator then names it at boot.",
                Gate: "DocumentIntelligence:RecordMatchingEnabled"),
        },
        ["AgentServiceOptions"] = new[]
        {
            new Guard("AgentService:Endpoint", Disposition.Exempt, "AgentServiceOptionsValidator: required only when AgentService:Enabled=true; the default is false and no stamp channel enables it (ADR-032: gated Foundry agent).", Gate: "AgentService:Enabled"),
            new Guard("AgentService:AgentId", Disposition.Exempt, "As AgentService:Endpoint (AgentServiceOptionsValidator, ADR-032).", Gate: "AgentService:Enabled"),
        },
        ["CustomerOptions"] = new[]
        {
            new Guard("Customer:Id", Disposition.Supplied, "CustomerOptionsValidator (D-14, T238): required in Production. Manifest per_env_settings Customer__Id and customer.bicep Customer__Id."),
        },
        ["WorkforceIdentityOptions"] = Array.Empty<Guard>(), // WorkforceIdentityOptionsValidator checks the SHAPE of CustomerTenantIds elements only; an empty list is valid, so no key is demanded.
    };

    // ───────────────────────────────────── channels ─────────────────────────────────────

    /// <summary>Config keys (colon form, case-insensitive) written for a stamp by the manifest or the stamp Bicep.</summary>
    internal sealed class Channels
    {
        public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ManifestModules { get; } = new(StringComparer.Ordinal);

        /// <summary>Is <paramref name="key"/> written — directly, or as an indexed child (<c>Graph:Scopes</c> ← <c>Graph:Scopes:0</c>)?</summary>
        public bool Writes(string key)
            => Keys.Contains(key) || Keys.Any(k => k.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string settingName) => settingName.Replace("__", ":", StringComparison.Ordinal);

    internal static Channels ParseChannels(string manifestYaml, string bicepText)
    {
        var channels = new Channels();
        var inAppSettings = false;
        foreach (var raw in manifestYaml.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Regex.IsMatch(line, @"^\s*app_settings:\s*$")) { inAppSettings = true; continue; }
            var item = Regex.Match(line, @"^\s*-\s*'(?<v>[^']+)'\s*$");
            if (inAppSettings && item.Success) { channels.Keys.Add(Normalize(item.Groups["v"].Value)); continue; }
            if (inAppSettings && !Regex.IsMatch(line, @"^\s*(#.*)?$")) inAppSettings = false;

            var key = Regex.Match(line, @"^\s*-\s*key:\s*'(?<v>[^']+)'");
            if (key.Success) channels.Keys.Add(Normalize(key.Groups["v"].Value));

            var module = Regex.Match(line, @"^\s*iOptionsModule:\s*'(?<v>[^']+)'");
            if (module.Success) channels.ManifestModules.Add(module.Groups["v"].Value);
        }

        // Bicep app-settings: object-literal members such as `Customer__Id: customerId` / `'Redis__Enabled': 'true'`
        // and `{ name: 'X__Y', value: … }` entries.
        foreach (Match m in Regex.Matches(bicepText, @"(?m)^\s*'?(?<k>[A-Za-z][A-Za-z0-9]*(?:__[A-Za-z0-9]+)+)'?\s*:"))
            channels.Keys.Add(Normalize(m.Groups["k"].Value));
        foreach (Match m in Regex.Matches(bicepText, @"name:\s*'(?<k>[A-Za-z][A-Za-z0-9]*(?:__[A-Za-z0-9]+)+)'"))
            channels.Keys.Add(Normalize(m.Groups["k"].Value));
        return channels;
    }

    // ───────────────────────────────────── inventory ─────────────────────────────────────

    internal sealed record Chain(string TypeText, string File, int Line, bool CustomValidate, string? Section);

    private static readonly Regex AddOptionsRx = new(@"\bAddOptions(?<w>WithValidateOnStart)?\s*<\s*(?<t>[\w\.]+)\s*>", RegexOptions.Compiled);

    /// <summary>Every options chain that ends in <c>ValidateOnStart</c>. Paren-aware: a lambda body inside the chain does not end it.</summary>
    internal static IReadOnlyList<Chain> Inventory(IEnumerable<(string File, string Source)> sources)
    {
        var chains = new List<Chain>();
        foreach (var (file, source) in sources)
        {
            var blank = DiRegistrationScan.Blank(source);
            foreach (Match m in AddOptionsRx.Matches(blank))
            {
                int depth = 0, end = blank.Length;
                for (var i = m.Index; i < blank.Length; i++)
                {
                    var c = blank[i];
                    if (c is '(' or '{' or '[') depth++;
                    else if (c is ')' or '}' or ']') depth--;
                    else if (c == ';' && depth <= 0) { end = i; break; }
                }

                var blankChain = blank.Substring(m.Index, end - m.Index);
                if (!m.Groups["w"].Success && !blankChain.Contains("ValidateOnStart(", StringComparison.Ordinal)) continue;

                var origChain = source.Substring(m.Index, end - m.Index);
                var sm = Regex.Match(origChain, @"GetSection\(\s*(?:""(?<lit>[^""]+)""|(?<ty>[\w\.]+)\.SectionName)");
                var section = !sm.Success ? null : sm.Groups["lit"].Success ? sm.Groups["lit"].Value : "type:" + sm.Groups["ty"].Value;
                chains.Add(new Chain(m.Groups["t"].Value, file, source.Take(m.Index).Count(ch => ch == '\n') + 1,
                    blankChain.Contains(".Validate(", StringComparison.Ordinal), section));
            }
        }

        return chains;
    }

    internal sealed class Findings
    {
        public List<string> NoCensusEntry { get; } = new();
        public List<string> NoChannel { get; } = new();
        public List<string> NowSupplied { get; } = new();
        public List<string> StaleCensus { get; } = new();
        public List<string> UnknownSection { get; } = new();
        public List<string> BadReason { get; } = new();
        public List<string> GateOn { get; } = new();
        public int TypesInventoried { get; set; }
        public bool Any => NoCensusEntry.Count + NoChannel.Count + NowSupplied.Count + StaleCensus.Count + UnknownSection.Count + BadReason.Count + GateOn.Count > 0;
    }

    internal static Findings Check(
        IReadOnlyList<Chain> chains,
        Func<string, Type?> resolve,
        Channels channels,
        IReadOnlyDictionary<string, Guard[]> census)
    {
        var f = new Findings();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chain in chains)
        {
            var simple = chain.TypeText.Split('.').Last();
            if (!seenTypes.Add(simple)) continue;
            f.TypesInventoried++;

            var type = resolve(chain.TypeText);
            if (type is null) { f.UnknownSection.Add($"{simple}: type not found in the scanned assembly ({chain.File}:{chain.Line})"); continue; }

            var section = chain.Section;
            if (section is not null && section.StartsWith("type:", StringComparison.Ordinal))
                section = resolve(section[5..]) is { } holder ? ConstSectionName(holder) : null;
            section ??= ConstSectionName(type);

            var hasValidator = type.Assembly.GetTypes().Any(x => !x.IsAbstract && x.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidateOptions<>) && i.GetGenericArguments()[0] == type));
            census.TryGetValue(simple, out var guards);

            if ((hasValidator || chain.CustomValidate) && guards is null)
                f.NoCensusEntry.Add($"{simple} ({chain.File}:{chain.Line}) has custom startup validation (IValidateOptions<{simple}> or a .Validate(…) lambda) but no entry in IOptionsDriftTests.Census");

            // keys demanded by DataAnnotations on a default-constructed instance
            var demanded = new Dictionary<string, Guard?>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in DefaultInstanceFailures(type))
            {
                if (section is null) { f.UnknownSection.Add($"{simple}.{member} fails DataAnnotations on defaults but the config section could not be derived ({chain.File}:{chain.Line})"); continue; }
                demanded[$"{section}:{member}"] = null;
            }

            foreach (var g in guards ?? Array.Empty<Guard>())
            {
                demanded[g.Key] = g;
                if (string.IsNullOrWhiteSpace(g.Reason) || g.Reason.Length < 25)
                    f.BadReason.Add($"{simple} / {g.Key}: census entries need a written reason (and an ADR / source citation)");
                if (g.Disposition == Disposition.Exempt && string.IsNullOrWhiteSpace(g.Gate))
                    f.BadReason.Add($"{simple} / {g.Key}: an Exempt entry names its Gate (the setting that turns the demand on)");
            }

            foreach (var (key, guard) in demanded)
            {
                var written = channels.Writes(key);
                switch (guard?.Disposition ?? Disposition.Supplied)
                {
                    case Disposition.Supplied when !written:
                        f.NoChannel.Add($"{simple} demands '{key}' at startup but no manifest app_settings / per_env_settings entry and no customer.bicep app setting writes it");
                        break;
                    case Disposition.KnownDrift when written:
                        f.NowSupplied.Add($"{simple} / '{key}' is ledgered KnownDrift but a channel now writes it — change the census entry to Supplied");
                        break;
                    case Disposition.Exempt when !written && guard!.Gate is { Length: > 0 } gate && channels.Writes(gate):
                        f.GateOn.Add($"{simple} / '{key}' is Exempt behind '{gate}', but a stamp channel writes '{gate}' — supply '{key}' (a secret: a Key Vault reference) or stop writing the gate");
                        break;
                }
            }
        }

        foreach (var name in census.Keys)
            if (!seenTypes.Contains(name))
                f.StaleCensus.Add($"Census entry '{name}' has no AddOptions<{name}>()…ValidateOnStart() chain any more — remove it");

        return f;
    }

    private static string? ConstSectionName(Type t)
        => t.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;

    /// <summary>Members that fail DataAnnotations on <c>new T()</c> — i.e. those the configuration MUST populate.</summary>
    private static IEnumerable<string> DefaultInstanceFailures(Type type)
    {
        object instance;
        try { instance = Activator.CreateInstance(type)!; }
        catch (Exception) { return Array.Empty<string>(); }

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal).ToList();
    }

    // ───────────────────────────────────── the real tree ─────────────────────────────────────

    private static string ReadRepo(params string[] parts) => File.ReadAllText(Path.Combine(new[] { SourceScan.RepoRoot }.Concat(parts).ToArray()));

    private static IEnumerable<(string File, string Source)> BffSources()
    {
        var bff = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        return Directory.EnumerateFiles(bff, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (SourceScan.Relative(f).Replace('\\', '/'), File.ReadAllText(f)))
            .ToList();
    }

    private static readonly Lazy<Dictionary<string, List<Type>>> BffTypes = new(() =>
        typeof(Program).Assembly.GetTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList()));

    private static Type? ResolveBffType(string text)
    {
        var simple = text.Split('.').Last();
        return BffTypes.Value.TryGetValue(simple, out var list)
            ? list.FirstOrDefault(t => (t.FullName ?? "").EndsWith(text, StringComparison.Ordinal)) ?? list[0]
            : null;
    }

    [Fact(DisplayName = "IOptions inventory: every ValidateOnStart options type's demanded keys are written by a stamp deployment channel")]
    public void IOptionsInventoryMustMatchManifest()
    {
        var channels = ParseChannels(
            ReadRepo("scripts", "canonical-secret-catalog", "manifest.yaml"),
            ReadRepo("infrastructure", "bicep", "customer.bicep"));
        var chains = Inventory(BffSources());

        // The inventory is only worth anything if it found the tree: the BFF registers ~30 such chains, and the paren-aware
        // reader found them all when this was written. A parser regression that silently finds nothing must not read as "clean".
        Assert.True(chains.Select(c => c.TypeText).Distinct().Count() >= 25,
            $"Inventory found only {chains.Select(c => c.TypeText).Distinct().Count()} ValidateOnStart options types; the BFF has ~33. The chain reader regressed.");
        Assert.True(channels.Keys.Count >= 40 && channels.ManifestModules.Count >= 5,
            $"Channel parse found {channels.Keys.Count} keys / {channels.ManifestModules.Count} modules; the manifest + customer.bicep carry far more. The parser regressed.");

        var findings = Check(chains, ResolveBffType, channels, Census);

        Assert.True(!findings.Any, Report(findings));
    }

    private static string Report(Findings f)
    {
        var sb = new System.Text.StringBuilder("IOptions inventory drift (Tier-1 ValidateOnStart types vs stamp deployment channels). Procedure: see the header of IOptionsDriftTests.\n");
        void Section(string title, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"  {title}:");
            foreach (var i in items) sb.AppendLine("    - " + i);
        }

        Section("NO CHANNEL", f.NoChannel);
        Section("NO CENSUS ENTRY", f.NoCensusEntry);
        Section("NOW SUPPLIED (remove the KnownDrift ledger line)", f.NowSupplied);
        Section("STALE CENSUS ENTRY", f.StaleCensus);
        Section("UNRESOLVED", f.UnknownSection);
        Section("MISSING REASON / GATE", f.BadReason);
        Section("EXEMPT BUT GATE ON", f.GateOn);
        return sb.ToString();
    }

    // ───────────────────────────────────── controls (negative + positive) ─────────────────────────────────────
    // Fixture options types. Names are prefixed Fx so a resolver over this assembly cannot collide with BFF types.

    public sealed class FxRequiredOptions
    {
        public const string SectionName = "FxSection";
        [Required] public string Endpoint { get; set; } = string.Empty;
        public int Retries { get; set; } = 3;
    }

    public sealed class FxDefaultsOptions
    {
        [Range(1, 10)] public int Retries { get; set; } = 3;
    }

    public sealed class FxCustomOptions
    {
        public string? Token { get; set; }
    }

    private static Type? ResolveFixture(string text)
        => typeof(IOptionsDriftTests).GetNestedTypes().FirstOrDefault(t => t.Name == text.Split('.').Last());

    private const string FixtureModule = """
        public static class FxModule
        {
            public static IServiceCollection AddFx(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
            {
                services.AddOptions<FxRequiredOptions>()
                    .Bind(configuration.GetSection(FxRequiredOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddOptions<FxDefaultsOptions>().Bind(configuration.GetSection("FxDefaults")).ValidateDataAnnotations().ValidateOnStart();

                services.AddOptions<FxCustomOptions>()
                    .Bind(configuration.GetSection("FxCustom"))
                    .Validate(
                        o =>
                        {
                            if (env.IsDevelopment()) { return true; }
                            return !string.IsNullOrEmpty(o.Token);
                        },
                        "FxCustom:Token is required in deployed environments")
                    .ValidateOnStart();

                // no ValidateOnStart: must NOT be inventoried
                services.AddOptions<FxIgnoredOptions>().Bind(configuration.GetSection("FxIgnored"));
                return services;
            }
        }
        """;

    private static IReadOnlyList<Chain> FixtureChains() => Inventory(new[] { ("FxModule.cs", FixtureModule) });

    [Fact(DisplayName = "control: the chain reader reads a lambda-bodied chain whole and ignores a chain without ValidateOnStart")]
    public void Control_InventoryReadsTheFixtureChains()
    {
        var chains = FixtureChains();
        Assert.Equal(new[] { "FxRequiredOptions", "FxDefaultsOptions", "FxCustomOptions" }, chains.Select(c => c.TypeText).ToArray());
        Assert.True(chains.Single(c => c.TypeText == "FxCustomOptions").CustomValidate, "the .Validate(…) lambda is the custom-validation signal");
        Assert.False(chains.Single(c => c.TypeText == "FxRequiredOptions").CustomValidate);
        Assert.Equal("type:FxRequiredOptions", chains.Single(c => c.TypeText == "FxRequiredOptions").Section);
        Assert.Equal("FxDefaults", chains.Single(c => c.TypeText == "FxDefaultsOptions").Section);
    }

    private static readonly IReadOnlyDictionary<string, Guard[]> NoCensus = new Dictionary<string, Guard[]>();

    [Fact(DisplayName = "control (negative): a [Required] key that no channel writes, and an uncensused custom validator, are both reported")]
    public void Control_Negative_ReportsMissingChannelAndMissingCensusEntry()
    {
        var channels = ParseChannels("per_env_settings:\n  - key: 'Other__Thing'\n", "");
        var f = Check(FixtureChains(), ResolveFixture, channels, NoCensus);

        Assert.Contains(f.NoChannel, m => m.Contains("FxRequiredOptions") && m.Contains("FxSection:Endpoint"));
        Assert.Contains(f.NoCensusEntry, m => m.Contains("FxCustomOptions"));
        Assert.DoesNotContain(f.NoChannel, m => m.Contains("FxDefaultsOptions")); // safe defaults demand nothing
        Assert.True(f.Any);
    }

    [Fact(DisplayName = "control (positive): the same module is clean when the manifest writes the key and the custom validator is censused")]
    public void Control_Positive_CleanWhenChannelWritesTheKeyAndTheValidatorIsCensused()
    {
        var census = new Dictionary<string, Guard[]>
        {
            ["FxCustomOptions"] = new[] { new Guard("FxCustom:Token", Disposition.Supplied, "Fixture: custom lambda demands the token outside Development; the manifest writes it.") },
        };
        var channels = ParseChannels("per_env_settings:\n  - key: 'FxSection__Endpoint'\nsecrets:\n  - canonical_name: 'X'\n    app_settings:\n      - 'FxCustom__Token'\n", "");

        var f = Check(FixtureChains(), ResolveFixture, channels, census);

        Assert.False(f.Any, Report(f));
        Assert.Equal(3, f.TypesInventoried);
    }

    [Fact(DisplayName = "control (positive): a Bicep app-setting object-literal member is a channel")]
    public void Control_Positive_BicepAppSettingIsAChannel()
    {
        var channels = ParseChannels("", "appSettings: {\n  FxSection__Endpoint: 'https://x'\n  'Other__Y': 'z'\n}\n");
        Assert.True(channels.Writes("FxSection:Endpoint"));
        Assert.True(channels.Writes("Other:Y"));
        Assert.False(channels.Writes("FxSection:Missing"));
    }

    [Fact(DisplayName = "control: an indexed setting (Graph__Scopes__0) satisfies the array key (Graph:Scopes)")]
    public void Control_IndexedSettingSatisfiesTheArrayKey()
    {
        var channels = ParseChannels("per_env_settings:\n  - key: 'Graph__Scopes__0'\n", "");
        Assert.True(channels.Writes("Graph:Scopes"));
        Assert.False(channels.Writes("Graph:Scope"));
    }

    [Fact(DisplayName = "control (negative): a KnownDrift key that a channel now writes, and a census entry with no chain, are reported")]
    public void Control_Negative_ReportsStaleLedgerEntries()
    {
        var census = new Dictionary<string, Guard[]>
        {
            ["FxRequiredOptions"] = new[] { new Guard("FxSection:Endpoint", Disposition.KnownDrift, "Fixture: ledgered as not written, filed as a punch-list row.") },
            ["FxGoneOptions"] = new[] { new Guard("Gone:Key", Disposition.Exempt, "Fixture: a type that no longer has a ValidateOnStart chain.") },
            ["FxCustomOptions"] = Array.Empty<Guard>(),
        };
        var channels = ParseChannels("per_env_settings:\n  - key: 'FxSection__Endpoint'\n", "");

        var f = Check(FixtureChains(), ResolveFixture, channels, census);

        Assert.Contains(f.NowSupplied, m => m.Contains("FxSection:Endpoint"));
        Assert.Contains(f.StaleCensus, m => m.Contains("FxGoneOptions"));
    }

    [Fact(DisplayName = "control (negative): an Exempt key whose gate a stamp channel writes is reported; an Exempt entry without a gate too")]
    public void Control_Negative_ReportsExemptKeyWhoseGateIsOn()
    {
        var gated = new Dictionary<string, Guard[]>
        {
            ["FxCustomOptions"] = new[] { new Guard("FxCustom:Token", Disposition.Exempt, "Fixture: demanded only when FxCustom:Enabled; no stamp sets it.", Gate: "FxCustom:Enabled") },
        };
        var clean = Check(FixtureChains(), ResolveFixture, ParseChannels("per_env_settings:\n  - key: 'FxSection__Endpoint'\n", ""), gated);
        Assert.False(clean.Any, Report(clean));

        var gateOn = Check(FixtureChains(), ResolveFixture,
            ParseChannels("per_env_settings:\n  - key: 'FxSection__Endpoint'\n  - key: 'FxCustom__Enabled'\n", ""), gated);
        Assert.Contains(gateOn.GateOn, m => m.Contains("FxCustom:Token") && m.Contains("FxCustom:Enabled"));

        var noGate = new Dictionary<string, Guard[]>
        {
            ["FxCustomOptions"] = new[] { new Guard("FxCustom:Token", Disposition.Exempt, "Fixture: an Exempt entry that does not say which gate.") },
        };
        var missing = Check(FixtureChains(), ResolveFixture, ParseChannels("per_env_settings:\n  - key: 'FxSection__Endpoint'\n", ""), noGate);
        Assert.Contains(missing.BadReason, m => m.Contains("FxCustom:Token") && m.Contains("Gate"));
    }

    [Fact(DisplayName = "control (negative): a census entry without a written reason is reported")]
    public void Control_Negative_ReportsMissingReason()
    {
        var census = new Dictionary<string, Guard[]> { ["FxCustomOptions"] = new[] { new Guard("FxCustom:Token", Disposition.Exempt, "") } };
        var f = Check(FixtureChains(), ResolveFixture, ParseChannels("per_env_settings:\n  - key: 'FxSection__Endpoint'\n", ""), census);
        Assert.Contains(f.BadReason, m => m.Contains("FxCustom:Token"));
    }
}

/// <summary>Referenced by the fixture module text only (it has no ValidateOnStart); never resolved.</summary>
internal sealed class FxIgnoredOptions { }
