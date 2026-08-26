using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Services;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services;

/// <summary>
/// Verifies the remaining inline notification integration points (AnalysisEndpoints,
/// IncomingCommunicationProcessor, WorkAssignmentEndpoints) still
/// reference NotificationService after R3 task 023 deleted the legacy
/// PlaybookSchedulerService.
///
/// <para><b>Was four, now three</b> — unified-access-control-r2 task 073 deleted
/// <c>Api/UploadEndpoints.cs</c>, whose <c>PUT /api/containers/{containerId}/files/{*path}</c> handler
/// raised the "New document uploaded" notification. That route had ZERO callers (every live upload
/// flow uses the OBO sibling <c>PUT /api/obo/containers/{id}/files/{*path}</c>), so no notification
/// that any user actually received has been lost. If the OBO upload path should raise an upload
/// notification, that is a new behaviour to add there deliberately — not something this inventory
/// test was ever asserting.</para>
///
/// These tests were previously housed alongside the
/// PlaybookSchedulerService unit tests but are independent of the scheduler itself —
/// they are inventory-style assertions about which endpoints/services consume the
/// notification facade. R3 task 023 relocates them so the scheduler test file can be
/// retired with the legacy class.
/// </summary>
/// <remarks>
/// <para>Originated in <c>PlaybookSchedulerServiceTests.cs</c> "Inline Notification
/// Integration Points Verification" region (tests 1–4); preserved verbatim.</para>
/// </remarks>
public class InlineNotificationIntegrationPointsTests
{


    /// <summary>
    /// Verifies that IncomingCommunicationProcessor injects NotificationService for email notifications.
    /// </summary>
    [Fact]
    public void IncomingCommunicationProcessor_InjectsNotificationService()
    {
        var processorType = typeof(Sprk.Bff.Api.Services.Communication.IncomingCommunicationProcessor);
        processorType.Should().NotBeNull("IncomingCommunicationProcessor should exist");

        var constructors = processorType.GetConstructors();
        var hasNotificationService = constructors.Any(c =>
            c.GetParameters().Any(p => p.ParameterType == typeof(NotificationService)));

        hasNotificationService.Should().BeTrue(
            "IncomingCommunicationProcessor constructor should accept NotificationService for inline notifications");
    }


    /// <summary>
    /// Comprehensive check that the surviving inline notification integration points exist
    /// and have NotificationService wired in.
    /// </summary>
    /// <remarks>
    /// <c>typeof(Sprk.Bff.Api.Api.UploadEndpoints)</c> was removed from this list on 2026-08-26 by
    /// unified-access-control-r2 task 073, which deleted that class along with its three
    /// zero-caller app-only write routes. Renamed from <c>AllFourIntegrationPoints_…</c> because the
    /// count is now three and a name that misstates its own subject is worse than a renamed test.
    /// </remarks>
    [Fact]
    public void AllRemainingIntegrationPoints_HaveNotificationServiceWiredIn()
    {
        var integrationPoints = new[]
        {
            typeof(Sprk.Bff.Api.Api.Ai.AnalysisEndpoints),
            typeof(Sprk.Bff.Api.Services.Communication.IncomingCommunicationProcessor),
            typeof(Sprk.Bff.Api.Api.WorkAssignmentEndpoints)
        };

        foreach (var type in integrationPoints)
        {
            HasNotificationServiceDependency(type).Should().BeTrue(
                $"{type.Name} should have NotificationService wired in for inline notifications");
        }
    }

    /// <summary>
    /// Checks whether a type has NotificationService as a dependency (constructor, method
    /// parameter, or lambda parameter in the same assembly).
    /// </summary>
    private static bool HasNotificationServiceDependency(Type type)
    {
        var constructorMatch = type.GetConstructors()
            .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(NotificationService)));
        if (constructorMatch) return true;

        var methodMatch = type.GetMethods(
                BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.NonPublic | BindingFlags.Public)
            .Any(m => m.GetParameters().Any(p => p.ParameterType == typeof(NotificationService)));
        if (methodMatch) return true;

        var assemblyMethodParams = type.Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Distinct();
        return assemblyMethodParams.Contains(typeof(NotificationService));
    }
}
