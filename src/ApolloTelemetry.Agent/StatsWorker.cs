using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using ApolloTelemetry.Common.Models;

namespace ApolloTelemetry.Agent;

public class StatsWorker : BackgroundService
{
    private readonly ILogger<StatsWorker> _logger;
    private readonly HttpClient _httpClient;

 
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

        var stats = new ServerStats
        {
            DeviceName = Environment.MachineName,
            Uptime = GetReadableUptime(),
            OSVersion = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            TotalMemoryBytes = memInfo.TotalAvailableMemoryBytes,
            AvailableMemoryBytes = memInfo.TotalAvailableMemoryBytes - memInfo.MemoryLoadBytes,
            Drives = new List<DriveData>()
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
                        stats.Drives.Add(new DriveData
                        {
                            Name = drive.Name,
                            TotalSize = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace,
                            UsedPercentage =
                                Math.Round(100 - ((double)drive.AvailableFreeSpace / drive.TotalSize * 100), 2)
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

    private string GetReadableUptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{t.Days}d {t.Hours}h {t.Minutes}m {t.Seconds}s";
    }
}