using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Content;
using TurboSquadApp.Data;
using TurboSquadApp.Events;
using TurboSquadApp.Questions;

namespace TurboSquadApp.Tests.Content;

public class ContentSeederTests
{
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private async Task Seed()
    {
        await using var db = new AppDbContext(_options);
        await new ContentSeeder(db).SeedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Empty_database_gets_directories_scored_events_and_trip_settings()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        Assert.Equal(["loyalty", "safety"], await db.Scales.OrderBy(s => s.Code).Select(s => s.Code).ToListAsync());
        Assert.Equal(
            ["standard", "comfort", "business", "first"],
            await db.ServiceClasses.OrderBy(c => c.SortOrder).Select(c => c.Code).ToListAsync());
        Assert.Equal(
            [("sit-06", 4), ("sit-33", 4), ("zastup", 1)],
            (await db.EventDocuments.ToListAsync()).Select(e => (e.EventId, e.Version)).Order());
        var trip = Assert.Single(await db.TripSettings.ToListAsync());
        Assert.Equal("zastup", JsonSerializer.Deserialize<TripSettings>(trip.Document, EventJson.Options)!.ShiftStartEvent);
    }

    [Fact]
    public async Task Stored_event_document_is_readable_by_validator()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        var stored = await db.EventDocuments.SingleAsync(e => e.EventId == "sit-33");
        var report = EventValidator.ValidateJson(stored.Document, SeedContent.Directory, SeedContent.FlagsSetElsewhere("sit-33"));
        Assert.True(report.IsValid);
    }

    [Fact]
    public async Task Empty_database_gets_published_swipe_questions()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        var questions = await db.Questions.Where(q => q.Type == QuestionTypes.Swipe).ToListAsync();
        Assert.Equal(15, questions.Count);
        Assert.All(questions, q => Assert.Equal(QuestionStatuses.Published, q.Status));
        var pet = questions.Single(q => q.Id == "sw-pet-carrier");
        Assert.Equal("left", JsonNode.Parse(pet.Options)!["correct"]!.GetValue<string>());
        Assert.Equal(SwipeSide.Left, Assert.IsType<SwipeOptions>(pet.ReadOptions()).Correct);
        Assert.All(questions, q => Assert.True(QuestionValidator.ValidateForPublication(q, SeedContent.Directory).IsValid));
        Assert.Equal(("только в переноске", "Ситуации на борту, №4"), (pet.ExplanationKeyFact, pet.Source));
    }

    private static readonly string[] BlitzTypes = [QuestionTypes.Single, QuestionTypes.Multiple, QuestionTypes.Sequence];

    // Верные ответы и порядок — из «Ситуаций на борту».
    private static readonly Dictionary<string, string[]> BlitzAnswers = new()
    {
        ["bz-alcohol-bistro"] = ["Только в вагоне-бистро"],
        ["bz-dead-phone-ticket"] = ["Проверить данные билета по документу, на который он оформлен"],
        ["bz-unattended-item"] = ["Не трогать вещь", "Сообщить начальнику поезда", "Сообщить сотрудникам ПТБ по поездной радиосвязи"],
        ["bz-passenger-unwell"] =
            ["Вызвать начальника поезда любым доступным способом", "Спросить о медиках среди пассажиров по громкой связи", "Оказать помощь в рамках своей компетенции"],
        ["bz-role-model"] = ["Признать ситуацию", "Обозначить правило", "Предложить решение", "Заверить"],
        ["bz-pet-carrier-steps"] =
            ["Вежливо напомнить правила провоза питомцев", "Предложить разместить животное в переноске, при продаже на борту — приобрести её", "При отказе вызвать начальника поезда"],
    };

    [Fact]
    public async Task Empty_database_gets_published_blitz_questions_of_three_types()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        var blitz = await db.Questions.Where(q => BlitzTypes.Contains(q.Type)).ToListAsync();
        Assert.All(BlitzTypes, type => Assert.True(blitz.Count(q => q.Type == type) >= 2, $"{type}: меньше двух Вопросов"));
        Assert.All(blitz, q =>
        {
            Assert.Equal(QuestionStatuses.Published, q.Status);
            Assert.False(string.IsNullOrWhiteSpace(q.Quote), $"{q.Id}: нет цитаты");
            Assert.Matches(@"^Ситуации на борту, (№\d+|раздел «.+»)", q.Source);
        });
        Assert.Equal(BlitzAnswers.Keys.Order(), blitz.Select(q => q.Id).Order());
        Assert.All(blitz, q => Assert.Equal(BlitzAnswers[q.Id], Answer(q)));
    }

    /// <summary>Верные варианты в порядке показа или шаги в верном порядке.</summary>
    private static IEnumerable<string> Answer(QuestionRecord question) => question.ReadOptions() switch
    {
        ChoiceOptions choice => choice.Options.Where(o => o.Correct).Select(o => o.Text),
        SequenceOptions sequence => sequence.Steps.Select(s => s.Text),
        var other => throw new InvalidOperationException($"{question.Id}: не Вопрос Блица ({other.GetType().Name})"),
    };

    [Fact]
    public async Task Repeated_start_creates_no_duplicates()
    {
        await Seed();
        await Seed();

        await using var db = new AppDbContext(_options);
        Assert.Equal(
            (2, 4, 3, 1, 21),
            (await db.Scales.CountAsync(), await db.ServiceClasses.CountAsync(),
             await db.EventDocuments.CountAsync(), await db.TripSettings.CountAsync(), await db.Questions.CountAsync()));
    }

    [Fact]
    public async Task Repeated_start_keeps_methodologist_edits_of_blitz_question()
    {
        await Seed();
        await using (var db = new AppDbContext(_options))
        {
            var question = await db.Questions.SingleAsync(q => q.Id == "bz-unattended-item");
            question.Statement = "Правка Методиста";
            question.TimeLimitSec = 30;
            await db.SaveChangesAsync();
        }

        await Seed();

        await using (var db = new AppDbContext(_options))
        {
            var question = await db.Questions.SingleAsync(q => q.Id == "bz-unattended-item");
            Assert.Equal(("Правка Методиста", 30), (question.Statement, question.TimeLimitSec));
        }
    }

    [Theory]
    [InlineData("""{"steps":[{"id":"a","text":"Признать ситуацию"},{"id":"b","text":"Заверить"}]}""", "options.steps")]
    [InlineData("""{"steps":42}""", "options.steps")]
    [InlineData("""{"steps":[{"id":"a","text":"Признать ситуацию"},{"id":"a","text":""},{"id":"c","text":"Заверить"}]}""", "options.steps[1].text")]
    public async Task Invalid_blitz_seed_stops_loading_with_question_id_and_field(string options, string field)
    {
        var valid = SeedContent.Questions.Single(q => q.Id == "bz-role-model");
        var broken = valid with { Id = "bz-broken", Options = JsonDocument.Parse(options).RootElement };

        await using var db = new AppDbContext(_options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ContentSeeder(db, [valid, broken]).SeedAsync(CancellationToken.None));

        Assert.Contains("'bz-broken'", error.Message);
        Assert.Contains(field, error.Message);
        Assert.Empty(await db.Questions.ToListAsync());
    }
}
