using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Questions;

namespace TurboSquadApp.Tests.Content;

public class QuestionModelTests
{
    public static TheoryData<string, IQuestionOptions> ValidOptions => new()
    {
        { QuestionTypes.Single, new ChoiceOptions([new("a", "Да", true), new("b", "Нет", false)]) },
        { QuestionTypes.Multiple, new ChoiceOptions([new("a", "Первый", true), new("b", "Второй", true), new("c", "Третий", false)]) },
        { QuestionTypes.Sequence, new SequenceOptions([new("a", "Первый"), new("b", "Второй"), new("c", "Третий")]) },
    };

    [Theory]
    [MemberData(nameof(ValidOptions))]
    public async Task Published_question_round_trips_typed_options_through_the_existing_column(string type, IQuestionOptions options)
    {
        var database = Guid.NewGuid().ToString();
        var configuration = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(database).Options;
        var question = Published(type);
        question.SetOptions(options);
        Assert.True(QuestionValidator.ValidateForPublication(question).IsValid);

        await using (var db = new AppDbContext(configuration))
        {
            db.Questions.Add(question);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(configuration))
        {
            var stored = await db.Questions.SingleAsync();
            Assert.Equal(question.Options, QuestionOptionsCodec.Write(type, stored.ReadOptions()));
        }
    }

    [Fact]
    public async Task Incomplete_draft_can_be_saved_but_cannot_be_published()
    {
        var draft = new QuestionRecord { Id = "draft", Status = QuestionStatuses.Draft, Type = QuestionTypes.Single };
        var configuration = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new AppDbContext(configuration))
        {
            db.Questions.Add(draft);
            await db.SaveChangesAsync();
        }
        await using (var db = new AppDbContext(configuration))
            Assert.Equal("{}", (await db.Questions.SingleAsync()).Options);
        Assert.Contains(QuestionValidator.ValidateForPublication(draft).Errors, error => error.Path == "statement");
        Assert.Contains(QuestionValidator.ValidateForPublication(draft).Errors, error => error.Path == "options.options");
        Assert.Contains(QuestionValidator.ValidateForPublication(draft).Errors, error => error.Path == "timeLimitSec");
    }

    [Theory]
    [InlineData(QuestionTypes.Single, "{\"options\":[{\"id\":\"a\",\"text\":\"A\",\"correct\":true},{\"id\":\"b\",\"text\":\"B\",\"correct\":true}]}", "options.options")]
    [InlineData(QuestionTypes.Multiple, "{\"options\":[{\"id\":\"a\",\"text\":\"A\",\"correct\":true},{\"id\":\"b\",\"text\":\"B\",\"correct\":false}]}", "options.options")]
    [InlineData(QuestionTypes.Sequence, "{\"steps\":[{\"id\":\"a\",\"text\":\"A\"},{\"id\":\"b\",\"text\":\"B\"}]}", "options.steps")]
    public void Invalid_answer_rules_report_the_list_field(string type, string json, string path)
    {
        var question = Published(type);
        question.Options = json;
        Assert.Contains(QuestionValidator.ValidateForPublication(question).Errors, error => error.Path == path);
    }

    [Theory]
    [InlineData(QuestionTypes.Single, "{\"options\":[{\"id\":\"a\",\"text\":\"A\",\"correct\":true},{\"id\":\"a\",\"text\":\"\",\"correct\":false}]}", "options.options[1].id", "options.options[1].text")]
    [InlineData(QuestionTypes.Sequence, "{\"steps\":[{\"id\":\"a\",\"text\":\"A\"},{\"id\":\"a\",\"text\":\"\"},{\"id\":\"c\",\"text\":\"C\"}]}", "options.steps[1].id", "options.steps[1].text")]
    public void Invalid_items_report_precise_paths(string type, string json, string idPath, string textPath)
    {
        var question = Published(type);
        question.Options = json;
        var errors = QuestionValidator.ValidateForPublication(question).Errors;
        Assert.Contains(errors, error => error.Path == idPath && error.Message.Length > 0);
        Assert.Contains(errors, error => error.Path == textPath && error.Message.Length > 0);
    }

    [Fact]
    public void Publication_reports_all_missing_required_fields()
    {
        var question = new QuestionRecord { Id = "q", Type = QuestionTypes.Single, Status = QuestionStatuses.Draft };
        var paths = QuestionValidator.ValidateForPublication(question).Errors.Select(error => error.Path).ToHashSet();
        Assert.True(new[] { "statement", "topic", "explanationText", "explanationKeyFact", "source", "timeLimitSec", "options.options" }.All(paths.Contains));
    }

    [Fact]
    public void Malformed_options_report_the_json_field()
    {
        var question = Published(QuestionTypes.Sequence);
        question.Options = "{\"steps\": 42}";
        Assert.Contains(QuestionValidator.ValidateForPublication(question).Errors,
            error => error.Path == "options.steps" && error.Message.Length > 0);
    }

    [Fact]
    public void Nonpositive_time_limit_blocks_publication()
    {
        var question = Published(QuestionTypes.Single);
        question.SetOptions(new ChoiceOptions([new("a", "Да", true), new("b", "Нет", false)]));
        question.TimeLimitSec = 0;
        Assert.Contains(QuestionValidator.ValidateForPublication(question).Errors,
            error => error.Path == "timeLimitSec");
    }

    [Fact]
    public void Choice_without_correct_flag_reports_its_field()
    {
        var question = Published(QuestionTypes.Single);
        question.Options = "{\"options\":[{\"id\":\"a\",\"text\":\"Да\",\"correct\":true},{\"id\":\"b\",\"text\":\"Нет\"}]}";
        Assert.Contains(QuestionValidator.ValidateForPublication(question).Errors,
            error => error.Path == "options.options[1].correct");
    }

    [Fact]
    public void Mismatched_typed_options_cannot_be_written()
    {
        var question = Published(QuestionTypes.Sequence);
        Assert.Throws<ArgumentException>(() => question.SetOptions(new ChoiceOptions([])));
    }

    private static QuestionRecord Published(string type) => new()
    {
        Id = "q", Type = type, Status = QuestionStatuses.Published, Statement = "Что делать?",
        Topic = "Посадка", ExplanationText = "Пояснение", ExplanationKeyFact = "факт",
        Source = "Источник, п. 1", TimeLimitSec = 10,
    };
}
