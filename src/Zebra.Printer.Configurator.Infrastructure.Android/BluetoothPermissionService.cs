using Microsoft.Maui.ApplicationModel;
using Zebra.Printer.Configurator.Core.Abstractions;

namespace Zebra.Printer.Configurator.Infrastructure.Android;

public sealed class BluetoothPermissionService : IBluetoothPermissionService
{
    public async Task<bool> EnsureGrantedAsync(CancellationToken cancellationToken = default)
    {
        var status = await Permissions.CheckStatusAsync<BluetoothConnectionPermissions>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<BluetoothConnectionPermissions>();
        }

        return status == PermissionStatus.Granted;
    }
}
