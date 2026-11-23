
using System.Collections.Generic;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Foundation;
using WinRT;

namespace Filo.AudioEffects.Windows;

// Applies a per-track normalization gain to decoded 32-bit float PCM.
//
// The app controls the gain by writing "GainDb" into the PropertySet it
// passed to MediaPlayer.AddAudioEffect. The effect re-reads that value
// every frame, so toggles and track changes take effect during playback.
//
// Do not use static fields to communicate with the app: WinRT.Host loads
// this component in its own AssemblyLoadContext, so statics set by the
// app are not the statics seen here.
public sealed class NormalizationAudioEffect : IBasicAudioEffect
{
    private const string GainDbKey = "GainDb";

    private const double MinGainDb = -60.0;
    private const double MaxGainDb = 24.0;

    // Gain changes fade over this long to avoid clicks.
    private const double RampSeconds = 0.02;

    private readonly IReadOnlyList<AudioEncodingProperties> _supportedEncodingProperties;

    private IPropertySet? _configuration;

    // Written by SetEncodingProperties, read on the audio thread.
    private volatile bool _bypass;
    private int _channelCount = 2;
    private int _rampFrames = 882;

    // Audio-thread state.
    private double _lastGainDb = double.NaN;
    private float _targetMultiplier = 1.0f;
    private float _currentMultiplier = 1.0f;
    private float _rampStep;

#if DEBUG
    private long _framesProcessed;
    private bool _reportNextFrameAfterDiscard;
#endif

    public NormalizationAudioEffect()
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

        _supportedEncodingProperties = supported;
    }

    public bool UseInputFrameForOutput => true;

    public IReadOnlyList<AudioEncodingProperties> SupportedEncodingProperties
        => _supportedEncodingProperties;

    public void SetEncodingProperties(AudioEncodingProperties encodingProperties)
    {
        // Never throw here: an exception fails the whole playback session.
        // Fall back to pass-through instead.
        bool isFloat32 =
            encodingProperties.BitsPerSample == 32 &&
            string.Equals(
                encodingProperties.Subtype,
                MediaEncodingSubtypes.Float,
                StringComparison.OrdinalIgnoreCase);

        _channelCount = (int)Math.Max(1u, encodingProperties.ChannelCount);
        _rampFrames = Math.Max(1, (int)(encodingProperties.SampleRate * RampSeconds));
        _currentMultiplier = _targetMultiplier;
        _bypass = !isFloat32;

        string format =
            $"{encodingProperties.Subtype} {encodingProperties.BitsPerSample}-bit " +
            $"{encodingProperties.SampleRate} Hz {encodingProperties.ChannelCount} ch";

        System.Diagnostics.Debug.WriteLine(
            $"Normalization: Effect format {format}" +
            (_bypass ? " (unsupported, bypassing)" : string.Empty));

#if DEBUG
        TryWriteDiagnostic("Filo.Effect.Format", format);
        TryWriteDiagnostic("Filo.Effect.Bypass", _bypass);
#endif
    }

    public void SetProperties(IPropertySet configuration)
    {
        System.Threading.Volatile.Write(ref _configuration, configuration);
        RefreshGainFromConfiguration();
        _currentMultiplier = _targetMultiplier;

#if DEBUG
        var loadContext = System.Runtime.Loader.AssemblyLoadContext
            .GetLoadContext(typeof(NormalizationAudioEffect).Assembly);

        TryWriteDiagnostic(
            "Filo.Effect.LoadContext",
            $"{loadContext?.GetType().Name} '{loadContext?.Name}'");
#endif
    }

    private void RefreshGainFromConfiguration()
    {
        var configuration = System.Threading.Volatile.Read(ref _configuration);

        if (configuration == null ||
            !configuration.TryGetValue(GainDbKey, out object? value) ||
            value == null)
        {
            return;
        }

        double gainDb;

        try
        {
            gainDb = Convert.ToDouble(value);
        }
        catch (Exception)
        {
            return;
        }

        if (!double.IsFinite(gainDb) || gainDb == _lastGainDb)
            return;

        gainDb = Math.Clamp(gainDb, MinGainDb, MaxGainDb);

        _lastGainDb = gainDb;
        _targetMultiplier = (float)Math.Pow(10.0, gainDb / 20.0);
        _rampStep = (_targetMultiplier - _currentMultiplier) / _rampFrames;

        System.Diagnostics.Debug.WriteLine(
            $"Normalization: Audio thread applying {gainDb:F2} dB");

#if DEBUG
        TryWriteDiagnostic("Filo.Effect.AppliedGainDb", gainDb);
#endif
    }

    public unsafe void ProcessFrame(ProcessAudioFrameContext context)
    {
        if (_bypass)
            return;

        try
        {
            RefreshGainFromConfiguration();

            float current = _currentMultiplier;
            float target = _targetMultiplier;

            using var buffer =
                context.InputFrame.LockBuffer(AudioBufferAccessMode.ReadWrite);

            using var reference = buffer.CreateReference();

            reference.As<IMemoryBufferByteAccess>()
                .GetBuffer(out byte* data, out uint capacity);

            if (data == null)
                return;

            int sampleCount =
                (int)(Math.Min(buffer.Length, capacity) / sizeof(float));

            float* samples = (float*)data;

#if DEBUG
            float peakIn = 0f;
            for (int i = 0; i < sampleCount; i++)
                peakIn = Math.Max(peakIn, Math.Abs(samples[i]));
#endif

            bool steady = current == target;

            if (steady && target == 1.0f)
            {
                // Unity gain: leave the buffer untouched.
            }
            else if (steady)
            {
                for (int i = 0; i < sampleCount; i++)
                    samples[i] = Math.Clamp(samples[i] * target, -1.0f, 1.0f);
            }
            else
            {
                // Ramp linearly towards the new gain; the ramp may span
                // several buffers when they are short.
                int channels = _channelCount;
                float step = _rampStep;

                for (int i = 0; i < sampleCount; i += channels)
                {
                    if (current != target)
                    {
                        current += step;

                        if (step > 0 ? current >= target : current <= target)
                            current = target;
                    }

                    int end = Math.Min(i + channels, sampleCount);

                    for (int j = i; j < end; j++)
                        samples[j] = Math.Clamp(samples[j] * current, -1.0f, 1.0f);
                }

                _currentMultiplier = current;
            }

#if DEBUG
            TryWriteDiagnostic("Filo.Effect.FramesProcessed", ++_framesProcessed);
            double frameTime = context.InputFrame.RelativeTime?.TotalSeconds ?? -1.0;
            TryWriteDiagnostic("Filo.Effect.FrameTime", frameTime);

            if (_reportNextFrameAfterDiscard)
            {
                _reportNextFrameAfterDiscard = false;
                TryWriteDiagnostic(
                    "Filo.Effect.FirstFrameAfterDiscard",
                    $"{frameTime:F3}s @ {_lastGainDb:F2} dB");
            }

            if (peakIn > 1e-4f && steady)
            {
                float peakOut = 0f;
                for (int i = 0; i < sampleCount; i++)
                    peakOut = Math.Max(peakOut, Math.Abs(samples[i]));

                TryWriteDiagnostic(
                    "Filo.Effect.MeasuredGainDb",
                    20.0 * Math.Log10(peakOut / peakIn));
            }
#endif
        }
        catch (Exception ex)
        {
            // Never let an exception escape onto the audio thread.
            System.Diagnostics.Debug.WriteLine(
                $"Normalization: ProcessFrame error: {ex.Message}");
        }
    }

    public void DiscardQueuedFrames()
    {
        // Called on seeks. Audio after a seek is discontinuous anyway, so
        // jump straight to the latest gain instead of ramping.
        RefreshGainFromConfiguration();
        _currentMultiplier = _targetMultiplier;

#if DEBUG
        _reportNextFrameAfterDiscard = true;
#endif
    }

    public void Close(MediaEffectClosedReason reason)
    {
        System.Diagnostics.Debug.WriteLine(
            $"Normalization: Effect closed ({reason})");

        System.Threading.Volatile.Write(ref _configuration, null);
    }

#if DEBUG
    // Debug builds report what the audio thread is doing back through the
    // shared PropertySet so it can be verified without listening.
    private void TryWriteDiagnostic(string key, object value)
    {
        try
        {
            var configuration = System.Threading.Volatile.Read(ref _configuration);

            if (configuration != null)
                configuration[key] = value;
        }
        catch (Exception)
        {
        }
    }
#endif
}
