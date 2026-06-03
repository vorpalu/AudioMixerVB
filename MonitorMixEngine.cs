using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioMixerVB;

public sealed class MonitorMixEngine : IDisposable
{
    private const int MixSampleRate = 48000;
    private const int MixChannels = 2;
    private readonly object syncRoot = new();
    private readonly Dictionary<string, string> channelInputEndpointIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> channelGains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> channelMutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeterState> channelMeters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitorChannelSampleProvider> activeProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CaptureChannel> captureChannels = [];

    private WasapiOut? output;
    private string? outputEndpointId;
    private bool disposed;

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

    public int LatencyMs { get; set; } = 60;

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
            return Math.Clamp(channelMeters.Values.Sum(meter => meter.GetPeak()), 0f, 1f);
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
    {
        var gain = Math.Clamp(volume0to1, 0f, 1f);
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

    public void Start()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (IsRunning)
            {
                return;
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
                StopLocked();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            StopLocked();
        }
    }

    public void Restart()
    {
        lock (syncRoot)
        {
            StopLocked();
            Start();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            StopLocked();
            disposed = true;
        }
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
            var capture = new WasapiCapture(captureDevice);
            var bufferedProvider = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(Math.Max(500, LatencyMs * 12)),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            capture.DataAvailable += (_, args) =>
            {
                try
                {
                    bufferedProvider.AddSamples(args.Buffer, 0, args.BytesRecorded);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, ex);
                    OnLog?.Invoke(this, $"Capture error for {channelName}: {ex.Message}");
                }
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    OnError?.Invoke(this, args.Exception);
                    OnLog?.Invoke(this, $"Capture stopped with error for {channelName}: {args.Exception.Message}");
                }
            };

            ISampleProvider sampleProvider = bufferedProvider.ToSampleProvider();
            sampleProvider = EnsureStereo(sampleProvider);
            if (sampleProvider.WaveFormat.SampleRate != MixSampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, MixSampleRate);
            }

            var gainProvider = new MonitorChannelSampleProvider(
                channelName,
                sampleProvider,
                channelGains.GetValueOrDefault(channelName, 0.5f),
                channelMutes.GetValueOrDefault(channelName),
                message => OnLog?.Invoke(this, message),
                (peak, rms) => UpdateChannelMeter(channelName, peak, rms));

            activeProviders[channelName] = gainProvider;
            mixer.AddMixerInput(gainProvider);
            captureChannels.Add(new CaptureChannel(channelName, capture, bufferedProvider));
            OnLog?.Invoke(this, $"Started capture for {channelName}: {captureDevice.FriendlyName}");
        }

        if (captureChannels.Count == 0)
        {
            throw new InvalidOperationException("Select at least one monitor input before starting.");
        }

        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, LatencyMs);
        output.Init(mixer.ToWaveProvider());

        foreach (var channel in captureChannels)
        {
            channel.Capture.StartRecording();
        }

        output.Play();
        OnLog?.Invoke(this, "Started monitor output.");
    }

    private void StopLocked()
    {
        if (!IsRunning && output is null && captureChannels.Count == 0)
        {
            return;
        }

        OnLog?.Invoke(this, "Stopping monitor engine.");

        foreach (var channel in captureChannels.ToList())
        {
            try
            {
                channel.Capture.StopRecording();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(this, $"Stop capture error for {channel.ChannelName}: {ex.Message}");
            }

            channel.Dispose();
        }

        captureChannels.Clear();
        activeProviders.Clear();
        ResetMeters();

        try
        {
            output?.Stop();
        }
        catch (Exception ex)
        {
            OnLog?.Invoke(this, $"Stop output error: {ex.Message}");
        }

        output?.Dispose();
        output = null;
        IsRunning = false;
        OnLog?.Invoke(this, "Stopped monitor engine.");
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

    private void ResetMeters()
    {
        foreach (var meter in channelMeters.Values)
        {
            meter.Reset();
        }
    }

    private sealed class CaptureChannel(
        string channelName,
        WasapiCapture capture,
        BufferedWaveProvider bufferedProvider) : IDisposable
    {
        public string ChannelName { get; } = channelName;

        public WasapiCapture Capture { get; } = capture;

        public BufferedWaveProvider BufferedProvider { get; } = bufferedProvider;

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
