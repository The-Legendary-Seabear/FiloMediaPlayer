
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
    private int _firstFrameLogged = 0;

    public bool UseInputFrameForOutput => true;

    public IReadOnlyList<AudioEncodingProperties>
        SupportedEncodingProperties
    {
        get
        {
            var supported = new List<AudioEncodingProperties>();

            foreach (uint sampleRate in new uint[] { 44100, 48000 })
            {
                foreach (uint channels in new uint[] { 1, 2 })
                {
                    var properties =
                        AudioEncodingProperties.CreatePcm(
                            sampleRate,
                            channels,
                            32);

                    properties.Subtype = MediaEncodingSubtypes.Float;

                    supported.Add(properties);
                }
            }

            return supported;
        }
    }


    public void SetEncodingProperties(
        AudioEncodingProperties encodingProperties)
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
        if (configuration != null &&
            configuration.TryGetValue("GainDb", out object value))
        {
            double gain = Convert.ToDouble(value);

            if (double.IsFinite(gain))
            {
                _gainDb = gain;
                _gainMultiplier = Math.Pow(
                    10.0,
                    _gainDb / 20.0);
            }
        }
    }


    public unsafe void ProcessFrame(ProcessAudioFrameContext context)
    {
        if (System.Threading.Interlocked.Exchange(
        ref _firstFrameLogged, 1) == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Normalization: Processing audio! Gain = {_gainDb} dB");
        }

        using var buffer = context.InputFrame
            .LockBuffer(AudioBufferAccessMode.ReadWrite);

        using var reference = buffer.CreateReference();

        var memoryAccess = reference.As<IMemoryBufferByteAccess>();

        memoryAccess.GetBuffer(
            out byte* data,
            out uint capacity);

        // This implementation expects 32-bit floating-point audio.
        // Do not interpret integer PCM samples as floats.
        if (data == null)
            return;

        uint usableBytes = Math.Min(buffer.Length, capacity);

        int sampleCount = (int)(usableBytes / sizeof(float));

        float* samples = (float*)data;

        double multiplier = _gainMultiplier;

        for (int i = 0; i < sampleCount; i++)
        {
            double sample = samples[i] * multiplier;

            // Prevent values outside the valid floating-point
            // audio sample amplitude range.
            sample = Math.Clamp(sample, -1.0, 1.0);

            samples[i] = (float)sample;
        }
    }


    public void DiscardQueuedFrames()
    {
    }

    public void Close(MediaEffectClosedReason reason)
    {
    }
}
