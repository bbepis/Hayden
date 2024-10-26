using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Expressions;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace Hayden;

public static class SerilogManager
{
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LoggingFunctions))]
	private static readonly ExpressionTemplate expressionTemplate = new(
		"[{@t:dd-MMM HH:mm:ss} {@l:t5}]{FilterSourceContext(SourceContext)} {@m}{#if IsError()}\n{requestInfo}{#end}\n{@x}",
		new CultureInfo("en-GB"), theme: TemplateTheme.Code,
		nameResolver: new StaticMemberNameResolver(typeof(LoggingFunctions)));

	public static LoggingLevelSwitch LevelSwitch { get; } = new();

	public static LoggerConfiguration Config { get; } = new LoggerConfiguration()
		.Enrich.FromLogContext()
		.Enrich.WithDemystifiedStackTraces()
		.MinimumLevel.ControlledBy(LevelSwitch)
		.WriteTo.Console(expressionTemplate);

	public static void SetLogger()
	{
		Log.Logger = Config.CreateLogger();
	}

	public static ILogger CreateSubLogger(string category)
		=> Log.Logger.ForContext("SourceContext", category);

	internal static class LoggingFunctions
	{
		public static LogEventPropertyValue FilterSourceContext(
			LogEventPropertyValue context)
		{
			if (context is ScalarValue sv && sv.Value != null && sv.Value is string s)
			{
				if (s == "Microsoft.Hosting.Lifetime")
					return new ScalarValue(string.Empty);

				return new ScalarValue($" [{s}]");
			}

			// Undefined - argument was not a string.
			return null;
		}

		public static LogEventPropertyValue AddRequestInfo(
			LogEvent @event)
		{
			if (@event.Level >= LogEventLevel.Error)
			{
				return new ScalarValue(@event.Properties.TryGetValue("requestInfo", out var requestInfo) ? "\n" + requestInfo : null);
			}

			return null;
		}

		public static LogEventPropertyValue IsError(
			LogEvent @event)
		{
			return new ScalarValue(@event.Level >= LogEventLevel.Error);
		}
	}
}