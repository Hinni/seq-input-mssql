using System;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using Seq.Apps;
using Seq.Input.MSSql;
using Serilog.Events;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedType.Global

namespace Seq.Input.MsSql
{
    [SeqApp("MSSQL Input", AllowReprocessing = false,
        Description = "Ingest events into Seq directly from MSSQL table.")]
    // ReSharper disable once InconsistentNaming
    public class MSSQLInput : SeqApp, IPublishJson, IDisposable
    {
        private const string DefaultTimePeriod = "00:00-23:59";
        private ExecutorTask _executorTask;

        [SeqAppSetting(
            DisplayName = "Debug logging",
            IsOptional = true,
            InputType = SettingInputType.Checkbox,
            HelpText = "Log additional debug information.")]
        public bool? Debug { get; set; }

        [SeqAppSetting(
            DisplayName = "Refresh every x seconds",
            IsOptional = false,
            InputType = SettingInputType.Integer,
            HelpText = "Search for new rows every x seconds; recommend at least 5 seconds, default 15")]
        public int QueryEverySeconds { get; set; } = 15;

        [SeqAppSetting(
            DisplayName = "Server instance name",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "MSSQL server instance name. Optionally Specify TCP port name via comma, eg. SERVERNAME,1433.")]
        public string ServerInstance { get; set; }

        [SeqAppSetting(
            DisplayName = "SQL connect timeout",
            IsOptional = true,
            InputType = SettingInputType.Integer,
            HelpText = "Connect timeout in seconds, default 15, maximum 120.")]
        public int? ConnectTimeout { get; set; } = 15;

        [SeqAppSetting(
            DisplayName = "SQL query timeout",
            IsOptional = true,
            InputType = SettingInputType.Integer,
            HelpText = "Query timeout in seconds, default 60, maximum 300.")]
        public int? CommandTimeout { get; set; } = 60;

        [SeqAppSetting(
            DisplayName = "Encrypted connection",
            IsOptional = true,
            InputType = SettingInputType.Checkbox,
            HelpText = "Use encryption on this connection.")]
        public bool? Encrypt { get; set; }

        [SeqAppSetting(
            DisplayName = "Trust server certificate",
            IsOptional = true,
            InputType = SettingInputType.Checkbox,
            HelpText = "If encryption is used, optionally check this box to trust any certificate presented.")]
        public bool? TrustCertificate { get; set; }

        [SeqAppSetting(
            DisplayName = "Initial catalog",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "MSSQL ConnectionString Initial Catalog.")]
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
            DisplayName = "Column name of TimeStamp.",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText =
                "Select timestamp column. Your view or query must include a timestamp that can be used to create a log entry with a date and time.")]
        public string ColumnNameTimeStamp { get; set; }

        [SeqAppSetting(
            DisplayName = "Query time delay",
            IsOptional = true,
            InputType = SettingInputType.Integer,
            HelpText =
                "Allows setting a time delay on the period that will be queried, allowing for database rows being inserted late and timestamps that don't measure in milliseconds. Minimum 1, maximum 86400 (24 hours), default 1.")]
        public int? SecondsDelay { get; set; } = 1;

        [SeqAppSetting(
            DisplayName = "Column name of Message.",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText =
                "Select message text column. Your view or query must supply a column that can be used as a log message.")]
        public string ColumnNameMessage { get; set; }

        [SeqAppSetting(
            DisplayName = "Include following columns as properties",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "Comma separated column name list. For structured logging, more properties are better.")]
        public string ColumnNamesInclude { get; set; }

        [SeqAppSetting(
            DisplayName = "Log application name as property",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Add an application name property to every LogEvent (recommended for filtering logs based on application). If not specified, will default to the instance title. This will only be used when your query produces an event, and to identify the instance to SQL.")]
        public string ApplicationName { get; set; }

        [SeqAppSetting(
            DisplayName = "Application property name",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText = "If using 'Log application name as property', set the property name. Defaults to Application.")]
        public string ApplicationPropertyName { get; set; } = "Application";

        [SeqAppSetting(
            DisplayName = "Column name of Event Level",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Event Level, specify it here, and set mapping via Value mapping for Event Level.")]
        public string ColumnNameEventLevel { get; set; }

        [SeqAppSetting(
            DisplayName = "Event Level mapping",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Set a keypair mapping if Value mapping for Log Event Level is used (eg. Highest=Fatal,Error=Error,Exception=Warning). Use Fatal, Error, Warning, Information, Verbose, and Debug as your values.")]
        public string EventLevelMapping { get; set; }

        [SeqAppSetting(
            DisplayName = "Event Level",
            IsOptional = false,
            InputType = SettingInputType.Integer,
            HelpText =
                "Specify the log event level as a number; serves as a default if Column name of Event Level is used and not matched. 0=Verbose, 1=Debug, 2=Info (Default), 3=Warn, 4=Error, 5=Fatal.")]
        public int LogEventLevel { get; set; } = 2;

        [SeqAppSetting(
            DisplayName = "Valid local time period",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText = "Local time period in which the query is executed, or leave empty. Default (00:00-23:59).")]
        public string TimePeriod { get; set; } = DefaultTimePeriod;

        [SeqAppSetting(
            DisplayName = "Tags",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Specify a comma delimited lst of tags, or (optionally) a keypair mapping (Key=Priority) for column values that can be mapped to a tags, eg. Info=LabelInfo,Error=Outage,Exception=Linux")]
        public string Tags { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Tags",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Tags, specify it here, and optionally set mapping via Value mapping for Tags.")]
        public string ColumnNameTags { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Priority",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for priority, specify it here, and optionally set mapping via Value mapping for Priority.")]
        public string ColumnNamePriority { get; set; }

        [SeqAppSetting(
            DisplayName = "Value mapping for Priority",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Optionally specify a comma delimited keypair mapping (Key=Priority) for column values that can be mapped to a priority, eg. Highest=P1,Error=Highest,Exception=P3")]
        public string PriorityMapping { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Responder",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Responder, specify it here, and optionally set mapping via Value mapping for Responder.")]
        public string ColumnNameResponder { get; set; }

        [SeqAppSetting(
            DisplayName = "Value mapping for Responder",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Optionally specify a comma delimited keypair mapping (Key=Value) for column values that can be mapped to a Responder, eg. IT=JSmith,TechGuys=Windows Escalation")]
        public string ResponderMapping { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Project Key",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Project Key, specify it here, and optionally set mapping via Value mapping for ProjectKey.")]
        public string ColumnNameProjectKey { get; set; }

        [SeqAppSetting(
            DisplayName = "Value mapping for ProjectKey",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Optionally specify a comma delimited keypair mapping (Key=Value) for column values that can be mapped to a Project Key, eg. IT=SERVICEDESK,TechGuys=DEV")]
        public string ProjectKeyMapping { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Initial Estimate",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Initial Estimate, specify it here, and optionally set mapping via Value mapping for InitialEstimate.")]
        public string ColumnNameInitialEstimate { get; set; }

        [SeqAppSetting(
            DisplayName = "Value mapping for InitialEstimate",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Optionally specify a comma delimited keypair mapping (Key=Value) for column values that can be mapped to a Initial Estimate, eg. IT=SERVICEDESK,TechGuys=DEV")]
        public string InitialEstimateMapping { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Remaining Estimate",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Remaining Estimate, specify it here, and optionally set mapping via Value mapping for RemainingEstimate.")]
        public string ColumnNameRemainingEstimate { get; set; }

        [SeqAppSetting(
            DisplayName = "Value mapping for RemainingEstimate",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Optionally specify a comma delimited keypair mapping (Key=Value) for column values that can be mapped to a Remaining Estimate, eg. IT=SERVICEDESK,TechGuys=DEV")]
        public string RemainingEstimateMapping { get; set; }

        [SeqAppSetting(
            DisplayName = "Column name of Due Date",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "If you have a column that can serve as the source for Due Date, specify it here, and optionally set mapping via Value mapping for DueDate.")]
        public string ColumnNameDueDate { get; set; }

        [SeqAppSetting(
            DisplayName = "Value mapping for DueDate",
            IsOptional = true,
            InputType = SettingInputType.Text,
            HelpText =
                "Optionally specify a comma delimited keypair mapping (Key=Value) for column values that can be mapped to a Due Date, eg. IT=SERVICEDESK,TechGuys=DEV")]
        public string DueDateMapping { get; set; }

        public void Dispose()
        {
            _executorTask?.Dispose();
        }

        public void Start(TextWriter inputWriter)
        {
            var settingsFileInfo = new FileInfo(Path.Combine(App.StoragePath, "lastScan.txt"));
            var query = $"SELECT * FROM {TableOrViewName}";
            var stringBuilder = new SqlConnectionStringBuilder
            {
                DataSource = SqlConfig.ServerInstance,
                InitialCatalog = SqlConfig.InitialCatalog,
                ConnectTimeout = SqlConfig.ConnectTimeout,
                CommandTimeout = SqlConfig.CommandTimeout,
                ApplicationName = SqlConfig.ApplicationName
            };

            if (SqlConfig.Encrypt)
            {
                stringBuilder.Encrypt = SqlConfig.Encrypt;
                stringBuilder.TrustServerCertificate = SqlConfig.TrustCertificate;
            }

            if (SqlConfig.IntegratedSecurity)
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
                Log.Warning("Defined TimePeriod {TimePeriod} is not valid. Use default {DefaultValue}.", TimePeriod,
                    DefaultTimePeriod);
                TimePeriod = DefaultTimePeriod;
            }

            var executor = new Executor(Log, inputWriter, settingsFileInfo, stringBuilder.ToString(), query);
            _executorTask = new ExecutorTask(Log, TimeSpan.FromSeconds(QueryEverySeconds), TimePeriod, executor);
        }

        public void Stop()
        {
            _executorTask?.Stop();
        }

        protected override void OnAttached()
        {
            //Populate SqlConfig when the instance starts
            if (Debug != null)
                SqlConfig.Debug = (bool)Debug;
            SqlConfig.ApplicationName = !string.IsNullOrEmpty(ApplicationName) ? ApplicationName : App.Title;
            SqlConfig.ApplicationPropertyName = !string.IsNullOrEmpty(ApplicationPropertyName) ? ApplicationPropertyName : "Application";
            Log.Debug("Debug logging: {Debug}", SqlConfig.Debug);
            if (SqlConfig.Debug) Log.Debug("Application Property Name: {AppPropertyName}, App Name: {AppName}", SqlConfig.ApplicationPropertyName, SqlConfig.ApplicationName);
            SqlConfig.QueryEverySeconds = QueryEverySeconds;
            if (SqlConfig.Debug) Log.Debug("Query seconds: {QuerySeconds}", SqlConfig.QueryEverySeconds);
            SqlConfig.ServerInstance = ServerInstance;
            if (SqlConfig.Debug) Log.Debug("SQL Server instance: {SqlInstance}", SqlConfig.ServerInstance);
            if (ConnectTimeout == null || ConnectTimeout < 1 || ConnectTimeout > 120)
                ConnectTimeout = 15;
            SqlConfig.ConnectTimeout = (int)ConnectTimeout;
            if (SqlConfig.Debug) Log.Debug("SQL Connect Timeout: {ConnectTimeout}", SqlConfig.ConnectTimeout);
            if (CommandTimeout == null || CommandTimeout < 1 || CommandTimeout > 300)
                CommandTimeout = 60;
            SqlConfig.CommandTimeout = (int)CommandTimeout;
            if (SqlConfig.Debug) Log.Debug("SQL Query Timeout: {CommandTimeout}", SqlConfig.CommandTimeout);
            if (Encrypt != null) SqlConfig.Encrypt = (bool)Encrypt;
            if (SqlConfig.Debug) Log.Debug("Encrypted connection: {Encrypt}", SqlConfig.Encrypt);
            if (TrustCertificate != null) SqlConfig.TrustCertificate = (bool)TrustCertificate;
            if (SqlConfig.Debug) Log.Debug("Trust server certificate: {TrustCertificate}", SqlConfig.TrustCertificate);
            SqlConfig.InitialCatalog = InitialCatalog;
            if (SqlConfig.Debug) Log.Debug("Initial Catalog: {InitialCatalog}", SqlConfig.InitialCatalog);
            SqlConfig.IntegratedSecurity = IntegratedSecurity;
            if (SqlConfig.Debug) Log.Debug("Use integrated security: {IntegratedSecurity}", SqlConfig.IntegratedSecurity);
            SqlConfig.DatabaseUsername = DatabaseUsername;
            SqlConfig.DatabasePassword = DatabasePassword;
            SqlConfig.TableOrViewName = TableOrViewName;
            if (SqlConfig.Debug) Log.Debug("Table or view to query: {TableOrView}", SqlConfig.TableOrViewName);
            SqlConfig.AdditionalFilterClause = AdditionalFilterClause;
            if (SqlConfig.Debug) Log.Debug("Additional filter: {Filter}", SqlConfig.AdditionalFilterClause);
            SqlConfig.ColumnNameTimeStamp = ColumnNameTimeStamp;
            if (SqlConfig.Debug) Log.Debug("Column for Timestamp: {ColumnTimestamp}", SqlConfig.ColumnNameTimeStamp);
            if (SecondsDelay == null || SecondsDelay < 1 || SecondsDelay > 86400)
                SecondsDelay = 1;
            SqlConfig.SecondsDelay = (int)SecondsDelay;
            if (SqlConfig.Debug) Log.Debug("Seconds Delay Config: {SecondsDelay}, will delay query by {SecondsDelayActual}", SecondsDelay, SqlConfig.SecondsDelay);
            SqlConfig.ColumnNameMessage = ColumnNameMessage;
            if (SqlConfig.Debug) Log.Debug("Column for Message: {ColumnMessage}", SqlConfig.ColumnNameMessage);
            SqlConfig.ColumnNamesInclude = SqlConfig.SplitAndTrim(',', ColumnNamesInclude).ToList();
            if (SqlConfig.Debug) Log.Debug("Columns to include: {ColumnNames}", SqlConfig.ColumnNamesInclude);
            SqlConfig.ColumnNameEventLevel = ColumnNameEventLevel;
            if (SqlConfig.Debug) Log.Debug("Column for Event Level: {ColumnEventLevel}", ColumnNameEventLevel);
            if (SqlConfig.ParseEventKeyPairList(EventLevelMapping, out var eventLevelMappings))
            {
                SqlConfig.EventLevelMapping = eventLevelMappings;
                if (SqlConfig.Debug) Log.Debug("Event level mappings: {LevelMappings}", SqlConfig.EventLevelMapping);
            }

            SqlConfig.LogEventLevel = LogEventLevel;
            if (SqlConfig.Debug) Log.Debug("Event level / Default event level: {EventLevel}", (LogEventLevel)SqlConfig.LogEventLevel);
            SqlConfig.TimePeriod = TimePeriod;
            if (SqlConfig.Debug) Log.Debug("Time Period: {TimePeriod}", SqlConfig.TimePeriod);
            if (SqlConfig.IsKeyPairList(Tags) && SqlConfig.ParseKeyPairList(Tags, out var tagMappings))
            {
                SqlConfig.TagMappings = tagMappings;
                if (SqlConfig.Debug) Log.Debug("Tag mappings: {TagMappings}", SqlConfig.TagMappings);
            }
            else if (SqlConfig.IsValue(Tags))
            {
                SqlConfig.Tags = SqlConfig.SplitAndTrim(',', Tags);
                if (SqlConfig.Debug) Log.Debug("Tags: {Tags}", SqlConfig.Tags);
            }

            SqlConfig.ColumnNameTags = ColumnNameTags;
            if (SqlConfig.Debug) Log.Debug("Column for Tags: {ColumnTags}", SqlConfig.ColumnNameTags);

            SqlConfig.ColumnNamePriority = ColumnNamePriority;
            if (SqlConfig.Debug) Log.Debug("Column for Priority: {ColumnPriority}", SqlConfig.ColumnNamePriority);
            if (SqlConfig.ParseKeyPairList(PriorityMapping, out var priorityMappings))
            {
                SqlConfig.PriorityMapping = priorityMappings;
                Log.Debug("Priority Mappings: {PriorityMappings}", SqlConfig.PriorityMapping);
            }

            SqlConfig.ColumnNameResponder = ColumnNameResponder;
            if (SqlConfig.Debug) Log.Debug("Column for Responder: {ColumnResponder}", SqlConfig.ColumnNameResponder);
            if (SqlConfig.ParseKeyPairList(ResponderMapping, out var responderMappings))
            {
                SqlConfig.ResponderMapping = responderMappings;
                if (SqlConfig.Debug) Log.Debug("Responder Mappings: {ResponderMappings}", SqlConfig.ResponderMapping);
            }

            SqlConfig.ColumnNameProjectKey = ColumnNameProjectKey;
            if (SqlConfig.Debug) Log.Debug("Column for Project Key: {ColumnProjectKey}", SqlConfig.ColumnNameProjectKey);
            if (SqlConfig.ParseKeyPairList(ProjectKeyMapping, out var projectKeyMappings))
            {
                SqlConfig.ProjectKeyMapping = projectKeyMappings;
                if (SqlConfig.Debug) Log.Debug("Project Key Mappings: {ProjectKeyMappings}", SqlConfig.ProjectKeyMapping);
            }

            SqlConfig.ColumnNameInitialEstimate = ColumnNameInitialEstimate;
            if (SqlConfig.Debug) Log.Debug("Column for Initial Estimate: {ColumnInitialEstimate}", SqlConfig.ColumnNameInitialEstimate);
            if (SqlConfig.ParseKeyPairList(InitialEstimateMapping, out var initialEstimateMappings))
            {
                SqlConfig.InitialEstimateMapping = initialEstimateMappings;
                if (SqlConfig.Debug) Log.Debug("Initial Estimate Mappings: {InitialEstimateMappings}", SqlConfig.InitialEstimateMapping);
            }

            SqlConfig.ColumnNameRemainingEstimate = ColumnNameRemainingEstimate;
            if (SqlConfig.Debug) Log.Debug("Column for Remaining Estimate: {ColumnRemainingEstimate}", SqlConfig.ColumnNameRemainingEstimate);
            if (SqlConfig.ParseKeyPairList(RemainingEstimateMapping, out var remainingEstimateMappings))
            {
                SqlConfig.RemainingEstimateMapping = remainingEstimateMappings;
                if (SqlConfig.Debug) Log.Debug("Remaining Estimate Mappings: {RemainingEstimateMappings}", SqlConfig.RemainingEstimateMapping);
            }

            SqlConfig.ColumnNameDueDate = ColumnNameDueDate;
            if (SqlConfig.Debug) Log.Debug("Column for Due Date: {ColumnDueDate}", SqlConfig.ColumnNameDueDate);
            if (SqlConfig.ParseKeyPairList(DueDateMapping, out var dueDateMappings))
            {
                SqlConfig.DueDateMapping = dueDateMappings;
                if (SqlConfig.Debug) Log.Debug("Due Date Mappings: {DueDateMappings}", SqlConfig.DueDateMapping);
            }
        }
    }
}
