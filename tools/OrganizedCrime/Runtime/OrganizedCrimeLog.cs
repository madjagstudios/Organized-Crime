namespace OrganizedCrime.Runtime;

/// <summary>
/// Small static log seam so runtime components can emit warning and receipt lines without a
/// direct MelonLoader dependency. Mod.cs assigns both delegates once during OnInitializeMelon;
/// they default to no-ops so tests never need MelonLoader.
/// </summary>
public static class OrganizedCrimeLog
{
    public static Action<string> Warning { get; set; } = _ => { };
    public static Action<string> Receipt { get; set; } = _ => { };
    // The mod installs the final diagnostic sink. Engine-free consumers retain
    // their injected sink, so receipt observations do not require MelonLoader.
    internal static Action<string>? DiagnosticSink { get; set; }
    internal static void Diagnostic(string message, Action<string>? fallback = null) =>
        (DiagnosticSink ?? fallback ?? Receipt)(message);
    internal static Action FlushWarningSummaries { get; set; } = () => { };
    internal static Action<string> CommandResponse { get; set; } = message => Receipt(message);
}
