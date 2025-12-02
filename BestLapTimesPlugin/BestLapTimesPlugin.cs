using System.Net.Http;
using System.Text;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BestLapTimesPlugin;

public class BestLapTimesPlugin : BackgroundService, IDisposable
{
    private readonly BestLapTimesConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly Dictionary<string, uint> _bestLapTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _sessionBestLapTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private string _csvFilePath = null!;

    public BestLapTimesPlugin(BestLapTimesConfiguration configuration, EntryCarManager entryCarManager)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _httpClient = new HttpClient { Timeout = _configuration.ApiTimeout };
        
        _entryCarManager.ClientConnected += OnClientConnected;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.Information("BestLapTimesPlugin started with config: ApiUrl={ApiUrl}, ApiTimeout={Timeout}s, CsvEnabled={CsvEnabled}, CsvPath={CsvPath}, MinLapTime={MinLap}ms, MaxCuts={MaxCuts}, SubmitAllLaps={SubmitAll}, IgnoreCsvOnStartup={IgnoreCsv}",
            _configuration.LapTimeApiUrl, _configuration.ApiTimeoutSeconds, _configuration.EnableCsvOutput,
            _configuration.EnableCsvOutput ? Path.Combine(_configuration.OutputDirectory, _configuration.CsvFileName) : "N/A",
            _configuration.MinimumLapTimeMs, _configuration.MaxAllowedCuts, _configuration.SubmitAllLaps, _configuration.IgnoreCsvOnStartup);

        if (_configuration.EnableCsvOutput)
        {
            // Create output directory if it doesn't exist
            Directory.CreateDirectory(_configuration.OutputDirectory);
            _csvFilePath = Path.Combine(_configuration.OutputDirectory, _configuration.CsvFileName);

            // Load existing best lap times from CSV
            await LoadBestLapTimesFromCsvAsync();
        }

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

            // Reject laps with too many cuts
            if (cuts > _configuration.MaxAllowedCuts)
            {
                Log.Debug("Lap from {Nickname} rejected: {Cuts} cuts detected (max allowed: {MaxCuts})", 
                    nickname, cuts, _configuration.MaxAllowedCuts);
                return;
            }

            if (lapTime < _configuration.MinimumLapTimeMs)
            {
                Log.Debug("Lap from {Nickname} rejected: lap time {LapTime}ms is below minimum {MinLapTime}ms",
                    nickname, lapTime, _configuration.MinimumLapTimeMs);
                return;
            }

            // Check if this is a new best lap for this nickname
            bool isNewAllTimeBest = false;
            bool isNewSessionBest = false;
            await _fileLock.WaitAsync();
            try
            {
                // Check all-time best (for CSV)
                if (!_bestLapTimes.TryGetValue(nickname, out uint currentAllTimeBest) || lapTime < currentAllTimeBest)
                {
                    _bestLapTimes[nickname] = lapTime;
                    isNewAllTimeBest = true;
                    
                    Log.Information("New best lap for {Nickname}: {FormattedTime} ({LapTime}ms)",
                        nickname, FormatLapTime(lapTime), lapTime);
                }

                // Check session best (for API when IgnoreCsvOnStartup is enabled)
                if (_configuration.IgnoreCsvOnStartup)
                {
                    if (!_sessionBestLapTimes.TryGetValue(nickname, out uint currentSessionBest) || lapTime < currentSessionBest)
                    {
                        _sessionBestLapTimes[nickname] = lapTime;
                        isNewSessionBest = true;
                    }
                }
            }
            finally
            {
                _fileLock.Release();
            }

            // Write to CSV if there's a new all-time best and CSV output is enabled
            if (isNewAllTimeBest && _configuration.EnableCsvOutput)
            {
                await WriteBestLapsToCsvAsync();
            }

            // Determine if we should send to API:
            // - If SubmitAllLaps: always send
            // - If IgnoreCsvOnStartup: send if it's a new session best
            // - Otherwise: send if it's a new all-time best
            bool shouldSendToApi = _configuration.SubmitAllLaps || 
                                   (_configuration.IgnoreCsvOnStartup ? isNewSessionBest : isNewAllTimeBest);

            if (shouldSendToApi)
            {
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

    private async Task SendLapTimeToApiAsync(string nickName, uint lapTimeMs, string formattedTime)
    {
        try
        {
            var payload = $"{{\"nickName\":\"{nickName.Replace("\"", "\\\"")}\",\"bestLapTimeMs\":{lapTimeMs},\"formattedTime\":\"{formattedTime}\"}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            
            // Hardcoded URL to work around YAML parser IP address parsing issue
            const string apiUrl = "http://192.168.2.220:8080/lap-times";
            var response = await _httpClient.PostAsync(apiUrl, content);
            
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
            Log.Warning(ex, "HTTP request timed out when sending lap time to API for {Nickname}", nickName);
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        _fileLock.Dispose();
        base.Dispose();
    }
}