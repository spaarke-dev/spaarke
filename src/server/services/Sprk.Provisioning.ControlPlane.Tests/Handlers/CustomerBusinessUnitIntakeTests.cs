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
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CustomerBusinessUnitIntakeTests
{
    private static CustomerBusinessUnitIntakeOutcome Validate(string value)
        => CustomerBusinessUnitIntake.Validate(
            new Dictionary<string, string> { [IntakeParameterCatalog.DisplayName] = value },
            CustomerBusinessUnitIntake.ReservedBusinessUnitNames);

    [Fact]
    public void TheReservedNames_AreTheSecureRecordUnit_AndTheOntologyWriterUnit()
        => CustomerBusinessUnitIntake.ReservedBusinessUnitNames.Should().Equal(
            SecureRecordOwnerRoleSet.Embedded.BusinessUnitName, SecureRecordSetupProcedure.OntologyWriterBusinessUnitName);

    /// <summary>Task 267 (#2618): the ontology writer's own unit is reserved like the Secure Record unit - same rule, same code.</summary>
    [Theory]
    [InlineData("Spaarke Ontology Writer")]
    [InlineData("SPAARKE ONTOLOGY WRITER")]
    [InlineData("Spaarke Ontology Wríter")]
    public void TheOntologyWriterUnitName_InAnyCaseOrAccent_IsRefused(string name)
        => Validate(name).Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(H10Rejections.CustomerDisplayNameInvalid);

    [Fact]
    public void ANameThatOnlyContainsTheOntologyWriterUnitName_IsAccepted()
        => Validate("Spaarke Ontology Writers Inc").Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Valid>();

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

    [Theory]
    [InlineData("Sécure Record")]
    [InlineData("SECURE RÉCORD")]
    [InlineData("Secure Récord")]   // combining acute accent
    public void TheSecureRecordName_WithAccents_IsRefusedAtIntake_AsAFirstFilter(string name)
        => Validate(name).Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(H10Rejections.CustomerDisplayNameInvalid);

    [Theory]
    [InlineData("Société Générale")]   // accents elsewhere are fine
    [InlineData("Secure Récords")]
    public void AnAccentedName_ThatIsNotTheSecureRecordName_IsAccepted_Verbatim(string name)
        => Validate(name).Should().Be(new CustomerBusinessUnitIntakeOutcome.Valid(name));

    [Fact]
    public void ARefusal_NeverEchoesAControlCharacter()
    {
        var invalid = Validate("Acme\u001b[31mCorp").Should().BeOfType<CustomerBusinessUnitIntakeOutcome.Invalid>().Subject;

        invalid.Diagnostic.Should().NotContain("\u001b").And.Contain("control character");
    }
}
