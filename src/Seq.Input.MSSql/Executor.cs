using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace Seq.Input.MSSql
{
    public class Executor
    {
        private readonly ILogger _logger;
        private readonly TextWriter _textWriter;
        private readonly string _connectionString;
        private readonly string _query;
        private readonly string _timeStampColumnName;
        private readonly string _messageColumnName;

        public Executor(ILogger logger, TextWriter textWriter, string connectionString, string query, string timeStampColumnName, string messageColumnName)
        {
            _logger = logger;
            _textWriter = textWriter;
            _connectionString = connectionString;
            _query = query;
            _timeStampColumnName = timeStampColumnName;
            _messageColumnName = messageColumnName;
        }

        public void Start()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = _query;
                    command.CommandType = CommandType.Text;

                    var dataReader = command.ExecuteReader();

                    // Get column names for property structure
                    var columns = dataReader.GetSchemaTable()?.Columns;
                    var columnNameList = (from DataColumn column in columns select column.ColumnName).ToList();
                    var timeStampIndex = columnNameList.FindIndex(s => s == _timeStampColumnName);
                    var messageIndex = columnNameList.FindIndex(s => s == _messageColumnName);

                    while (dataReader.Read())
                    {
                        var timeStamp = dataReader.GetDateTimeOffset(timeStampIndex);
                        var message =  dataReader.GetString(messageIndex);

                        _logger.BindMessageTemplate(message, new object[0], out var messageTemplate, out var boundProperties);
                        var logEvent = new LogEvent(timeStamp, LogEventLevel.Debug, null, messageTemplate, boundProperties);

                        for (var i = 0; i < dataReader.FieldCount; i++)
                        {
                            if (_logger.BindProperty(columnNameList[i], dataReader[i], false, out var property))
                            {
                                logEvent.AddOrUpdateProperty(property);
                            }
                        }

                        // Write to input
                        var writer = new TextWriterSink(_textWriter);
                        writer.Emit(logEvent);
                    }
                }
            }
        }
    }
}