namespace ApolloTelemetry.Common.Models;

public class ProcessInfo
{
    public string Name { get; set; } = string.Empty;
    public double MemoryUsageMB { get; set; }
    public double CpuUsagePercent { get; set; }
}