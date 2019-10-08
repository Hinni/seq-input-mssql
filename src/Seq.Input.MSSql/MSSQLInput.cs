using System;
using System.Data.SqlClient;
using System.IO;
using Seq.Apps;

namespace Seq.Input.MSSql
{
    [SeqApp("MSSQL Input", AllowReprocessing = false,
        Description = "Ingest events into Seq directly from MSSQL table.")]
    public class MSSQLInput : SeqApp, IPublishJson, IDisposable
    {
        private ExecutorTask _executorTask;

        [SeqAppSetting(
            DisplayName = "Refresh every x seconds",
            IsOptional = false,
            InputType = SettingInputType.Integer,
            HelpText = "Search for new rows every x seconds.")]
        public int QueryEverySeconds { get; set; } = 15;

        [SeqAppSetting(
            DisplayName = "Server instance name",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "MSSQL server instance name.")]
        public string ServerInstance { get; set; }

        [SeqAppSetting(
            DisplayName = "Database name",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "MSSQL database name.")]
        public string DatabaseName { get; set; }

        [SeqAppSetting(
            DisplayName = "Trusted Connection",
            IsOptional = false,
            InputType = SettingInputType.Checkbox,
            HelpText = "Use Windows credentials and not fields below.")]
        public bool IntegratedSecurity { get; set; }

        [SeqAppSetting(
            DisplayName = "Username",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText = "Username for SQL credentials.")]
        public string DatabaseUsername { get; set; }

        [SeqAppSetting(
            DisplayName = "Password",
            IsOptional = true,
            InputType = SettingInputType.Password,
            HelpText = "Password for SQL credentials.")]
        public string DatabasePassword { get; set; }

        [SeqAppSetting(
            DisplayName = "Table or view name",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "SQL table or view to query.")]
        public string TableOrViewName { get; set; }

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
            var settingsFileInfo = new FileInfo(Path.Combine(App.StoragePath, "lastScan.txt"));
            var query = $"SELECT * FROM {TableOrViewName}";
            var stringBuilder = new SqlConnectionStringBuilder()
            {
                DataSource = ServerInstance,
                InitialCatalog = DatabaseName
            };

            if (IntegratedSecurity)
            {
                stringBuilder.IntegratedSecurity = true;
            }
            else
            {
                stringBuilder.UserID = DatabaseUsername;
                stringBuilder.Password = DatabasePassword;
            }

            var executor = new Executor(Log, inputWriter, settingsFileInfo, stringBuilder.ToString(), query, ColumnNameTimeStamp, ColumnNameMessage, ColumnNamesInclude);
            _executorTask = new ExecutorTask(Log, TimeSpan.FromSeconds(QueryEverySeconds), executor);
        }

        public void Stop()
        {
            _executorTask?.Stop();
        }

        public void Dispose()
        {
            _executorTask?.Dispose();
        }
    }
}