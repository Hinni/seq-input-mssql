using System;
using System.IO;
using Seq.Apps;

namespace Seq.Input.MSSql
{
    [SeqApp("MSSQL Input", AllowReprocessing = false,
        Description = "")]
    public class MSSQLInput : SeqApp, IPublishJson
    {
        private Executor _executor;

        [SeqAppSetting(
            DisplayName = "Refresh every x seconds",
            IsOptional = false,
            InputType = SettingInputType.Integer,
            HelpText = "")]
        public int QueryEverySeconds { get; set; } = 15;

        [SeqAppSetting(
            DisplayName = "Database Connection string",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "")]
        public string DatabaseConnectionString { get; set; }

        [SeqAppSetting(
            DisplayName = "Username",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "")]
        public string DatabaseUsername { get; set; }

        [SeqAppSetting(
            DisplayName = "Password",
            IsOptional = false,
            InputType = SettingInputType.Password,
            HelpText = "")]
        public string DatabasePassword { get; set; }

        [SeqAppSetting(
            DisplayName = "Query to execute on server",
            IsOptional = false,
            InputType = SettingInputType.Text,
            HelpText = "")]
        public string ExecuteQuery { get; set; }

        public void Start(TextWriter inputWriter)
        {
            _executor = new Executor(Log, inputWriter, DatabaseConnectionString, ExecuteQuery, "", "");
            _executor.Start();
        }

        public void Stop()
        {
        }
    }
}