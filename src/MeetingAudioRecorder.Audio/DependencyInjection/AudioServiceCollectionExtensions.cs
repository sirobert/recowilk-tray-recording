using MeetingAudioRecorder.Audio.Capture;
using MeetingAudioRecorder.Audio.Devices;
using MeetingAudioRecorder.Audio.Encoding;
using MeetingAudioRecorder.Audio.Mixing;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingAudioRecorder.Audio.DependencyInjection;

public static class AudioServiceCollectionExtensions
{
    public static IServiceCollection AddAudioServices(this IServiceCollection services)
    {
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IAudioMixingService, AudioMixingService>();
        services.AddSingleton<IMp3EncodingService, Mp3EncodingService>();
        services.AddTransient<IMicrophoneCaptureService, WasapiMicrophoneCapture>();
        services.AddTransient<ILoopbackCaptureService, WasapiLoopbackCaptureService>();
        services.AddTransient<LevelMeterService>();

        // Fabryki dla coordinatora (nowa instancja na każde nagranie)
        services.AddSingleton<Func<IMicrophoneCaptureService>>(sp =>
            () => sp.GetRequiredService<IMicrophoneCaptureService>());
        services.AddSingleton<Func<ILoopbackCaptureService>>(sp =>
            () => sp.GetRequiredService<ILoopbackCaptureService>());

        services.AddSingleton<IRecordingCoordinator, RecordingCoordinator>();
        return services;
    }
}
