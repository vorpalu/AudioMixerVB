using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMixerVB;

public sealed class StreamMixEngine : IDisposable
{
    private const int MixSampleRate = 48000;
    private const int MixChannels = 2;
    private const int CaptureBufferMs = 20;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, string> channelInputEndpointIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> channelGains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> channelMutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeterState> channelMeters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StreamChannelSampleProvider> activeProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CaptureChannel> captureChannels = [];
    private readonly MeterState masterMeter = new();

    private WasapiOut? output;
    private MasterSampleProvider? masterProvider;
    private string? outputEndpointId;
    private bool disposed;
    private bool isStopping;
    private float masterGain = 1f;
    private bool masterMuted;

    public StreamMixEngine()
    {
        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            channelGains[channelName] = 1.0f;
            channelMutes[channelName] = false;
            channelMeters[channelName] = new MeterState();
        }
    }

    public event EventHandler<string>? OnLog;

    public event EventHandler<Exception>? OnError;

    public bool IsRunning { get; private set; }

    public bool IsStopping
    {
        get
        {
            lock (syncRoot)
            {
                return isStopping;
            }
        }
    }

    public int LatencyMs { get; set; } = 20;

    public float GetChannelPeak(string channelName)
    {
        lock (syncRoot)
        {
            return channelMeters.TryGetValue(channelName, out var meter) ? meter.GetPeak() : 0f;
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

    public void SetChannelInput(string channelName, string captureEndpointId)
    {
        lock (syncRoot)
        {
            if (string.IsNullOrWhiteSpace(captureEndpointId))
            {
                channelInputEndpointIds.Remove(channelName);
            }
            else
            {
                channelInputEndpointIds[channelName] = captureEndpointId;
            }
        }
    }

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
                throw new InvalidOperationException("Stream mix engine is stopping.");
            }

            if (string.IsNullOrWhiteSpace(outputEndpointId))
            {
                throw new InvalidOperationException("Select a stream output device before starting.");
            }

            OnLog?.Invoke(this, "Starting stream mix engine.");

            try
            {
                StartLocked();
                IsRunning = true;
                OnLog?.Invoke(this, "Started stream mix engine.");
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
        OnLog?.Invoke(this, $"Stream output = {outputDevice.FriendlyName}");

        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(MixSampleRate, MixChannels))
        {
            ReadFully = true
        };

        foreach (var channelName in MixerChannel.DefaultChannelNames)
        {
            if (!channelInputEndpointIds.TryGetValue(channelName, out var endpointId) ||
                string.IsNullOrWhiteSpace(endpointId))
            {
                OnLog?.Invoke(this, $"Stream {channelName} input not selected; skipping capture.");
                continue;
            }

            var captureDevice = FindDevice(endpointId, NAudio.CoreAudioApi.DataFlow.Capture);
            var capture = new WasapiCapture(captureDevice, true, CaptureBufferMs);
            var bufferedProvider = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(Math.Max(200, LatencyMs * 4)),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            var trimBuffer = new byte[capture.WaveFormat.AverageBytesPerSecond / 4];
            var lastTrimLogTicks = 0L;
            EventHandler<WaveInEventArgs> dataAvailableHandler = (_, args) =>
            {
                if (IsStopping)
                {
                    return;
                }

                try
                {
                    bufferedProvider.AddSamples(args.Buffer, 0, args.BytesRecorded);

                    // Capture and render run on different device clocks; when capture drifts
                    // ahead the queue grows and so does latency. Skip ahead to keep it bounded.
                    var format = bufferedProvider.WaveFormat;
                    var maxBacklogBytes = (long)format.AverageBytesPerSecond * Math.Max(50, LatencyMs * 2) / 1000;
                    if (bufferedProvider.BufferedBytes > maxBacklogBytes)
                    {
                        var targetBytes = (long)format.AverageBytesPerSecond * Math.Max(20, LatencyMs) / 1000;
                        var excessBytes = bufferedProvider.BufferedBytes - (int)targetBytes;
                        excessBytes -= excessBytes % format.BlockAlign;
                        var droppedBytes = 0;
                        while (excessBytes > 0)
                        {
                            var chunk = Math.Min(excessBytes, trimBuffer.Length);
                            chunk -= chunk % format.BlockAlign;
                            var read = bufferedProvider.Read(trimBuffer, 0, chunk);
                            if (read <= 0)
                            {
                                break;
                            }

                            droppedBytes += read;
                            excessBytes -= read;
                        }

                        var now = Environment.TickCount64;
                        if (droppedBytes > 0 && now - lastTrimLogTicks >= 5000)
                        {
                            lastTrimLogTicks = now;
                            var droppedMs = droppedBytes * 1000L / format.AverageBytesPerSecond;
                            OnLog?.Invoke(this, $"Stream {channelName} backlog trimmed by {droppedMs} ms to limit latency drift.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, ex);
                    OnLog?.Invoke(this, $"Stream capture error for {channelName}: {ex.Message}");
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
                    OnLog?.Invoke(this, $"Stream capture stopped with error for {channelName}: {args.Exception.Message}");
                }
            };
            capture.DataAvailable += dataAvailableHandler;
            capture.RecordingStopped += recordingStoppedHandler;

            ISampleProvider sampleProvider = bufferedProvider.ToSampleProvider();
            sampleProvider = EnsureStereo(sampleProvider);
            if (sampleProvider.WaveFormat.SampleRate != MixSampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, MixSampleRate);
            }

            var gainProvider = new StreamChannelSampleProvider(
                channelName,
                sampleProvider,
                channelGains.GetValueOrDefault(channelName, 1.0f),
                channelMutes.GetValueOrDefault(channelName),
                message => OnLog?.Invoke(this, message),
                (peak, rms) => UpdateChannelMeter(channelName, peak, rms));

            activeProviders[channelName] = gainProvider;
            mixer.AddMixerInput(gainProvider);
            captureChannels.Add(new CaptureChannel(
                channelName,
                capture,
                bufferedProvider,
                dataAvailableHandler,
                recordingStoppedHandler));
            OnLog?.Invoke(this, $"Started stream capture for {channelName}: {captureDevice.FriendlyName}");
        }

        if (captureChannels.Count == 0)
        {
            throw new InvalidOperationException("Select at least one stream input before starting.");
        }

        masterProvider = new MasterSampleProvider(mixer, masterGain, masterMuted, UpdateMasterMeter);
        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, LatencyMs);
        output.Init(masterProvider.ToWaveProvider());

        foreach (var channel in captureChannels)
        {
            channel.Capture.StartRecording();
        }

        output.Play();
        OnLog?.Invoke(this, "Started stream output.");
    }

    private void StopCore()
    {
        List<CaptureChannel> capturesToStop;
        WasapiOut? outputToStop;

        OnLog?.Invoke(this, "Stream Stop requested.");

        lock (syncRoot)
        {
            if (isStopping)
            {
                OnLog?.Invoke(this, "Stream stop ignored; stop already in progress.");
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

        OnLog?.Invoke(this, "Stream stopping captures...");

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
                OnLog?.Invoke(this, $"Stream stop error: {ex.Message}");
            }

            try
            {
                channel.Dispose();
                OnLog?.Invoke(this, $"Stream capture disposed: {channel.ChannelName}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
                OnLog?.Invoke(this, $"Stream stop error: {ex.Message}");
            }
        }

        OnLog?.Invoke(this, "Stream stopping output...");

        try
        {
            outputToStop?.Stop();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            OnLog?.Invoke(this, $"Stream stop error: {ex.Message}");
        }

        try
        {
            outputToStop?.Dispose();
            if (outputToStop is not null)
            {
                OnLog?.Invoke(this, "Stream output disposed.");
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(this, ex);
            OnLog?.Invoke(this, $"Stream stop error: {ex.Message}");
        }

        lock (syncRoot)
        {
            ResetMeters();
            isStopping = false;
        }

        OnLog?.Invoke(this, "Stream stopped.");
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
            throw new ObjectDisposedException(nameof(StreamMixEngine));
        }
    }

    private void UpdateChannelMeter(string channelName, float peak, float rms)
    {
        lock (syncRoot)
        {
            if (!channelMeters.TryGetValue(channelName, out var meter))
            {
                meter = new MeterState();
                channelMeters[channelName] = meter;
            }

            meter.Update(peak, rms);
        }
    }

    private void UpdateMasterMeter(float peak, float rms)
    {
        lock (syncRoot)
        {
            masterMeter.Update(peak, rms);
        }
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

    private sealed class StreamChannelSampleProvider : ISampleProvider
    {
        private readonly string channelName;
        private readonly ISampleProvider source;
        private readonly Action<string> log;
        private readonly Action<float, float> updateMeter;
        private readonly object gainLock = new();
        private long lastUnderrunLogTicks;
        private float gain;
        private bool muted;

        public StreamChannelSampleProvider(
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
            log($"Stream buffer underrun on {channelName}; using silence.");
        }
    }

    private sealed class MasterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly Action<float, float> updateMeter;
        private readonly object masterLock = new();
        private float gain;
        private bool muted;

        public MasterSampleProvider(
            ISampleProvider source,
            float gain,
            bool muted,
            Action<float, float> updateMeter)
        {
            this.source = source;
            this.gain = Math.Clamp(gain, 0f, 1f);
            this.muted = muted;
            this.updateMeter = updateMeter;
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
