namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// Some Android components are instantiated directly by the OS (a Service started via an Intent, a
/// manifest-declared BroadcastReceiver), outside the normal DI-constructed object graph, so they need
/// some way to reach the app's registered services - MauiProgram.cs (which already builds the full DI
/// container) sets this once at startup. Used by FirmwareUpdateForegroundService and
/// BluetoothPairingReceiver. Kept to the base IServiceProvider.GetService(Type) BCL method rather than
/// the generic GetRequiredService&lt;T&gt; extension, so this project doesn't need a
/// Microsoft.Extensions.DependencyInjection.Abstractions package reference it otherwise has no use for.
/// </summary>
public static class AppServiceLocator
{
    public static IServiceProvider? Services { get; set; }

    public static T GetRequiredService<T>() where T : notnull =>
        (T)(Services?.GetService(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered."));
}
