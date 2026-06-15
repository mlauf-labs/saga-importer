using SagaImporter.Daemon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    // Integrate with systemd: sd_notify, journald-formatted log output, SIGTERM handling.
    .UseSystemd()
    .ConfigureServices(services =>
    {
        services.AddHostedService<ImporterWorker>();
    })
    .Build();

await host.RunAsync();
