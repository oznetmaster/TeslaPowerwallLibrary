using Microsoft.Extensions.Logging;

namespace TeslaPowerwallLibrary.TestConsole;

/// <summary>Caller-owned console logging for connections opened by this tool.</summary>
internal static class VerboseLogging
	{
	private static volatile bool _enabled;
	internal static ILogger Logger { get; } = new ConsoleLogger ();
	public static void Enable () => _enabled = true;

	private sealed class ConsoleLogger : ILogger
		{
		public bool IsEnabled (LogLevel logLevel) => _enabled && logLevel != LogLevel.None;
		public IDisposable? BeginScope<TState> (TState state) where TState : notnull => null;
		public void Log<TState> (LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
			{
			if (IsEnabled (logLevel))
				Console.Error.WriteLine ($"{DateTime.Now:HH:mm:ss} {logLevel} Powerwall: {formatter (state, exception)}");
			}
		}
	}