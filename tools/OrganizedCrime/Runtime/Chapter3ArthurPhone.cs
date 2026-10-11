using OrganizedCrime.Model;

namespace OrganizedCrime.Runtime;

/// <summary>
/// OC-134: Arthur's Chapter 3 phone fallback, after two missed visit
/// evenings. Total like the other world boundaries: a bool plus a
/// reason, never an exception out. Host only.
/// </summary>
public interface IChapter3ArthurPhone
{
    bool TryCall(Chapter3ArthurVisitKind kind, IReadOnlyList<string> stages, int chapterDay, out string reason);
}
