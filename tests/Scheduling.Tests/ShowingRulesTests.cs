using HomeShowing.Listings.Contracts;

namespace Scheduling.Tests;

/// <summary>Slot lengths, buffers and window edges must sit on the 5 minute claim grid (see SlotClaim.Buckets).</summary>
public class ShowingRulesTests
{
    private static ShowingRules Rules(int slot, int buffer, string start = "09:00", string end = "12:00") => new()
    {
        SlotMinutes = slot, BufferMinutes = buffer, Weekly = [new WeeklyWindow { DayOfWeek = "Saturday", Start = start, End = end }],
    };

    [Theory]
    [InlineData(20, 10)]
    [InlineData(30, 0)]
    [InlineData(45, 15)]
    [InlineData(60, 120)]
    public void Multiples_of_five_are_accepted(int slot, int buffer) => Assert.Empty(Rules(slot, buffer).Validate());

    [Fact]
    public void Slot_length_off_the_five_minute_grid_is_rejected()
    {
        var problems = Rules(33, 0).Validate().ToList();
        Assert.Single(problems);
        Assert.Contains("slotMinutes must be a multiple of 5", problems[0]);
    }

    [Fact]
    public void Buffer_off_the_five_minute_grid_is_rejected()
    {
        Assert.Contains(Rules(30, 7).Validate(), p => p.Contains("bufferMinutes must be a multiple of 5"));
    }

    [Fact]
    public void Range_errors_take_precedence_over_grid_errors()
    {
        Assert.Contains(Rules(7, 0).Validate(), p => p.Contains("between 10 and 240"));
        Assert.DoesNotContain(Rules(7, 0).Validate(), p => p.Contains("multiple"));
    }

    [Theory]
    [InlineData("09:03", "12:00")]
    [InlineData("09:00", "11:59")]
    public void Window_edges_off_the_grid_are_rejected(string start, string end)
    {
        Assert.Contains(Rules(30, 0, start, end).Validate(), p => p.Contains("5 minute boundary"));
    }

    [Theory]
    [InlineData("09:30", "24:00")]
    [InlineData("17:35", "19:55")]
    public void Window_edges_on_the_grid_are_accepted(string start, string end) => Assert.Empty(Rules(30, 0, start, end).Validate());
}
