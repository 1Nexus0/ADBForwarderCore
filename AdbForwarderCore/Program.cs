using AdbForwarderCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder()
    .ConfigureServices((context,services) =>
        { 
            services.Configure<DevicesOptions>(context.Configuration.GetSection("Devices"));
            services.AddHostedService<ForwarderService>();
        }
    ).ConfigureLogging((l) =>
    {

        l.AddConsole();
        
        #if DEBUG
         l.SetMinimumLevel(LogLevel.Debug);
        #else
         l.SetMinimumLevel(LogLevel.Information);
        #endif
        l.AddFilter("Microsoft",LogLevel.Warning);

    });

builder.UseSystemd();
builder.UseWindowsService();

var host = builder.Build();

await host.RunAsync();




