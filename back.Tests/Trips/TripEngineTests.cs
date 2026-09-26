using System.Text.Json;
using TurboSquadApp.Content;
using TurboSquadApp.Events;
using TurboSquadApp.Tests.Events;
using TurboSquadApp.Trips;

namespace TurboSquadApp.Tests.Trips;

// Сценарии — из тикета #6, контент — из сидов. Все Рейсы начинаются с Заступа: Варианты a, a.
public class TripEngineTests
{
    [Fact]
    public void Successful_trip_in_business_arrives_at_full_loyalty()
    {
        var trip = Trip.Start("business").Choose("a", "a")
            .Proactive("obhod")
            .Choose("a", "a")                 // №6
            .Choose("a", "a", "b", "a");      // №33

        Assert.Equal(TripStatus.Arrived, trip.State.Status);
        Assert.Equal((100, 95), trip.Scales);

        var debrief = Debrief.Build(trip.State);
        Assert.Equal(TripStatus.Arrived, debrief.Result);
        Assert.Equal(
            [("zastup", EventResult.Success), ("sit-06", EventResult.Success), ("sit-33", EventResult.Success)],
            debrief.Events.Select(e => (e.EventId, e.Result)));
        Assert.Equal("Пройти по вагонам", Assert.Single(debrief.Items.OfType<DebriefProactiveChoice>()).Choice);
        var reassure = Decision(debrief, "sit-33", "s4");
        Assert.Equal(("a", false), (reassure.VariantId, reassure.TimedOut));
        Assert.Equal(["reassure"], reassure.RoleStages);
        Assert.StartsWith("Заверить", reassure.Comment);
        Assert.Equal("Ролевая модель, этап 4", reassure.Source);
        var loyalty = Assert.Single(reassure.Changes);
        Assert.Equal(("loyalty", "Лояльность пассажира", 10, 0, 100, 100),
            (loyalty.Scale, loyalty.Name, loyalty.Nominal, loyalty.Applied, loyalty.Before, loyalty.After));
    }

    [Fact]
    public void Loyalty_at_zero_fails_trip_before_transition_and_interrupts_event()
    {
        var trip = Trip.Start("standard").Choose("a", "a")
            .Proactive("obhod")
            .Choose("c", "a")                 // №6: неудачный Исход, Рейс идёт дальше
            .Choose("b", "b");                // №33

        Assert.Equal(TripStatus.Failed, trip.State.Status);
        Assert.Equal(new TripFailure(FailureCause.Scale, "loyalty"), trip.State.Failure);
        Assert.Equal((0, 70), trip.Scales);
        Assert.Equal(RejectionReason.TripNotRunning, trip.Rejected(new ChooseVariant("a")).Reason);

        var debrief = Debrief.Build(trip.State);
        Assert.Equal(TripStatus.Failed, debrief.Result);
        Assert.Contains("Лояльность пассажира", debrief.Summary);
        Assert.Equal(
            [("zastup", EventResult.Success), ("sit-06", EventResult.Failure), ("sit-33", EventResult.Interrupted)],
            debrief.Events.Select(e => (e.EventId, e.Result)));
        Assert.Equal("fail_escalated", debrief.Events.Single(e => e.EventId == "sit-06").OutcomeStepId);
        Assert.Equal(["s1", "s2"], debrief.Events.Single(e => e.EventId == "sit-33").Decisions.Select(d => d.StepId));
    }

    [Fact]
    public void Critical_error_fails_trip_while_scales_are_above_zero()
    {
        var trip = Trip.Start("comfort").Choose("a", "a")
            .Proactive("obhod")
            .Choose("b", "b");                // №6: не подходить, потом вывести пассажира самому

        Assert.Equal(TripStatus.Failed, trip.State.Status);
        Assert.Equal(new TripFailure(FailureCause.CriticalError), trip.State.Failure);
        Assert.Equal((55, 25), trip.Scales);

        var debrief = Debrief.Build(trip.State);
        Assert.Contains("Вступить в спор и вывести пассажира из вагона самому", debrief.Summary);
        Assert.Equal(EventResult.Interrupted, debrief.Events.Single(e => e.EventId == "sit-06").Result);
        Assert.True(Decision(debrief, "sit-06", "s3").CriticalError);
    }

    [Fact]
    public void Timeouts_follow_timeout_branch_and_low_loyalty_leads_to_failed_outcome()
    {
        var trip = Trip.Start("standard");
        Assert.Equal(RejectionReason.NoTimer, trip.Rejected(new TimeOut()).Reason);

        trip = trip.Choose("a", "a")
            .Proactive("obhod")
            .TimeOut().TimeOut()              // №6
            .TimeOut().Choose("a", "a", "b"); // №33: на s4 лояльность 35 < 40

        Assert.Equal(TripStatus.Arrived, trip.State.Status);
        Assert.Equal((35, 45), trip.Scales);

        var debrief = Debrief.Build(trip.State);
        Assert.Equal(
            [("sit-06", EventResult.Failure, "fail_escalated"), ("sit-33", EventResult.Failure, "fail_complaint")],
            debrief.Events.Skip(1).Select(e => (e.EventId, e.Result, e.OutcomeStepId)));
        var timedOut = Decision(debrief, "sit-06", "s1");
        Assert.Equal((null, true), (timedOut.VariantId, timedOut.TimedOut));
        Assert.StartsWith("Замер", timedOut.Text);
    }

    [Fact]
    public void Flag_set_in_situation_6_opens_report_step_in_situation_33()
    {
        var trip = Trip.Start("business").Choose("a", "a")
            .Proactive("obhod")
            .Choose("a", "c")                 // №6: не беспокоить начальника поезда
            .Choose("a", "a");                // №33

        Assert.Contains("did_not_report_drunk", trip.State.Flags);
        Assert.Equal(new StepPosition("sit-33", "report_to_manager"), trip.State.Current);

        trip = trip.Choose("a", "b", "a");

        Assert.Equal(TripStatus.Arrived, trip.State.Status);
        Assert.Equal((100, 55), trip.Scales);
        Assert.Equal(["did_not_report_drunk"], Decision(Debrief.Build(trip.State), "sit-06", "s2").FlagsSet);
    }

    [Fact]
    public void Flag_set_after_situation_33_does_not_affect_it()
    {
        var trip = Trip.Start("business").Choose("a", "a")
            .Proactive("tech")
            .Choose("a", "a", "b", "a")       // №33: Флага ещё нет, после s2 — сразу s3
            .Choose("a", "c");                // №6: Флаг ставится, но проверять его уже некому

        Assert.Equal(TripStatus.Arrived, trip.State.Status);
        Assert.Equal((100, 65), trip.Scales);
        Assert.Contains("did_not_report_drunk", trip.State.Flags);
        Assert.Equal(["s1", "s2", "s3", "s4"],
            Debrief.Build(trip.State).Events.Single(e => e.EventId == "sit-33").Decisions.Select(d => d.StepId));
    }

    [Fact]
    public void Upgrade_offer_is_hidden_and_rejected_in_first_class()
    {
        var trip = Trip.Start("first").Choose("a", "a")
            .Proactive("tech")
            .Choose("a", "a");                // №33 → s3

        var upgrade = trip.State.Choices.Single(c => c.Variant.Id == "b");
        Assert.False(upgrade.Available);
        Assert.Contains("standard, comfort, business", Assert.Single(upgrade.HiddenBecause));
        Assert.True(trip.State.Choices.Single(c => c.Variant.Id == "a").Available);
        Assert.Equal(RejectionReason.HiddenVariant, trip.Rejected(new ChooseVariant("b")).Reason);

        trip = trip.Choose("a", "a")          // №33
            .Choose("a", "a");                // №6

        Assert.Equal(TripStatus.Arrived, trip.State.Status);
        Assert.Equal((100, 95), trip.Scales);
    }

    [Fact]
    public void Trip_does_not_start_with_content_that_fails_validator()
    {
        var broken = JsonSerializer.Deserialize<EventDocument>(TestData.BrokenDraftJson(), EventJson.Options)!;
        var content = Trip.SeedTrip() with { Events = Trip.SeedTrip().Events.Select(e => e.Id == broken.Id ? broken : e).ToList() };

        var result = TripEngine.Reduce(TripState.Initial, new StartTrip(content, "business"));

        Assert.Equal(RejectionReason.InvalidContent, result.Rejection?.Reason);
        Assert.Contains("sit-33", result.Rejection!.Message);
        Assert.Same(TripState.Initial, result.State);
    }

    [Fact]
    public void Trip_does_not_start_when_trip_settings_do_not_match_content()
    {
        var seed = Trip.SeedTrip();
        var proactive = seed.Settings.ProactiveChoice;
        var missingPoolEvent = seed.Settings with
        {
            ProactiveChoice = proactive with { Options = [proactive.Options[0] with { Pool = ["sit-06", "sit-99"] }] },
        };

        Assert.Equal(RejectionReason.InvalidContent, StartRejection(seed with { Events = seed.Events.Skip(1).ToList() }));
        Assert.Equal(RejectionReason.InvalidContent, StartRejection(seed with { Events = [.. seed.Events, seed.Events[1]] }));
        Assert.Equal(RejectionReason.InvalidContent, StartRejection(seed with { Settings = missingPoolEvent }));
        Assert.Equal(RejectionReason.UnknownServiceClass, StartRejection(seed, "vip"));
    }

    [Fact]
    public void Event_condition_filters_it_out_of_the_pool()
    {
        var seed = Trip.SeedTrip();
        var notInFirstClass = new Condition { Type = "class", Classes = ["standard", "comfort", "business"] };
        var content = seed with
        {
            Events = seed.Events.Select(e => e.Id == "sit-06" ? e with { Conditions = [notInFirstClass] } : e).ToList(),
        };

        var first = Trip.Start(content, "first").Choose("a", "a").Proactive("obhod");
        var business = Trip.Start(content, "business").Choose("a", "a").Proactive("obhod");

        Assert.Equal(new StepPosition("sit-33", "s1"), first.State.Current);
        Assert.Equal(new StepPosition("sit-06", "s1"), business.State.Current);
    }

    [Fact]
    public void Proactive_choice_is_accepted_only_between_events()
    {
        var atShiftStart = Trip.Start("business");
        Assert.Equal(RejectionReason.NotProactiveChoice, atShiftStart.Rejected(new ChooseProactive("obhod")).Reason);

        var atChoice = atShiftStart.Choose("a", "a");
        Assert.Equal(TripPhase.ProactiveChoice, atChoice.State.Phase);
        Assert.Equal(RejectionReason.UnknownProactiveOption, atChoice.Rejected(new ChooseProactive("sluzhebnoe")).Reason);
        Assert.Equal(RejectionReason.UnknownVariant, atChoice.Rejected(new ChooseVariant("a")).Reason);
        Assert.Equal(RejectionReason.NoTimer, atChoice.Rejected(new TimeOut()).Reason);
    }

    private static RejectionReason? StartRejection(TripContent content, string serviceClass = "business")
    {
        var result = TripEngine.Reduce(TripState.Initial, new StartTrip(content, serviceClass));
        Assert.Same(TripState.Initial, result.State);
        return result.Rejection?.Reason;
    }

    private static DebriefDecision Decision(Debrief debrief, string eventId, string stepId) =>
        debrief.Events.Single(e => e.EventId == eventId).Decisions.Single(d => d.StepId == stepId);

    /// <summary>Проигрывает действия через редьюсер и падает на любом неожиданном отказе.</summary>
    private sealed class Trip(TripState state)
    {
        public TripState State { get; } = state;

        public (int Loyalty, int Safety) Scales => (State.Scales["loyalty"], State.Scales["safety"]);

        public static TripContent SeedTrip() =>
            new(SeedContent.Directory, SeedContent.Events.Select(e => e.Document).ToList(), SeedContent.Trip);

        public static Trip Start(string serviceClass) => Start(SeedTrip(), serviceClass);

        public static Trip Start(TripContent content, string serviceClass) =>
            new Trip(TripState.Initial).Do(new StartTrip(content, serviceClass));

        public Trip Choose(params string[] variantIds) =>
            variantIds.Aggregate(this, (trip, id) => trip.Do(new ChooseVariant(id)));

        public Trip Proactive(string optionId) => Do(new ChooseProactive(optionId));

        public Trip TimeOut() => Do(new TimeOut());

        /// <summary>Действие отклонено, состояние осталось тем же.</summary>
        public Rejection Rejected(TripAction action)
        {
            var result = TripEngine.Reduce(State, action);
            Assert.Same(State, result.State);
            return result.Rejection ?? throw new Xunit.Sdk.XunitException($"{action} принято, а должно быть отклонено");
        }

        private Trip Do(TripAction action)
        {
            var result = TripEngine.Reduce(State, action);
            Assert.True(result.Rejection is null, $"{action} отклонено: {result.Rejection?.Message}");
            return new Trip(result.State);
        }
    }
}
