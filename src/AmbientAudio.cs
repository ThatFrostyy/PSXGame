using System;
using System.IO;
using Silk.NET.OpenAL;

namespace PSXGame;

public sealed class AmbientAudio : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly nint _device;
    private readonly nint _context;
    private readonly uint _buffer;
    private readonly uint _source;
    private readonly bool _isInitialized;

    public unsafe AmbientAudio()
    {
        _alc = ALContext.GetApi();
        _al = AL.GetApi();

        _device = (nint)_alc.OpenDevice(string.Empty);
        if (_device == 0)
        {
            Console.Error.WriteLine("OpenAL initialization failed: unable to open audio device. Audio disabled.");
            return;
        }

        _context = (nint)_alc.CreateContext((Device*)_device, null);
        if (_context == 0)
        {
            Console.Error.WriteLine("OpenAL initialization failed: unable to create audio context. Audio disabled.");
            _alc.CloseDevice((Device*)_device);
            return;
        }

        _alc.MakeContextCurrent((Context*)_context);

        _buffer = _al.GenBuffer();
        _source = _al.GenSource();
        _isInitialized = true;

        string wavPath = Path.Combine(AppContext.BaseDirectory, "src", "sounds", "crickets.wav");
        if (!File.Exists(wavPath))
        {
            wavPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "sounds", "crickets.wav");
        }

        var wav = LoadWav(wavPath);
        fixed (byte* p = wav.Data)
        {
            _al.BufferData(_buffer, wav.Format, p, wav.Data.Length, wav.SampleRate);
        }

        _al.SetSourceProperty(_source, SourceInteger.Buffer, (int)_buffer);
        _al.SetSourceProperty(_source, SourceBoolean.Looping, true);
        _al.SetSourceProperty(_source, SourceFloat.Gain, 0.35f);
        _al.SourcePlay(_source);
    }

    private static (BufferFormat Format, int SampleRate, byte[] Data) LoadWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        string riff = new(reader.ReadChars(4));
        if (riff != "RIFF") throw new InvalidDataException("Invalid WAV (missing RIFF).");
        _ = reader.ReadInt32();
        string wave = new(reader.ReadChars(4));
        if (wave != "WAVE") throw new InvalidDataException("Invalid WAV (missing WAVE).");

        ushort channels = 0;
        int sampleRate = 0;
        ushort bitsPerSample = 0;
        byte[] data = Array.Empty<byte>();

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            if (chunkId == "fmt ")
            {
                ushort audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                if (chunkSize > 16) reader.BaseStream.Seek(chunkSize - 16, SeekOrigin.Current);
                if (audioFormat != 1) throw new InvalidDataException("Only PCM WAV is supported.");
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }
            else
            {
                reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
            }

            if ((chunkSize & 1) == 1)
            {
                reader.BaseStream.Seek(1, SeekOrigin.Current);
            }
        }

        if (data.Length == 0)
        {
            throw new InvalidDataException("Invalid WAV (missing data chunk).");
        }

        BufferFormat format = (channels, bitsPerSample) switch
        {
            (1, 8) => BufferFormat.Mono8,
            (1, 16) => BufferFormat.Mono16,
            (2, 8) => BufferFormat.Stereo8,
            (2, 16) => BufferFormat.Stereo16,
            _ => throw new InvalidDataException($"Unsupported WAV format: {channels} channels, {bitsPerSample} bits.")
        };
        return (format, sampleRate, data);
    }

    public unsafe void Dispose()
    {
        if (!_isInitialized) return;

        _al.SourceStop(_source);
        _al.DeleteSource(_source);
        _al.DeleteBuffer(_buffer);
        _alc.DestroyContext((Context*)_context);
        _alc.CloseDevice((Device*)_device);
    }
}
