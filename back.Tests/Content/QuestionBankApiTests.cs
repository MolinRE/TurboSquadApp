using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Content;

public class QuestionBankApiTests(TripApiFactory factory) : IClassFixture<TripApiFactory>
{
    [Fact]
    public async Task Methodologist_can_save_incomplete_draft_then_publish_and_filter_it()
    {
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await client.PostAsJsonAsync("/api/cms/questions", new { type = "single" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<JsonNode>())!;
        var id = (string)draft["id"]!;
        Assert.Equal("draft", (string)draft["status"]!);

        var rejected = await client.PostAsync($"/api/cms/questions/{id}/publish", null);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var errors = (await rejected.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray();
        Assert.Contains(errors, error => (string)error!["path"]! == "statement");
        Assert.Contains(errors, error => (string)error!["path"]! == "options.options");

        var saved = await client.PutAsJsonAsync($"/api/cms/questions/{id}", SingleQuestion());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var published = await client.PostAsync($"/api/cms/questions/{id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal("published", (string)(await published.Content.ReadFromJsonAsync<JsonNode>())!["status"]!);

        var list = (await client.GetFromJsonAsync<JsonNode>("/api/cms/questions?topic=Посадка&category=Штатная&serviceClass=first&type=single&status=published"))!.AsArray();
        Assert.Contains(list, question => (string)question!["id"]! == id);
        var excluded = (await client.GetFromJsonAsync<JsonNode>("/api/cms/questions?category=Нештатная"))!.AsArray();
        Assert.DoesNotContain(excluded, question => (string)question!["id"]! == id);
    }

    [Fact]
    public async Task Invalid_edit_does_not_change_published_question_and_unpublish_preserves_history()
    {
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var before = await factory.Database(db => db.Questions.SingleAsync(q => q.Id == "sw-pet-carrier"));
        var edit = new
        {
            type = "swipe", statement = "Невалидная правка", options = new { },
            explanationText = "Пояснение", explanationKeyFact = "факт", source = "Источник",
            topic = "Посадка", categories = new[] { "Штатная" }, serviceClasses = Array.Empty<string>(),
            baseFrequency = 1, timeLimitSec = 10,
        };
        var rejected = await client.PutAsJsonAsync("/api/cms/questions/sw-pet-carrier", edit);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var after = await factory.Database(db => db.Questions.SingleAsync(q => q.Id == "sw-pet-carrier"));
        Assert.Equal(before.Statement, after.Statement);
        Assert.Equal(before.Options, after.Options);

        await factory.Database(async db =>
        {
            db.SwipeShifts.Add(new SwipeShiftRecord
            {
                Id = Guid.NewGuid(), Deck = "[\"sw-pet-carrier\"]", Scales = "[]",
                Mode = "calm", Status = "passed", StartedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return true;
        });

        var unpublished = await client.PostAsync("/api/cms/questions/sw-pet-carrier/unpublish", null);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        Assert.Equal("draft", await factory.Database(db => db.Questions.Where(q => q.Id == "sw-pet-carrier").Select(q => q.Status).SingleAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/cms/questions/sw-pet-carrier")).StatusCode);
    }

    [Fact]
    public async Task Only_methodologist_can_access_question_bank()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/cms/questions")).StatusCode);
        using var conductor = await factory.CreateConductorClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.GetAsync("/api/cms/questions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.PostAsJsonAsync("/api/cms/questions", new { type = "single" })).StatusCode);
    }

    [Fact]
    public async Task All_four_types_can_be_published_and_published_question_can_be_edited_in_place()
    {
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var samples = new (string Type, string Options)[]
        {
            ("single", "{\"options\":[{\"id\":\"a\",\"text\":\"Да\",\"correct\":true},{\"id\":\"b\",\"text\":\"Нет\",\"correct\":false}]}"),
            ("multiple", "{\"options\":[{\"id\":\"a\",\"text\":\"Первый\",\"correct\":true},{\"id\":\"b\",\"text\":\"Второй\",\"correct\":true},{\"id\":\"c\",\"text\":\"Третий\",\"correct\":false}]}"),
            ("sequence", "{\"steps\":[{\"id\":\"a\",\"text\":\"Первый\"},{\"id\":\"b\",\"text\":\"Второй\"},{\"id\":\"c\",\"text\":\"Третий\"}]}"),
            ("swipe", "{\"right\":{\"label\":\"Да\",\"scaleDeltas\":{}},\"left\":{\"label\":\"Нет\",\"scaleDeltas\":{}},\"correct\":\"right\"}"),
        };
        foreach (var (type, options) in samples)
        {
            var created = await client.PostAsJsonAsync("/api/cms/questions", new { type });
            var id = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;
            var input = QuestionInput(type, JsonNode.Parse(options)!);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/cms/questions/{id}", input)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/cms/questions/{id}/publish", null)).StatusCode);
            var edited = QuestionInput(type, JsonNode.Parse(options)!, "Исправленная формулировка");
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/cms/questions/{id}", edited)).StatusCode);
            var stored = (await client.GetFromJsonAsync<JsonNode>($"/api/cms/questions/{id}"))!;
            Assert.Equal(("published", "Исправленная формулировка"), ((string)stored["status"]!, (string)stored["statement"]!));
        }
    }

    [Fact]
    public async Task New_draft_can_be_deleted()
    {
        using var client = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await client.PostAsJsonAsync("/api/cms/questions", new { type = "sequence" });
        var id = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/cms/questions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/cms/questions/{id}")).StatusCode);
    }

    [Fact]
    public async Task Publication_controls_new_swipe_decks_without_breaking_existing_shift()
    {
        await using var isolated = new TripApiFactory();
        using var methodologist = await isolated.CreateUserClient(UserRoles.Methodologist);
        using var conductor = await isolated.CreateConductorClient();
        await isolated.Database(async db =>
        {
            foreach (var question in await db.Questions.Where(q => q.Type == "swipe").ToListAsync())
                question.Status = "draft";
            await db.SaveChangesAsync();
            return true;
        });

        var created = await methodologist.PostAsJsonAsync("/api/cms/questions", new { type = "swipe" });
        var id = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;
        var options = JsonNode.Parse("{\"right\":{\"label\":\"Да\",\"scaleDeltas\":{}},\"left\":{\"label\":\"Нет\",\"scaleDeltas\":{}},\"correct\":\"right\"}")!;
        Assert.Equal(HttpStatusCode.OK, (await methodologist.PutAsJsonAsync($"/api/cms/questions/{id}", QuestionInput("swipe", options))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await methodologist.PostAsync($"/api/cms/questions/{id}/publish", null)).StatusCode);

        var first = await conductor.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var shift = (await first.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(id, (string)shift["card"]!["questionId"]!);

        Assert.Equal(HttpStatusCode.OK, (await methodologist.PostAsync($"/api/cms/questions/{id}/unpublish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await conductor.GetAsync($"/api/swipe-shifts/{(string)shift["shiftId"]!}")).StatusCode);
        var next = await conductor.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" });
        Assert.Equal(HttpStatusCode.Conflict, next.StatusCode);
        Assert.Equal("NoPublishedQuestions", (string)(await next.Content.ReadFromJsonAsync<JsonNode>())!["reason"]!);
    }

    private static object QuestionInput(string type, JsonNode options, string statement = "Что делать?") => new
    {
        type, statement, options, explanationText = "Пояснение", explanationKeyFact = "факт",
        source = "СТО, п. 1", topic = "Посадка", categories = new[] { "Штатная" },
        serviceClasses = Array.Empty<string>(), baseFrequency = 1, timeLimitSec = 10,
    };

    private static object SingleQuestion() => new
    {
        type = "single", statement = "Что делать при посадке?",
        options = new { options = new[] { new { id = "a", text = "Проверить билет", correct = true }, new { id = "b", text = "Не проверять", correct = false } } },
        explanationText = "Проверьте билет", explanationKeyFact = "билет", source = "СТО, п. 1",
        topic = "Посадка", categories = new[] { "Штатная" }, serviceClasses = Array.Empty<string>(),
        baseFrequency = 1, timeLimitSec = 10,
    };
}
