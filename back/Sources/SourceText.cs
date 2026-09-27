using System.Text.RegularExpressions;

namespace TurboSquadApp.Sources;

public sealed record SourceSection(string Reference, string Text);

public static partial class SourceText
{
    public static IReadOnlyList<SourceSection> Sections(string text)
    {
        var result = new List<SourceSection>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var reference = "Текст";
        var body = new List<string>();
        void Flush()
        {
            var value = string.Join("\n", body).Trim();
            if (value.Length > 0) result.Add(new(reference, value));
            body.Clear();
        }
        foreach (var line in lines)
        {
            var point = Point().Match(line);
            if (point.Success)
            {
                Flush();
                reference = point.Groups[1].Value;
                body.Add(point.Groups[2].Value);
            }
            else if (!string.IsNullOrWhiteSpace(line)) body.Add(line.Trim());
        }
        Flush();
        return result;
    }

    public static string RemovePersonalData(string text)
    {
        var cleaned = Email().Replace(text, "[почта удалена]");
        cleaned = Passport().Replace(cleaned, "[паспорт удалён]");
        cleaned = Phone().Replace(cleaned, "[телефон удалён]");
        return FullName().Replace(cleaned, "[ФИО удалено]");
    }

    [GeneratedRegex(@"^\s*(?:§\s*)?(\d+(?:\.\d+)*[.)]?)\s+(.+)$")]
    private static partial Regex Point();

    [GeneratedRegex(@"(?<![\w.+-])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}(?![\w.-])", RegexOptions.IgnoreCase)]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?:паспорт(?:\s+(?:серия|серии))?|серия)\s*[:№]?\s*\d{4}\s*(?:(?:номер|№)\s*)?\d{6}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Passport();

    [GeneratedRegex(@"(?<!\d)(?:\+7|8)[\s(.-]*\d{3}[\s).-]*\d{3}[\s.-]*\d{2}[\s.-]*\d{2}(?!\d)")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\b[А-ЯЁ][а-яё]{1,}(?:-[А-ЯЁ][а-яё]{1,})?\s+[А-ЯЁ][а-яё]{1,}\s+[А-ЯЁ][а-яё]{1,}\b")]
    private static partial Regex FullName();
}
