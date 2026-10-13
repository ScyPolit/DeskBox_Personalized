using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class RestoreAutoStartReconcilePolicyTests
{
    [Theory]
    [InlineData(StartupRegistrationState.NotRegistered)]
    public void ShouldRegister_HonorsARestoredOnChoiceOnAnUnregisteredMachine(
        StartupRegistrationState state)
    {
        Assert.True(RestoreAutoStartReconcilePolicy.ShouldRegister(true, state));
    }

    [Theory]
    [InlineData(StartupRegistrationState.Enabled)]
    [InlineData(StartupRegistrationState.Pending)]
    [InlineData(StartupRegistrationState.DisabledByUser)]
    [InlineData(StartupRegistrationState.DisabledByTaskScheduler)]
    [InlineData(StartupRegistrationState.PathMismatch)]
    [InlineData(StartupRegistrationState.BlockedOrFailed)]
    public void ShouldRegister_NeverOverridesAnExistingRegistrationOrADeliberateDisable(
        StartupRegistrationState state)
    {
        Assert.False(RestoreAutoStartReconcilePolicy.ShouldRegister(true, state));
    }

    [Theory]
    [InlineData(StartupRegistrationState.NotRegistered)]
    [InlineData(StartupRegistrationState.Enabled)]
    [InlineData(StartupRegistrationState.DisabledByUser)]
    [InlineData(StartupRegistrationState.DisabledByTaskScheduler)]
    [InlineData(StartupRegistrationState.PathMismatch)]
    public void ShouldRegister_RespectsARestoredOffChoice(StartupRegistrationState state)
    {
        Assert.False(RestoreAutoStartReconcilePolicy.ShouldRegister(false, state));
    }

    [Fact]
    public void IsRegistered_OnlyABackLiveRegistrationBacksTheNotification()
    {
        Assert.True(RestoreAutoStartReconcilePolicy.IsRegistered(StartupRegistrationState.Enabled));
    }

    [Theory]
    [InlineData(StartupRegistrationState.Pending)]
    [InlineData(StartupRegistrationState.NotRegistered)]
    [InlineData(StartupRegistrationState.DisabledByUser)]
    [InlineData(StartupRegistrationState.DisabledByTaskScheduler)]
    [InlineData(StartupRegistrationState.PathMismatch)]
    [InlineData(StartupRegistrationState.BlockedOrFailed)]
    public void IsRegistered_PendingConsentAndEveryNonLiveStateDoNot(
        StartupRegistrationState state)
    {
        // The packaged channel's consent dialog is still in flight when the
        // enable result is Pending — the user may decline it, so the restore
        // notification must not claim the startup task exists.
        Assert.False(RestoreAutoStartReconcilePolicy.IsRegistered(state));
    }
}
