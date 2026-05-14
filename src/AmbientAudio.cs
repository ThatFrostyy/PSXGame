using System;
using Silk.NET.OpenAL;

namespace PSXGame;

public sealed class AmbientAudio : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly unsafe Device* _device;
    private readonly unsafe Context* _context;
    private readonly uint _buffer;
    private readonly uint _source;
    private readonly bool _isInitialized;

    public unsafe AmbientAudio()
    {
        _alc = ALContext.GetApi();
        _al = AL.GetApi();
        _device = _alc.OpenDevice(string.Empty);
        if (_device is null)
        {
            Console.Error.WriteLine("OpenAL initialization failed: unable to open audio device. Audio disabled.");
            return;
        }

        _context = _alc.CreateContext(_device, null);
        if (_context is null)
        {
            Console.Error.WriteLine("OpenAL initialization failed: unable to create audio context. Audio disabled.");
            _alc.CloseDevice(_device);
            return;
        }

        _alc.MakeContextCurrent(_context);

        _buffer = _al.GenBuffer();
        _source = _al.GenSource();
        _isInitialized = true;

        const int sampleRate = 22050;
        const int seconds = 4;
        short[] pcm = new short[sampleRate * seconds];
        var rng = new Random(7);
        for (int i = 0; i < pcm.Length; i++)
        {
            float t = i / (float)sampleRate;
            float chirp = MathF.Max(0, MathF.Sin(t * 43f + MathF.Sin(t * 2f) * 2f));
            float gate = MathF.Max(0, MathF.Sin(t * 6.5f + (float)rng.NextDouble() * 0.2f));
            float noise = ((float)rng.NextDouble() - 0.5f) * 0.3f;
            float sample = (chirp * gate * 0.35f) + noise * 0.05f;
            pcm[i] = (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
        }

        unsafe
        {
            fixed (short* p = pcm)
            {
                _al.BufferData(_buffer, BufferFormat.Mono16, p, pcm.Length * sizeof(short), sampleRate);
            }
        }

        _al.SetSourceProperty(_source, SourceInteger.Buffer, (int)_buffer);
        _al.SetSourceProperty(_source, SourceBoolean.Looping, true);
        _al.SetSourceProperty(_source, SourceFloat.Gain, 0.35f);
        _al.SourcePlay(_source);
    }

    public unsafe void Dispose()
    {
        if (!_isInitialized)
        {
            return;
        }

        _al.SourceStop(_source);
        _al.DeleteSource(_source);
        _al.DeleteBuffer(_buffer);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
    }
}
