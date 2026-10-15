using PictureIt.Core.Operations;

namespace PictureIt.Core.Tests.Operations;

public class ExifDateTimeTests
{
    [Theory]
    [InlineData("2024:06:15 14:32:10")]
    [InlineData("2024:06:15 14:32")]
    [InlineData("2024:06:15")]
    [InlineData("2024:06:15 14:32:10+01:00")]
    [InlineData("2024:06:15 14:32:10.123-05:00")]
    [InlineData("2024:06:15 14:32:10Z")]
    public void Round_trips_every_shape(string text)
    {
        ExifDateTime.TryParse(text, out var value).ShouldBeTrue();
        value.ToString().ShouldBe(text);
    }

    [Fact]
    public void Shifts_across_midnight_and_keeps_its_shape()
    {
        ExifDateTime.TryParse("2024:12:31 23:30:00.5+01:00", out var value).ShouldBeTrue();

        value.Shift(TimeSpan.FromHours(1)).ToString().ShouldBe("2025:01:01 00:30:00.5+01:00");
    }

    [Fact]
    public void Setting_a_zone_does_not_move_the_clock()
    {
        ExifDateTime.TryParse("2024:06:15 14:32:10", out var value).ShouldBeTrue();

        value.WithZone("+10:30").ToString().ShouldBe("2024:06:15 14:32:10+10:30");
    }

    [Fact]
    public void A_date_without_a_time_gets_no_zone()
    {
        ExifDateTime.TryParse("2024:06:15", out var value).ShouldBeTrue();

        value.WithZone("+01:00").ToString().ShouldBe("2024:06:15");
    }

    [Theory]
    [InlineData("+01:00", true)]
    [InlineData("-05:30", true)]
    [InlineData("Z", true)]
    [InlineData("+14:00", true)]
    [InlineData("+15:00", false)]
    [InlineData("01:00", false)]
    [InlineData("+1", false)]
    public void Validates_zones(string zone, bool valid)
    {
        ExifDateTime.IsValidZone(zone).ShouldBe(valid);
    }
}
