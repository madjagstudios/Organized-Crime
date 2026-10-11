namespace OrganizedCrime.Model;

/// <summary>
/// OC-134: which of Arthur's Chapter 3 visits is pending, if any. The
/// warning visit enforces overdue make-goods; the cooperation visit
/// answers a 5-refusal streak; the beating tops the ladder for
/// make-goods at BeatingDue (OC-135). Saved on <see cref="Chapter3State"/> so
/// a reload resumes the same pending series instead of restarting it.
/// </summary>
public enum Chapter3ArthurVisitKind
{
    None,
    Warning,
    Cooperation,
    Beating
}
