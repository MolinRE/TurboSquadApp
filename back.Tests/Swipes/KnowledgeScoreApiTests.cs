using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Swipes;

public class KnowledgeScoreApiTests
{
    [Fact]
    public async Task Correct_repeat_in_the_same_shift_restores_mastery()
    {
        using var factory = new TripApiFactory();
        await factory.Database(async db =>
        {
            // Колода из 4 свайпов: Вопросы Блица сортируются раньше и в Смену всё равно не попадают.
            var questions = await db.Questions.Where(q => q.Type == "swipe").OrderBy(q => q.Id).ToListAsync();
            foreach (var item in questions.Skip(4)) item.Status = "draft";
            await db.SaveChangesAsync();
            return true;
        });
        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");
        var firstQuestion = shift.CardId;
        await shift.Answer(SwipeApiTests.Wrong(shift.Card!));
        Assert.Equal(0, await Points());
        for (var i = 0; i < 3; i++)
        {
            await shift.NextCard();
            await shift.Answer(SwipeApiTests.Correct(shift.Card!));
        }
        Assert.Equal(30, await Points());
        await shift.NextCard();
        Assert.Equal(firstQuestion, shift.CardId);
        Assert.True((bool)shift.Card!["isRepeat"]!);
        await shift.Answer(SwipeApiTests.Correct(shift.Card!));
        Assert.Equal(40, await Points());

        async Task<int> Points() => (int)(await shift.Http.GetFromJsonAsync<JsonNode>("/api/profile"))!["knowledgePoints"]!;
    }

    [Fact]
    public async Task Earned_rank_remains_after_knowledge_points_fall()
    {
        using var factory = new TripApiFactory();
        using var http = await factory.CreateConductorClient();
        var question = await factory.Database(async db =>
        {
            var questions = await db.Questions.ToListAsync();
            foreach (var item in questions.Skip(1)) item.Status = "draft";
            questions[0].KnowledgeCost = 50;
            await db.SaveChangesAsync();
            return questions[0];
        });
        var correct = SwipeApiTests.Options(SwipeApiTests.Seed(question.Id)).Correct.ToString().ToLowerInvariant();
        var wrong = correct == "right" ? "left" : "right";

        await Answer(correct);
        Assert.Equal((50, "Проводник"), await Profile());
        await Answer(wrong);
        Assert.Equal((0, "Проводник"), await Profile());

        async Task Answer(string side)
        {
            var shift = (await (await http.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" }))
                .Content.ReadFromJsonAsync<JsonNode>())!;
            var response = await http.PostAsJsonAsync($"/api/swipe-shifts/{(string)shift["shiftId"]!}/answer",
                new { questionId = question.Id, answer = side });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        async Task<(int, string)> Profile()
        {
            var profile = (await http.GetFromJsonAsync<JsonNode>("/api/profile"))!;
            return ((int)profile["knowledgePoints"]!, (string)profile["rank"]!);
        }
    }

    [Fact]
    public async Task Published_question_keeps_its_cost_and_rejects_zero_cost()
    {
        using var factory = new TripApiFactory();
        using var http = await factory.CreateUserClient(UserRoles.Methodologist);
        var created = await http.PostAsJsonAsync("/api/cms/questions", new { type = "single" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (string)(await created.Content.ReadFromJsonAsync<JsonNode>())!["id"]!;

        object Input(int cost) => new
        {
            type = "single", statement = "Что проверить?",
            options = new { options = new[] { new { id = "a", text = "Билет", correct = true }, new { id = "b", text = "Ничего", correct = false } } },
            explanationText = "Проверьте билет", explanationKeyFact = "билет", source = "СТО",
            topic = "Посадка", categories = Array.Empty<string>(), serviceClasses = Array.Empty<string>(),
            baseFrequency = 1, timeLimitSec = 10, knowledgeCost = cost,
        };

        var invalidDraft = await http.PutAsJsonAsync($"/api/cms/questions/{id}", Input(0));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDraft.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync($"/api/cms/questions/{id}", Input(17))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync($"/api/cms/questions/{id}/publish", null)).StatusCode);
        var saved = await http.GetFromJsonAsync<JsonNode>($"/api/cms/questions/{id}");
        Assert.Equal(17, (int)saved!["knowledgeCost"]!);

        var invalid = await http.PutAsJsonAsync($"/api/cms/questions/{id}", Input(0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray();
        Assert.Contains(errors, error => (string)error!["path"]! == "knowledgeCost");
        Assert.Equal(17, (int)(await http.GetFromJsonAsync<JsonNode>($"/api/cms/questions/{id}"))!["knowledgeCost"]!);
    }

    [Fact]
    public async Task Last_answer_to_a_question_determines_knowledge_points_across_shifts()
    {
        using var factory = new TripApiFactory();
        var http = await factory.CreateConductorClient();
        var question = await factory.Database(async db =>
        {
            var questions = await db.Questions.ToListAsync();
            foreach (var item in questions.Skip(1)) item.Status = "draft";
            await db.SaveChangesAsync();
            return questions[0];
        });
        var correct = SwipeApiTests.Options(SwipeApiTests.Seed(question.Id)).Correct.ToString().ToLowerInvariant();
        var wrong = correct == "right" ? "left" : "right";

        Assert.Equal((0, "Стажёр"), await Profile());
        await Answer(correct);
        Assert.Equal((10, "Стажёр"), await Profile());
        await Answer(wrong);
        Assert.Equal((0, "Стажёр"), await Profile());
        await Answer(correct);
        Assert.Equal((10, "Стажёр"), await Profile());
        await Answer(correct);
        Assert.Equal((10, "Стажёр"), await Profile());

        async Task<(int Points, string Rank)> Profile()
        {
            var response = await http.GetAsync("/api/profile");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.Equal((int)json["knowledgePoints"]!, (int)json["competencePoints"]!);
            return ((int)json["knowledgePoints"]!, (string)json["rank"]!);
        }

        async Task Answer(string side)
        {
            var started = await http.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" });
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            var shift = (await started.Content.ReadFromJsonAsync<JsonNode>())!;
            var id = (string)shift["shiftId"]!;
            var answered = await http.PostAsJsonAsync($"/api/swipe-shifts/{id}/answer", new { questionId = question.Id, answer = side });
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
            var repeated = await http.PostAsJsonAsync($"/api/swipe-shifts/{id}/answer", new { questionId = question.Id, answer = side });
            Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        }
    }
}
