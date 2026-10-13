// spaarke-SPA-external-access-platform-r3, owner decision 2026-10-11 (part 3): the grant / registration email's system
// sender is CONFIGURATION (Communication:DefaultMailbox), not the literal "demo@demo.spaarke.com" that
// RegistrationEmailService.FromMailbox used to be. The configured value is what is sent from; a missing value fails the
// host's start outside Development/Testing; the shipped template carries the central mailbox.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.OptionsValidation;

[Trait("Category", "Configuration")]
public class SystemSenderMailboxConfigurationTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Sprk.Bff.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static FakeHostEnvironment Env(string name) => new() { EnvironmentName = name };

    // ── the configured value is the sender ────────────────────────────────────────────────────────

    [Fact]
    public void TheConfiguredMailbox_IsTheSender_NotTheOldLiteral()
    {
        var sender = RegistrationEmailService.ResolveFromMailbox(new CommunicationOptions { DefaultMailbox = "ops-sender@customer.test" });

        sender.Should().Be("ops-sender@customer.test");
        sender.Should().NotBe("demo@demo.spaarke.com");
    }

    [Fact]
    public void TheConfiguredMailbox_ReachesTheRequestTheNotificationSends()
    {
        // BuildPortalNotificationRequest is what SendPortalNotificationEmailAsync sends; the mailbox it is given is the
        // resolved configuration value.
        using var stream = typeof(RegistrationEmailService).Assembly.GetManifestResourceStream(
            "Sprk.Bff.Api.Services.Registration.EmailTemplates.PortalNotificationTemplate.html");
        var template = new StreamReader(stream!).ReadToEnd();
        var from = RegistrationEmailService.ResolveFromMailbox(new CommunicationOptions { DefaultMailbox = "  ops-sender@customer.test " });

        var request = RegistrationEmailService.BuildPortalNotificationRequest(
            template, "partner@outside.test", "S", "H", "M", "https://portal.test/", "Open", "corr", 2026, from);

        request.FromMailbox.Should().Be("ops-sender@customer.test", "trimmed, and exactly the configured value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ASendWithNoConfiguredMailbox_Throws_RatherThanSendingFromASubstitute(string? configured)
    {
        var act = () => RegistrationEmailService.ResolveFromMailbox(new CommunicationOptions { DefaultMailbox = configured! });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Communication:DefaultMailbox*");
    }

    // ── a missing value fails validation (and so the host's start) ────────────────────────────────

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", "")]
    [InlineData("Demo", "")]
    public void AMissingMailbox_OutsideDevelopmentAndTesting_FailsValidation_NamingTheKey(string environment, string mailbox)
    {
        var result = new CommunicationOptionsValidator(Env(environment)).Validate(null, new CommunicationOptions { DefaultMailbox = mailbox });

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("Communication:DefaultMailbox");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AMissingMailbox_InDevelopmentOrTesting_Succeeds(string environment)
        => new CommunicationOptionsValidator(Env(environment)).Validate(null, new CommunicationOptions { DefaultMailbox = "" })
            .Succeeded.Should().BeTrue();

    private static CommunicationOptions Options(string? defaultMailbox, params string[] approved) => new()
    {
        DefaultMailbox = defaultMailbox,
        ApprovedSenders = approved.Select(e => new ApprovedSenderConfig { Email = e, DisplayName = "Test" }).ToArray(),
    };

    [Fact]
    public void AConfiguredMailbox_ThatIsAnApprovedSender_SucceedsInProduction()
        => new CommunicationOptionsValidator(Env(Environments.Production))
            .Validate(null, Options("mailbox-central@spaarke.com", "mailbox-central@spaarke.com"))
            .Succeeded.Should().BeTrue();

    [Fact]
    public void TheApprovedSenderMatch_IgnoresCase_AndMayBeAnyEntry()
        => new CommunicationOptionsValidator(Env(Environments.Production))
            .Validate(null, Options("Mailbox-Central@Spaarke.com", "other@spaarke.com", "mailbox-central@SPAARKE.com"))
            .Succeeded.Should().BeTrue();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Demo")]
    public void AConfiguredMailbox_ThatIsNotAnApprovedSender_FailsValidation_NamingBothKeys(string environment)
    {
        // The send path rejects an explicit FromMailbox that is not an approved sender (InvalidSender) and the grant email
        // is dropped, so a non-blank DefaultMailbox is not enough.
        var result = new CommunicationOptionsValidator(Env(environment))
            .Validate(null, Options("mailbox-central@spaarke.com", "someone-else@spaarke.com"));

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("Communication:DefaultMailbox").And.Contain("Communication:ApprovedSenders");
    }

    [Fact]
    public void AConfiguredMailbox_WithNoApprovedSendersAtAll_FailsValidation_InProduction()
        => new CommunicationOptionsValidator(Env(Environments.Production))
            .Validate(null, Options("mailbox-central@spaarke.com"))
            .Failed.Should().BeTrue();

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AnUnapprovedMailbox_InDevelopmentOrTesting_Succeeds(string environment)
        => new CommunicationOptionsValidator(Env(environment))
            .Validate(null, Options("mailbox-central@spaarke.com"))
            .Succeeded.Should().BeTrue();

    [Fact]
    public void ReadingTheOptions_InProduction_WithNoMailbox_ThrowsOptionsValidationException_AsTheHostStartDoes()
    {
        // StartupValidationService reads IOptions<CommunicationOptions>.Value at host start; the validator registered by
        // CommunicationModule turns an empty DefaultMailbox into the exception that stops the host.
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(Env(Environments.Production));
        services.Configure<CommunicationOptions>(o => o.DefaultMailbox = string.Empty);
        services.AddSingleton<IValidateOptions<CommunicationOptions>, CommunicationOptionsValidator>();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CommunicationOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Failures.Should().Contain(f => f.Contains("Communication:DefaultMailbox"));
    }

    [Fact]
    public void ReadingTheOptions_InProduction_WithAnUnapprovedMailbox_ThrowsOptionsValidationException_AsTheHostStartDoes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(Env(Environments.Production));
        services.Configure<CommunicationOptions>(o =>
        {
            o.DefaultMailbox = "mailbox-central@spaarke.com";
            o.ApprovedSenders = new[] { new ApprovedSenderConfig { Email = "someone-else@spaarke.com", DisplayName = "Test" } };
        });
        services.AddSingleton<IValidateOptions<CommunicationOptions>, CommunicationOptionsValidator>();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<CommunicationOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Which.Failures.Should().Contain(f => f.Contains("Communication:ApprovedSenders"));
    }

    // ── the deploy scripts supply BOTH keys ──────────────────────────────────────────────────────

    private static string ReadRepoFile(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray())))
            dir = dir.Parent;
        dir.Should().NotBeNull($"{string.Join('/', relative)} is part of the repository checkout");
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(relative).ToArray()));
    }

    [Fact]
    public void ConfigureProductionAppSettings_EmitsTheApprovedSenderNextToTheDefaultMailbox()
    {
        var text = ReadRepoFile("scripts", "Configure-ProductionAppSettings.ps1");

        text.Should().Contain("\"Communication__DefaultMailbox=$(KVRef 'Communication-DefaultMailbox')\"");
        text.Should().Contain("\"Communication__ApprovedSenders__0__Email=$(KVRef 'Communication-DefaultMailbox')\"",
            "the sender must be the same Key Vault value as DefaultMailbox, or the validator fails the host's start");
        text.Should().Contain("\"Communication__ApprovedSenders__0__IsDefault=true\"");
        text.Should().Contain("\"Communication__ApprovedSenders__0__DisplayName=Spaarke Central\"");
    }

    [Fact]
    public void TheGeneratedAppServiceSettings_EmitTheApprovedSenderNextToTheDefaultMailbox()
    {
        // GENERATED from scripts/canonical-secret-catalog/manifest.yaml; edit the manifest and regenerate, never this file.
        var text = ReadRepoFile("scripts", "canonical-secret-catalog", "generated", "Configure-AppServiceSettings.generated.ps1");

        text.Should().Contain("\"Communication__DefaultMailbox=$(Format-KvRef 'Communication-DefaultMailbox')\"");
        text.Should().Contain("\"Communication__ApprovedSenders__0__Email=$(Format-KvRef 'Communication-DefaultMailbox')\"");
        text.Should().Contain("\"Communication__ApprovedSenders__0__IsDefault=true\"");
        text.Should().Contain("\"Communication__ApprovedSenders__0__DisplayName=Spaarke Central\"");
    }

    // ── the shipped template carries the central mailbox ─────────────────────────────────────────

    [Fact]
    public void TheTemplate_CarriesTheCentralMailbox_AsDefaultAndAsAnApprovedSender()
    {
        // Documents intent only: the template is CopyToPublishDirectory=Never, so a deployed host never receives it. What a
        // deployed host gets is App Settings, pinned by the two script tests above.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "appsettings.template.json")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the template is part of the repository checkout");

        // Text assertions, not a JSON parse: the template carries unquoted deploy-time #{TOKEN}# placeholders elsewhere
        // (e.g. numeric settings), so it is not valid JSON until the pipeline substitutes them.
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "server", "api", "Sprk.Bff.Api", "appsettings.template.json"));

        text.Should().MatchRegex("\"DefaultMailbox\"\\s*:\\s*\"mailbox-central@spaarke\\.com\"");
        text.Should().MatchRegex("\"_DefaultMailbox_comment\"\\s*:\\s*\"[^\"]+\"");
        text.Should().MatchRegex("\"ApprovedSenders\"\\s*:\\s*\\[\\s*\\{\\s*\"Email\"\\s*:\\s*\"mailbox-central@spaarke\\.com\"",
            "ApprovedSenderValidator rejects a default mailbox that is not an approved sender");
        text.Should().NotContain("#{COMMUNICATION_DEFAULT_MAILBOX}#", "the sender is no longer a deploy-time token");
    }
}
