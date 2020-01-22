using System;
using Xunit;

namespace Seq.Input.MSSql.Tests
{
    public class TimePeriodHelperTest
    {
        [Fact]
        public void IsValidTimePeriod_IsValid_InRange()
        {
            var dateTime = new DateTime(2020, 1, 22, 9, 5, 0);
            var timePeriod = "08:00-22:00";

            Assert.True(TimePeriodHelper.IsValidTimePeriod(dateTime, timePeriod));
        }

        [Fact]
        public void IsValidTimePeriod_IsValid_OnPoint()
        {
            var dateTime = new DateTime(2020, 1, 22, 22, 0, 0);
            var timePeriod = "10:00-22:00";

            Assert.True(TimePeriodHelper.IsValidTimePeriod(dateTime, timePeriod));
        }

        [Fact]
        public void IsValidTimePeriod_IsInValid()
        {
            var dateTime = new DateTime(2020, 1, 22, 23, 51, 0);
            var timePeriod = "00:00-23:50";

            Assert.False(TimePeriodHelper.IsValidTimePeriod(dateTime, timePeriod));
        }

        [Fact]
        public void IsStringValid_IsValid_Case1()
        {
            var timePeriod = "00:00-23:59";

            Assert.True(TimePeriodHelper.IsStringValid(timePeriod));
        }

        [Fact]
        public void IsStringValid_IsValid_Case2()
        {
            var timePeriod = "10:00-11:20";

            Assert.True(TimePeriodHelper.IsStringValid(timePeriod));
        }

        [Fact]
        public void IsStringValid_IsValid_Case3()
        {
            var timePeriod = "20:00-12:50";

            Assert.True(TimePeriodHelper.IsStringValid(timePeriod));
        }

        [Fact]
        public void IsStringValid_IsValid_Case4()
        {
            var timePeriod = "23:00-23:00";

            Assert.True(TimePeriodHelper.IsStringValid(timePeriod));
        }

        [Fact]
        public void IsStringValid_IsValid_Case_WrongTime()
        {
            var timePeriod = "00:00-00:60";

            Assert.False(TimePeriodHelper.IsStringValid(timePeriod));
        }

        [Fact]
        public void IsStringValid_IsValid_Case_WrongCharacter()
        {
            var timePeriod = "23:0O-23:E0";

            Assert.False(TimePeriodHelper.IsStringValid(timePeriod));
        }
    }
}