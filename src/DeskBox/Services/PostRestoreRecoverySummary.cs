namespace DeskBox.Services;

/// <summary>
/// What the post-restore recovery pass did on the launch that applied a full
/// restore. Surfaced in the restore-complete notification so the user knows
/// the app adapted the restored profile to this machine.
/// </summary>
/// <param name="AutoStartReRegistered">
/// The restored "autostart on" choice had no registration on this machine and
/// was re-registered.
/// </param>
/// <param name="LocalPathsRebased">
/// Machine-local paths (managed storage root, snapshot folder, or default-
/// storage file widgets) moved from the source user profile to this one.
/// </param>
internal sealed record PostRestoreRecoverySummary(
    bool AutoStartReRegistered,
    bool LocalPathsRebased)
{
    internal static PostRestoreRecoverySummary None { get; } = new(false, false);
}

/// <summary>
/// Startup-sequence decisions around the launch that applied a full restore.
/// </summary>
internal static class PostRestoreRecoveryPolicy
{
    /// <summary>
    /// The restore-apply launch must not run the scheduled automatic
    /// snapshot: its options were read from the raw, not-yet-rebased
    /// settings file, so it would target the source machine's profile —
    /// failing into a misleading fallback warning under standard rights, or
    /// creating the old account's directory tree under a permissive ACL.
    /// The pre-restore safety archive created during the apply already
    /// protects this state; the next launch snapshots with rebased options.
    /// Scoped restores leave the settings file in place, so their options
    /// are current and the scheduled snapshot stays meaningful.
    /// </summary>
    internal static bool ShouldSkipStartupScheduledSnapshot(
        DeskBoxRestoreApplyKind kind,
        bool applySucceeded) =>
        kind == DeskBoxRestoreApplyKind.Full && applySucceeded;
}
