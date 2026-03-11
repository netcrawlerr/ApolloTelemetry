using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using ApolloTelemetry.Common.Models;

namespace ApolloTelemetry.Agent;

public class StatsWorker : BackgroundService
{
    private readonly ILogger<StatsWorker> _logger;
    private readonly HttpClient _httpClient;
    private const double GB_IN_BYTES = 1073741824.0;
    
    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;
    private DateTime _lastNetworkCheck = DateTime.Now;

    // private const string DashboardUrl = "http://127.0:5002/telemetry/";

    // my LTP IP
    private const string DashboardUrl = "http://192.168.1.10:5002/telemetry/";
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    public StatsWorker(ILogger<StatsWorker> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent started. Sending to: {url}", DashboardUrl);

        // better-logs
        var logOptions = new JsonSerializerOptions { WriteIndented = true };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stats = CollectSystemStats();

                string jsonDebug = JsonSerializer.Serialize(stats, logOptions);
                _logger.LogInformation("DIAGNOSTIC LOG:\n{json}", jsonDebug);

                var response = await _httpClient.PostAsJsonAsync(DashboardUrl, stats, stoppingToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully pushed stats at {time}", DateTime.Now.ToShortTimeString());
                }
                else
                {
                    _logger.LogWarning("Dashboard reachable but returned: {code}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Communication or Permission Error: {msg}", ex.Message);
            }

            await Task.Delay(5000, stoppingToken);
        }
    }

    private ServerStats CollectSystemStats()
    {
        // using the GC API 
        var memInfo = GC.GetGCMemoryInfo();
        
        var (dl, ul) = CalculateNetworkSpeed();

        var stats = new ServerStats
        {
            DeviceName = Environment.MachineName,
            Uptime = GetReadableUptime(),
            OSVersion = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            CpuUsage = GetCpuUsage(), 
            TopProcesses = GetTopProcesses(),
            DownloadSpeedMbps = dl,
            UploadSpeedMbps = ul,
            LastUpdated = DateTime.Now,
            TotalMemoryBytes = memInfo.TotalAvailableMemoryBytes,
            AvailableMemoryBytes = memInfo.TotalAvailableMemoryBytes - memInfo.MemoryLoadBytes,
            Drives = new List<DriveData>(),
            DatabaseServices = CheckDatabaseServices()
        };

        try
        {
            var drives = DriveInfo.GetDrives();

            foreach (var drive in drives)
            {
                try
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed && drive.TotalSize > 0)
                    {
                        double totalGb = drive.TotalSize / GB_IN_BYTES;
                        double freeGb = drive.AvailableFreeSpace / GB_IN_BYTES;
                        double usedGb = totalGb - freeGb;

                        stats.Drives.Add(new DriveData
                        {
                            Name = drive.Name,
                            TotalSize = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace,
                            TotalGB = Math.Round(totalGb, 1),
                            UsedGB = Math.Round(usedGb, 1),
                            UsedPercentage = Math.Round(100 - (freeGb / totalGb * 100), 2)
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogDebug("Skipping restricted system drive: {name}", drive.Name);
                }
                catch (Exception driveEx)
                {
                    _logger.LogDebug("Could not read drive {name}: {msg}", drive.Name, driveEx.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to enumerate drives: {msg}", ex.Message);
        }

        return stats;
    }

    private List<DatabaseService> CheckDatabaseServices()
    {
        var services = new List<DatabaseService>
        {
            new() { Name = "MySQL", Status = "Unknown" },
            new() { Name = "PostgreSQL", Status = "Unknown" },
            new() { Name = "MSSQL", Status = "Unknown" },
            new() { Name = "MongoDB", Status = "Unknown" }
        };
        
        var dbNames = new Dictionary<string, (string Win, string Nix)>
        {
            { "MySQL", ("MySQL84", "mysql") },
            { "PostgreSQL", ("postgresql-x64-18", "postgresql") },
            { "MSSQL", ("MSSQLSERVER", "mssql-server") },
            { "MongoDB", ("MongoDB", "mongod") }
        };

        foreach (var service in services)
        {
            var names = dbNames[service.Name];

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                service.Status = GetWindowsServiceStatus(names.Win);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                service.Status = GetLinuxServiceStatus(names.Nix);
        }

        return services;
    }

    private string GetWindowsServiceStatus(string serviceName)
    {
        try
        {
            var startInfo = new ProcessStartInfo("sc", $"query \"{serviceName}\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(startInfo);
            string output = process?.StandardOutput.ReadToEnd() ?? "";
            if (output.Contains("RUNNING")) return "Running";
            if (output.Contains("STOPPED")) return "Stopped";
            return "Not Found";
        }
        catch
        {
            return "Not Found";
        }
    }

    private string GetLinuxServiceStatus(string serviceName)
    {
        try
        {
            var startInfo = new ProcessStartInfo("systemctl", $"is-active {serviceName}")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(startInfo);
            string output = process?.StandardOutput.ReadToEnd().Trim() ?? "";
            return output == "active" ? "Running" : (output == "inactive" ? "Stopped" : "Not Found");
        }
        catch
        {
            return "Not Found";
        }
    }

    private double GetCpuUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try {
                var info = new ProcessStartInfo("bash", "-c \"top -bn1 | grep 'Cpu(s)' | awk '{print $2 + $4}'\"") 
                    { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(info);
                return double.TryParse(p?.StandardOutput.ReadToEnd(), out var res) ? res : 0;
            } catch { return 0; }
        }
        return 0; 
    }
    
    private List<ProcessInfo> GetTopProcesses()
    {
        try
        {
            return Process.GetProcesses()
                .Where(p => p.WorkingSet64 > 0)
                .OrderByDescending(p => p.WorkingSet64)
                .Take(5)
                .Select(p => new ProcessInfo
                {
                    Name = p.ProcessName,
                    MemoryUsageMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 1),
                })
                .ToList();
        }
        catch
        {
            return new List<ProcessInfo>();
        }
    }

    private (double dl, double ul) CalculateNetworkSpeed()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        long currentReceived = interfaces.Sum(i => i.GetIPStatistics().BytesReceived);
        long currentSent = interfaces.Sum(i => i.GetIPStatistics().BytesSent);
        
        var elapsed = (DateTime.Now - _lastNetworkCheck).TotalSeconds;
        
        double dl = ((currentReceived - _lastBytesReceived) * 8 / 1_048_576.0) / elapsed;
        double ul = ((currentSent - _lastBytesSent) * 8 / 1_048_576.0) / elapsed;

        _lastBytesReceived = currentReceived;
        _lastBytesSent = currentSent;
        _lastNetworkCheck = DateTime.Now;

        return (Math.Max(0, Math.Round(dl, 2)), Math.Max(0, Math.Round(ul, 2)));
    }
    private string GetReadableUptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{t.Days}d {t.Hours}h {t.Minutes}m {t.Seconds}s";
    }
}