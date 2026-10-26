using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Jobs;

/// <summary>
/// Goes through a job step's spots one at a time, so one that throws doesn't stop the rest (#147): a post whose page the
/// checker can't read, say, would otherwise stop every blurb check, daily email, last call and backup ask for the two days
/// it's checked.
/// </summary>
public static class EachSpot
{
    /// <summary>
    /// Runs <paramref name="advance"/> for each spot. One that throws is passed to <paramref name="failed"/> (to log) and
    /// skipped, after clearing <paramref name="db"/>'s change tracker so nothing it left half done is saved with the next
    /// spot; the next run tries it again. A missing table or column isn't caught: every spot would fail the same way, so the
    /// worker logs it once, as a warning (#225).
    /// </summary>
    public static async Task RunAsync(DbContext db, IEnumerable<int> slotIds, Func<int, Task> advance, Action<Exception, int> failed, CancellationToken cancellationToken)
    {
        foreach (var slotId in slotIds)
        {
            try
            {
                await advance(slotId);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !MissingSchema.IsMissingTableOrColumn(exception))
            {
                db.ChangeTracker.Clear();
                failed(exception, slotId);
            }
        }
    }
}
