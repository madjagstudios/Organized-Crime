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
}
