using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MeetingAudioRecorder.Infrastructure.Logging;

public static class SerilogSetup
{
    public static IServiceCollection AddAppLogging(this IServiceCollection services)
    {
        AppPaths.EnsureDirectories();
        var logPath = Path.Combine(AppPaths.LogsDirectory, "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
        });

        return services;
    }

    public static void CloseAndFlush() => Log.CloseAndFlush();
}
