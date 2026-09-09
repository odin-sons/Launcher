namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// One step in the install run, tagged with the collapsible group it belongs to.
    /// Steps sharing a <see cref="Group"/> label must be contiguous in the list passed to
    /// <see cref="IUpdateUi.SetSteps"/> — the group owns a run of consecutive steps, and
    /// <see cref="InstallStepModel"/> folds a whole group into one row once the run moves on.
    /// </summary>
    public sealed record InstallStep(string Group, string Label);
}
