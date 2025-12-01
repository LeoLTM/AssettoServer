using System.Net.Http;
using System.Text;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BestLapTimesPlugin;

public class BestLapTimesPlugin : BackgroundService, IDisposable
{
    // Hardcoded configuration
    private const string CsvFileName = "best_laps.csv";
    private const string OutputDirectory = "lap_times";
    private const uint MinimumLapTimeMs = 10000;
    private const string LapTimeApiUrl = "http://localhost:8080/lap-times";
    
    private readonly EntryCarManager _entryCarManager;
    private readonly Dictionary<string, uint> _bestLapTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly HttpClient _httpClient = new();
    private string _csvFilePath = null!;

    public BestLapTimesPlugin(EntryCarManager entryCarManager)
    {
        _entryCarManager = entryCarManager;
        
        _entryCarManager.ClientConnected += OnClientConnected;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Create output directory if it doesn't exist
        Directory.CreateDirectory(OutputDirectory);
        _csvFilePath = Path.Combine(OutputDirectory, CsvFileName);

        // Load existing best lap times from CSV
        await LoadBestLapTimesFromCsvAsync();

        Log.Information("BestLapTimesPlugin started. CSV file: {CsvFilePath}", _csvFilePath);

        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        client.LapCompleted += OnLapCompleted;
    }

    private async void OnLapCompleted(ACTcpClient sender, LapCompletedEventArgs args)
    {
        try
        {
            string nickname = sender.Name ?? "Unknown";
            uint lapTime = args.Packet.LapTime;
            byte cuts = args.Packet.Cuts;

            // Reject laps with cuts (hardcoded: cuts not allowed)
            if (cuts > 0)
            {
                Log.Debug("Lap from {Nickname} rejected: {Cuts} cuts detected", nickname, cuts);
                return;
            }

            if (lapTime < MinimumLapTimeMs)
            {
                Log.Debug("Lap from {Nickname} rejected: lap time {LapTime}ms is below minimum {MinLapTime}ms",
                    nickname, lapTime, MinimumLapTimeMs);
                return;
            }

            // Check if this is a new best lap for this nickname
            bool isNewBest = false;
            await _fileLock.WaitAsync();
            try
            {
                if (!_bestLapTimes.TryGetValue(nickname, out uint currentBest) || lapTime < currentBest)
                {
                    _bestLapTimes[nickname] = lapTime;
                    isNewBest = true;
                    
                    Log.Information("New best lap for {Nickname}: {FormattedTime} ({LapTime}ms)",
                        nickname, FormatLapTime(lapTime), lapTime);
                }
            }
            finally
            {
                _fileLock.Release();
            }

            // Write to CSV if there's a new best
            if (isNewBest)
            {
                await WriteBestLapsToCsvAsync();
                
                // Send to API (fire-and-forget)
                string formattedTime = FormatLapTime(lapTime);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendLapTimeToApiAsync(nickname, lapTime, formattedTime);
                    }
                    catch (Exception apiEx)
                    {
                        Log.Error(apiEx, "Error sending lap time to API for {Nickname}", nickname);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing lap completion for {Nickname}", sender.Name);
        }
    }

    private async Task LoadBestLapTimesFromCsvAsync()
    {
        if (!File.Exists(_csvFilePath))
        {
            Log.Debug("No existing CSV file found at {CsvFilePath}, starting fresh", _csvFilePath);
            return;
        }

        try
        {
            await _fileLock.WaitAsync();
            try
            {
                var lines = await File.ReadAllLinesAsync(_csvFilePath);
                
                // Skip header row
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(',');
                    if (parts.Length >= 2 && uint.TryParse(parts[1], out uint lapTime))
                    {
                        string nickname = parts[0];
                        _bestLapTimes[nickname] = lapTime;
                    }
                }

                Log.Information("Loaded {Count} best lap times from CSV", _bestLapTimes.Count);
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading best lap times from CSV");
        }
    }

    private async Task WriteBestLapsToCsvAsync()
    {
        try
        {
            await _fileLock.WaitAsync();
            try
            {
                // Sort by lap time (fastest first)
                var sortedTimes = _bestLapTimes
                    .OrderBy(kvp => kvp.Value)
                    .ToList();

                await using var writer = new StreamWriter(_csvFilePath, false);
                
                // Write header
                await writer.WriteLineAsync("Nickname,BestLapTimeMs,FormattedTime,LastUpdated");
                
                // Write all entries
                foreach (var (nickname, lapTime) in sortedTimes)
                {
                    string formattedTime = FormatLapTime(lapTime);
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    await writer.WriteLineAsync($"{EscapeCsvField(nickname)},{lapTime},{formattedTime},{timestamp}");
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error writing best lap times to CSV");
        }
    }

    private static string FormatLapTime(uint lapTimeMs)
    {
        return TimeSpan.FromMilliseconds(lapTimeMs).ToString(@"mm\:ss\.fff");
    }

    private static string EscapeCsvField(string field)
    {
        // If the field contains comma, quote, or newline, wrap in quotes and escape quotes
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\""; 
        }
        return field;
    }

    private async Task SendLapTimeToApiAsync(string nickName, uint bestLapTimeMs, string formattedTime)
    {
        try
        {
            var payload = $"{{\"nickName\":\"{nickName.Replace("\"", "\\\"")}\",\"bestLapTimeMs\":{bestLapTimeMs},\"formattedTime\":\"{formattedTime}\"}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(LapTimeApiUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Failed to send lap time to API. Status: {StatusCode}, Reason: {ReasonPhrase}",
                    (int)response.StatusCode, response.ReasonPhrase);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "HTTP request failed when sending lap time to API for {Nickname}", nickName);
        }
        catch (TaskCanceledException ex)
        {
            Log.Error(ex, "HTTP request timed out when sending lap time to API for {Nickname}", nickName);
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        _fileLock.Dispose();
        base.Dispose();
    }
}