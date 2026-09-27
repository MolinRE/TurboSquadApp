using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Data;
using TurboSquadApp.Questions;
using TurboSquadApp.Tests.Swipes;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Achievements;

public class AchievementApiTests
{
    [Fact]
    public async Task Clean_completed_shift_awards_three_achievements_only_once()
    {
        using var factory = new TripApiFactory();
        var http = await factory.CreateConductorClient();
        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");
        var before = await Awards();
        Assert.Equal(10, before.Count);
        Assert.All(before, item => Assert.Null(item!["earnedAt"]));

        await shift.Play((card, _) => SwipeApiTests.Correct(card));
        Assert.Equal("passed", shift.Status);
        var earned = await Awards();
        Assert.Equal(new[] { "clean-shift", "first-shift", "knowledge-10" },
            earned.Where(item => item!["earnedAt"] is not null).Select(item => (string)item!["id"]!).OrderBy(id => id));
        Assert.Equal(10, earned.Select(item => (string)item!["id"]!).Distinct().Count());
        Assert.Equal((string)earned.Single(item => (string)item!["id"]! == "first-shift")!["earnedAt"]!,
            (string)(await Awards()).Single(item => (string)item!["id"]! == "first-shift")!["earnedAt"]!);

        async Task<JsonArray> Awards() => (await shift.Http.GetFromJsonAsync<JsonArray>("/api/achievements"))!;
    }

    [Fact]
    public async Task Arrivals_award_trip_rules_and_two_formats_but_a_failed_trip_does_not()
    {
        using var factory = new TripApiFactory();
        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");
        await shift.Play((card, _) => SwipeApiTests.Correct(card));

        var failed = await TripApiTests.TripClient.Start(factory, "standard", shift.Http);
        await failed.Choose("a", "a");
        await failed.Proactive("obhod");
        await failed.Choose("c", "a");
        await failed.Choose("b", "b");
        Assert.Equal("failed", failed.Status);
        Assert.Null((await Award("first-arrival"))["earnedAt"]);
        Assert.Null((await Award("flawless-trip"))["earnedAt"]);

        var first = await TripApiTests.TripClient.Start(factory, "business", shift.Http);
        await Arrive(first);
        Assert.NotNull((await Award("first-arrival"))["earnedAt"]);
        Assert.NotNull((await Award("flawless-trip"))["earnedAt"]);
        Assert.NotNull((await Award("two-formats"))["earnedAt"]);
        Assert.Null((await Award("first-class"))["earnedAt"]);
        Assert.Null((await Award("three-arrivals"))["earnedAt"]);

        var second = await TripApiTests.TripClient.Start(factory, "first", shift.Http);
        await Arrive(second);
        Assert.NotNull((await Award("first-class"))["earnedAt"]);
        var third = await TripApiTests.TripClient.Start(factory, "business", shift.Http);
        await Arrive(third);
        Assert.NotNull((await Award("three-arrivals"))["earnedAt"]);

        async Task<JsonNode> Award(string id) => (await shift.Http.GetFromJsonAsync<JsonArray>("/api/achievements"))!
            .Single(item => (string)item!["id"]! == id)!;
        static async Task Arrive(TripApiTests.TripClient trip)
        {
            await trip.Choose("a", "a");
            await trip.Proactive("obhod");
            await trip.Choose("a", "a");
            await trip.Choose("a", "a", "a", "a");
            Assert.Equal("arrived", trip.Status);
        }
    }

    [Fact]
    public async Task Three_passed_shifts_and_twenty_distinct_correct_questions_unlock_thresholds()
    {
        using var factory = new TripApiFactory();
        var http = await factory.CreateConductorClient();
        var correctSides = await factory.Database(async db =>
        {
            var swipeQuestions = await db.Questions.Where(question => question.Type == "swipe")
                .OrderBy(question => question.Id).ToListAsync();
            var sides = swipeQuestions.ToDictionary(question => question.Id,
                question => ((SwipeOptions)question.ReadOptions()).Correct.ToString().ToLowerInvariant());
            for (var index = 0; index < 5; index++)
            {
                var source = swipeQuestions[index];
                var copy = new QuestionRecord
                {
                    Id = $"achievement-extra-{index}", Type = source.Type, Status = "published",
                    Statement = source.Statement, Options = source.Options,
                    ExplanationText = source.ExplanationText, ExplanationKeyFact = source.ExplanationKeyFact,
                    Source = source.Source, Topic = source.Topic, Categories = source.Categories,
                    ServiceClasses = source.ServiceClasses, BaseFrequency = source.BaseFrequency,
                    TimeLimitSec = source.TimeLimitSec, KnowledgeCost = source.KnowledgeCost,
                };
                db.Questions.Add(copy);
                swipeQuestions.Add(copy);
                sides[copy.Id] = sides[source.Id];
            }
            foreach (var question in swipeQuestions.OrderBy(question => question.Id).Skip(10))
                question.Status = "draft";
            await db.SaveChangesAsync();
            return sides;
        });

        var first = await StartShift(http);
        await first.Play((card, _) => correctSides[(string)card["questionId"]!]);
        Assert.Equal("passed", first.Status);
        Assert.Null((await Award("knowledge-20"))["earnedAt"]);
        Assert.Null((await Award("three-shifts"))["earnedAt"]);

        await factory.Database(async db =>
        {
            var questions = await db.Questions.Where(question => question.Type == "swipe")
                .OrderBy(question => question.Id).ToListAsync();
            foreach (var question in questions.Take(10)) question.Status = "draft";
            foreach (var question in questions.Skip(10)) question.Status = "published";
            await db.SaveChangesAsync();
            return true;
        });
        var second = await StartShift(http);
        await second.Play((card, _) => correctSides[(string)card["questionId"]!]);
        Assert.Equal("passed", second.Status);
        var distinctAnswers = await factory.Database(db => db.SwipeAnswers.Where(answer => answer.ShiftId == first.Id || answer.ShiftId == second.Id)
            .Where(answer => answer.Verdict == "correct").Select(answer => answer.QuestionId).Distinct().CountAsync());
        Assert.Equal(20, distinctAnswers);
        Assert.NotNull((await Award("knowledge-20"))["earnedAt"]);
        Assert.Null((await Award("three-shifts"))["earnedAt"]);

        var third = await StartShift(http);
        await third.Play((card, _) => correctSides[(string)card["questionId"]!]);
        Assert.Equal("passed", third.Status);
        Assert.NotNull((await Award("three-shifts"))["earnedAt"]);

        async Task<SwipeApiTests.ShiftClient> StartShift(HttpClient client)
        {
            var response = await client.PostAsJsonAsync("/api/swipe-shifts", new { mode = "calm" });
            response.EnsureSuccessStatusCode();
            return new SwipeApiTests.ShiftClient(client, (await response.Content.ReadFromJsonAsync<JsonNode>())!);
        }
        async Task<JsonNode> Award(string id) => (await http.GetFromJsonAsync<JsonArray>("/api/achievements"))!
            .Single(item => (string)item!["id"]! == id)!;
    }
}
