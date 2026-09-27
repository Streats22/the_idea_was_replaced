using Godot;

namespace Delvework.Game.Audio;

/// <summary>
/// A tiny offline synthesizer: voices are mixed into a float buffer that wraps around, so
/// notes that ring past the end fade in at the start and the loop is seamless.
/// </summary>
public sealed class Synth(double seconds, int rate = 22050)
{
    public int Rate { get; } = rate;
    public float[] Buffer { get; } = new float[(int)(seconds * rate)];
    private uint _noise = 0x9E3779B9;

    public static double Midi(double note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

    private void Add(int i, float v) => Buffer[((i % Buffer.Length) + Buffer.Length) % Buffer.Length] += v;

    private float Noise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return (_noise & 0xFFFF) / 32768f - 1f;
    }

    /// <summary>A soft pad or lead: a few detuned harmonics with attack and release.</summary>
    public void Tone(double start, double length, double freq, float amp, double attack = 0.05, double release = 0.3,
        float bright = 0.3f, float detune = 0.0f, float vibrato = 0f)
    {
        var s0 = (int)(start * Rate);
        var n = (int)((length + release) * Rate);
        double p1 = 0, p2 = 0, p3 = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Rate;
            var env = t < attack ? t / attack : t < length ? 1 : Math.Max(0, 1 - (t - length) / release);
            var f = freq * (1 + vibrato * Math.Sin(t * 2 * Math.PI * 5.2) * Math.Min(1, t / 0.4));
            p1 += f / Rate;
            p2 += f * (1 + detune) / Rate;
            p3 += f * 2 / Rate;
            var v = Math.Sin(p1 * 2 * Math.PI) + 0.6 * Math.Sin(p2 * 2 * Math.PI) + bright * Math.Sin(p3 * 2 * Math.PI) + bright * 0.4 * Math.Sin(p1 * 3 * 2 * Math.PI);
            Add(s0 + i, (float)(v * env * amp / 2));
        }
    }

    /// <summary>Plucked string (Karplus-Strong): warm, harp- or lute-like.</summary>
    public void Pluck(double start, double freq, float amp, double length = 1.6, float damping = 0.996f)
    {
        var period = Math.Max(2, (int)(Rate / freq));
        var ring = new float[period];
        for (var i = 0; i < period; i++) ring[i] = Noise();
        var s0 = (int)(start * Rate);
        var n = (int)(length * Rate);
        var idx = 0;
        for (var i = 0; i < n; i++)
        {
            var next = (idx + 1) % period;
            var v = ring[idx];
            ring[idx] = (ring[idx] + ring[next]) * 0.5f * damping;
            idx = next;
            var fade = i > n - 200 ? (n - i) / 200f : 1;
            Add(s0 + i, v * amp * fade);
        }
    }

    /// <summary>Bell or chime: inharmonic partials with long exponential decay.</summary>
    public void Bell(double start, double freq, float amp, double decay = 2.5)
    {
        (double Ratio, double Amp, double Decay)[] partials = [(1, 1, 1), (2.76, 0.5, 0.6), (5.4, 0.25, 0.35), (8.93, 0.12, 0.2)];
        var s0 = (int)(start * Rate);
        var n = (int)(decay * 3 * Rate);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Rate;
            double v = 0;
            foreach (var (r, a, d) in partials) v += a * Math.Sin(2 * Math.PI * freq * r * t) * Math.Exp(-t / (decay * d));
            var attack = Math.Min(1, t / 0.004);
            Add(s0 + i, (float)(v * amp * attack * 0.5));
        }
    }

    /// <summary>Low drum: a pitch-dropping sine with a noise click.</summary>
    public void Drum(double start, float amp, double freq = 70, double decay = 0.35, float noise = 0.25f)
    {
        var s0 = (int)(start * Rate);
        var n = (int)(decay * 4 * Rate);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)Rate;
            var f = freq * (1 + 1.5 * Math.Exp(-t / 0.03));
            phase += f / Rate;
            var v = Math.Sin(phase * 2 * Math.PI) * Math.Exp(-t / decay) + noise * Noise() * Math.Exp(-t / 0.02);
            Add(s0 + i, (float)(v * amp));
        }
    }

    /// <summary>Filtered noise swell (wind, breath, hiss).</summary>
    public void Wind(double start, double length, float amp, float smooth = 0.02f)
    {
        var s0 = (int)(start * Rate);
        var n = (int)(length * Rate);
        float lp = 0, lp2 = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)n;
            lp += (Noise() - lp) * smooth;
            lp2 += (lp - lp2) * smooth;
            var env = (float)Math.Sin(Math.PI * t);
            Add(s0 + i, lp2 * amp * env * 6);
        }
    }

    /// <summary>A tone whose pitch glides from <paramref name="from"/> to <paramref name="to"/> (sweeps, zaps).</summary>
    public void Sweep(double start, double length, double from, double to, float amp, float square = 0f)
    {
        var s0 = (int)(start * Rate);
        var n = (int)(length * Rate);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)n;
            phase += (from + (to - from) * t) / Rate;
            var sin = Math.Sin(phase * 2 * Math.PI);
            var v = square > 0 ? (1 - square) * sin + square * Math.Sign(sin) * 0.6 : sin;
            var env = Math.Min(1, t * 20) * (1 - t);
            Add(s0 + i, (float)(v * amp * env));
        }
    }

    public void Noise(double start, double length, float amp, float smooth = 0.5f)
    {
        var s0 = (int)(start * Rate);
        var n = (int)(length * Rate);
        float lp = 0;
        for (var i = 0; i < n; i++)
        {
            lp += (Noise() - lp) * smooth;
            var t = i / (double)n;
            Add(s0 + i, lp * amp * (float)Math.Pow(1 - t, 2));
        }
    }

    /// <summary>A cheap feedback delay for space.</summary>
    public void Echo(double delay, float feedback, float mix)
    {
        var d = (int)(delay * Rate);
        var wet = new float[Buffer.Length];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < Buffer.Length; i++)
            {
                var j = (i - d + Buffer.Length) % Buffer.Length;
                wet[i] = Buffer[j] * mix + wet[j] * feedback;
            }
        }
        for (var i = 0; i < Buffer.Length; i++) Buffer[i] += wet[i];
    }

    public AudioStreamWav ToStream(bool loop, float gain = 1f)
    {
        var peak = Buffer.Max(Math.Abs);
        var scale = peak > 0.95f ? 0.95f / peak : 1f;
        var bytes = new byte[Buffer.Length * 2];
        for (var i = 0; i < Buffer.Length; i++)
        {
            var s = (short)Math.Clamp(Buffer[i] * scale * gain * 32767f, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? Buffer.Length : 0,
        };
    }
}
