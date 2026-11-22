using CsvProjectXml.Core.Services;

namespace CsvProjectXml.Maui.Services;

/// <summary>
/// Keeps the conflict wizard (and every edit made in it) alive while the user leaves the wizard page,
/// so going back to the conversion page and returning does not lose work.
/// </summary>
public sealed class ConflictWizardState
{
    public ConflictWizard? Active { get; private set; }

    public void Set(ConflictWizard wizard) => Active = wizard;

    /// <summary>Forgets the wizard without touching staged files (used after a successful finish).</summary>
    public void Clear() => Active = null;

    /// <summary>Throws away every edit and staged copy, then forgets the wizard.</summary>
    public void Discard()
    {
        Active?.Cancel();
        Active = null;
    }
}
