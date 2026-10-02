using Microsoft.Extensions.Options;
using NexKnx.OilTankWatcher;
using NexKnx.OilTankWatcher.Configuration;
using NexKnx.OilTankWatcher.Knx;
using NexKnx.OilTankWatcher.Mqtt;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<IKnxGateway, FalconKnxGateway>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
