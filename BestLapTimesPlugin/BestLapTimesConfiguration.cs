using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace BestLapTimesPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class BestLapTimesConfiguration : IValidateConfiguration<BestLapTimesConfigurationValidator>
{
    [YamlMember(Description = "API URL to submit lap times to (required)")]
    public string LapTimeApiUrl { get; init; } = null!;

    [YamlMember(Description = "Timeout in seconds for API requests")]
    public int ApiTimeoutSeconds { get; init; } = 1;

    [YamlMember(Description = "Enable saving lap times to a CSV file")]
    public bool EnableCsvOutput { get; init; } = true;

    [YamlMember(Description = "Output directory for the CSV file")]
    public string OutputDirectory { get; init; } = "lap_times";

    [YamlMember(Description = "CSV filename for best lap times")]
    public string CsvFileName { get; init; } = "best_laps.csv";

    [YamlMember(Description = "Minimum valid lap time in milliseconds (laps below this are rejected)")]
    public uint MinimumLapTimeMs { get; init; } = 10000;

    [YamlMember(Description = "Maximum allowed cuts for a valid lap (0 = no cuts allowed)")]
    public byte MaxAllowedCuts { get; init; } = 0;

    [YamlMember(Description = "Submit all valid laps to API, not just personal bests")]
    public bool SubmitAllLaps { get; init; } = false;

    [YamlMember(Description = "Ignore CSV lap times when determining new bests (each session starts fresh for API submissions, but CSV still tracks all-time bests)")]
    public bool IgnoreCsvOnStartup { get; init; } = false;

    [YamlIgnore]
    public TimeSpan ApiTimeout => TimeSpan.FromSeconds(ApiTimeoutSeconds);
}
