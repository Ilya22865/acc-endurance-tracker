using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace AccEndurance.Agent;

[SupportedOSPlatform("windows")]
public sealed class AccSharedMemoryReader : IAccDataSource, IDisposable
{
    private MemoryMappedFile? _physics;
    private MemoryMappedViewAccessor? _view;
    private readonly object _lock = new();

    private const int PacketIdOffset = 0;
    private const int GasOffset = 4;
    private const int BrakeOffset = 8;
    private const int SteerAngleOffset = 24;

    public event Action<TelemetryReading>? OnSample;

    public void Start()
    {
        AttachPhysics();
    }

    private void AttachPhysics()
    {
        try
        {
            _physics = MemoryMappedFile.OpenExisting(@"Local\acpmf_physics");
            _view = _physics.CreateViewAccessor();
        }
        catch (FileNotFoundException)
        {
            _physics = null;
            _view = null;
        }
    }

    public bool TryReadSample(out TelemetryReading sample)
    {
        sample = default;
        var view = _view;
        if (view == null)
        {
            AttachPhysics();
            if (view == null) _view = null;
            view = _view;
            if (view == null) return false;
        }

        lock (_lock)
        {
            try
            {
                var packetId = view.ReadInt32(PacketIdOffset);
                if (packetId == 0) return false;

                sample = new TelemetryReading(
                    packetId,
                    view.ReadSingle(GasOffset),
                    view.ReadSingle(BrakeOffset),
                    view.ReadSingle(SteerAngleOffset));
                return true;
            }
            catch (AccessViolationException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        _view?.Dispose();
        _physics?.Dispose();
        _view = null;
        _physics = null;
    }
}