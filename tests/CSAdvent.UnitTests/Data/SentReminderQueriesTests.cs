using CSAdvent.Web.Data;

namespace CSAdvent.UnitTests.Data;

/// <summary>
/// What a reminder run skips because it's already recorded (#157): only the same kind of reminder, for the same run date.
/// Anything else still has to go out, so it mustn't be skipped.
/// </summary>
public sealed class SentReminderQueriesTests
{
    private static readonly DateOnly December3 = new(2026, 12, 3);

    private static readonly SentReminder[] Recorded =
    [
        Reminder(ReminderKind.ContributorDailyShare, December3, slotId: 10),
        Reminder(ReminderKind.ContributorDailyShare, December3, slotId: 11),
        Reminder(ReminderKind.ContributorDailyShare, December3.AddDays(-1), slotId: 12),
        Reminder(ReminderKind.ContributorActionNeeded, December3, slotId: 13),
        Reminder(ReminderKind.AdminDailySummary, December3, slotId: null),
    ];

    [Fact]
    public void SlotsWithTheSameKindOfReminderForTheSameDayAreRecorded() =>
        Assert.Equal([10, 11], Recorded.AsQueryable().RecordedSlots(ReminderKind.ContributorDailyShare, December3).Order());

    [Fact]
    public void AnotherDaysReminderIsntRecordedForToday()
    {
        // Yesterday's daily email to slot 12 doesn't stop today's.
        Assert.Equal([12], Recorded.AsQueryable().RecordedSlots(ReminderKind.ContributorDailyShare, December3.AddDays(-1)));
        Assert.Empty(Recorded.AsQueryable().RecordedSlots(ReminderKind.ContributorDailyShare, December3.AddDays(1)));
    }

    [Fact]
    public void AnotherKindOfReminderIsntRecorded()
    {
        // Slot 13's post link reminder doesn't stop its last call or the daily email.
        Assert.Equal([13], Recorded.AsQueryable().RecordedSlots(ReminderKind.ContributorActionNeeded, December3));
        Assert.Empty(Recorded.AsQueryable().RecordedSlots(ReminderKind.ContributorDayOf, December3));
        Assert.DoesNotContain(13, Recorded.AsQueryable().RecordedSlots(ReminderKind.ContributorDailyShare, December3));
    }

    [Fact]
    public void RecordsWithoutASlotAreLeftOut() =>
        Assert.Empty(Recorded.AsQueryable().RecordedSlots(ReminderKind.AdminDailySummary, December3));

    private static SentReminder Reminder(ReminderKind kind, DateOnly runDate, int? slotId) =>
        new() { Kind = kind, RunDate = runDate, SlotId = slotId, SentAt = DateTimeOffset.UnixEpoch };
}
