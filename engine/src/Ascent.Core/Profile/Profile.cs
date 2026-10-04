using System.Globalization;
using System.Text.RegularExpressions;
using Ascent.Core.Errors;
using Ascent.Core.Platform;
using Ascent.Core.Progress;
using Ascent.Core.Time;

namespace Ascent.Core.Profile;

/// <summary>Profile keys (domain entities: LearnerProfile).</summary>
public static class ProfileKeys
{
    /// <summary>Random local identifier; never transmitted.</summary>
    public const string LearnerId = "learnerId";

    /// <summary>When the profile was created.</summary>
    public const string CreatedUtc = "createdUtc";

    /// <summary>When the rules of engagement were accepted (E1-04).</summary>
    public const string RulesAcceptedUtc = "rulesAcceptedUtc";

    /// <summary>Study hours per week, 1–40.</summary>
    public const string WeeklyHours = "weeklyHours";

    /// <summary>IANA or Windows time-zone ID.</summary>
    public const string TimeZone = "timeZone";

    /// <summary>Stand-up days per week, 1–7 (WG-01).</summary>
    public const string WeeklyGoal = "weeklyGoal";

    /// <summary>Where Teach-backs are saved (E1-07).</summary>
    public const string TeachBackFolder = "teachBackFolder";

    /// <summary>Plain output by default.</summary>
    public const string PlainMode = "plainMode";

    /// <summary>GitHub user for <c>ascent sync</c> (BUG-02).</summary>
    public const string GitHubUser = "githubUser";

    /// <summary>OpenAI-compatible endpoint for the optional AI reviewer.</summary>
    public const string AiEndpoint = "ai.endpoint";

    /// <summary>Model name for the AI reviewer.</summary>
    public const string AiModel = "ai.model";

    /// <summary>The remote AI endpoint the Learner already confirmed (PRV-02).</summary>
    public const string AiRemoteConfirmed = "ai.remoteConfirmed";

    /// <summary>The booked exam date (FR-24).</summary>
    public const string ExamDate = "examDate";

    /// <summary>The exam-date prompt is snoozed until this local date (EXM-01).</summary>
    public const string ExamPromptSnoozedUntil = "examPromptSnoozedUntil";

    /// <summary>The Azure region for Cloud Stages.</summary>
    public const string AzureLocation = "azure.location";

    /// <summary>When <c>start</c> offered to make the workspace a git repository (P4); it offers once.</summary>
    public const string WorkspaceOfferedUtc = "workspaceOfferedUtc";
}

/// <summary>The Learner profile, read from its key/value store.</summary>
public sealed record LearnerProfile(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>The default weekly goal (FQ1 = C).</summary>
    public const int DefaultWeeklyGoal = 5;

    /// <summary>The random local Learner ID.</summary>
    public string? LearnerId => Values.GetValueOrDefault(ProfileKeys.LearnerId);

    /// <summary>True once the rules of engagement were accepted.</summary>
    public bool RulesAccepted => Values.ContainsKey(ProfileKeys.RulesAcceptedUtc);

    /// <summary>Stand-up days per week.</summary>
    public int WeeklyGoal => int.TryParse(Values.GetValueOrDefault(ProfileKeys.WeeklyGoal), NumberStyles.None, CultureInfo.InvariantCulture, out var goal) ? goal : DefaultWeeklyGoal;

    /// <summary>Study hours per week, if set.</summary>
    public int? WeeklyHours => int.TryParse(Values.GetValueOrDefault(ProfileKeys.WeeklyHours), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ? hours : null;

    /// <summary>The configured time zone, if any.</summary>
    public string? TimeZone => Values.GetValueOrDefault(ProfileKeys.TimeZone);

    /// <summary>The configured Teach-back folder, if any.</summary>
    public string? TeachBackFolder => Values.GetValueOrDefault(ProfileKeys.TeachBackFolder);

    /// <summary>The configured GitHub user, if any.</summary>
    public string? GitHubUser => Values.GetValueOrDefault(ProfileKeys.GitHubUser);

    /// <summary>The configured AI endpoint, if any.</summary>
    public Uri? AiEndpoint => Uri.TryCreate(Values.GetValueOrDefault(ProfileKeys.AiEndpoint), UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>The configured AI model, if any.</summary>
    public string? AiModel => Values.GetValueOrDefault(ProfileKeys.AiModel);

    /// <summary>The booked exam date, if any.</summary>
    public DateOnly? ExamDate => DateOnly.TryParseExact(Values.GetValueOrDefault(ProfileKeys.ExamDate), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>The Azure region for Cloud Stages; <c>eastus</c> unless set.</summary>
    public string AzureLocation => Values.GetValueOrDefault(ProfileKeys.AzureLocation) is { Length: > 0 } location ? location : "eastus";

    /// <summary>The local date the exam prompt is snoozed until, if any.</summary>
    public DateOnly? ExamPromptSnoozedUntil =>
        DateOnly.TryParseExact(Values.GetValueOrDefault(ProfileKeys.ExamPromptSnoozedUntil), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

/// <summary>One Learner-settable configuration key.</summary>
/// <param name="Key">The key.</param>
/// <param name="Description">What it controls.</param>
/// <param name="Validate">Returns an error message, or null when the value is valid.</param>
public sealed record ConfigSetting(string Key, string Description, Func<string, string?> Validate);

/// <summary>The settings <c>ascent config</c> can change, with their validation.</summary>
public static partial class ConfigCatalog
{
    /// <summary>Every settable key.</summary>
    public static IReadOnlyList<ConfigSetting> Settings { get; } =
    [
        new(ProfileKeys.WeeklyHours, "Study hours per week (1-40); used by the study plan.", value => IntegerIn(value, 1, 40)),
        new(ProfileKeys.TimeZone, "Time zone for local days and weeks (IANA or Windows ID); empty means the system zone.", ValidateZone),
        new(ProfileKeys.WeeklyGoal, "Stand-up days per week (1-7).", value => IntegerIn(value, 1, 7)),
        new(ProfileKeys.TeachBackFolder, "Where Teach-backs are saved (absolute, or relative to the repository).", value => string.IsNullOrWhiteSpace(value) ? "Give a folder." : null),
        new(ProfileKeys.PlainMode, "Plain output by default (true or false).", value => value is "true" or "false" ? null : "Use true or false."),
        new(ProfileKeys.GitHubUser, "Your GitHub user name, for 'ascent sync'.", value => GitHubUserPattern().IsMatch(value) ? null : "That isn't a valid GitHub user name."),
        new(ProfileKeys.AiEndpoint, "OpenAI-compatible endpoint for the optional AI reviewer (HTTPS, or HTTP on this machine).", ValidateEndpoint),
        new(ProfileKeys.AiModel, "Model name for the AI reviewer.", value => value.Length is > 0 and <= 100 ? null : "Give a model name of up to 100 characters."),
        new(ProfileKeys.ExamDate, "Your exam date (yyyy-MM-dd).", value => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? null : "Use a date like 2027-02-15."),
        new(ProfileKeys.AzureLocation, "Azure region for Cloud Stages, such as eastus or westeurope (default eastus).", value => AzureRegionPattern().IsMatch(value) ? null : "Use a region name such as eastus, in lower case."),
    ];

    /// <summary>Finds a setting, or null.</summary>
    public static ConfigSetting? Find(string key) => Settings.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.Ordinal));

    private static string? IntegerIn(string value, int minimum, int maximum) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"Use a whole number from {minimum} to {maximum}.");

    private static string? ValidateZone(string value)
    {
        try
        {
            LocalCalendar.ResolveZone(value);
            return null;
        }
        catch (AscentException ex)
        {
            return ex.Message + " " + ex.NextStep;
        }
    }

    private static string? ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return "Give an absolute http(s) URL, such as http://localhost:11434/v1.";
        }

        return new NetworkPolicy(uri).Check(uri, NetworkPurpose.AiReviewer) is { } reason ? "Not allowed: " + reason + "." : null;
    }

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex GitHubUserPattern();

    [GeneratedRegex("^[a-z][a-z0-9]{1,39}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AzureRegionPattern();
}

/// <summary>Creates the profile, records the rules of engagement, and changes settings (E1-04, E1-07).</summary>
public sealed class ProfileService
{
    private readonly IProfileStore store;
    private readonly TimeProvider time;
    private readonly IRandomSource random;

    /// <summary>Creates the service.</summary>
    public ProfileService(IProfileStore store, TimeProvider time, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(random);
        this.store = store;
        this.time = time;
        this.random = random;
    }

    /// <summary>The current profile.</summary>
    public LearnerProfile Load() => new(store.All());

    /// <summary>Creates the profile on first use and returns it.</summary>
    public LearnerProfile EnsureProfile()
    {
        if (!store.All().ContainsKey(ProfileKeys.LearnerId))
        {
            store.Write(ProfileKeys.LearnerId, random.Hex(16));
            store.Write(ProfileKeys.CreatedUtc, Utc.ToText(time.GetUtcNow()));
        }

        return Load();
    }

    /// <summary>Records acceptance of the rules of engagement (E1-04).</summary>
    public void AcceptRules() => store.Write(ProfileKeys.RulesAcceptedUtc, Utc.ToText(time.GetUtcNow()));

    /// <summary>Validates and stores a setting.</summary>
    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var setting = ConfigCatalog.Find(key) ?? throw UnknownKey(key);
        var trimmed = value.Trim();
        if (setting.Validate(trimmed) is { } error)
        {
            throw new UsageException(key + ": " + error, "Run 'ascent config' to see every setting.");
        }

        store.Write(key, trimmed);
    }

    /// <summary>Removes a setting so its default applies.</summary>
    public void Unset(string key)
    {
        _ = ConfigCatalog.Find(key) ?? throw UnknownKey(key);
        store.Remove(key);
    }

    /// <summary>Remembers that the Learner confirmed a remote AI endpoint (PRV-02).</summary>
    public void ConfirmRemoteEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        store.Write(ProfileKeys.AiRemoteConfirmed, endpoint.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>Records that <c>start</c> offered to make the workspace a git repository, so it offers once (P4).</summary>
    public void MarkWorkspaceOffered() => store.Write(ProfileKeys.WorkspaceOfferedUtc, Utc.ToText(time.GetUtcNow()));

    /// <summary>Snoozes the exam-date prompt until the next local day (EXM-01).</summary>
    public void SnoozeExamPrompt(DateOnly today) => store.Write(ProfileKeys.ExamPromptSnoozedUntil, today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static UsageException UnknownKey(string key) =>
        new("There is no setting called '" + SafeText.Sanitize(key, 60) + "'.", "Run 'ascent config' to see every setting.");
}
