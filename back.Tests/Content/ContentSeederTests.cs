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
    public async Task Empty_database_gets_directories_events_version_1_and_trip_settings()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        Assert.Equal(["loyalty", "safety"], await db.Scales.OrderBy(s => s.Code).Select(s => s.Code).ToListAsync());
        Assert.Equal(
            ["standard", "comfort", "business", "first"],
            await db.ServiceClasses.OrderBy(c => c.SortOrder).Select(c => c.Code).ToListAsync());
        Assert.Equal(
            [("sit-06", 2), ("sit-33", 2), ("zastup", 1)],
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

    [Fact]
    public async Task Empty_database_gets_published_blitz_questions_of_three_types()
    {
        await Seed();

        await using var db = new AppDbContext(_options);
        var blitz = await db.Questions
            .Where(q => q.Type == QuestionTypes.Single || q.Type == QuestionTypes.Multiple || q.Type == QuestionTypes.Sequence)
            .ToListAsync();
        Assert.All(new[] { QuestionTypes.Single, QuestionTypes.Multiple, QuestionTypes.Sequence },
            type => Assert.True(blitz.Count(q => q.Type == type) >= 2, $"{type}: меньше двух Вопросов"));
        Assert.All(blitz, q =>
        {
            Assert.Equal(QuestionStatuses.Published, q.Status);
            Assert.True(QuestionValidator.ValidateForPublication(q, SeedContent.Directory).IsValid, q.Id);
            Assert.False(string.IsNullOrWhiteSpace(q.Quote), $"{q.Id}: нет цитаты");
            Assert.StartsWith("Ситуации на борту", q.Source);
        });

        // Ожидания — из «Ситуаций на борту»: ролевая модель и №21.
        var roleModel = Assert.IsType<SequenceOptions>(blitz.Single(q => q.Id == "bz-role-model").ReadOptions());
        Assert.Equal(["Признать ситуацию", "Обозначить правило", "Предложить решение", "Заверить"], roleModel.Steps.Select(s => s.Text));
        var alcohol = Assert.IsType<ChoiceOptions>(blitz.Single(q => q.Id == "bz-alcohol-bistro").ReadOptions());
        Assert.Equal("Только в вагоне-бистро", alcohol.Options.Single(o => o.Correct).Text);
    }

    [Fact]
    public async Task Repeated_start_creates_no_duplicates()
    {
        await Seed();
        await Seed();

        await using var db = new AppDbContext(_options);
        Assert.Equal(
            (2, 4, 3, 1, SeedContent.Questions.Count),
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

    [Fact]
    public async Task Invalid_blitz_seed_stops_loading_with_question_id_and_field()
    {
        var valid = SeedContent.Questions.Single(q => q.Id == "bz-role-model");
        var broken = valid with
        {
            Id = "bz-broken",
            Options = JsonDocument.Parse("""{"steps":[{"id":"a","text":"Признать ситуацию"},{"id":"b","text":"Заверить"}]}""").RootElement,
        };

        await using var db = new AppDbContext(_options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ContentSeeder(db, [valid, broken]).SeedAsync(CancellationToken.None));

        Assert.Contains("'bz-broken'", error.Message);
        Assert.Contains("options.steps", error.Message);
        Assert.Empty(await db.Questions.ToListAsync());
    }
}
