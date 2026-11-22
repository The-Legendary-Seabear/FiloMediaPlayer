
using System.Collections.Generic;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Foundation;
using WinRT;

namespace Filo.AudioEffects.Windows;

public sealed class NormalizationAudioEffect : IBasicAudioEffect
{
    private double _gainDb = 0.0;
    private double _gainMultiplier = 1.0;

    private static double _liveGainDb = 0.0;
    private static double _liveMultiplier = 1.0;

    private double _lastLoggedLiveGainDb = double.NaN;
    private int _firstFrameLogged = 0;

    private IPropertySet? _configuration;

    public bool UseInputFrameForOutput => true;

    /*
    public IReadOnlyList<AudioEncodingProperties> SupportedEncodingProperties
    {
        get
        {
            var supported = new List<AudioEncodingProperties>();

            foreach (uint sampleRate in new uint[] { 44100, 48000 })
                foreach (uint channels in new uint[] { 1, 2 })
                {
                    var properties =
                        AudioEncodingProperties.CreatePcm(sampleRate, channels, 32);

                    properties.Subtype = MediaEncodingSubtypes.Float;
                    supported.Add(properties);
                }

            return supported;
        }
    }
    */
    public IReadOnlyList<AudioEncodingProperties> SupportedEncodingProperties
    => Array.Empty<AudioEncodingProperties>();

    public void SetEncodingProperties(AudioEncodingProperties encodingProperties)
    {
        if (encodingProperties.BitsPerSample != 32 ||
            encodingProperties.Subtype != MediaEncodingSubtypes.Float)
        {
            throw new NotSupportedException(
                "Normalization requires 32-bit float audio.");
        }
    }

    public void SetProperties(IPropertySet configuration)
    {
        if (_configuration != null)
            _configuration.MapChanged -= OnConfigurationChanged;

        _configuration = configuration;

        if (_configuration != null)
        {
            _configuration.MapChanged += OnConfigurationChanged;
            UpdateGainFromConfiguration();
        }
    }

    private void OnConfigurationChanged(
        IObservableMap<string, object> sender,
        IMapChangedEventArgs<string> args)
    {
        if (args.Key == "GainDb")
            UpdateGainFromConfiguration();
    }

    private void UpdateGainFromConfiguration()
    {
        if (_configuration == null ||
            !_configuration.TryGetValue("GainDb", out object value))
        {
            return;
        }

        double gain = Convert.ToDouble(value);

        if (!double.IsFinite(gain))
            return;

        SetLiveGain(gain);

        System.Diagnostics.Debug.WriteLine(
            $"Normalization: Gain updated to {gain:F2} dB");
    }

    public unsafe void ProcessFrame(ProcessAudioFrameContext context)
    {
        double liveGain =
            System.Threading.Volatile.Read(ref _liveGainDb);

        if (liveGain != _lastLoggedLiveGainDb)
        {
            _lastLoggedLiveGainDb = liveGain;

            System.Diagnostics.Debug.WriteLine(
                $"Normalization: Audio thread sees {liveGain:F2} dB");
        }

        using var buffer =
            context.InputFrame.LockBuffer(AudioBufferAccessMode.ReadWrite);

        using var reference = buffer.CreateReference();

        var memoryAccess = reference.As<IMemoryBufferByteAccess>();
        memoryAccess.GetBuffer(out byte* data, out uint capacity);

        if (data == null)
            return;

        uint usableBytes = Math.Min(buffer.Length, capacity);
        int sampleCount = (int)(usableBytes / sizeof(float));

        float* samples = (float*)data;

        double multiplier =
            System.Threading.Volatile.Read(ref _liveMultiplier);

        for (int i = 0; i < sampleCount; i++)
        {
            double sample = samples[i] * multiplier;

            sample = Math.Clamp(sample, -1.0, 1.0);

            samples[i] = (float)sample;
        }
    }

    public void DiscardQueuedFrames()
    {
    }

    public void Close(MediaEffectClosedReason reason)
    {
        if (_configuration != null)
        {
            _configuration.MapChanged -= OnConfigurationChanged;
            _configuration = null;
        }
    }

    public static void SetLiveGain(double gainDb)
    {
        if (!double.IsFinite(gainDb))
            return;

        double multiplier = Math.Pow(10.0, gainDb / 20.0);

        System.Threading.Volatile.Write(ref _liveMultiplier, multiplier);
        System.Threading.Volatile.Write(ref _liveGainDb, gainDb);

        System.Diagnostics.Debug.WriteLine(
            $"Normalization: Shared gain set to {gainDb:F2} dB");
    }
}
