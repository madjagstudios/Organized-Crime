using System.Reflection;
using S1API.Console;

namespace OrganizedCrime.Runtime;

/// <summary>
/// The oc_ch3 owner-QA console command. S1API auto-discovery requires a public
/// parameterless constructor, so the private constructor keeps it out of player
/// command lists. Composition registers it explicitly only when OwnerQaKeys is on.
/// Not linked into tests: S1API and MelonLoader only.
/// </summary>
public sealed class Chapter3QaConsoleCommand : BaseConsoleCommand
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;
    private static bool _registrationWarningLogged;

    private Chapter3QaConsoleCommand() { }

    internal static bool TryRegisterForOwner(Action<string> diagnostic)
    {
        try
        {
            lock (RegistrationLock)
            {
                if (_registered)
                    return true;

                var registry = typeof(BaseConsoleCommand).Assembly.GetType("S1API.Console.CustomConsoleRegistry")
                    ?? throw new MissingMemberException("S1API.Console.CustomConsoleRegistry");
                var register = registry.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static,
                    binder: null, types: new[] { typeof(BaseConsoleCommand) }, modifiers: null)
                    ?? throw new MissingMethodException(registry.FullName, "Register(BaseConsoleCommand)");
                register.Invoke(null, new object[] { new Chapter3QaConsoleCommand() });
                _registered = true;
                try
                {
                    OrganizedCrimeLog.Diagnostic("Chapter 3 owner QA console command registered.", diagnostic);
                }
                catch
                {
                    // A diagnostic sink failure must not invalidate registration.
                }
                return true;
            }
        }
        catch
        {
            try
            {
                lock (RegistrationLock)
                {
                    if (!_registrationWarningLogged)
                    {
                        _registrationWarningLogged = true;
                        OrganizedCrimeLog.Warning("Chapter 3 QA command could not be registered on this S1API version.");
                    }
                }
            }
            catch
            {
                // Optional owner QA must not abort initialization, even if logging fails.
            }
            return false;
        }
    }

    public override string CommandWord => Chapter3QaCommand.CommandWord;
    public override string CommandDescription => "Chapter 3 owner QA: board and ledger status, job and make-good controls.";
    public override string ExampleUsage => "oc_ch3 status";

    public override void ExecuteCommand(List<string> args)
    {
        try
        {
            var target = Chapter3QaAccess.Service;
            if (target is null)
            {
                OrganizedCrimeLog.CommandResponse("Chapter 3 QA: the Chapter 3 service is not constructed.");
                return;
            }
            foreach (var line in Chapter3QaCommand.Execute(target, args, OwnerQaKeysPreference.Read()))
                OrganizedCrimeLog.CommandResponse(line);
        }
        catch (Exception ex)
        {
            OrganizedCrimeLog.Warning($"Chapter 3 QA command failed: {ex.Message}");
        }
    }
}
