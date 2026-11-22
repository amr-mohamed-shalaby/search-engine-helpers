using CsvProjectXml.Core.Models;

namespace CsvProjectXml.Core.Services;

/// <summary>
/// Walks through the conflicts of several departments, one slide per department followed by a review slide.
/// Edits are kept while moving back and forth; nothing is converted until <see cref="CompleteAsync"/>, and
/// <see cref="Cancel"/> throws every edit away.
/// </summary>
public sealed class ConflictWizard
{
    public ConflictWizard(IEnumerable<DepartmentRepair> departments)
    {
        Departments = departments.ToArray();
    }

    public IReadOnlyList<DepartmentRepair> Departments { get; }

    /// <summary>0..Departments.Count-1 are department slides; Departments.Count is the review slide.</summary>
    public int CurrentIndex { get; private set; }

    public int SlideCount => Departments.Count + 1;
    public bool IsReviewSlide => CurrentIndex >= Departments.Count;
    public DepartmentRepair? Current => IsReviewSlide ? null : Departments[CurrentIndex];
    public bool CanGoBack => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex < Departments.Count;
    public int VerifiedCount => Departments.Count(department => department.State == DepartmentRepairState.Verified);

    public void Next()
    {
        if (CanGoNext) CurrentIndex++;
    }

    public void Back()
    {
        if (CanGoBack) CurrentIndex--;
    }

    public bool GoTo(Guid departmentRepairId)
    {
        for (var index = 0; index < Departments.Count; index++)
        {
            if (Departments[index].Id != departmentRepairId) continue;
            CurrentIndex = index;
            return true;
        }

        return false;
    }

    /// <summary>Checks the current department's corrections and moves to the next slide when none are left.</summary>
    public async Task<bool> FinishCurrentAsync(CsvRepairService repair, CsvProjectConverter converter,
        CancellationToken cancellationToken = default)
    {
        var current = Current ?? throw new InvalidOperationException("There is no department on this slide.");
        var verified = current.State == DepartmentRepairState.Verified ||
                       await current.VerifyAsync(repair, converter, cancellationToken);
        if (verified) Next();
        return verified;
    }

    /// <summary>Converts every department whose conflicts are all fixed. The others are left as they were.</summary>
    public async Task<IReadOnlyList<WizardConversionOutcome>> CompleteAsync(CsvProjectConverter converter,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<WizardConversionOutcome>();
        foreach (var department in Departments.Where(x => x.State == DepartmentRepairState.Verified))
        {
            try
            {
                var result = await department.ConvertAsync(converter, cancellationToken);
                outcomes.Add(new WizardConversionOutcome(department.Id, department.Slug, department.DisplayName, result, null));
            }
            catch (Exception ex) when (ex is ConversionException or IOException or InvalidOperationException)
            {
                outcomes.Add(new WizardConversionOutcome(department.Id, department.Slug, department.DisplayName, null,
                    ex is ConversionException conversion ? conversion.ToString() : ex.Message));
            }
        }

        return outcomes;
    }

    /// <summary>Discards every edit and every corrected copy written by the wizard.</summary>
    public void Cancel()
    {
        foreach (var department in Departments) department.Discard();
    }
}
