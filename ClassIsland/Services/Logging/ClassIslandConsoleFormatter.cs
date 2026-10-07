using System;
using System.IO;
using ClassIsland.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace ClassIsland.Services.Logging;

/// <summary>控制台日志格式化：纯文本单色输出，不依赖终端 ANSI 颜色支持。</summary>
public class ClassIslandConsoleFormatter() : ConsoleFormatter("classisland")
{
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var now = DateTimeOffset.Now.ToString("yyyy/MM/dd HH:mm:ss");
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception)
                      + (logEntry.Exception != null ? "\n" + logEntry.Exception : "");
        message = LogMaskingHelper.MaskLog(message);

        textWriter.Write(now);
        textWriter.Write(" | ");
        textWriter.Write(GetLogLevelString(logEntry.LogLevel));
        textWriter.Write(" | ");
        textWriter.Write(logEntry.Category);
        textWriter.Write(" | ");
        scopeProvider?.ForEachScope((scope, state) =>
        {
            state.Write(scope?.ToString());
            state.Write(" => ");
        }, textWriter);
        textWriter.Write(message);
        textWriter.Write(Environment.NewLine);
    }

    private static string GetLogLevelString(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "unkn"
    };
}
