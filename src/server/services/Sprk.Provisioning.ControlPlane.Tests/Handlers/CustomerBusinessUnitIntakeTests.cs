// -----------------------------------------------------------------------------
// CustomerBusinessUnitIntakeTests.cs
//
// T259 (ISS-010) — the one rule for intake displayName (the customer business unit's name), shared by POST /api/runs
// and H10. The endpoint-level refusals (absent, blank, the Secure Record name in any case, whitespace, a control
// character) are pinned in RunsEndpointsTests and H10DataverseAppUserGraphParityHandlerTests; this file pins the
// boundaries those do not: the Dataverse length limit, and that a refusal never echoes a control character.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CustomerBusinessUnitIntakeTests
{
    private const string SecureUnitName = "Secure Record";

    private static CustomerBusinessUnitIntakeOutcome Validate(string value)
        => CustomerBusinessUnitIntake.Validate(
            new Dictionary<string, string> { [IntakeParameterCatalog.DisplayName] = value }, SecureUnitName);

    [Fact]
    public void ANameAtTheDataverseLimit_IsAccepted_AndOneCharacterMoreIsRefused()
    {
        Validate(new string('a', CustomerBusinessUnitIntake.MaxLength))
            .Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Valid>();

        Validate(new string('a', CustomerBusinessUnitIntake.MaxLength + 1))
            .Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(H10Rejections.CustomerDisplayNameInvalid);
    }

    [Theory]
    [InlineData("Acme Corporation")]
    [InlineData("Secure Records Ltd")]   // only the exact Secure Record unit name is refused
    [InlineData("O'Brien & Co")]
    public void AnOrdinaryName_IsAccepted_Verbatim(string name)
        => Validate(name).Should().Be(new CustomerBusinessUnitIntakeOutcome.Valid(name));

    [Fact]
    public void ARefusal_NeverEchoesAControlCharacter()
    {
        var invalid = Validate("Acme\u001b[31mCorp").Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Invalid>().Subject;

        invalid.Diagnostic.Should().NotContain("\u001b").And.Contain("control character");
    }
}
