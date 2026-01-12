using System;
using ApolloTelemetry.Common.Models;

namespace ApolloTelemetry.Dashboard.Models;

public class ServerDisplayData
{
    public ServerStats? Stats { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}