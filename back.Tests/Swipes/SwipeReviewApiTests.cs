using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TurboSquadApp.Tests.Trips;

namespace TurboSquadApp.Tests.Swipes;

public class SwipeReviewApiTests
{
    [Fact]
    public async Task Failed_shift_has_a_debrief_with_every_answer_before_failure()
    {
        using var factory = new TripApiFactory();
        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");
        await shift.Play((card, _) => SwipeApiTests.Wrong(card));
        Assert.Equal("failed", shift.Status);

        var review = (await shift.Http.GetFromJsonAsync<JsonNode>($"/api/swipe-shifts/{shift.Id}/debrief"))!;
        Assert.Equal("failed", (string)review["result"]!);
        Assert.Equal((string)shift.Json["result"]!["failedScale"]!, (string)review["failedScale"]!);
        Assert.Equal(shift.Shown.Count, review["answers"]!.AsArray().Count);
        Assert.All(review["answers"]!.AsArray(), answer => Assert.Equal("wrong", (string)answer!["verdict"]!));
    }

    [Fact]
    public async Task Finished_shift_keeps_every_answer_and_question_text_after_an_edit()
    {
        using var factory = new TripApiFactory();
        await factory.Database(async db =>
        {
            var questions = await db.Questions.Where(q => q.Type == "swipe").OrderBy(q => q.Id).ToListAsync();
            foreach (var item in questions.Skip(4)) item.Status = "draft";
            await db.SaveChangesAsync();
            return true;
        });
        var shift = await SwipeApiTests.ShiftClient.Start(factory, "calm");
        var firstId = shift.CardId;
        var firstStatement = (string)shift.Card!["statement"]!;
        Assert.Empty((await shift.Http.GetFromJsonAsync<JsonArray>("/api/swipe-shifts/debriefs"))!);

        await shift.Answer(SwipeApiTests.Wrong(shift.Card!));
        for (var i = 0; i < 3; i++)
        {
            await shift.NextCard();
            await shift.Answer(SwipeApiTests.Correct(shift.Card!));
        }
        await shift.NextCard();
        await shift.Answer(SwipeApiTests.Correct(shift.Card!));
        Assert.Equal("passed", shift.Status);

        await factory.Database(async db =>
        {
            var question = await db.Questions.SingleAsync(q => q.Id == firstId);
            question.Statement = "Новая формулировка";
            question.ExplanationText = "Новое пояснение";
            await db.SaveChangesAsync();
            return true;
        });

        var history = (await shift.Http.GetFromJsonAsync<JsonArray>("/api/swipe-shifts/debriefs"))!;
        Assert.Equal(shift.Id.ToString(), (string)Assert.Single(history)!["id"]!);
        var review = (await shift.Http.GetFromJsonAsync<JsonNode>($"/api/swipe-shifts/{shift.Id}/debrief"))!;
        var answers = review["answers"]!.AsArray();
        Assert.Equal(5, answers.Count);
        Assert.Equal(firstStatement, (string)answers[0]!["statement"]!);
        Assert.Equal(firstStatement, (string)answers[4]!["statement"]!);
        Assert.Equal("wrong", (string)answers[0]!["verdict"]!);
        Assert.True((bool)answers[4]!["isRepeat"]!);
        Assert.Equal(10, (int)answers[4]!["knowledgeDelta"]!);
        Assert.NotEqual("Новое пояснение", (string)answers[0]!["explanation"]!["text"]!);

        using var anotherConductor = await factory.CreateConductorClient();
        Assert.Empty((await anotherConductor.GetFromJsonAsync<JsonArray>("/api/swipe-shifts/debriefs"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anotherConductor.GetAsync($"/api/swipe-shifts/{shift.Id}/debrief")).StatusCode);
    }
}
