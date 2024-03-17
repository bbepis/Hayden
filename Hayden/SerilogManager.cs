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
}