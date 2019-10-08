using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace Seq.Input.MSSql
{
    public class Executor
    {
        private readonly ILogger _logger;
        private readonly TextWriter _textWriter;
        private readonly FileInfo _fileInfo;
        private readonly string _connectionString;
        private readonly string _query;
        private readonly string _columnNameTimeStamp;
        private readonly string _columnNameMessage;
        private readonly string _columnNamesInclude;

        public Executor(ILogger logger, TextWriter textWriter, FileInfo fileInfo, string connectionString, string query, string columnNameTimeStamp, string columnNameMessage, string columnNamesInclude)
        {
            _logger = logger;
            _textWriter = textWriter;
            _fileInfo = fileInfo;
            _connectionString = connectionString;
            _query = query;
            _columnNameTimeStamp = columnNameTimeStamp;
            _columnNameMessage = columnNameMessage;
            _columnNamesInclude = columnNamesInclude;
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
                        // Get last timestamp from file
                        var queryString = _query;
                        if (_fileInfo.Exists)
                        {
                            var dateTime = DateTime.Parse(File.ReadAllText(_fileInfo.FullName));
                            queryString += $" WHERE {_columnNameTimeStamp} >= '{dateTime:yyyy-MM-dd HH:mm:ss.fff}'";
                        }

                        // Create command and execute
                        command.CommandText = queryString;
                        command.CommandType = CommandType.Text;
                        var dataReader = await command.ExecuteReaderAsync();

                        // Write new timestamp to file
                        using (var sw = _fileInfo.CreateText())
                        {
                            sw.Write(DateTime.Now.ToString("u"));
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
                            var message = dataReader.GetString(messageIndex);
                            _logger.BindMessageTemplate(message, new object[0], out var messageTemplate, out var boundProperties);
                            var logEvent = new LogEvent(timeStamp, LogEventLevel.Error, null, messageTemplate, boundProperties);

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
    }
}