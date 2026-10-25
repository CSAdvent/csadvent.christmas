using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Manage;

public sealed class SeasonForm
{
    // Below the test season (Season.TestYear).
    [Range(2017, 8999, ErrorMessage = "Enter a year from 2017 to 8999.")]
    public int Year { get; set; }

    [Range(1, 2, ErrorMessage = "Choose one or two posts per day.")]
    public int SlotsPerDay { get; set; } = 2;

    /// <summary>When sign-ups open, in the reveal time zone. Empty means not scheduled yet.</summary>
    public DateTime? ClaimsOpen { get; set; }
}

public sealed class SignUpsForm
{
    /// <summary>When sign-ups open, in the reveal time zone. Empty means not scheduled yet.</summary>
    public DateTime? ClaimsOpen { get; set; }
}

public sealed class ApproveForm
{
    /// <summary>The spot the organizer chose. Zero until they choose one, when the requested spot has gone.</summary>
    public int SlotId { get; set; }
}

/// <summary>Declining has no fields; the model only tells the page that the decline form was posted.</summary>
public sealed class DeclineForm;
