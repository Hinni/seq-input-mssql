using System;
using Xunit;

namespace Seq.Input.MSSql.Tests
{
    public class TimePeriodHelperTest
    {
        [Fact]
        public void IsValidTimePeriod_IsValid_InRange()
        {
            var dateTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 9, 5, 0);
            const string timePeriod = "08:00-22:00";

            Assert.True(TimePeriodHelper.IsValidTimePeriod(dateTime, timePeriod));
        }

        [Fact]
        public void IsValidTimePeriod_IsValid_OnPoint()
        {
            var dateTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 22, 0, 0);
            const string timePeriod = "10:00-22:00";

            Assert.True(TimePeriodHelper.IsValidTimePeriod(dateTime, timePeriod));
        }

        [Fact]
        public void IsValidTimePeriod_IsInValid()
        {
            var dateTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 23, 51, 0);
            const string timePeriod = "00:00-23:50";

            Assert.False(TimePeriodHelper.IsValidTimePeriod(dateTime, timePeriod));
        }

        [Theory]
        [InlineData("00:00-23:59", true)]
        [InlineData("10:00-11:20", true)]
        [InlineData("20:00-12:50", true)]
        [InlineData("23:00-23:00", true)]
        [InlineData("00:00-00:60", false)]
        [InlineData("23:0O-23:E0", false)]
        public void IsStringValid_Test(string timePeriod, bool expectedResult)
        {
            Assert.True(TimePeriodHelper.IsStringValid(timePeriod) == expectedResult);
        }
    }
}
