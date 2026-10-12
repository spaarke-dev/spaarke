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

    [Fact]
    public void AConfiguredMailbox_SucceedsInProduction()
        => new CommunicationOptionsValidator(Env(Environments.Production))
            .Validate(null, new CommunicationOptions { DefaultMailbox = "mailbox-central@spaarke.com" })
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

    // ── the shipped template carries the central mailbox ─────────────────────────────────────────

    [Fact]
    public void TheTemplate_CarriesTheCentralMailbox_AsDefaultAndAsAnApprovedSender()
    {
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
