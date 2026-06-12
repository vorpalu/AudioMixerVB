using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMixerVB;

public sealed class MonitorMixEngine : IDisposable
{
    private const int MixSampleRate = 48000;
    private const int MixChannels = 2;
    private const int CaptureBufferMs = 20;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, string> channelInputEndpointIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> channelGains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> channelMutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeterState> channelMeters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitorChannelSampleProvider> activeProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CaptureChannel> captureChannels = [];
    private readonly MeterState masterMeter = new();

    private WasapiOut? output;
    private MasterSampleProvider? masterProvider;
    private string? outputEndpointId;
    private bool disposed;
    private volatile bool isStopping;
    private float masterGain = 1f;
    private bool masterMuted;

    public MonitorMixEngine()
    {
        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            channelGains[channelName] = 0.5f;
            channelMutes[channelName] = false;
            channelMeters[channelName] = new MeterState();
        }
    }

    public event EventHandler<string>? OnLog;

    public event EventHandler<Exception>? OnError;

    public bool IsRunning { get; private set; }

    // Volatile read instead of locking syncRoot: this is checked on every capture
    // packet, and sharing a lock with the UI meter timer stalls the audio threads
    // whenever the UI thread is preempted while holding it.
    public bool IsStopping => isStopping;

    public int LatencyMs { get; set; } = 20;

    public bool UseExclusiveOutput { get; set; }

    public float GetChannelPeak(string channelName)
    {
        lock (syncRoot)
        {
            return channelMeters.TryGetValue(channelName, out var meter) ? meter.GetPeak() : 0f;
        }
    }

    public float GetChannelRms(string channelName)
    {
        lock (syncRoot)
        {
            return channelMeters.TryGetValue(channelName, out var meter) ? meter.GetRms() : 0f;
        }
    }

    public float GetMasterPeak()
    {
        lock (syncRoot)
        {
            return masterMeter.GetPeak();
        }
    }

    public IReadOnlyDictionary<string, float> GetChannelPeaks()
    {
        lock (syncRoot)
        {
            return channelMeters.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.GetPeak(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetOutputDevice(string endpointId)
    {
        lock (syncRoot)
        {
            outputEndpointId = string.IsNullOrWhiteSpace(endpointId) ? null : endpointId;
        }
    }

    public void SetChannelInput(string channelName, string recordingEndpointId)
    {
        lock (syncRoot)
        {
            if (string.IsNullOrWhiteSpace(recordingEndpointId))
            {
                channelInputEndpointIds.Remove(channelName);
            }
            else
            {
                channelInputEndpointIds[channelName] = recordingEndpointId;
            }
        }
    }

    public void SetChannelVolume(string channelName, float volume0to1)
        => SetChannelGain(channelName, volume0to1);

    public void SetChannelGain(string channelName, float gain0to1)
    {
        var gain = Math.Clamp(gain0to1, 0f, 1f);
        lock (syncRoot)
        {
            channelGains[channelName] = gain;
            if (activeProviders.TryGetValue(channelName, out var provider))
            {
                provider.SetGain(gain);
            }
        }
    }

    public void SetMasterGain(float gain0to1)
    {
        var gain = Math.Clamp(gain0to1, 0f, 1f);
        lock (syncRoot)
        {
            masterGain = gain;
            masterProvider?.SetMaster(masterGain, masterMuted);
        }
    }

    public void SetMasterMute(bool muted)
    {
        lock (syncRoot)
        {
            masterMuted = muted;
            masterProvider?.SetMaster(masterGain, masterMuted);
        }
    }

    public void SetChannelMute(string channelName, bool muted)
    {
        lock (syncRoot)
        {
            channelMutes[channelName] = muted;
            if (activeProviders.TryGetValue(channelName, out var provider))
            {
                provider.SetMute(muted);
            }
        }
    }

    public void Start()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (IsRunning)
            {
                return;
            }

            if (isStopping)
            {
                throw new InvalidOperationException("Monitor engine is stopping.");
            }

            if (string.IsNullOrWhiteSpace(outputEndpointId))
            {
                throw new InvalidOperationException("Select a monitor output device before starting.");
            }

            OnLog?.Invoke(this, "Starting monitor engine.");

            try
            {
                StartLocked();
                IsRunning = true;
                OnLog?.Invoke(this, "Started monitor engine.");
            }
            catch
            {
                StopCore();
                throw;
            }
        }
    }

    public void Stop()
        => StopCore();

    public Task StopAsync()
        => Task.Run(StopCore);

    public void Restart()
    {
        Stop();
        Start();
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Start();
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        Stop();
    }

    private void StartLocked()
    {
        var outputDevice = FindDevice(outputEndpointId!, NAudio.CoreAudioApi.DataFlow.Render);
        OnLog?.Invoke(this, $"Monitor output = {outputDevice.FriendlyName}");

        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixSampleRate, MixChannels))
        {
            ReadFully = true
        };

        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            if (!channelInputEndpointIds.TryGetValue(channelName, out var endpointId) ||
                string.IsNullOrWhiteSpace(endpointId))
            {
                OnLog?.Invoke(this, $"Monitor {channelName} input not selected; skipping capture.");
                continue;
            }

            var captureDevice = FindDevice(endpointId, NAudio.CoreAudioApi.DataFlow.Capture);
            var capture = new WasapiCapture(captureDevice, true, CaptureBufferMs);
            var bufferedProvider = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(Math.Max(500, LatencyMs * 4)),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            var captureBytesPerMs = Math.Max(1, capture.WaveFormat.AverageBytesPerSecond / 1000);
            var queueState = new CaptureQueueState(Math.Max(15, LatencyMs));
            var trimBuffer = new byte[capture.WaveFormat.AverageBytesPerSecond / 4];
            var lastTrimLogTicks = 0L;
            var lastDryLogTicks = 0L;
            EventHandler<WaveInEventArgs> dataAvailableHandler = (_, args) =>
            {
                ProAudioThread.Register();
                if (IsStopping)
                {
                    return;
                }

                try
                {
                    var format = bufferedProvider.WaveFormat;
                    queueState.OnPacket(args.BytesRecorded / (double)captureBytesPerMs);

                    // A dry queue means the source just (re)started: idle cables stop
                    // delivering, so the cushion is gone. Pre-fill with silence so playback
                    // does not starve on every packet boundary for the seconds the servo
                    // would need to rebuild the cushion at its capped rate.
                    if (bufferedProvider.BufferedBytes == 0)
                    {
                        var prefillBytes = Math.Min(
                            (int)(queueState.EffectiveTargetMs * captureBytesPerMs),
                            trimBuffer.Length);
                        prefillBytes -= prefillBytes % format.BlockAlign;
                        if (prefillBytes > 0)
                        {
                            Array.Clear(trimBuffer, 0, prefillBytes);
                            bufferedProvider.AddSamples(trimBuffer, 0, prefillBytes);

                            var dryNow = Environment.TickCount64;
                            if (dryNow - lastDryLogTicks >= 5000)
                            {
                                lastDryLogTicks = dryNow;
                                OnLog?.Invoke(this, $"Monitor {channelName} queue ran dry; inserted {prefillBytes / captureBytesPerMs} ms cushion.");
                            }
                        }
                    }

                    bufferedProvider.AddSamples(args.Buffer, 0, args.BytesRecorded);

                    // Ordinary clock drift is corrected inaudibly by the servo resampler;
                    // this skip-ahead only recovers from real stalls that leave a large
                    // burst queued. The backlog must be re-read on every pass: the playback
                    // thread drains this buffer concurrently, and with ReadFully enabled
                    // Read never reports starvation, so a stale excess would over-trim.
                    var targetBytes = (int)(queueState.EffectiveTargetMs * captureBytesPerMs);
                    var thresholdBytes = targetBytes + captureBytesPerMs * 60;
                    if (bufferedProvider.BufferedBytes > thresholdBytes)
                    {
                        var droppedBytes = 0;
                        while (true)
                        {
                            var excessBytes = bufferedProvider.BufferedBytes - targetBytes;
                            var chunk = Math.Min(excessBytes, trimBuffer.Length);
                            chunk -= chunk % format.BlockAlign;
                            if (chunk <= 0)
                            {
                                break;
                            }

                            droppedBytes += bufferedProvider.Read(trimBuffer, 0, chunk);
                        }

                        var now = Environment.TickCount64;
                        if (droppedBytes > 0 && now - lastTrimLogTicks >= 5000)
                        {
                            lastTrimLogTicks = now;
                            OnLog?.Invoke(this, $"Monitor {channelName} stall recovery: dropped {droppedBytes / captureBytesPerMs} ms of queued audio.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, ex);
                    OnLog?.Invoke(this, $"Capture error for {channelName}: {ex.Message}");
                }
            };
            EventHandler<StoppedEventArgs> recordingStoppedHandler = (_, args) =>
            {
                if (IsStopping)
                {
                    return;
                }

                if (args.Exception is not null)
                {
                    OnError?.Invoke(this, args.Exception);
                    OnLog?.Invoke(this, $"Capture stopped with error for {channelName}: {args.Exception.Message}");
                }
            };
            capture.DataAvailable += dataAvailableHandler;
            capture.RecordingStopped += recordingStoppedHandler;

            ISampleProvider sampleProvider = bufferedProvider.ToSampleProvider();
            sampleProvider = EnsureStereo(sampleProvider);

            // The servo resampler keeps the queue at the target depth by nudging the
            // playback rate, so per-cable clock drift never accumulates into trims.
            sampleProvider = new DriftCompensatingSampleProvider(
                sampleProvider,
                MixSampleRate,
                () => (double)bufferedProvider.BufferedBytes / captureBytesPerMs,
                () => queueState.EffectiveTargetMs,
                queueState.OnRead,
                $"Monitor {channelName}",
                message => OnLog?.Invoke(this, message));

            // Resolve the meter once so the playback thread updates it directly and
            // never touches syncRoot, which the UI meter timer locks ~25x per second.
            var channelMeter = GetOrCreateMeter(channelName);
            var gainProvider = new MonitorChannelSampleProvider(
                channelName,
                sampleProvider,
                channelGains.GetValueOrDefault(channelName, 0.5f),
                channelMutes.GetValueOrDefault(channelName),
                message => OnLog?.Invoke(this, message),
                channelMeter.Update);

            activeProviders[channelName] = gainProvider;
            mixer.AddMixerInput(gainProvider);
            captureChannels.Add(new CaptureChannel(
                channelName,
                capture,
                bufferedProvider,
                dataAvailableHandler,
                recordingStoppedHandler));
            OnLog?.Invoke(this, $"Started capture for {channelName}: {captureDevice.FriendlyName}");
        }

        if (captureChannels.Count == 0)
        {
            throw new InvalidOperationException("Select at least one monitor input before starting.");
        }

        masterProvider = new MasterSampleProvider(
            mixer,
            masterGain,
            masterMuted,
            masterMeter.Update,
            LatencyMs,
            message => OnLog?.Invoke(this, message));
        output = CreateOutput(outputDevice, masterProvider);

        foreach (var channel in captureChannels)
        {
            channel.Capture.StartRecording();
        }

        output.Play();
        OnLog?.Invoke(this, "Started monitor output.");
    }

    private WasapiOut CreateOutput(MMDevice outputDevice, MasterSampleProvider master)
    {
        if (UseExclusiveOutput)
        {
            WasapiOut? exclusive = null;
            try
            {
                // Exclusive mode bypasses the shared Windows audio engine path entirely.
                // Most devices only accept integer PCM there, so feed 16-bit samples.
                exclusive = new WasapiOut(outputDevice, AudioClientShareMode.Exclusive, true, LatencyMs);
                exclusive.Init(new SampleToWaveProvider16(master));
                OnLog?.Invoke(this, "Monitor output running in WASAPI exclusive mode (16-bit PCM).");
                return exclusive;
            }
            catch (Exception ex)
            {
                exclusive?.Dispose();
                OnLog?.Invoke(this, $"Exclusive mode unavailable ({ex.Message}); falling back to shared mode.");
            }
        }

        var shared = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, LatencyMs);
        shared.Init(master.ToWaveProvider());
        return shared;
    }

    private void StopCore()
    {
        List<CaptureChannel> capturesToStop;
        WasapiOut? outputToStop;

        OnLog?.Invoke(this, "Monitor Stop requested.");

        lock (syncRoot)
        {
            if (isStopping)
            {
                OnLog?.Invoke(this, "Monitor stop ignored; stop already in progress.");
                return;
            }

            if (!IsRunning && output is null && captureChannels.Count == 0)
            {
                return;
            }

            isStopping = true;
            capturesToStop = captureChannels.ToList();
            outputToStop = output;
            captureChannels.Clear();
            activeProviders.Clear();
            output = null;
            masterProvider = null;
            IsRunning = false;
        }

        OnLog?.Invoke(this, "Monitor stopping captures...");

        foreach (var channel in capturesToStop)
        {
            channel.DetachHandlers();

            try
            {
                channel.Capture.StopRecording();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
                OnLog?.Invoke(this, $"Monitor stop error: {ex.Message}");
            }

            try
            {
                channel.Dispose();
                OnLog?.Invoke(this, $"Monitor capture disposed: {channel.ChannelName}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
                OnLog?.Invoke(this, $"Monitor stop error: {ex.Message}");
            }
        }

        OnLog?.Invoke(this, "Monitor stopping output...");

        try
        {
            outputToStop?.Stop();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            OnLog?.Invoke(this, $"Monitor stop error: {ex.Message}");
        }

        try
        {
            outputToStop?.Dispose();
            if (outputToStop is not null)
            {
                OnLog?.Invoke(this, "Monitor output disposed.");
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            OnLog?.Invoke(this, $"Monitor stop error: {ex.Message}");
        }

        lock (syncRoot)
        {
            ResetMeters();
            isStopping = false;
        }

        OnLog?.Invoke(this, "Monitor stopped.");
    }

    private static MMDevice FindDevice(string endpointId, NAudio.CoreAudioApi.DataFlow dataFlow)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(dataFlow, DeviceState.Active)
            .FirstOrDefault(device => device.ID.Equals(endpointId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Audio endpoint not found: {endpointId}");
    }

    private static ISampleProvider EnsureStereo(ISampleProvider source)
    {
        return source.WaveFormat.Channels == MixChannels
            ? source
            : new StereoAdapterSampleProvider(source);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MonitorMixEngine));
        }
    }

    private MeterState GetOrCreateMeter(string channelName)
    {
        if (!channelMeters.TryGetValue(channelName, out var meter))
        {
            meter = new MeterState();
            channelMeters[channelName] = meter;
        }

        return meter;
    }

    private void ResetMeters()
    {
        foreach (var meter in channelMeters.Values)
        {
            meter.Reset();
        }

        masterMeter.Reset();
    }

    private sealed class CaptureChannel(
        string channelName,
        WasapiCapture capture,
        BufferedWaveProvider bufferedProvider,
        EventHandler<WaveInEventArgs> dataAvailableHandler,
        EventHandler<StoppedEventArgs> recordingStoppedHandler) : IDisposable
    {
        public string ChannelName { get; } = channelName;

        public WasapiCapture Capture { get; } = capture;

        public BufferedWaveProvider BufferedProvider { get; } = bufferedProvider;

        public void DetachHandlers()
        {
            Capture.DataAvailable -= dataAvailableHandler;
            Capture.RecordingStopped -= recordingStoppedHandler;
        }

        public void Dispose()
        {
            Capture.Dispose();
        }
    }

    private sealed class MeterState
    {
        private readonly object syncRoot = new();
        private float smoothedPeak;
        private float smoothedRms;
        private long lastUpdateTicks = Environment.TickCount64;

        public void Update(float peak, float rms)
        {
            lock (syncRoot)
            {
                DecayToNow();
                smoothedPeak = Math.Max(Math.Clamp(peak, 0f, 1f), smoothedPeak);
                smoothedRms = Math.Max(Math.Clamp(rms, 0f, 1f), smoothedRms);
            }
        }

        public float GetPeak()
        {
            lock (syncRoot)
            {
                DecayToNow();
                return smoothedPeak;
            }
        }

        public float GetRms()
        {
            lock (syncRoot)
            {
                DecayToNow();
                return smoothedRms;
            }
        }

        public void Reset()
        {
            lock (syncRoot)
            {
                smoothedPeak = 0f;
                smoothedRms = 0f;
                lastUpdateTicks = Environment.TickCount64;
            }
        }

        private void DecayToNow()
        {
            var now = Environment.TickCount64;
            var elapsedMs = Math.Max(0, now - lastUpdateTicks);
            if (elapsedMs == 0)
            {
                return;
            }

            var decay = (float)Math.Pow(0.90, elapsedMs / 50.0);
            smoothedPeak = smoothedPeak < 0.001f ? 0f : smoothedPeak * decay;
            smoothedRms = smoothedRms < 0.001f ? 0f : smoothedRms * decay;
            lastUpdateTicks = now;
        }
    }

    private sealed class MonitorChannelSampleProvider : ISampleProvider
    {
        private readonly string channelName;
        private readonly ISampleProvider source;
        private readonly Action<string> log;
        private readonly Action<float, float> updateMeter;
        private readonly object gainLock = new();
        private long lastUnderrunLogTicks;
        private float gain;
        private bool muted;

        public MonitorChannelSampleProvider(
            string channelName,
            ISampleProvider source,
            float gain,
            bool muted,
            Action<string> log,
            Action<float, float> updateMeter)
        {
            this.channelName = channelName;
            this.source = source;
            this.gain = Math.Clamp(gain, 0f, 1f);
            this.muted = muted;
            this.log = log;
            this.updateMeter = updateMeter;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public void SetGain(float value)
        {
            lock (gainLock)
            {
                gain = Math.Clamp(value, 0f, 1f);
            }
        }

        public void SetMute(bool value)
        {
            lock (gainLock)
            {
                muted = value;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            if (read == 0)
            {
                MaybeLogUnderrun();
                Array.Clear(buffer, offset, count);
                updateMeter(0f, 0f);
                return count;
            }

            float currentGain;
            bool currentlyMuted;
            lock (gainLock)
            {
                currentGain = gain;
                currentlyMuted = muted;
            }

            var scalar = currentlyMuted ? 0f : currentGain;
            var peak = 0f;
            double squareSum = 0.0;
            for (var index = 0; index < read; index++)
            {
                var sample = buffer[offset + index] * scalar;
                buffer[offset + index] = sample;

                var absoluteSample = Math.Abs(sample);
                if (absoluteSample > peak)
                {
                    peak = absoluteSample;
                }

                squareSum += sample * sample;
            }

            updateMeter(
                Math.Clamp(peak, 0f, 1f),
                read == 0 ? 0f : Math.Clamp((float)Math.Sqrt(squareSum / read), 0f, 1f));

            if (read < count)
            {
                MaybeLogUnderrun();
                Array.Clear(buffer, offset + read, count - read);
                return count;
            }

            return read;
        }

        private void MaybeLogUnderrun()
        {
            var now = Environment.TickCount64;
            if (now - lastUnderrunLogTicks < 5000)
            {
                return;
            }

            lastUnderrunLogTicks = now;
            log($"Buffer underrun on {channelName}; using silence.");
        }
    }

    private sealed class MasterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly Action<float, float> updateMeter;
        private readonly Action<string> log;
        private readonly int stallThresholdMs;
        private readonly object masterLock = new();
        private long lastReadTicks;
        private long lastStallLogTicks;
        private float gain;
        private bool muted;

        public MasterSampleProvider(
            ISampleProvider source,
            float gain,
            bool muted,
            Action<float, float> updateMeter,
            int expectedReadIntervalMs,
            Action<string> log)
        {
            this.source = source;
            this.gain = Math.Clamp(gain, 0f, 1f);
            this.muted = muted;
            this.updateMeter = updateMeter;
            this.log = log;
            stallThresholdMs = expectedReadIntervalMs * 2 + 10;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public void SetMaster(float value, bool isMuted)
        {
            lock (masterLock)
            {
                gain = Math.Clamp(value, 0f, 1f);
                muted = isMuted;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            ProAudioThread.Register();

            // Long gaps between reads mean the playback thread itself stalled
            // (GC, driver, scheduler) - the one failure mode queue logs can't see.
            var now = Environment.TickCount64;
            if (lastReadTicks != 0)
            {
                var gapMs = now - lastReadTicks;
                if (gapMs > stallThresholdMs && now - lastStallLogTicks >= 5000)
                {
                    lastStallLogTicks = now;
                    log($"Monitor output read gap of {gapMs} ms; the playback thread stalled.");
                }
            }

            lastReadTicks = now;
            var read = source.Read(buffer, offset, count);

            float currentGain;
            bool currentlyMuted;
            lock (masterLock)
            {
                currentGain = gain;
                currentlyMuted = muted;
            }

            var scalar = currentlyMuted ? 0f : currentGain;
            var peak = 0f;
            double squareSum = 0.0;
            for (var index = 0; index < read; index++)
            {
                var sample = buffer[offset + index] * scalar;
                buffer[offset + index] = sample;

                var absoluteSample = Math.Abs(sample);
                if (absoluteSample > peak)
                {
                    peak = absoluteSample;
                }

                squareSum += sample * sample;
            }

            updateMeter(
                Math.Clamp(peak, 0f, 1f),
                read == 0 ? 0f : Math.Clamp((float)Math.Sqrt(squareSum / read), 0f, 1f));

            if (read < count)
            {
                Array.Clear(buffer, offset + read, count - read);
                return count;
            }

            return read;
        }
    }

    private sealed class StereoAdapterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private float[] sourceBuffer;

        public StereoAdapterSampleProvider(ISampleProvider source)
        {
            this.source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, MixChannels);
            sourceBuffer = new float[4096 * Math.Max(1, source.WaveFormat.Channels)];
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var outputFrames = count / MixChannels;
            var sourceChannels = source.WaveFormat.Channels;
            var sourceSamplesNeeded = outputFrames * sourceChannels;

            if (sourceBuffer.Length < sourceSamplesNeeded)
            {
                sourceBuffer = new float[sourceSamplesNeeded];
            }

            var read = source.Read(sourceBuffer, 0, sourceSamplesNeeded);
            var framesRead = read / sourceChannels;
            for (var frame = 0; frame < framesRead; frame++)
            {
                var sourceOffset = frame * sourceChannels;
                var left = sourceBuffer[sourceOffset];
                var right = sourceChannels > 1 ? sourceBuffer[sourceOffset + 1] : left;
                var targetOffset = offset + frame * MixChannels;
                buffer[targetOffset] = left;
                buffer[targetOffset + 1] = right;
            }

            return framesRead * MixChannels;
        }
    }
}
