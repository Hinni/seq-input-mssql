using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Seq.Input.MsSql
{
    public class TextWriterSink : ILogEventSink
    {
        private readonly TextWriter _textWriter;
        private readonly ITextFormatter _textFormatter;
        private readonly object _syncRoot = new object();

        public TextWriterSink(TextWriter textWriter)
        {
            _textFormatter = new CompactJsonFormatter();
            _textWriter = textWriter;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
            lock (_syncRoot)
            {
                _textFormatter.Format(logEvent, _textWriter);
                _textWriter.Flush();
            }
        }
    }
}