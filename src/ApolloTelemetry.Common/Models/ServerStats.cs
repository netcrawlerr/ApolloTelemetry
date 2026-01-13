using System.Collections.Generic;

namespace ApolloTelemetry.Common.Models;

public class ServerStats
{
    public string DeviceName { get; set; } = string.Empty;
    public string Uptime { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public double CpuUsage { get; set; }           
    public double DownloadSpeedMbps { get; set; } 
    public double UploadSpeedMbps { get; set; }   
    public DateTime LastUpdated { get; set; }    
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public List<DriveData> Drives { get; set; } = new();
    public List<DatabaseService> DatabaseServices { get; set; } = new();
    public List<ProcessInfo> TopProcesses { get; set; } = new();
}

public class DriveData
{
    public string Name { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public double UsedPercentage { get; set; }
    
    public double TotalGB { get; set; }
    public double UsedGB { get; set; }
}