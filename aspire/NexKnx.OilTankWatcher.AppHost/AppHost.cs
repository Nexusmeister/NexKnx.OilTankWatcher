using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var mosquitto = builder.AddContainer("mosquitto", "eclipse-mosquitto", "2")
    .WithBindMount("../../mosquitto/config/mosquitto.conf", "/mosquitto/config/mosquitto.conf", isReadOnly: true)
    .WithEndpoint(port: 1883, targetPort: 1883, name: "mqtt", scheme: "tcp")
    .WithLifetime(ContainerLifetime.Persistent);

var mqttEndpoint = mosquitto.GetEndpoint("mqtt");

builder.AddProject<Projects.NexKnx_OilTankWatcher>("oiltank-watcher")
    .WithEnvironment("Mqtt__Host", mqttEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Mqtt__Port", mqttEndpoint.Property(EndpointProperty.TargetPort))
    .WaitFor(mosquitto);

builder.Build().Run();
