using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace Seq.Input.MsSql
{
    public class Executor
    {
        private readonly ILogger _logger;
        private readonly TextWriter _textWriter;
        private readonly FileInfo _fileInfo;
        private readonly string _connectionString;
        private readonly string _query;
        private readonly string _additionalFilterClause;
        private readonly string _columnNameTimeStamp;
        private readonly string _columnNameMessage;
        private readonly string _columnNamesInclude;
        private readonly string _applicationName;
        private readonly int _logEventLevel;

        public Executor(ILogger logger, TextWriter textWriter, FileInfo fileInfo, string connectionString, string query, string additionalFilterClause, string columnNameTimeStamp, string columnNameMessage, string columnNamesInclude, string applicationName, int logEventLevel)
        {
            _logger = logger;
            _textWriter = textWriter;
            _fileInfo = fileInfo;
            _connectionString = connectionString;
            _query = query;
            _additionalFilterClause = additionalFilterClause;
            _columnNameTimeStamp = columnNameTimeStamp;
            _columnNameMessage = columnNameMessage;
            _columnNamesInclude = columnNamesInclude;
            _applicationName = applicationName;
            _logEventLevel = logEventLevel;
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

                        if (!string.IsNullOrEmpty(_additionalFilterClause))
                        {
                            clauseList.Add(_additionalFilterClause);
                        }

                        //Set the current run time, less 1 second, to allow for timestamps that don't measure in milliseconds (such as timestamps calculated from MSDB sysjobhistory)
                        var runTime = DateTime.Now.AddSeconds(-1);
                        bool validContent = false;
                        var dateTime = DateTime.Now;

                        // Get last valid timestamp from file
                        if (_fileInfo.Exists)
                        {
                            var content = File.ReadAllText(_fileInfo.FullName).Trim();
                            if (!string.IsNullOrEmpty(content))
                            {
                                validContent = true;
                                dateTime = DateTime.Parse(content);

                                clauseList.Add($"{_columnNameTimeStamp} > '{dateTime:yyyy-MM-dd HH:mm:ss.fff}'");
                                clauseList.Add($"{_columnNameTimeStamp} <= '{runTime:yyyy-MM-dd HH:mm:ss.fff}'");
                            }
                            else
                                _logger.Debug("lastScan.txt did not contain content");
                        }
                        else
                            _logger.Debug("lastScan.txt not found");
                        
                        if (!validContent)
                        {
                            //Avoid continual repeating loop ingesting all events on first run by limiting the query to last day up to current runTime (-1 sec)
                            dateTime = DateTime.Now.AddDays(-1);
                            _logger.Debug("Could not determine last scan time - query limited to last day");
                        }

                        //Only retrieve events that occurs after the last dateTime and up to the current runTime (- 1 sec)
                        clauseList.Add($"{_columnNameTimeStamp} > '{dateTime:yyyy-MM-dd HH:mm:ss.fff}'");
                        clauseList.Add($"{_columnNameTimeStamp} <= '{runTime:yyyy-MM-dd HH:mm:ss.fff}'");
                        _logger.Debug("Query new table rows starting at {StartDateTime}", dateTime);

                        var queryString = _query + CreateWhereClause(clauseList);

                        // Create command and execute
                        command.CommandText = queryString;
                        command.CommandType = CommandType.Text;
                        var dataReader = await command.ExecuteReaderAsync();

                        // Write current runTime to file
                        using (var sw = _fileInfo.CreateText())
                        {
                            sw.Write(runTime.ToString("O"));
                        }


                        // Get data for properties
                        var columns = dataReader.GetColumnSchema();
                        var columnNameList = columns.Select(s => s.ColumnName).ToList();
                        var includeColumnList = _columnNamesInclude.Split(',').ToList();
                        var timeStampIndex = columnNameList.FindIndex(s => s == _columnNameTimeStamp);
                        var messageIndex = columnNameList.FindIndex(s => s == _columnNameMessage);

                        while (await dataReader.ReadAsync())
                        {
                            var timeStamp = dataReader.GetDateTime(timeStampIndex);
                            var message = dataReader.IsDBNull(messageIndex) ? string.Empty : dataReader.GetString(messageIndex);
                            _logger.BindMessageTemplate(message, new object[0], out var messageTemplate, out var boundProperties);
                            var logEvent = new LogEvent(timeStamp, (LogEventLevel)Enum.Parse(typeof(LogEventLevel), _logEventLevel.ToString()), null, messageTemplate, boundProperties);

                            for (var i = 0; i < dataReader.FieldCount; i++)
                            {
                                if (includeColumnList.Contains(columnNameList[i]))
                                {
                                    if (_logger.BindProperty(columnNameList[i], dataReader[i], false, out var property))
                                    {
                                        logEvent.AddOrUpdateProperty(property);
                                    }
                                }
                            }

                            // Add additional properties
                            _logger.BindProperty("Application", _applicationName, false, out var applicationProperty);
                            logEvent.AddOrUpdateProperty(applicationProperty);

                            // Write LogEvent as Json to text writer
                            var writer = new TextWriterSink(_textWriter);
                            writer.Emit(logEvent);
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.Error(ex, "A SQL exception was occured.");
            }
        }

        private string CreateWhereClause(ICollection<string> clauses)
        {
            if (clauses.Count == 0)
                return string.Empty;

            return $" WHERE {string.Join(" AND ", clauses)}";
        }
    }
}