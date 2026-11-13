using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GuitarBassTuner.Audio;

internal enum CaptureStatus { Searching, Disconnected, Listening, Unavailable }

internal sealed record CaptureSnapshot(
    CaptureStatus Status, PitchReading? Reading = null, int SampleRate = 0, int Channels = 0, int ErrorCode = 0)
{
    public InputSelection? Selection { get; init; }
}

internal sealed class InputSelection(CaptureEndpoint endpoint)
{
    public CaptureEndpoint Endpoint { get; } = endpoint;
}

internal sealed class AudioInput : IDisposable
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);
    private readonly AutoResetEvent selectionChanged = new(false);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private CaptureSnapshot snapshot = new(CaptureStatus.Searching);
    private CaptureEndpoints? endpoints;
    private InputSelection? selected;
    private bool disposed;

    public AudioInput()
    {
        worker = Task.Factory.StartNew(
            () => Listen(cancellation.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public CaptureSnapshot Snapshot => Volatile.Read(ref snapshot);

    public CaptureEndpoints? Endpoints => Volatile.Read(ref endpoints);

    // Called by the UI thread. The capture thread releases its current stream before opening the next one.
    public void Select(InputSelection? selection)
    {
        Volatile.Write(ref selected, selection);
        selectionChanged.Set();
    }

    private void Listen(CancellationToken stop)
    {
        WaitHandle[] idleEvents = [stop.WaitHandle, selectionChanged];
        while (!stop.IsCancellationRequested)
        {
            RefreshEndpoints();
            selectionChanged.Reset();
            InputSelection? requested = Volatile.Read(ref selected);
            if (requested is not null)
                ListenTo(requested, stop);

            if (ReferenceEquals(Volatile.Read(ref selected), requested))
                WaitHandle.WaitAny(idleEvents, 1200);
        }
    }

    private void ListenTo(InputSelection selection, CancellationToken stop)
    {
        Wasapi.IMMDevice? device = null;
        Volatile.Write(ref snapshot, new(CaptureStatus.Searching) { Selection = selection });
        try
        {
            device = Wasapi.OpenCaptureEndpoint(selection.Endpoint.Id);
            if (device is null)
                Volatile.Write(ref snapshot, new(CaptureStatus.Disconnected) { Selection = selection });
            else
                Capture(device, selection, stop);
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or NotSupportedException)
        {
            CaptureStatus status = error.HResult is DeviceInvalidated or Wasapi.DeviceNotFound
                ? CaptureStatus.Disconnected
                : CaptureStatus.Unavailable;
            Volatile.Write(ref snapshot, new(status, ErrorCode: error.HResult) { Selection = selection });
        }
        finally
        {
            Wasapi.Release(device);
        }
    }

    private void RefreshEndpoints()
    {
        CaptureEndpoints refreshed;
        try
        {
            refreshed = Wasapi.ListCaptureEndpoints();
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException)
        {
            refreshed = (Endpoints ?? new([], null)) with { ErrorCode = error.HResult };
        }

        CaptureEndpoints? previous = Endpoints;
        if (previous is null || previous.DefaultId != refreshed.DefaultId || previous.ErrorCode != refreshed.ErrorCode ||
            !previous.Devices.SequenceEqual(refreshed.Devices))
        {
            Volatile.Write(ref endpoints, refreshed);
        }
    }

    private void Capture(Wasapi.IMMDevice device, InputSelection selection, CancellationToken stop)
    {
        using var ready = new AutoResetEvent(false);
        Wasapi.IAudioClient? client = null;
        Wasapi.IAudioCaptureClient? capture = null;
        IntPtr formatPointer = IntPtr.Zero;
        bool started = false;
        try
        {
            client = Wasapi.Activate(device);
            Wasapi.Check(client.GetMixFormat(out formatPointer));
            WaveFormat format = WaveFormat.Read(formatPointer);
            Wasapi.Check(client.Initialize(0, Wasapi.EventCallback, 0, 0, formatPointer, IntPtr.Zero));
            Wasapi.Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            Wasapi.Check(client.GetBufferSize(out uint capacity));
            capture = Wasapi.GetCaptureClient(client);

            var tracker = new PitchTracker(format.SampleRate);
            var mono = new float[capacity];
            var state = new CaptureSnapshot(CaptureStatus.Listening, SampleRate: format.SampleRate, Channels: format.Channels)
            {
                Selection = selection
            };
            long lastDeviceScan = Stopwatch.GetTimestamp();
            WaitHandle[] events = [stop.WaitHandle, selectionChanged, ready];
            Wasapi.Check(client.Start());
            started = true;
            Volatile.Write(ref snapshot, state);
            while (!stop.IsCancellationRequested && ReferenceEquals(Volatile.Read(ref selected), selection))
            {
                if (WaitHandle.WaitAny(events, 250) == 0 ||
                    !ReferenceEquals(Volatile.Read(ref selected), selection))
                    break;

                Wasapi.Check(capture.GetNextPacketSize(out uint remaining));
                while (remaining > 0 && !stop.IsCancellationRequested && ReferenceEquals(Volatile.Read(ref selected), selection))
                {
                    Wasapi.Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    if (frames == 0)
                        break;

                    Span<float> samples = default;
                    try
                    {
                        samples = mono.AsSpan(0, checked((int)frames));
                        if ((flags & Wasapi.SilentBuffer) != 0)
                            samples.Clear();
                        else
                            format.DecodeMono(data, samples);
                    }
                    finally
                    {
                        Wasapi.Check(capture.ReleaseBuffer(frames));
                    }

                    if ((flags & Wasapi.DataDiscontinuity) != 0)
                        tracker.Reset();

                    if (tracker.AddSamples(samples, Stopwatch.GetTimestamp()) is PitchReading reading)
                        Volatile.Write(ref snapshot, state with { Reading = reading });

                    Wasapi.Check(capture.GetNextPacketSize(out remaining));
                }

                if (Stopwatch.GetElapsedTime(lastDeviceScan).TotalSeconds >= 1.2)
                {
                    RefreshEndpoints();
                    lastDeviceScan = Stopwatch.GetTimestamp();
                }
            }
        }
        finally
        {
            if (started)
                client!.Stop();
            Marshal.FreeCoTaskMem(formatPointer);
            Wasapi.Release(capture);
            Wasapi.Release(client);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        cancellation.Cancel();
        _ = worker.ContinueWith(task =>
        {
            _ = task.Exception;
            selectionChanged.Dispose();
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }
}
