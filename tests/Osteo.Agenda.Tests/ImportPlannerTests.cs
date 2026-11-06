using Osteo.Agenda.Core.Domain;
using Osteo.Agenda.Core.Import;

namespace Osteo.Agenda.Tests;

public class ImportPlannerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc);

    private static CalendarEvent Ev(string id, string title, DateTime startUtc, int minutes = 60, string? color = null, bool allDay = false) =>
        new(id, title, new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(startUtc.AddMinutes(minutes), TimeSpan.Zero), allDay, color);

    private static ImportState Empty() => new(
        new Dictionary<string, ExistingAppointment>(),
        new Dictionary<string, ExistingBlock>(),
        [],
        [],
        new HashSet<string>());

    [Fact]
    public void Past_events_become_completed_and_future_booked()
    {
        var plan = ImportPlanner.Plan(
        [
            Ev("a", "Maria #912345678", Now.AddDays(-30)),
            Ev("b", "Maria #912345678", Now.AddDays(3)),
        ], Empty(), Now);

        Assert.Equal(AppointmentStatus.Completed, plan.Items.Single(i => i.Event.Id == "a").Status);
        Assert.Equal(AppointmentStatus.Booked, plan.Items.Single(i => i.Event.Id == "b").Status);
        Assert.All(plan.Items, i => Assert.Equal(ImportAction.Create, i.Action));
        Assert.Equal(1, plan.NewContacts);
    }

    [Fact]
    public void Green_or_check_means_confirmed_and_orange_means_reschedule()
    {
        var plan = ImportPlanner.Plan(
        [
            Ev("a", "✅ Ana #912345678", Now.AddDays(1)),
            Ev("b", "Rui #913345678", Now.AddDays(2), color: "10"),
            Ev("c", "Inês #914345678", Now.AddDays(3), color: "6"),
        ], Empty(), Now);

        Assert.Equal(AppointmentStatus.Confirmed, plan.Items[0].Status);
        Assert.Equal(AppointmentStatus.Confirmed, plan.Items[1].Status);
        Assert.Equal(AppointmentStatus.RescheduleRequested, plan.Items[2].Status);
    }

    [Fact]
    public void Events_without_phone_or_all_day_are_skipped()
    {
        var plan = ImportPlanner.Plan(
        [
            Ev("a", "Contabilista", Now.AddDays(1)),
            Ev("b", "Aniversário #912345678", Now.Date.AddDays(2), 24 * 60, allDay: true),
            Ev("c", "Rui #91234567", Now.AddDays(1)),
        ], Empty(), Now);

        Assert.Contains(ImportIssue.MissingPhone, plan.Items.Single(i => i.Event.Id == "a").Issues);
        Assert.Contains(ImportIssue.AllDayIgnored, plan.Items.Single(i => i.Event.Id == "b").Issues);
        Assert.Contains(ImportIssue.InvalidPhone, plan.Items.Single(i => i.Event.Id == "c").Issues);
        Assert.All(plan.Items, i => Assert.Equal(ImportAction.Skip, i.Action));
    }

    [Fact]
    public void Future_overlaps_keep_the_first_and_skip_the_second()
    {
        var start = Now.AddDays(1);
        var plan = ImportPlanner.Plan(
        [
            Ev("a", "Ana #912345678", start),
            Ev("b", "Rui #913345678", start.AddMinutes(30)),
        ], Empty(), Now);

        Assert.Equal(ImportAction.Create, plan.Items[0].Action);
        Assert.Equal(ImportAction.Skip, plan.Items[1].Action);
        Assert.Contains(ImportIssue.OverlapsAppointment, plan.Items[1].Issues);
    }

    [Fact]
    public void Past_overlaps_are_kept_as_history()
    {
        var start = Now.AddDays(-10);
        var plan = ImportPlanner.Plan(
        [
            Ev("a", "Ana #912345678", start),
            Ev("b", "Rui #913345678", start.AddMinutes(30)),
        ], Empty(), Now);

        Assert.All(plan.Items, i => Assert.Equal(ImportAction.Create, i.Action));
    }

    [Fact]
    public void Appointments_inside_a_block_are_skipped()
    {
        var day = Now.Date.AddDays(5);
        var plan = ImportPlanner.Plan(
        [
            Ev("blk", "#bloqueio Formação", day.AddHours(9), 4 * 60),
            Ev("a", "Ana #912345678", day.AddHours(10)),
        ], Empty(), Now);

        var block = plan.Items.Single(i => i.Event.Id == "blk");
        Assert.Equal(ImportItemKind.Block, block.Kind);
        Assert.Equal(ImportAction.Create, block.Action);
        Assert.Contains(ImportIssue.OverlapsBlock, plan.Items.Single(i => i.Event.Id == "a").Issues);
    }

    [Fact]
    public void Same_phone_with_different_names_is_a_warning_not_a_skip()
    {
        var plan = ImportPlanner.Plan(
        [
            Ev("a", "Tomás #912345678", Now.AddDays(-20)),
            Ev("b", "Inês #912345678", Now.AddDays(-19)),
        ], Empty(), Now);

        Assert.All(plan.Items, i =>
        {
            Assert.Equal(ImportAction.Create, i.Action);
            Assert.Contains(ImportIssue.SamePhoneDifferentName, i.Issues);
        });
    }

    [Fact]
    public void Re_running_is_unchanged_or_update_never_create()
    {
        var start = Now.AddDays(2);
        var id = Guid.NewGuid();
        var state = Empty() with
        {
            AppointmentsByEventId = new Dictionary<string, ExistingAppointment>
            {
                ["a"] = new(id, start, start.AddHours(1), AppointmentStatus.Booked),
                ["b"] = new(Guid.NewGuid(), start.AddHours(3), start.AddHours(4), AppointmentStatus.Booked),
            },
            KnownPhones = new HashSet<string> { "+351912345678", "+351913345678" },
        };

        var plan = ImportPlanner.Plan(
        [
            Ev("a", "Ana #912345678", start),
            Ev("b", "Rui #913345678", start.AddHours(5)), // moved in the calendar
        ], state, Now);

        Assert.Equal(ImportAction.Unchanged, plan.Items[0].Action);
        Assert.Equal(id, plan.Items[0].ExistingId);
        Assert.Equal(ImportAction.Update, plan.Items[1].Action);
        Assert.Equal(0, plan.NewContacts);
    }

    [Fact]
    public void App_decisions_like_cancel_are_not_overwritten()
    {
        var start = Now.AddDays(2);
        var state = Empty() with
        {
            AppointmentsByEventId = new Dictionary<string, ExistingAppointment>
            {
                ["a"] = new(Guid.NewGuid(), start, start.AddHours(1), AppointmentStatus.Cancelled),
            },
        };

        var plan = ImportPlanner.Plan([Ev("a", "Ana #912345678", start)], state, Now);

        Assert.Equal(ImportAction.Unchanged, plan.Items[0].Action);
        Assert.Equal(AppointmentStatus.Cancelled, plan.Items[0].Status);
    }
}
