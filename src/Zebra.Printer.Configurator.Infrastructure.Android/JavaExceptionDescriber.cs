using Throwable = Java.Lang.Throwable;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Java exceptions crossing the .NET-for-Android binding often carry no .NET InnerException chain
/// even though the JVM-side cause chain (Throwable.Cause) has the actual reason - and their .Message
/// is frequently null, which makes an unadorned ex.Message collapse to .NET's generic
/// "Exception of type 'Java.IO.IOException' was thrown." with no diagnostic value at all. This walks
/// the real Java cause chain (falling back to the .NET InnerException chain for non-Java exceptions)
/// so failures like a Bluetooth connect error are actually diagnosable from the Activity Log instead
/// of showing that same opaque message every time.
/// </summary>
internal static class JavaExceptionDescriber
{
    private const int MaxChainLength = 5;

    public static string Describe(System.Exception ex)
    {
        var parts = new List<string>();

        if (ex is Throwable throwable)
        {
            for (var t = throwable; t is not null && parts.Count < MaxChainLength; t = t.Cause)
            {
                var className = t.GetType().Name;
                var message = t.Message;
                parts.Add(string.IsNullOrWhiteSpace(message) ? className : $"{className}: {message}");
            }
        }
        else
        {
            for (System.Exception? e = ex; e is not null && parts.Count < MaxChainLength; e = e.InnerException)
            {
                parts.Add(string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message);
            }
        }

        return string.Join(" <- ", parts);
    }
}
