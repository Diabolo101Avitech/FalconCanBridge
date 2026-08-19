using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>
/// Thin wrapper around the Win32 named memory-mapped file that Falcon 4 BMS publishes while
/// running ("FalconSharedMemoryArea"). BMS (and the standalone "Falcon4TexExp"/export tools
/// before it) creates this mapping only while a mission is active, so opening it can fail
/// simply because BMS isn't running yet or no flight is loaded - callers should treat that as
/// "not connected yet" and keep retrying rather than as a fatal error.
/// </summary>
public sealed class Falcon4SharedMemoryReader : IDisposable
{
    public const string MemoryMappedFileName = "FalconSharedMemoryArea";

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;

    public bool IsOpen => _accessor is not null;

    /// <summary>Actual size in bytes of the opened mapping, once known.</summary>
    public long Capacity => _accessor?.Capacity ?? 0;

    /// <summary>Attempts to open the mapping. Returns false (without throwing) if BMS is not currently running a mission.</summary>
    public bool TryOpen()
    {
        Close();
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MemoryMappedFileName, MemoryMappedFileRights.Read);
            // size 0 => map the entire underlying section so we learn its real capacity.
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return true;
        }
        catch (FileNotFoundException)
        {
            // Expected when BMS isn't running / hasn't started a flight yet.
            Close();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Close();
            return false;
        }
    }

    /// <summary>Copies up to <paramref name="destination"/>.Length bytes from the mapping. Returns bytes actually copied.</summary>
    public int ReadSnapshot(byte[] destination)
    {
        if (_accessor is null) return 0;

        try
        {
            int count = (int)Math.Min(destination.Length, _accessor.Capacity);
            _accessor.ReadArray(0, destination, 0, count);
            return count;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    public void Close()
    {
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
    }

    public void Dispose() => Close();
}
