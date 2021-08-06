﻿using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Events;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Seq.Input.MSSql
{
    public static class SqlConfig
    {
        public static int QueryEverySeconds { get; set; }
        public static string ServerInstance { get; set; }
        public static string InitialCatalog { get; set; }
        public static bool IntegratedSecurity { get; set; }
        public static string DatabaseUsername { get; set; }
        public static string DatabasePassword { get; set; }
        public static string TableOrViewName { get; set; }
        public static string AdditionalFilterClause { get; set; }
        public static string ColumnNameTimeStamp { get; set; }
        public static string ColumnNameMessage { get; set; }
        public static List<string> ColumnNamesInclude { get; set; }
        public static string ApplicationName { get; set; }
        public static string ApplicationPropertyName { get; set; }
        public static string ColumnNameEventLevel { get; set; }
        public static Dictionary<string, LogEventLevel> EventLevelMapping { get; set; }
        public static int LogEventLevel { get; set; }
        public static string TimePeriod { get; set; }
        public static IEnumerable<string> Tags { get; set; }
        public static Dictionary<string, string> TagMappings { get; set; }
        public static string ColumnNameTags { get; set; }
        public static string ColumnNamePriority { get; set; }
        public static Dictionary<string, string> PriorityMapping { get; set; }
        public static string ColumnNameResponder { get; set; }
        public static Dictionary<string, string> ResponderMapping { get; set; }
        public static string ColumnNameProjectKey { get; set; }
        public static Dictionary<string, string> ProjectKeyMapping { get; set; }
        public static string ColumnNameInitialEstimate { get; set; }
        public static Dictionary<string, string> InitialEstimateMapping { get; set; }
        public static string ColumnNameRemainingEstimate { get; set; }
        public static Dictionary<string, string> RemainingEstimateMapping { get; set; }
        public static string ColumnNameDueDate { get; set; }
        public static Dictionary<string, string> DueDateMapping { get; set; }

        public static bool ParseKeyPairList(string value, out Dictionary<string, string> mappings)
        {
            mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(value) || !value.Contains("=")) return false;
            var pairs = SplitAndTrim(',', value);
            foreach (var pair in pairs)
            {
                var kv = SplitAndTrim('=', pair).ToArray();
                if (kv.Length != 2) return false;
                mappings.Add(kv[0], kv[1]);
            }

            return false;
        }

        public static bool ParseEventKeyPairList(string value, out Dictionary<string, LogEventLevel> mappings)
        {
            mappings = new Dictionary<string, LogEventLevel>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(value) || !value.Contains("=")) return false;
            var pairs = SplitAndTrim(',', value);
            foreach (var pair in pairs)
            {
                var kv = SplitAndTrim('=', pair).ToArray();
                if (kv.Length != 2 || !Enum.TryParse(kv[1], true, out LogEventLevel level)) return false;
                mappings.Add(kv[0], level);
            }

            return false;
        }

        public static bool IsKeyPairList(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Contains("=");
        }

        public static bool IsValue(string value)
        {
            return !string.IsNullOrEmpty(value);
        }

        public static IEnumerable<string> SplitAndTrim(char splitOn, string setting)
        {
            return setting.Split(new[] {splitOn}, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim());
        }
    }
}