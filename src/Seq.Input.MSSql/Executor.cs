using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Seq.Input.MSSql;
using Serilog;
using Serilog.Events;

namespace Seq.Input.MsSql
{
    public class Executor
    {
        private readonly string _connectionString;
        private readonly FileInfo _fileInfo;
        private readonly ILogger _logger;
        private readonly string _query;
        private readonly TextWriter _textWriter;

        public Executor(ILogger logger, TextWriter textWriter, FileInfo fileInfo, string connectionString, string query)
        {
            _logger = logger;
            _textWriter = textWriter;
            _fileInfo = fileInfo;
            _connectionString = connectionString;
            _query = query;
        }

        public async Task Start()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (var command = connection.CreateCommand())
                    {
                        var clauseList = new List<string>();

                        if (!string.IsNullOrEmpty(SqlConfig.AdditionalFilterClause))
                            clauseList.Add(SqlConfig.AdditionalFilterClause);

                        //Set the current run time, less SecondsDelay, to allow for late-running rows or timestamps that don't measure in milliseconds (such as timestamps calculated from MSDB sysjobhistory)
                        var runTime = DateTime.Now.AddSeconds(-SqlConfig.SecondsDelay);
                        var validLastStamp = false;
                        var dateTime = DateTime.Now;

                        //Ensure we update the state of the file to avoid an infinite loop of event ingestion on first run
                        _fileInfo.Refresh();

                        // Get last valid timestamp from file
                        if (_fileInfo.Exists)
                        {
                            var content = File.ReadAllText(_fileInfo.FullName).Trim();
                            if (!string.IsNullOrEmpty(content))
                            {
                                validLastStamp = true;
                                dateTime = DateTime.Parse(content);
                            }
                            else
                            {
                                if (SqlConfig.Debug)
                                    _logger.Debug("lastScan.txt did not contain content");
                            }
                        }
                        else
                        {
                            if (SqlConfig.Debug)
                                _logger.Debug("lastScan.txt not found");
                        }

                        if (!validLastStamp)
                        {
                            //Avoid ingesting every event by limiting the query to last day up to current runTime (-1 sec)
                            dateTime = DateTime.Now.AddDays(-1);
                            if (SqlConfig.Debug)
                                _logger.Debug("Could not determine last scan time - query limited to last day");
                        }

                        //In case SecondsDelay is changed and we now would have an invalid query, adjust dateTime
                        if (dateTime >= runTime)
                            dateTime = runTime.AddSeconds(-SqlConfig.SecondsDelay);

                        //Only retrieve events that occurs after the last dateTime and up to the current runTime (- 1 sec)
                        clauseList.Add($"{SqlConfig.ColumnNameTimeStamp} > '{dateTime:yyyy-MM-dd HH:mm:ss.fff}'");
                        clauseList.Add($"{SqlConfig.ColumnNameTimeStamp} <= '{runTime:yyyy-MM-dd HH:mm:ss.fff}'");

                        var queryString = _query + CreateWhereClause(clauseList);
                        if (SqlConfig.Debug)
                            _logger.ForContext("ConnectionString", _connectionString)
                                .ForContext("QueryString", queryString).Debug(
                                    "Query new table rows from {StartDateTime} to {EndDateTime}", dateTime,
                                    runTime);
                        else
                            _logger.Debug("Query new table rows from {StartDateTime} to {EndDateTime}", dateTime,
                                runTime);

                        // Create command and execute
                        command.CommandText = queryString;
                        command.CommandType = CommandType.Text;
                        var dataReader = await command.ExecuteReaderAsync();

                        // Write current runTime to file
                        using (var sw = _fileInfo.CreateText())
                        {
                            await sw.WriteAsync(runTime.ToString("O"));
                        }

                        // Get data for properties
                        var columns = dataReader.GetColumnSchema();
                        var columnNameList = columns.Select(s => s.ColumnName).ToList();
                        var timeStampIndex = columnNameList.FindIndex(s =>
                            s.Equals(SqlConfig.ColumnNameTimeStamp, StringComparison.OrdinalIgnoreCase));
                        var messageIndex = columnNameList.FindIndex(s =>
                            s.Equals(SqlConfig.ColumnNameMessage, StringComparison.OrdinalIgnoreCase));
                        var eventLevelIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameEventLevel) &&
                            SqlConfig.EventLevelMapping.Count > 0)
                            eventLevelIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameEventLevel, StringComparison.OrdinalIgnoreCase));
                        var tagIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameTags))
                            tagIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameTags, StringComparison.OrdinalIgnoreCase));
                        var priorityIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNamePriority))
                            priorityIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNamePriority, StringComparison.OrdinalIgnoreCase));
                        var responderIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameResponder))
                            responderIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameResponder, StringComparison.OrdinalIgnoreCase));
                        var projectKeyIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameProjectKey))
                            projectKeyIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameProjectKey, StringComparison.OrdinalIgnoreCase));
                        var initialEstimateIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameInitialEstimate))
                            initialEstimateIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameInitialEstimate, StringComparison.OrdinalIgnoreCase));
                        var remainingEstimateIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameRemainingEstimate))
                            remainingEstimateIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameRemainingEstimate,
                                    StringComparison.OrdinalIgnoreCase));
                        var dueDateIndex = -1;
                        if (!string.IsNullOrEmpty(SqlConfig.ColumnNameDueDate))
                            dueDateIndex = columnNameList.FindIndex(s =>
                                s.Equals(SqlConfig.ColumnNameDueDate, StringComparison.OrdinalIgnoreCase));

                        while (await dataReader.ReadAsync())
                        {
                            var timeStamp = dataReader.GetDateTime(timeStampIndex);
                            var message = dataReader.IsDBNull(messageIndex)
                                ? string.Empty
                                : dataReader.GetString(messageIndex);
                            var logEventLevel = (LogEventLevel)Enum.Parse(typeof(LogEventLevel),
                                SqlConfig.LogEventLevel.ToString());
                            if (eventLevelIndex >= 0 && !dataReader.IsDBNull(eventLevelIndex) &&
                                TryGetEventLevelCI(dataReader.GetString(eventLevelIndex), out var eventLevel))
                                logEventLevel = eventLevel;

                            _logger.BindMessageTemplate(message, Array.Empty<object>(), out var messageTemplate,
                                out var boundProperties);
                            var logEvent = new LogEvent(timeStamp, logEventLevel, null, messageTemplate,
                                boundProperties);

                            for (var i = 0; i < dataReader.FieldCount; i++)
                                foreach (var unused in SqlConfig.ColumnNamesInclude.Where(columnName =>
                                    columnName.Equals(columnNameList[i], StringComparison.OrdinalIgnoreCase)))
                                    if (_logger.BindProperty(columnNameList[i], dataReader[i], false,
                                        out var property))
                                        logEvent.AddOrUpdateProperty(property);

                            var tags = new List<string>();
                            if (tagIndex == -1 && SqlConfig.Tags.Any())
                            {
                                tags.AddRange(SqlConfig.Tags);
                                if (_logger.BindProperty("Tags", tags, false, out var tagEvent))
                                    logEvent.AddOrUpdateProperty(tagEvent);
                            }
                            else if (tagIndex >= 0 && !dataReader.IsDBNull(tagIndex) &&
                                     TryGetPropertyValueCI(SqlConfig.TagMappings, dataReader.GetString(tagIndex),
                                         out var tag))
                            {
                                tags.Add(tag);
                                if (_logger.BindProperty("Tags", tags, false, out var tagEvent))
                                    logEvent.AddOrUpdateProperty(tagEvent);
                            }

                            if (priorityIndex >= 0 && !dataReader.IsDBNull(priorityIndex))
                            {
                                if (SqlConfig.PriorityMapping.Count > 0 && TryGetPropertyValueCI(
                                    SqlConfig.PriorityMapping, dataReader.GetString(priorityIndex),
                                    out var priorityValue))
                                {
                                    if (_logger.BindProperty("Priority", priorityValue, false, out var priority))
                                        logEvent.AddOrUpdateProperty(priority);
                                }
                                else
                                {
                                    if (_logger.BindProperty("Priority", dataReader.GetString(priorityIndex), false,
                                        out var priority))
                                        logEvent.AddOrUpdateProperty(priority);
                                }
                            }

                            if (responderIndex >= 0 && !dataReader.IsDBNull(responderIndex))
                            {
                                if (SqlConfig.ResponderMapping.Count > 0 && TryGetPropertyValueCI(
                                    SqlConfig.ResponderMapping, dataReader.GetString(responderIndex),
                                    out var responderValue))
                                {
                                    if (_logger.BindProperty("Responders", responderValue, false,
                                        out var responder))
                                        logEvent.AddOrUpdateProperty(responder);
                                }
                                else
                                {
                                    if (_logger.BindProperty("Responders", dataReader.GetString(responderIndex),
                                        false,
                                        out var responder))
                                        logEvent.AddOrUpdateProperty(responder);
                                }
                            }

                            if (projectKeyIndex >= 0 && !dataReader.IsDBNull(projectKeyIndex))
                            {
                                if (SqlConfig.ProjectKeyMapping.Count > 0 && TryGetPropertyValueCI(
                                    SqlConfig.ProjectKeyMapping, dataReader.GetString(projectKeyIndex),
                                    out var projectKeyValue))
                                {
                                    if (_logger.BindProperty("ProjectKey", projectKeyValue, false,
                                        out var projectKey))
                                        logEvent.AddOrUpdateProperty(projectKey);
                                }
                                else
                                {
                                    if (_logger.BindProperty("ProjectKey", dataReader.GetString(projectKeyIndex),
                                        false,
                                        out var projectKey))
                                        logEvent.AddOrUpdateProperty(projectKey);
                                }
                            }

                            if (initialEstimateIndex >= 0 && !dataReader.IsDBNull(initialEstimateIndex))
                            {
                                if (SqlConfig.InitialEstimateMapping.Count > 0 && TryGetPropertyValueCI(
                                    SqlConfig.InitialEstimateMapping, dataReader.GetString(initialEstimateIndex),
                                    out var initialEstimateValue))
                                {
                                    if (_logger.BindProperty("InitialEstimate", initialEstimateValue, false,
                                        out var initialEstimate))
                                        logEvent.AddOrUpdateProperty(initialEstimate);
                                }
                                else
                                {
                                    if (_logger.BindProperty("InitialEstimate",
                                        dataReader.GetString(initialEstimateIndex), false, out var initialEstimate))
                                        logEvent.AddOrUpdateProperty(initialEstimate);
                                }
                            }

                            if (remainingEstimateIndex >= 0 && !dataReader.IsDBNull(remainingEstimateIndex))
                            {
                                if (SqlConfig.RemainingEstimateMapping.Count > 0 && TryGetPropertyValueCI(
                                    SqlConfig.RemainingEstimateMapping,
                                    dataReader.GetString(remainingEstimateIndex),
                                    out var remainingEstimateValue))
                                {
                                    if (_logger.BindProperty("RemainingEstimate", remainingEstimateValue, false,
                                        out var remainingEstimate))
                                        logEvent.AddOrUpdateProperty(remainingEstimate);
                                }
                                else
                                {
                                    if (_logger.BindProperty("RemainingEstimate",
                                        dataReader.GetString(remainingEstimateIndex), false,
                                        out var remainingEstimate))
                                        logEvent.AddOrUpdateProperty(remainingEstimate);
                                }
                            }

                            if (dueDateIndex >= 0 && !dataReader.IsDBNull(dueDateIndex))
                            {
                                if (SqlConfig.DueDateMapping.Count > 0 && TryGetPropertyValueCI(
                                    SqlConfig.DueDateMapping, dataReader.GetString(dueDateIndex),
                                    out var dueDateValue))
                                {
                                    if (_logger.BindProperty("DueDate", dueDateValue, false, out var dueDate))
                                        logEvent.AddOrUpdateProperty(dueDate);
                                }
                                else
                                {
                                    if (_logger.BindProperty("DueDate", dataReader.GetString(dueDateIndex), false,
                                        out var dueDate))
                                        logEvent.AddOrUpdateProperty(dueDate);
                                }
                            }

                            // Add additional properties
                            if (!string.IsNullOrEmpty(SqlConfig.ApplicationName))
                            {
                                var appName = "Application";
                                if (!string.IsNullOrEmpty(SqlConfig.ApplicationPropertyName))
                                    appName = SqlConfig.ApplicationPropertyName;

                                _logger.BindProperty(appName, SqlConfig.ApplicationName, false,
                                    out var applicationProperty);
                                logEvent.AddOrUpdateProperty(applicationProperty);
                            }


                            // Write LogEvent as Json to text writer
                            var writer = new TextWriterSink(_textWriter);
                            writer.Emit(logEvent);
                        }
                    }

                }
            }
            catch (SqlException ex)
            {
                _logger.Error(ex, "A SQL exception has occurred.");
            }
        }

        // ReSharper disable once InconsistentNaming
        private static bool TryGetEventLevelCI(string propertyName, out LogEventLevel propertyValue)
        {
            var pair = SqlConfig.EventLevelMapping.FirstOrDefault(p =>
                p.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (pair.Key == null)
            {
                propertyValue = LogEventLevel.Debug;
                return false;
            }

            propertyValue = pair.Value;
            return true;
        }

        // ReSharper disable once InconsistentNaming
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

        private static string CreateWhereClause(ICollection<string> clauses)
        {
            return clauses.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", clauses)}";
        }
    }
}