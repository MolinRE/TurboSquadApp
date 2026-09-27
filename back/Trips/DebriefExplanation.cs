using System.Text;
using TurboSquadApp.Voice;

namespace TurboSquadApp.Trips;

/// <summary>Разбор, слой Б: ИИ-объяснение последствий и модель, которая его написала.</summary>
public sealed record DebriefExplanationView(string Text, string Model);

/// <summary>
/// Запрос к LLM для слоя Б Разбора (PRD v7 §8). Модель видит только факты слоя А — решения, Шкалы,
/// комментарии Вариантов с Источником и расшифровки голоса, — поэтому не может выдумать последствия
/// или цифры. Очки и Шкалы текст не меняет. Пока не сохраняется: пишется заново при каждом открытии.
/// </summary>
public static class DebriefExplanation
{
    private const string SystemPrompt =
        """
        Ты наставник проводников ВСМ «Москва — Санкт-Петербург». Тебе дают факты прошедшего тренировочного Рейса.
        Напиши проводнику 3–4 предложения на русском, обращаясь на «вы»: к чему в реальной поездке могли привести
        его ошибки и почему важно действовать так, как советует комментарий, со ссылкой на Источник, если он указан.
        Если ошибок не было, коротко объясни, почему его действия правильные.
        Опирайся только на переданные факты: не придумывай события, цифры, пункты документов и последствия,
        которых нет в фактах. Без заголовков, списков и markdown, не больше 600 символов.
        """;

    public static LlmRequest RequestFor(Debrief debrief)
    {
        var facts = new StringBuilder();
        facts.AppendLine($"Итог Рейса: {debrief.Summary}");
        foreach (var ev in debrief.Events)
        {
            facts.AppendLine();
            facts.AppendLine($"Событие «{ev.Title}» — {ResultOf(ev.Result)}.");
            foreach (var decision in ev.Decisions)
            {
                facts.AppendLine($"- Ситуация: {decision.Situation}");
                facts.AppendLine($"  Решение проводника: {(decision.TimedOut ? "не ответил вовремя" : decision.Text)}");
                var said = debrief.VoiceAttempts.LastOrDefault(attempt =>
                    attempt.EventId == ev.EventId && attempt.StepId == decision.StepId && attempt.Applied);
                if (said?.Transcript is { Length: > 0 } transcript) facts.AppendLine($"  Сказал вслух: «{transcript}»");
                if (decision.Changes.Count > 0)
                    facts.AppendLine("  Шкалы: " + string.Join(", ", decision.Changes.Select(change =>
                        $"{change.Name} {change.Before} → {change.After}")));
                if (decision.CriticalError) facts.AppendLine("  Критическая ошибка.");
                if (decision.Comment is { Length: > 0 } comment) facts.AppendLine($"  Комментарий: {comment}");
                if (decision.Source is { Length: > 0 } source) facts.AppendLine($"  Источник: {source}");
            }
            if (ev.OutcomeSituation is { Length: > 0 } outcome) facts.AppendLine($"Чем закончилось: {outcome}");
        }
        return new LlmRequest(SystemPrompt, facts.ToString());
    }

    private static string ResultOf(EventResult result) => result switch
    {
        EventResult.Success => "удачный исход",
        EventResult.Failure => "неудачный исход",
        _ => "прервано Срывом рейса",
    };
}
