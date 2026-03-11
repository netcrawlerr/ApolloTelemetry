using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using ApolloTelemetry.Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;

namespace ApolloTelemetry.Dashboard.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ServerStats? _stats;

    [ObservableProperty] private string _mySqlStatus = "Unknown";
    [ObservableProperty] private string _postgreSqlStatus = "Unknown";
    [ObservableProperty] private string _msSqlStatus = "Unknown";
    [ObservableProperty] private string _mongoDbStatus = "Unknown";

    private const double GB_IN_BYTES = 1073741824.0;


    public static string FormatBytesToGb(long bytes) => $"{Math.Round(bytes / GB_IN_BYTES, 1)} GB";


    public string MemoryDisplay => Stats == null
        ? "0 / 0 GB"
        : $"{Math.Round((Stats.TotalMemoryBytes - Stats.AvailableMemoryBytes) / GB_IN_BYTES, 2)} / {Math.Round(Stats.TotalMemoryBytes / GB_IN_BYTES, 2)} GB";

    public double MemoryUsedBytes => Stats == null ? 0 : (Stats.TotalMemoryBytes - Stats.AvailableMemoryBytes);

    public MainWindowViewModel()
    {
        Task.Run(StartListener);
    }

    private async Task StartListener()
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://*:5002/telemetry/");
        try
        {
            listener.Start();
            while (true)
            {
                var context = await listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream);
                string json = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var receivedData = JsonSerializer.Deserialize<ServerStats>(json, options);

                if (receivedData != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Stats = receivedData;

                        // re-read the calculated properties
                        OnPropertyChanged(nameof(MemoryDisplay));
                        OnPropertyChanged(nameof(MemoryUsedBytes));
                        
                        foreach (var service in receivedData.DatabaseServices)
                        {
                            switch (service.Name)
                            {
                                case "MySQL": MySqlStatus = service.Status; break;
                                case "PostgreSQL": PostgreSqlStatus = service.Status; break;
                                case "MSSQL": MsSqlStatus = service.Status; break;
                                case "MongoDB": MongoDbStatus = service.Status; break;
                            }
                        }
                    });
                }

                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($">>> Error: {ex.Message}");
        }
    }
}