namespace TurboSquadApp.Sources;

public static class GenerationModels
{
    public const string Default = "qwen/qwen3.6-35b-a3b";
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        Default, "openai/gpt-oss-20b", "deepseek/deepseek-v3.2",
    };
}

public sealed record GenerationModelRequest(string? Model);
