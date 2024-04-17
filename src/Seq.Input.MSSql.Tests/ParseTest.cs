using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace Seq.Input.MSSql.Tests
{
    public class ParseTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ParseTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [InlineData("Severe", "Highest", true)]
        [InlineData("Critical", "High", false)]
        [InlineData("High", "High", true)]
        [InlineData("Medium", "Medium", true)]
        [InlineData("Low", "Low", true)]
        [InlineData("Not Yet Classified", "Low", true)]
        [InlineData("Quack", "", false)]
        public void IsPriorityMapping(string priority, string expected, bool willParse)
        {
            Log.Logger = new LoggerConfiguration().CreateLogger();
            SqlConfig.ParseKeyPairList(//"Severe=Highest,High=High,Medium=Medium,Low=Low,Not Yet Classified=Low",
                                       "Severe=Highest,High=High,Medium=Medium,Low=Low,Not Yet Classified=Low",
                out var keyMappings);
            Assert.True(TryGetPropertyValueCI(keyMappings, priority, out var priorityValue) == willParse);
            _testOutputHelper.WriteLine("Priority value passed: {0}, Mapped: {1}", priority, priorityValue);
            if (willParse)
            {
                Assert.True(priorityValue == expected);
                Assert.True(Log.Logger.BindProperty("Priority", priorityValue, true, out var testPriority));
                _testOutputHelper.WriteLine("Resulting Property: {0} - {1}", testPriority.Name, testPriority.Value);
            }
            else
                Assert.True(string.IsNullOrEmpty(priorityValue));
        }

        private static bool TryGetPropertyValueCI(Dictionary<string, string> properties, string propertyName,
            out string propertyValue)
        {
            var pair = properties.FirstOrDefault(p => p.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (pair.Key == null)
            {
                propertyValue = null;
                return false;
            }

            propertyValue = pair.Value;
            return true;
        }
    }
}
