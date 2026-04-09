namespace ApolloTelemetry.Common.Models;

public class ProcessInfo
{
    public int Id { get; set; } 
    public string Name { get; set; } = string.Empty;
    public double MemoryUsageMB { get; set; }
    public double CpuUsagePercent { get; set; }
}