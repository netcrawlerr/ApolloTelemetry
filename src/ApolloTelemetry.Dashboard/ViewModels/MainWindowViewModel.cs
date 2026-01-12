using System;
using System.IO;
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

    private const double GB_IN_BYTES = 1073741824.0;
    public string MemoryDisplay => Stats == null ? "0 / 0 GB" : 
        $"{Math.Round((Stats.TotalMemoryBytes - Stats.AvailableMemoryBytes) / GB_IN_BYTES, 2)} / {Math.Round(Stats.TotalMemoryBytes / 1073741824.0, 2)} GB";

    public double MemoryUsedBytes => Stats == null ? 0 : (Stats.TotalMemoryBytes - Stats.AvailableMemoryBytes);

    public double MemoryPercent => (Stats == null || Stats.TotalMemoryBytes == 0) ? 0 : 
        Math.Round((double)(Stats.TotalMemoryBytes - Stats.AvailableMemoryBytes) / Stats.TotalMemoryBytes * 100, 1);

    
    partial void OnStatsChanged(ServerStats? value)
    {
        OnPropertyChanged(nameof(MemoryDisplay));
        OnPropertyChanged(nameof(MemoryUsedBytes));
        OnPropertyChanged(nameof(MemoryPercent));
    }

    public MainWindowViewModel()
    {
        Task.Run(StartListener);
    }

    private async Task StartListener()
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:5002/telemetry/");

        try {
            listener.Start();
            Console.WriteLine(">>> Dashboard Active on http://127.0.0.1:5002/telemetry/");

            while (true) {
                var context = await listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream);
                string json = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var receivedData = JsonSerializer.Deserialize<ServerStats>(json, options);

                if (receivedData != null) {
                    Dispatcher.UIThread.Post(() => { Stats = receivedData; });
                }

                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }
        catch (Exception ex) {
            Console.WriteLine($">>> CRITICAL ERROR: {ex.Message}");
        }
    }
}