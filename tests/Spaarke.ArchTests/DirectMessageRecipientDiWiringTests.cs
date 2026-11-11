using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The Direct-message recipient stamp needs the POA share read (<see cref="IDataverseRecordShareService"/>). Both
/// <see cref="ThreadResolver"/> and <see cref="CommunicationService"/> take it as an OPTIONAL constructor parameter
/// (<c>recordShares = null</c>) so unit compositions keep compiling, and with a null the stamp silently does nothing.
/// This guard judges the REAL composition: the BFF booted from <c>Program.cs</c> (<see cref="BootedBff"/>, Development and
/// Production) must hand both singletons a non-null share service. A registration change that dropped it, or a constructor
/// change that stopped binding it, fails here instead of turning the stamp off in production.
/// </summary>
/// <remarks>
/// Enforced by: this guard (build). Limit, stated exactly: it proves the services hold a share service, not that the stamp
/// decides correctly; that is proven by <c>DirectMessageRecipientStampTests</c>.
/// </remarks>
[Collection(BootedBffCollection.Name)]
public sealed class DirectMessageRecipientDiWiringTests
{
    private const string FieldName = "_recordShares";

    private readonly BootedBff _booted;

    public DirectMessageRecipientDiWiringTests(BootedBff booted) => _booted = booted;

    [Fact(DisplayName = "Direct-message stamp: the composed CommunicationService holds a record-share service (Development and Production boots)")]
    public void CommunicationService_IsComposedWithTheRecordShareService()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var service = app.Services.GetRequiredService<CommunicationService>();
            AssertHoldsRecordShares(app.Environment, service);
        }
    }

    [Fact(DisplayName = "Direct-message stamp: the composed IThreadResolver holds a record-share service (Development and Production boots)")]
    public void ThreadResolver_IsComposedWithTheRecordShareService()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var resolver = app.Services.GetRequiredService<IThreadResolver>();
            Assert.True(resolver is ThreadResolver,
                $"{app.Environment}: IThreadResolver resolved to {resolver.GetType().FullName}, not ThreadResolver; this guard reads the field of the real resolver.");
            AssertHoldsRecordShares(app.Environment, resolver);
        }
    }

    /// <summary>
    /// Task 054a piece 2b: the outbound Direct pair routing needs the Direct thread access service and the internal-user email
    /// read. Both are optional constructor parameters, and without either the routing silently routes nothing (every outbound
    /// Direct send takes the resolver path and gets no recipient).
    /// </summary>
    [Fact(DisplayName = "Direct pair routing: the composed CommunicationService holds the Direct thread access service and the internal-user email read (Development and Production boots)")]
    public void CommunicationService_IsComposedWithThePairRoutingDependencies()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var service = app.Services.GetRequiredService<CommunicationService>();
            AssertHolds(app.Environment, service, "_directThreadAccess", typeof(Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService));
            AssertHolds(app.Environment, service, "_contactIdentities", typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.IContactIdentityStore));
        }
    }

    private static void AssertHolds(string environment, object composed, string fieldName, Type expected)
    {
        var type = composed.GetType();
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null,
            $"{type.Name} no longer has a private field '{fieldName}'; update this guard to the field that holds the {expected.Name}.");

        var held = field!.GetValue(composed);
        Assert.True(held is not null,
            $"{environment}: the composed {type.Name} holds NO {expected.Name}, so the outbound Direct pair routing silently routes nothing. " +
            "Its constructor parameter is optional: check the DI registration and the constructor binding.");
        Assert.IsAssignableFrom(expected, held);
    }

    private static void AssertHoldsRecordShares(string environment, object composed)
    {
        var type = composed.GetType();
        var field = type.GetField(FieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null,
            $"{type.Name} no longer has a private field '{FieldName}'; update this guard to the field that holds the record-share service.");

        var held = field!.GetValue(composed);
        Assert.True(held is not null,
            $"{environment}: the composed {type.Name} holds NO IDataverseRecordShareService, so the Direct-message recipient stamp silently does nothing. " +
            "Its constructor parameter is optional (recordShares = null): check the DI registration and the constructor binding.");
        Assert.IsAssignableFrom<IDataverseRecordShareService>(held);
    }
}
