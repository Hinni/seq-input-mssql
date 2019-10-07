﻿using System.Data.SqlClient;
using System.IO;
using System.Threading;
using Seq.Apps;

namespace Seq.Input.MSSql
{
    [SeqApp("MSSQL Input", AllowReprocessing = false,
        Description = "")]
    public class MSSQLInput : SeqApp, IPublishJson
    {
        private Executor _executor;
        private Timer _timer;
        private SqlConnectionStringBuilder _stringBuilder;

        [SeqAppSetting(
            DisplayName = "Refresh every x milliseconds",
            IsOptional = false,
            InputType = SettingInputType.Integer,
            HelpText = "Search for new rows every x milliseconds.")]
        public int QueryEveryMilliseconds { get; set; } = 15;

        [SeqAppSetting(
            DisplayName = "Database connection string",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "MSSQL connection string - don't use TrustedConnection, just SQL credentials.")]
        public string DatabaseConnectionString { get; set; }

        [SeqAppSetting(
            DisplayName = "Username",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "Username for SQL credentials.")]
        public string DatabaseUsername { get; set; }

        [SeqAppSetting(
            DisplayName = "Password",
            IsOptional = false,
            InputType = SettingInputType.Password,
            HelpText = "Password for SQL credentials.")]
        public string DatabasePassword { get; set; }

        [SeqAppSetting(
            DisplayName = "Query to execute on server",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "SQL query to execute.")]
        public string ExecuteQuery { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of TimeStamp",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "Select timestamp column.")]
        public string ColumnNameTimeStamp { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Message",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "Select message text column.")]
        public string ColumnNameMessage { get; set; }

        [SeqAppSetting(
            DisplayName = "Include following columns as properties",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "Comma separated column name list.")]
        public string ColumnNamesInclude { get; set; }

        public void Start(TextWriter inputWriter)
        {
            _stringBuilder = new SqlConnectionStringBuilder(DatabaseConnectionString) { UserID = DatabaseUsername, Password = DatabasePassword };
            _executor = new Executor(Log, inputWriter, _stringBuilder.ToString(), ExecuteQuery, ColumnNameTimeStamp, ColumnNameMessage, ColumnNamesInclude);
            _timer = new Timer(_executor.Start, null, 5000, QueryEveryMilliseconds);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}