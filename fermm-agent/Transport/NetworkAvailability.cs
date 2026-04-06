using System.Net.NetworkInformation;

namespace FermmAgent.Transport;

internal static class NetworkAvailability
{
    public static Task WaitForNetworkAvailableAsync(CancellationToken ct)
    {
        if (NetworkInterface.GetIsNetworkAvailable())
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        NetworkAvailabilityChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (!args.IsAvailable)
            {
                return;
            }

            NetworkChange.NetworkAvailabilityChanged -= handler;
            tcs.TrySetResult();
        };

        NetworkChange.NetworkAvailabilityChanged += handler;

        if (NetworkInterface.GetIsNetworkAvailable())
        {
            NetworkChange.NetworkAvailabilityChanged -= handler;
            tcs.TrySetResult();
            return tcs.Task;
        }

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                NetworkChange.NetworkAvailabilityChanged -= handler;
                tcs.TrySetCanceled(ct);
            });
        }

        return tcs.Task;
    }
}
