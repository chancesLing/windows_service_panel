using System.ServiceProcess;

namespace RestartWindowsService;

internal sealed class ServiceManager
{
    private readonly string _serviceName;

    public ServiceManager(string serviceName)
    {
        _serviceName = serviceName;
    }

    public string ServiceName => _serviceName;

    public ServiceSnapshot GetSnapshot()
    {
        try
        {
            using var service = new ServiceController(_serviceName);
            var status = service.Status;
            return new ServiceSnapshot(true, status, null);
        }
        catch (InvalidOperationException ex)
        {
            return new ServiceSnapshot(false, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new ServiceSnapshot(false, null, ex.Message);
        }
    }

    public Task StartAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var service = new ServiceController(_serviceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                return;
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }, cancellationToken);
    }

    public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var service = new ServiceController(_serviceName);
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                return;
            }

            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }, cancellationToken);
    }

    public async Task RestartAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await StopAsync(timeout, cancellationToken).ConfigureAwait(false);
        await StartAsync(timeout, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record ServiceSnapshot(
    bool Exists,
    ServiceControllerStatus? Status,
    string? ErrorMessage);

