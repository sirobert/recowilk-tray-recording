using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Services;
using MeetingAudioRecorder.Infrastructure.IO;
using MeetingAudioRecorder.Infrastructure.Logging;
using MeetingAudioRecorder.Infrastructure.Recovery;
using MeetingAudioRecorder.Infrastructure.Settings;
using MeetingAudioRecorder.Infrastructure.SingleInstance;
using MeetingAudioRecorder.Infrastructure.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingAudioRecorder.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddAppLogging();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<ISingleInstanceService, NamedMutexSingleInstanceService>();
        services.AddSingleton<IRecordingRecoveryService, RecordingRecoveryService>();
        services.AddSingleton<IFileNameService, FileNameService>();
        services.AddSingleton<IDiskSpaceService, DiskSpaceService>();
        return services;
    }
}
