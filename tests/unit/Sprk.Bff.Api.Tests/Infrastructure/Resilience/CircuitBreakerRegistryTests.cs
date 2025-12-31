using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Resilience;

/// <summary>
/// Unit tests for CircuitBreakerRegistry.
/// </summary>
public class CircuitBreakerRegistryTests
{
    private readonly Mock<ILogger<CircuitBreakerRegistry>> _loggerMock;
    private readonly CircuitBreakerRegistry _registry;

    public CircuitBreakerRegistryTests()
    {
        _loggerMock = new Mock<ILogger<CircuitBreakerRegistry>>();
        _registry = new CircuitBreakerRegistry(_loggerMock.Object);
    }

    #region RegisterCircuit Tests

    [Fact]
    public void RegisterCircuit_NewService_RegistersSuccessfully()
    {
        // Arrange
        const string serviceName = "TestService";

        // Act
        _registry.RegisterCircuit(serviceName);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.Equal(serviceName, info.ServiceName);
        Assert.Equal(CircuitState.Closed, info.State);
        Assert.True(info.IsAvailable);
    }

    [Fact]
    public void RegisterCircuit_DuplicateService_DoesNotOverwrite()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordStateChange(serviceName, CircuitState.Open, TimeSpan.FromSeconds(30));

        // Act - Register again
        _registry.RegisterCircuit(serviceName);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert - State should still be Open (not overwritten)
        Assert.Equal(CircuitState.Open, info.State);
    }

    [Fact]
    public void RegisterCircuit_WellKnownServices_CanBeRegistered()
    {
        // Act
        _registry.RegisterCircuit(CircuitBreakerRegistry.AzureOpenAI);
        _registry.RegisterCircuit(CircuitBreakerRegistry.AzureAISearch);
        _registry.RegisterCircuit(CircuitBreakerRegistry.MicrosoftGraph);

        // Assert
        Assert.Equal(3, _registry.GetAllCircuits().Count);
    }

    #endregion

    #region GetCircuitInfo Tests

    [Fact]
    public void GetCircuitInfo_UnknownService_ReturnsUnknownState()
    {
        // Act
        var info = _registry.GetCircuitInfo("NonExistentService");

        // Assert
        Assert.Equal(CircuitState.Unknown, info.State);
        Assert.Equal("NonExistentService", info.ServiceName);
    }

    [Fact]
    public void GetCircuitInfo_CaseInsensitive()
    {
        // Arrange
        _registry.RegisterCircuit("TestService");

        // Act
        var info1 = _registry.GetCircuitInfo("testservice");
        var info2 = _registry.GetCircuitInfo("TESTSERVICE");

        // Assert
        Assert.Equal(CircuitState.Closed, info1.State);
        Assert.Equal(CircuitState.Closed, info2.State);
    }

    #endregion

    #region RecordStateChange Tests

    [Fact]
    public void RecordStateChange_ToOpen_SetsOpenUntil()
    {
        // Arrange
        const string serviceName = "TestService";
        var breakDuration = TimeSpan.FromSeconds(30);
        _registry.RegisterCircuit(serviceName);

        // Act
        _registry.RecordStateChange(serviceName, CircuitState.Open, breakDuration);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.Equal(CircuitState.Open, info.State);
        Assert.NotNull(info.OpenUntil);
        Assert.False(info.IsAvailable);
    }

    [Fact]
    public void RecordStateChange_ToClosed_ResetsConsecutiveFailures()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordFailure(serviceName);
        _registry.RecordFailure(serviceName);
        _registry.RecordFailure(serviceName);

        // Act
        _registry.RecordStateChange(serviceName, CircuitState.Closed);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.Equal(CircuitState.Closed, info.State);
        Assert.Equal(0, info.ConsecutiveFailures);
        Assert.True(info.IsAvailable);
    }

    [Fact]
    public void RecordStateChange_ToHalfOpen_AllowsAvailability()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordStateChange(serviceName, CircuitState.Open, TimeSpan.FromSeconds(30));

        // Act
        _registry.RecordStateChange(serviceName, CircuitState.HalfOpen);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.Equal(CircuitState.HalfOpen, info.State);
        Assert.True(info.IsAvailable);
    }

    [Fact]
    public void RecordStateChange_UnregisteredService_RegistersImplicitly()
    {
        // Act
        _registry.RecordStateChange("NewService", CircuitState.Open, TimeSpan.FromSeconds(30));
        var info = _registry.GetCircuitInfo("NewService");

        // Assert
        Assert.Equal(CircuitState.Open, info.State);
    }

    #endregion

    #region RecordFailure and RecordSuccess Tests

    [Fact]
    public void RecordFailure_IncrementsConsecutiveFailures()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);

        // Act
        _registry.RecordFailure(serviceName);
        _registry.RecordFailure(serviceName);
        _registry.RecordFailure(serviceName);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.Equal(3, info.ConsecutiveFailures);
    }

    [Fact]
    public void RecordSuccess_ResetsConsecutiveFailures()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordFailure(serviceName);
        _registry.RecordFailure(serviceName);

        // Act
        _registry.RecordSuccess(serviceName);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.Equal(0, info.ConsecutiveFailures);
    }

    #endregion

    #region GetAllCircuits Tests

    [Fact]
    public void GetAllCircuits_ReturnsAllRegistered()
    {
        // Arrange
        _registry.RegisterCircuit("Service1");
        _registry.RegisterCircuit("Service2");
        _registry.RegisterCircuit("Service3");

        // Act
        var circuits = _registry.GetAllCircuits();

        // Assert
        Assert.Equal(3, circuits.Count);
    }

    [Fact]
    public void GetAllCircuits_SortedByName()
    {
        // Arrange
        _registry.RegisterCircuit("Zebra");
        _registry.RegisterCircuit("Alpha");
        _registry.RegisterCircuit("Mango");

        // Act
        var circuits = _registry.GetAllCircuits();

        // Assert
        Assert.Equal("Alpha", circuits[0].ServiceName);
        Assert.Equal("Mango", circuits[1].ServiceName);
        Assert.Equal("Zebra", circuits[2].ServiceName);
    }

    #endregion

    #region IsServiceAvailable Tests

    [Fact]
    public void IsServiceAvailable_ClosedCircuit_ReturnsTrue()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);

        // Act
        var isAvailable = _registry.IsServiceAvailable(serviceName);

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public void IsServiceAvailable_OpenCircuit_ReturnsFalse()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordStateChange(serviceName, CircuitState.Open, TimeSpan.FromMinutes(5));

        // Act
        var isAvailable = _registry.IsServiceAvailable(serviceName);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public void IsServiceAvailable_HalfOpenCircuit_ReturnsTrue()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordStateChange(serviceName, CircuitState.HalfOpen);

        // Act
        var isAvailable = _registry.IsServiceAvailable(serviceName);

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public void IsServiceAvailable_UnknownService_ReturnsTrue()
    {
        // Act
        var isAvailable = _registry.IsServiceAvailable("NonExistent");

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public void IsServiceAvailable_ExpiredOpenCircuit_TransitionsToHalfOpen()
    {
        // Arrange
        const string serviceName = "TestService";
        _registry.RegisterCircuit(serviceName);
        _registry.RecordStateChange(serviceName, CircuitState.Open, TimeSpan.FromMilliseconds(1));
        Thread.Sleep(10); // Wait for expiry

        // Act
        var isAvailable = _registry.IsServiceAvailable(serviceName);
        var info = _registry.GetCircuitInfo(serviceName);

        // Assert
        Assert.True(isAvailable);
        Assert.Equal(CircuitState.HalfOpen, info.State);
    }

    #endregion
}
