using TurboSquadApp.Content;
using TurboSquadApp.Events;

namespace TurboSquadApp.Tests.Content;

// Ожидания — из тикета #5: контент прототипа движка в формате PRD v7 §5.2.
public class SeedContentTests
{
    private static EventDocument Event(string id) => SeedContent.Events.Single(e => e.Document.Id == id).Document;

    private static Variant Variant(string eventId, string stepId, string variantId) =>
        Event(eventId).Steps.Single(s => s.Id == stepId).Variants!.Single(v => v.Id == variantId);

    private static Step Step(string eventId, string stepId) => Event(eventId).Steps.Single(s => s.Id == stepId);

    [Fact]
    public void Seeds_are_shift_start_and_two_situations_in_version_1()
    {
        Assert.Equal(
            [("zastup", 1), ("sit-06", 1), ("sit-33", 1)],
            SeedContent.Events.Select(e => (e.Document.Id, e.Document.Version)));
    }

    [Fact]
    public void Every_seed_event_passes_validator_with_flags_of_other_seeds()
    {
        foreach (var seed in SeedContent.Events)
        {
            var report = EventValidator.Validate(seed.Document, SeedContent.Directory, SeedContent.FlagsSetElsewhere(seed.Document.Id));

            Assert.True(report.Errors.Count == 0 && report.Warnings.Count == 0,
                $"{seed.Document.Id}: {string.Join("; ", report.Errors.Concat(report.Warnings).Select(i => $"{i.Where} — {i.Message}"))}");
        }
    }

    [Fact]
    public void Stored_json_of_every_seed_event_is_readable_and_valid()
    {
        foreach (var seed in SeedContent.Events)
        {
            var reparsed = EventValidator.ValidateJson(seed.Json, SeedContent.Directory, SeedContent.FlagsSetElsewhere(seed.Document.Id));
            Assert.True(reparsed.IsValid);
            Assert.Contains($"\"id\": \"{seed.Document.Id}\"", seed.Json);
        }
    }

    [Fact]
    public void Outcome_penalties_are_moved_into_variants()
    {
        Assert.Equal(-20, Variant("sit-06", "s2", "c").ScaleDeltas!["safety"]);
        Assert.Equal(-10, Variant("sit-06", "s3", "a").ScaleDeltas!["loyalty"]);
        Assert.Equal(-20, Step("sit-06", "s3").Timeout!.ScaleDeltas!["loyalty"]);
        Assert.Equal(-30, Variant("sit-33", "s2", "b").ScaleDeltas!["loyalty"]);
        Assert.All(SeedContent.Events.SelectMany(e => e.Document.Steps).Where(s => s.IsOutcome),
            outcome => Assert.Null(outcome.ScaleDeltas));
    }

    [Fact]
    public void Flag_from_situation_6_opens_report_step_in_situation_33()
    {
        Assert.Equal(["did_not_report_drunk"], Variant("sit-06", "s2", "c").SetsFlags!);

        var transitions = Variant("sit-33", "s2", "a").Transitions!;
        Assert.Equal("report_to_manager", transitions[0].To);
        var condition = Assert.Single(transitions[0].Conditions!);
        Assert.Equal(("flag", "did_not_report_drunk", true), (condition.Type, condition.Flag, condition.Present));
        Assert.Equal("s3", transitions[1].To);
    }

    [Fact]
    public void Upgrade_offer_is_hidden_in_first_class_by_class_codes()
    {
        var condition = Assert.Single(Variant("sit-33", "s3", "b").Conditions!);

        Assert.Equal("class", condition.Type);
        Assert.Equal(["standard", "comfort", "business"], condition.Classes!);
    }

    [Fact]
    public void Arguing_and_removing_passenger_yourself_is_a_critical_error()
    {
        Assert.True(Variant("sit-06", "s3", "b").CriticalError);
    }

    [Fact]
    public void Trip_is_shift_start_then_proactive_choice_of_two_event_orders()
    {
        var trip = SeedContent.Trip;

        Assert.Equal("zastup", trip.ShiftStartEvent);
        Assert.Equal(2, trip.EventsAfterShiftStart);
        Assert.Equal(
            [("obhod", "Пройти по вагонам", "sit-06,sit-33"), ("tech", "Проверить техническое", "sit-33,sit-06")],
            trip.ProactiveChoice.Options.Select(o => (o.Id, o.Text, string.Join(",", o.Pool))));
    }

    [Fact]
    public void Directory_has_mandatory_scales_and_four_class_codes()
    {
        Assert.Equal(
            [("loyalty", 0, 100, 70, 0, true), ("safety", 0, 100, 70, 0, true)],
            SeedContent.Directory.Scales.Select(s => (s.Code, s.Min, s.Max, s.Start, s.FailureThreshold, s.Mandatory)));
        Assert.Equal(["standard", "comfort", "business", "first"], SeedContent.Directory.Classes.Select(c => c.Code));
        Assert.All(SeedContent.Directory.Classes, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
    }
}
