using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Services;
using MeetingAudioRecorder.Infrastructure.Google;
using MeetingAudioRecorder.Infrastructure.IO;
using MeetingAudioRecorder.Infrastructure.Logging;
using MeetingAudioRecorder.Infrastructure.Recovery;
using MeetingAudioRecorder.Infrastructure.Recowilk;
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
        services.AddSingleton<IBrowserExtensionInstaller, BrowserExtensionInstaller>();
        services.AddSingleton<ISingleInstanceService, NamedMutexSingleInstanceService>();
        services.AddSingleton<IRecordingRecoveryService, RecordingRecoveryService>();
        services.AddSingleton<IRecordingSessionManifestStore, JsonRecordingSessionManifestStore>();
        services.AddSingleton<IWavFileRepairService, WavFileRepairService>();
        services.AddSingleton<IFileNameService, FileNameService>();
        services.AddSingleton<IDiskSpaceService, DiskSpaceService>();
        services.AddSingleton<IGoogleTokenStore, ProtectedFileGoogleTokenStore>();
        services.AddSingleton<IGoogleOAuthUserConsent, GoogleLoopbackOAuthUserConsent>();
        services.AddHttpClient<IGoogleAuthorizationService, GoogleOAuthAuthorizationService>();
        services.AddHttpClient<IGoogleAccessTokenProvider, GoogleAccessTokenProvider>();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>();
        services.AddHttpClient<IGoogleMeetClient, GoogleMeetClient>();
        services.AddSingleton<IActiveMeetLinkProvider, FileActiveMeetLinkProvider>();
        services.AddSingleton<IRecowilkCredentialStore, ProtectedFileRecowilkCredentialStore>();
        services.AddHttpClient("recowilk", client => client.Timeout = TimeSpan.FromMinutes(10));
        services.AddSingleton<IRecowilkUploadQueue, RecowilkUploadQueue>();
        return services;
    }
}
