using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Treblle.Net.Core;

internal sealed class TreblleDiagnosticObserver :
    IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>
{
    private const string EfCoreSourceName = "Microsoft.EntityFrameworkCore";
    private const string CommandExecutedEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";

    // Caches (PropertyInfo getter, duration getter, commandText getter) per event data type
    private static readonly ConcurrentDictionary<Type, (Func<object, object?> GetCommand, Func<object, TimeSpan?> GetDuration, Func<object, string?> GetCommandText)> _accessors = new();

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (listener.Name == EfCoreSourceName)
            listener.Subscribe(this);
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> pair)
    {
        if (pair.Key != CommandExecutedEvent || pair.Value is null)
            return;

        try
        {
            var accessors = _accessors.GetOrAdd(pair.Value.GetType(), BuildAccessors);
            var command = accessors.GetCommand(pair.Value);
            var duration = accessors.GetDuration(pair.Value);
            var sql = command is not null ? accessors.GetCommandText(command) : null;

            if (sql is not null && duration.HasValue)
                TreblleQueryCollector.Add(sql, duration.Value.TotalMilliseconds);
        }
        catch
        {
            // Never fail in a diagnostic observer
        }
    }

    private static (Func<object, object?>, Func<object, TimeSpan?>, Func<object, string?>) BuildAccessors(Type eventType)
    {
        var commandProp = eventType.GetProperty("Command", BindingFlags.Public | BindingFlags.Instance);
        var durationProp = eventType.GetProperty("Duration", BindingFlags.Public | BindingFlags.Instance);
        var commandTextProp = commandProp?.PropertyType.GetProperty("CommandText", BindingFlags.Public | BindingFlags.Instance);

        Func<object, object?> getCommand = commandProp is not null
            ? o => commandProp.GetValue(o)
            : _ => null;

        Func<object, TimeSpan?> getDuration = durationProp is not null
            ? o => durationProp.GetValue(o) is TimeSpan ts ? ts : null
            : _ => null;

        Func<object, string?> getCommandText = commandTextProp is not null
            ? o => commandTextProp.GetValue(o) as string
            : _ => null;

        return (getCommand, getDuration, getCommandText);
    }

    void IObserver<DiagnosticListener>.OnCompleted() { }
    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
}
