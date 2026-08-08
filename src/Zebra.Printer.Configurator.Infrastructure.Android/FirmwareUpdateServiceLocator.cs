namespace Zebra.Printer.Configurator.Infrastructure.Android;

/// <summary>
/// FirmwareUpdateForegroundService is instantiated directly by Android (started via an Intent),
/// outside the normal DI-constructed object graph, so it needs some way to reach the app's
/// registered services - MauiProgram.cs (which already builds the full DI container) sets this once
/// at startup. Kept to the base IServiceProvider.GetService(Type) BCL method rather than the generic
/// GetRequiredService&lt;T&gt; extension, so this project doesn't need a
/// Microsoft.Extensions.DependencyInjection.Abstractions package reference it otherwise has no use for.
/// </summary>
public static class FirmwareUpdateServiceLocator
{
    public static IServiceProvider? Services { get; set; }

    public static T GetRequiredService<T>() where T : notnull =>
        (T)(Services?.GetService(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered."));
}
