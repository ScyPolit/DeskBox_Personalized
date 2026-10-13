namespace DeskBox.Services;

/// <summary>
/// Decides whether the launch that applied a full data restore should
/// re-register the startup task. A backup carries the source machine's
/// startup <em>choice</em> but not its registration — that lives outside the
/// data directory — so restoring onto a machine with no registration of its
/// own would otherwise leave an "on" setting that never runs. The choice is
/// honored by re-registering through the same path as the settings toggle.
/// Every deliberate outcome is left exactly as found: a restored "off"
/// choice, a Windows-level disable (Task Manager, policy), and another
/// installation's task are never overridden.
/// </summary>
internal static class RestoreAutoStartReconcilePolicy
{
    internal static bool ShouldRegister(bool restoredAutoStartEnabled, StartupRegistrationState state) =>
        restoredAutoStartEnabled && state == StartupRegistrationState.NotRegistered;

    /// <summary>
    /// Only a live registration may back the restore notification's "will
    /// launch at startup" assertion. The packaged channel's
    /// <see cref="StartupRegistrationState.Pending"/> means the system
    /// consent dialog is still in flight and the user may decline it, so it
    /// must not be reported as re-registered.
    /// </summary>
    internal static bool IsRegistered(StartupRegistrationState state) =>
        state == StartupRegistrationState.Enabled;
}
