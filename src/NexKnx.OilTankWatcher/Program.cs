using System.Reflection;
using Microsoft.Extensions.Options;
using NexKnx.OilTankWatcher;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Logging;
using NexKnx.OilTankWatcher.Mqtt;
using NexKnx.OilTankWatcher.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.SingleLine = true;
});

builder.AddServiceDefaults();

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.Configure<TankOptions>(builder.Configuration.GetSection(TankOptions.SectionName));
builder.Services.Configure<KnxOptions>(builder.Configuration.GetSection(KnxOptions.SectionName));
builder.Services.Configure<RuntimeEstimationOptions>(builder.Configuration.GetSection(RuntimeEstimationOptions.SectionName));

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MqttOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TankOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<KnxOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RuntimeEstimationOptions>>().Value);

builder.Services.AddSingleton<MqttOilLevelClient>();
// Must resolve to the same singleton instance as MqttOilLevelClient (not
// re-resolved via AddSingleton<IMqttOilLevelClient, MqttOilLevelClient>()) -
// otherwise a second instance with a second MQTT connection would result.
builder.Services.AddSingleton<IMqttOilLevelClient>(sp => sp.GetRequiredService<MqttOilLevelClient>());
builder.Services.AddSingleton<IKnxGateway, FalconKnxGateway>();
builder.Services.AddSingleton<IHistoryRepository>(sp =>
    new SqliteHistoryRepository(sp.GetRequiredService<RuntimeEstimationOptions>().DatabasePath));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Version is set from GitVersion at build/publish time via
// -p:InformationalVersion (see CI workflow/Dockerfile); without this
// argument (e.g. a local "dotnet run") the attribute stays empty.
var version = typeof(Worker).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
host.Services.GetRequiredService<ILogger<Program>>().LogStartup(version ?? "unknown");

await host.RunAsync();
