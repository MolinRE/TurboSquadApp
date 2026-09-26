namespace TurboSquadApp.Tests.Events;

internal static class TestData
{
    /// <summary>Черновик «Два пассажира на одно место» v2 из прототипа: 8 ошибок и 1 предупреждение.</summary>
    public static string BrokenDraftJson() =>
        File.ReadAllText(Path.Combine("Events", "TestData", "sit-33-v2-broken.json"));
}
