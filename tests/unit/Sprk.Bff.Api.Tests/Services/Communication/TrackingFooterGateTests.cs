using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tracking-footer resolver (FR-A1 / ADR-018) tests: the per-tenant IOptionsMonitor resolution behavior
/// (global, per-tenant override field-by-field, unknown-tenant fallback, disabled = no footer, CurrentValue
/// freshness). Mirrors the AutoFileGate resolution contract. Tests behavior, not DI wiring (ADR-038).
/// </summary>
public class TrackingFooterGateTests
{
    private static TrackingFooterGate Gate(TrackingFooterOptions options) =>
        new(Mock.Of<IOptionsMonitor<TrackingFooterOptions>>(m => m.CurrentValue == options));

    private static TrackingFooterOptions Global(bool enabled = true, string template = "Filed re {record-ref}. Ref: {signed-token}", string secret = "footer-hmac-key") =>
        new() { Enabled = enabled, MessageTemplate = template, SigningKeySecretName = secret };

    [Fact]
    public void Resolve_NullTenant_ReturnsGlobalSettings()
    {
        var gate = Gate(Global(enabled: true, template: "Global template {record-ref}", secret: "kv-secret-name"));

        var settings = gate.Resolve(null);

        settings.Enabled.Should().BeTrue();
        settings.MessageTemplate.Should().Be("Global template {record-ref}");
        settings.SigningKeySecretName.Should().Be("kv-secret-name");
    }

    [Fact]
    public void Resolve_PerTenantOverride_ReplacesFieldsFieldByField()
    {
        var options = Global(enabled: true, template: "GLOBAL", secret: "global-secret");
        options.Tenants["tenant-a"] = new TrackingFooterTenantOverride
        {
            Enabled = false,
            MessageTemplate = "TENANT-A template",
            SigningKeySecretName = "tenant-a-secret",
        };
        var gate = Gate(options);

        var settings = gate.Resolve("tenant-a");

        settings.Enabled.Should().BeFalse();
        settings.MessageTemplate.Should().Be("TENANT-A template");
        settings.SigningKeySecretName.Should().Be("tenant-a-secret");
    }

    [Fact]
    public void Resolve_PerTenantOverride_UnsetFieldsInheritGlobal()
    {
        var options = Global(enabled: true, template: "GLOBAL template", secret: "global-secret");
        // Only the template is overridden — Enabled + secret name inherit the global values.
        options.Tenants["tenant-b"] = new TrackingFooterTenantOverride { MessageTemplate = "TENANT-B only" };
        var gate = Gate(options);

        var settings = gate.Resolve("tenant-b");

        settings.MessageTemplate.Should().Be("TENANT-B only", "the overridden field wins");
        settings.Enabled.Should().BeTrue("unset override fields inherit the global value");
        settings.SigningKeySecretName.Should().Be("global-secret", "unset override fields inherit the global value");
    }

    [Fact]
    public void Resolve_UnknownTenant_ReturnsGlobal()
    {
        var options = Global(enabled: true, template: "GLOBAL", secret: "global-secret");
        options.Tenants["known"] = new TrackingFooterTenantOverride { Enabled = false };
        var gate = Gate(options);

        var settings = gate.Resolve("unknown-tenant");

        settings.Enabled.Should().BeTrue();
        settings.MessageTemplate.Should().Be("GLOBAL");
    }

    [Fact]
    public void Resolve_DisabledGlobally_SignalsNoFooter()
    {
        var gate = Gate(Global(enabled: false));

        gate.Resolve(null).Enabled.Should().BeFalse("012 injects nothing when the footer is disabled (NFR-04)");
    }

    [Fact]
    public void Resolve_DisabledPerTenant_SignalsNoFooter_EvenWhenGlobalEnabled()
    {
        var options = Global(enabled: true);
        options.Tenants["opted-out"] = new TrackingFooterTenantOverride { Enabled = false };
        var gate = Gate(options);

        gate.Resolve("opted-out").Enabled.Should().BeFalse();
    }

    [Fact]
    public void Resolve_TemplateEditedAfterFirstResolve_ReflectedOnNextResolve()
    {
        // ADR-018 freshness: CurrentValue is read on every call, so an operator's template edit takes effect
        // on the next send with no redeploy.
        var options = Global(enabled: true, template: "ORIGINAL");
        var gate = Gate(options);
        gate.Resolve(null).MessageTemplate.Should().Be("ORIGINAL");

        options.MessageTemplate = "EDITED WITHOUT REDEPLOY";

        gate.Resolve(null).MessageTemplate.Should().Be("EDITED WITHOUT REDEPLOY");
    }

    [Fact]
    public void Resolve_SigningKeySecretName_IsTheKeyVaultSecretName_NotKeyMaterial()
    {
        // NFR-07 / ADR-028: the settings carry the Key Vault secret NAME (an identifier the signer resolves),
        // never the key bytes. TrackingFooterOptions has no key field by design.
        var gate = Gate(Global(secret: "spaarke-footer-hmac"));

        gate.Resolve(null).SigningKeySecretName.Should().Be("spaarke-footer-hmac");
    }
}
