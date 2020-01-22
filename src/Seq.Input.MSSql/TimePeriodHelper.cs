using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Seq.Input.MSSql
{
    public static class TimePeriodHelper
    {
        public static bool IsValidTimePeriod(DateTime dateTime, string timePeriod)
        {
            var today = DateTime.Today;
            var start = timePeriod.Split('-').First().Split(':');
            var end = timePeriod.Split('-').Last().Split(':');

            var dateTimeStart = today.AddHours(double.Parse(start.First())).AddMinutes(double.Parse(start.Last()));
            var dateTimeEnd = today.AddHours(double.Parse(end.First())).AddMinutes(double.Parse(end.Last()));

            return (dateTime >= dateTimeStart && dateTime <= dateTimeEnd);
        }

        public static bool IsStringValid(string timePeriod)
        {
            return Regex.IsMatch(timePeriod, "^[0-2]{1}[0-9]{1}:[0-5]{1}[0-9]{1}-[0-2]{1}[0-9]{1}:[0-5]{1}[0-9]{1}$");
        }
    }
}