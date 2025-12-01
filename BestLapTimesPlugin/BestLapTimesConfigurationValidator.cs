using FluentValidation;

namespace BestLapTimesPlugin;

public class BestLapTimesConfigurationValidator : AbstractValidator<BestLapTimesConfiguration>
{
    public BestLapTimesConfigurationValidator()
    {
        RuleFor(cfg => cfg.LapTimeApiUrl)
            .NotEmpty()
            .WithMessage("LapTimeApiUrl is required");

        RuleFor(cfg => cfg.LapTimeApiUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) 
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .When(cfg => !string.IsNullOrEmpty(cfg.LapTimeApiUrl))
            .WithMessage("LapTimeApiUrl must be a valid HTTP or HTTPS URL");

        RuleFor(cfg => cfg.ApiTimeoutSeconds)
            .GreaterThan(0)
            .WithMessage("ApiTimeoutSeconds must be greater than 0");

        RuleFor(cfg => cfg.MinimumLapTimeMs)
            .GreaterThan(0u)
            .WithMessage("MinimumLapTimeMs must be greater than 0");

        RuleFor(cfg => cfg.OutputDirectory)
            .NotEmpty()
            .When(cfg => cfg.EnableCsvOutput)
            .WithMessage("OutputDirectory is required when EnableCsvOutput is true");

        RuleFor(cfg => cfg.CsvFileName)
            .NotEmpty()
            .When(cfg => cfg.EnableCsvOutput)
            .WithMessage("CsvFileName is required when EnableCsvOutput is true");
    }
}
