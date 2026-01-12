using ApolloTelemetry.Agent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<StatsWorker>();

var host = builder.Build();
host.Run();