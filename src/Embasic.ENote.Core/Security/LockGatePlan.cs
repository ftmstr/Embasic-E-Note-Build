namespace Embasic.ENote.Core.Security;

/// <summary>
/// A TESTABLE DESIGN-ONLY authorization plan. This is NOT cryptographic key
/// hierarchy enforcement and MUST NOT be used as the only access control.
/// </summary>
public enum LockLayer
{
    Application,
    Folder,
    Subfolder,
    NoteAdditional
}

public readonly record struct LockGate(Guid Id, LockLayer Layer);

public sealed class LockGatePlan
{
    private readonly LockGate[] _required;

    public LockGatePlan(IEnumerable<LockGate> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        _required = required.ToArray();
        if (_required.Any(x => x.Id == Guid.Empty) ||
            _required.Select(x => x.Id).Distinct().Count() != _required.Length)
            throw new ArgumentException("Lock identifiers must be distinct and non-empty.", nameof(required));
    }

    public IReadOnlyList<LockGate> Missing(IReadOnlySet<Guid> individuallyUnlocked)
    {
        ArgumentNullException.ThrowIfNull(individuallyUnlocked);
        return Array.AsReadOnly(_required.Where(x => !individuallyUnlocked.Contains(x.Id)).ToArray());
    }

    public bool AllIndividuallyUnlocked(IReadOnlySet<Guid> individuallyUnlocked) =>
        Missing(individuallyUnlocked).Count == 0;
}
