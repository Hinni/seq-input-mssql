using System;
using System.Data.SqlClient;
using System.IO;
using Seq.Apps;
using Seq.Input.MSSql;

namespace Seq.Input.MsSql
{
    [SeqApp("MSSQL Input", AllowReprocessing = false,
        Description = "Ingest events into Seq directly from MSSQL table.")]
    public class MSSQLInput : SeqApp, IPublishJson, IDisposable
    {
        private static readonly string DefaultTimePeriod = "00:00-23:59";
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
            DisplayName = "Initial catalog",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "MSSQL ConnectionString InitialCatalog.")]
        public string InitialCatalog { get; set; }

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
            DisplayName = "Additional filter clause",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText = "Allows to filter some rows.")]
        public string AdditionalFilterClause { get; set; }

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

        [SeqAppSetting(
            DisplayName = "Log application name as property",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText = "If you like a new additional property to every LogEvent.")]
        public string ApplicationName { get; set; }

        [SeqAppSetting(
            DisplayName = "Serilog.Events.LogEventLevel",
            IsOptional = false,
            InputType = SettingInputType.Integer,
            HelpText = "0=Verbose, 1=Debug, 2=Info (Default), 3=Warn, 4=Error, 5=Fatal.")]
        public int LogEventLevel { get; set; } = 2;

        [SeqAppSetting(
            DisplayName = "Valid time period",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText = "Time period in which the query is executed, or leave empty. Default (00:00-23:59).")]
        public string TimePeriod { get; set; } = DefaultTimePeriod;

        public void Start(TextWriter inputWriter)
        {
            var settingsFileInfo = new FileInfo(Path.Combine(App.StoragePath, "lastScan.txt"));
            var query = $"SELECT * FROM {TableOrViewName}";
            var stringBuilder = new SqlConnectionStringBuilder()
            {
                DataSource = ServerInstance,
                InitialCatalog = InitialCatalog
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

            if (!TimePeriodHelper.IsStringValid(TimePeriod))
            {
                Log.Warning("Defined TimePeriod {TimePeriod} is not valid. Use default {DefaultValue}.", TimePeriod, DefaultTimePeriod);
                TimePeriod = DefaultTimePeriod;
            }

            var executor = new Executor(Log, inputWriter, settingsFileInfo, stringBuilder.ToString(), query, AdditionalFilterClause, ColumnNameTimeStamp, ColumnNameMessage, ColumnNamesInclude, ApplicationName, LogEventLevel);
            _executorTask = new ExecutorTask(Log, TimeSpan.FromSeconds(QueryEverySeconds), TimePeriod, executor);
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