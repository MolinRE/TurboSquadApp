using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;

namespace TurboSquadApp.Content;

/// <summary>
/// Засев стартового контента при запуске: добавляет только то, чего в базе ещё нет,
/// поэтому повторный запуск дублей не создаёт, а правки Методиста не перезаписывает.
/// </summary>
public sealed class ContentSeeder(AppDbContext dbContext)
{
    public const string DefaultTripSettingsId = "default";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var directory = SeedContent.Directory;

        // Строгая валидация при сохранении (ADR-0001): битый сид останавливает запуск, а не попадает в базу.
        foreach (var seed in SeedContent.Events)
        {
            var report = EventValidator.Validate(seed.Document, directory, SeedContent.FlagsSetElsewhere(seed.Document.Id));
            if (!report.IsValid)
            {
                throw new InvalidOperationException(
                    $"Seed event '{seed.Document.Id}' is invalid: " +
                    string.Join("; ", report.Errors.Select(error => $"{error.Where}: {error.Message}")));
            }
        }

        var questions = SeedContent.Questions.Select(seed => QuestionRecordOf(seed, directory)).ToList();

        var existingScales = await dbContext.Scales.Select(scale => scale.Code).ToListAsync(cancellationToken);
        dbContext.Scales.AddRange(directory.Scales
            .Where(scale => !existingScales.Contains(scale.Code))
            .Select(scale => new ScaleRecord
            {
                Code = scale.Code, Name = scale.Name, Min = scale.Min, Max = scale.Max, Start = scale.Start,
                FailureThreshold = scale.FailureThreshold, Mandatory = scale.Mandatory, FailureReason = scale.FailureReason,
            }));

        var existingClasses = await dbContext.ServiceClasses.Select(serviceClass => serviceClass.Code).ToListAsync(cancellationToken);
        dbContext.ServiceClasses.AddRange(directory.Classes
            .Select((serviceClass, index) => new ServiceClassRecord
            {
                Code = serviceClass.Code, Name = serviceClass.Name, Description = serviceClass.Description, SortOrder = index,
            })
            .Where(record => !existingClasses.Contains(record.Code)));

        var existingEvents = await dbContext.EventDocuments
            .Select(record => new { record.EventId, record.Version })
            .ToListAsync(cancellationToken);
        dbContext.EventDocuments.AddRange(SeedContent.Events
            .Where(seed => !existingEvents.Any(existing => existing.EventId == seed.Document.Id && existing.Version == seed.Document.Version))
            .Select(seed => new EventDocumentRecord
            {
                EventId = seed.Document.Id, Version = seed.Document.Version, Document = seed.Json, PublishedAt = DateTimeOffset.UtcNow,
            }));

        if (!await dbContext.TripSettings.AnyAsync(settings => settings.Id == DefaultTripSettingsId, cancellationToken))
            dbContext.TripSettings.Add(new TripSettingsRecord
            {
                Id = DefaultTripSettingsId,
                Document = JsonSerializer.Serialize(SeedContent.Trip, EventJson.Options),
            });

        var existingQuestions = await dbContext.Questions.Select(question => question.Id).ToListAsync(cancellationToken);
        dbContext.Questions.AddRange(questions.Where(question => !existingQuestions.Contains(question.Id)));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Вопрос из сидов — опубликованным. Битый сид (свайп с чужой Шкалой или Классом) останавливает запуск.</summary>
    private static QuestionRecord QuestionRecordOf(SeedQuestion seed, ContentDirectory directory)
    {
        if (seed.Type != QuestionTypes.Swipe)
            throw new InvalidOperationException($"Seed question '{seed.Id}': only swipe questions are seeded, got '{seed.Type}'.");
        var options = seed.Options.Deserialize<SwipeOptions>(EventJson.Options)!;
        var unknownScales = options.Right.ScaleDeltas.Keys.Concat(options.Left.ScaleDeltas.Keys)
            .Where(code => directory.Scales.All(scale => scale.Code != code));
        var unknownClasses = seed.ServiceClasses.Where(code => directory.Classes.All(serviceClass => serviceClass.Code != code));
        if (unknownScales.Concat(unknownClasses).ToList() is [_, ..] unknown)
            throw new InvalidOperationException($"Seed question '{seed.Id}' refers to unknown codes: {string.Join(", ", unknown)}.");

        return new QuestionRecord
        {
            Id = seed.Id, Type = seed.Type, Status = QuestionStatuses.Published, Statement = seed.Statement,
            Options = JsonSerializer.Serialize(options, EventJson.Options),
            ExplanationText = seed.Explanation.Text, ExplanationKeyFact = seed.Explanation.KeyFact,
            Quote = seed.Explanation.Quote, Source = seed.Explanation.Source, Topic = seed.Topic,
            Categories = JsonSerializer.Serialize(seed.Categories), ServiceClasses = JsonSerializer.Serialize(seed.ServiceClasses),
            BaseFrequency = seed.BaseFrequency, TimeLimitSec = seed.TimeLimitSec,
        };
    }
}
