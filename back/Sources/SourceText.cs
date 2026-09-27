using System.Text.RegularExpressions;

namespace TurboSquadApp.Sources;

public sealed record SourceSection(string Reference, string Text, string? Context = null);

public static partial class SourceText
{
    private const int MaxSectionChars = 2_000;

    public static IReadOnlyList<SourceSection> Sections(string text)
    {
        var result = new List<SourceSection>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var reference = "Текст";
        var body = new List<string>();
        string? tableHeader = null;
        string? heading = null;
        void Flush()
        {
            var value = string.Join("\n", body).Trim();
            AddParts(reference, value, heading);
            body.Clear();
        }
        void AddParts(string point, string value, string? context = null)
        {
            if (value.Length == 0) return;
            if (context?.Length > 900) context = context[..900];
            var limit = MaxSectionChars - (context?.Length ?? 0) - 20;
            var parts = new List<string>();
            var start = 0;
            while (start < value.Length)
            {
                var end = Math.Min(start + limit, value.Length);
                if (end < value.Length)
                {
                    var floor = start + limit / 2;
                    var newline = value.LastIndexOf('\n', end - 1, end - start);
                    if (newline >= floor) end = newline + 1;
                    else
                    {
                        var sentenceEnd = Enumerable.Range(floor, end - floor).Reverse()
                            .FirstOrDefault(index => char.IsWhiteSpace(value[index]) &&
                                index > start && value[index - 1] is '.' or '!' or '?');
                        if (sentenceEnd > 0) end = sentenceEnd + 1;
                        else
                        {
                            var space = value.LastIndexOf(' ', end - 1, end - start);
                            if (space >= floor) end = space + 1;
                        }
                    }
                }
                parts.Add(value[start..end].Trim());
                start = end;
                while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
            }
            for (var part = 0; part < parts.Count; part++)
                result.Add(new(parts.Count == 1 ? point : $"{point}, часть {part + 1}", parts[part], context));
        }
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (MarkdownHeading().IsMatch(line))
            {
                Flush();
                tableHeader = null;
                reference = $"Раздел, строка {index + 1}";
                heading = line.Trim();
                continue;
            }
            if (line.Contains('|') && index + 1 < lines.Length && TableSeparator().IsMatch(lines[index + 1]))
            {
                Flush();
                tableHeader = line.Trim();
                index++;
                continue;
            }
            if (tableHeader is not null && line.Contains('|'))
            {
                var context = heading is null ? tableHeader : $"{heading}\n{tableHeader}";
                AddParts($"Таблица, строка {index + 1}", line.Trim(), context);
                continue;
            }
            tableHeader = null;
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

    [GeneratedRegex(@"^\s*#{1,6}\s+")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?\s*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"(?<![\w.+-])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}(?![\w.-])", RegexOptions.IgnoreCase)]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?:паспорт(?:\s+(?:серия|серии))?|серия)\s*[:№]?\s*\d{4}\s*(?:(?:номер|№)\s*)?\d{6}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Passport();

    [GeneratedRegex(@"(?<!\d)(?:\+7|8)[\s(.-]*\d{3}[\s).-]*\d{3}[\s.-]*\d{2}[\s.-]*\d{2}(?!\d)")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\b[А-ЯЁ][а-яё]{1,}(?:-[А-ЯЁ][а-яё]{1,})?\s+[А-ЯЁ][а-яё]{1,}\s+[А-ЯЁ][а-яё]{1,}\b")]
    private static partial Regex FullName();
}
