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
    private readonly uint _ambientBuffer;
    private readonly uint _ambientSource;
    private readonly uint _flashlightBuffer;
    private readonly uint _flashlightSource;
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

        _ambientBuffer = _al.GenBuffer();
        _ambientSource = _al.GenSource();
        _flashlightBuffer = _al.GenBuffer();
        _flashlightSource = _al.GenSource();

        // Load assets BEFORE marking as initialized so that if LoadWav throws,
        // Dispose() (called by the catch block below) can safely clean up the
        // OpenAL resources that were already allocated.
        try
        {
            var ambientWav = LoadWav(ResolveSoundPath("crickets.wav"));
            fixed (byte* p = ambientWav.Data)
            {
                _al.BufferData(_ambientBuffer, ambientWav.Format, p, ambientWav.Data.Length, ambientWav.SampleRate);
            }

            _al.SetSourceProperty(_ambientSource, SourceInteger.Buffer, (int)_ambientBuffer);
            _al.SetSourceProperty(_ambientSource, SourceBoolean.Looping, true);
            _al.SetSourceProperty(_ambientSource, SourceFloat.Gain, 0.35f);
            _al.SourcePlay(_ambientSource);

            var flashlightWav = LoadWav(ResolveSoundPath("flashlight.wav"));
            fixed (byte* p = flashlightWav.Data)
            {
                _al.BufferData(_flashlightBuffer, flashlightWav.Format, p, flashlightWav.Data.Length, flashlightWav.SampleRate);
            }

            _al.SetSourceProperty(_flashlightSource, SourceInteger.Buffer, (int)_flashlightBuffer);
            _al.SetSourceProperty(_flashlightSource, SourceBoolean.Looping, false);
            _al.SetSourceProperty(_flashlightSource, SourceFloat.Gain, 0.85f);

            // Only mark fully initialized once every resource is ready.
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Audio asset loading failed: {ex.Message}. Audio disabled.");
            // Free the OpenAL objects we already allocated before rethrowing,
            // because the constructor won't return an instance and Dispose()
            // will never be called by the caller.
            FreeOpenALResources();
        }
    }

    /// <summary>
    /// Releases OpenAL sources, buffers, context, and device.
    /// Safe to call even when sources/buffers are 0 (not yet generated).
    /// </summary>
    private unsafe void FreeOpenALResources()
    {
        if (_ambientSource  != 0) { _al.SourceStop(_ambientSource);   _al.DeleteSource(_ambientSource);   }
        if (_flashlightSource != 0) { _al.SourceStop(_flashlightSource); _al.DeleteSource(_flashlightSource); }
        if (_ambientBuffer  != 0) _al.DeleteBuffer(_ambientBuffer);
        if (_flashlightBuffer != 0) _al.DeleteBuffer(_flashlightBuffer);
        if (_context != 0) _alc.DestroyContext((Context*)_context);
        if (_device  != 0) _alc.CloseDevice((Device*)_device);
    }


    public void PlayFlashlightClick()
    {
        if (!_isInitialized) return;

        _al.SourceStop(_flashlightSource);
        _al.SetSourceProperty(_flashlightSource, SourceFloat.SecOffset, 0f);
        _al.SourcePlay(_flashlightSource);
    }

    private static string ResolveSoundPath(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "src", "sounds", fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "src", "sounds", fileName);
        }

        return path;
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

        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            if (chunkSize < 0)
            {
                throw new InvalidDataException($"Invalid WAV chunk size ({chunkSize}) in '{chunkId}'.");
            }

            long chunkDataStart = reader.BaseStream.Position;
            long chunkDataEnd = chunkDataStart + chunkSize;
            if (chunkDataEnd > reader.BaseStream.Length)
            {
                throw new InvalidDataException($"Invalid WAV chunk size ({chunkSize}) in '{chunkId}' exceeds stream length.");
            }

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException($"Invalid WAV fmt chunk size ({chunkSize}).");
                }

                ushort audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                if (audioFormat != 1) throw new InvalidDataException("Only PCM WAV is supported.");
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
                if (data.Length != chunkSize)
                {
                    throw new InvalidDataException("Unexpected end of WAV data chunk.");
                }
            }

            reader.BaseStream.Seek(chunkDataEnd, SeekOrigin.Begin);
            if ((chunkSize & 1) == 1)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                {
                    throw new InvalidDataException("Invalid WAV padding byte location.");
                }

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

    public void Dispose()
    {
        if (!_isInitialized) return;
        FreeOpenALResources();
    }
}
