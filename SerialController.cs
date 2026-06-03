using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AudioMixerVB;

public sealed class SerialController : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint PurgeRxClear = 0x0008;
    private const uint PurgeTxClear = 0x0004;
    private const uint DcbBinary = 0x00000001;
    private const uint DcbDtrControlEnable = 0x00000010;
    private const uint DcbRtsControlEnable = 0x00001000;

    private readonly object syncRoot = new();
    private CancellationTokenSource? cancellationTokenSource;
    private Task? readTask;
    private SafeFileHandle? handle;
    private FileStream? stream;

    public event EventHandler<SerialCommandReceivedEventArgs>? OnCommandReceived;

    public event EventHandler<string>? OnLogMessage;

    public bool IsConnected { get; private set; }

    public string? PortName { get; private set; }

    public int BaudRate { get; private set; }

    public static IReadOnlyList<string> GetPortNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        using var serialCommKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (serialCommKey is null)
        {
            return [];
        }

        return serialCommKey
            .GetValueNames()
            .Select(name => serialCommKey.GetValue(name) as string)
            .Where(port => !string.IsNullOrWhiteSpace(port))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(port => port, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Connect(string portName, int baudRate)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Serial COM ports are supported only on Windows.");
        }

        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("COM port is required.", nameof(portName));
        }

        if (baudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baudRate), "Baud rate must be greater than zero.");
        }

        lock (syncRoot)
        {
            DisconnectCore(waitForReader: true);

            var newHandle = CreateFileW(
                ToDevicePath(portName),
                GenericRead | GenericWrite,
                0,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            if (newHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                newHandle.Dispose();
                throw new Win32Exception(error, $"Could not open {portName}.");
            }

            try
            {
                ConfigurePort(newHandle, baudRate);
                SetupComm(newHandle, 4096, 4096);
                PurgeComm(newHandle, PurgeRxClear | PurgeTxClear);

                handle = newHandle;
                var newStream = new FileStream(handle, FileAccess.ReadWrite, 256, isAsync: false);
                var newCancellationTokenSource = new CancellationTokenSource();
                stream = newStream;
                cancellationTokenSource = newCancellationTokenSource;
                PortName = portName.Trim();
                BaudRate = baudRate;
                IsConnected = true;
                readTask = Task.Run(() => ReadLoop(newStream, newCancellationTokenSource.Token));
            }
            catch
            {
                newHandle.Dispose();
                throw;
            }
        }
    }

    public void Disconnect()
    {
        lock (syncRoot)
        {
            DisconnectCore(waitForReader: true);
        }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    private void DisconnectCore(bool waitForReader)
    {
        var task = readTask;
        readTask = null;

        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;

        stream?.Dispose();
        stream = null;

        handle?.Dispose();
        handle = null;

        IsConnected = false;
        PortName = null;
        BaudRate = 0;

        if (waitForReader && task is { IsCompleted: false })
        {
            try
            {
                task.Wait(TimeSpan.FromMilliseconds(500));
            }
            catch (AggregateException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void ReadLoop(FileStream serialStream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        var lineBuilder = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;

            try
            {
                bytesRead = serialStream.Read(buffer, 0, buffer.Length);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                OnLogMessage?.Invoke(this, $"Serial read error: {ex.Message}");
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                OnLogMessage?.Invoke(this, $"Serial read error: {ex.Message}");
                break;
            }

            if (bytesRead <= 0)
            {
                continue;
            }

            for (var index = 0; index < bytesRead; index++)
            {
                var character = (char)buffer[index];

                if (character == '\n')
                {
                    ProcessLine(lineBuilder.ToString());
                    lineBuilder.Clear();
                }
                else if (character != '\r')
                {
                    lineBuilder.Append(character);
                }

                if (lineBuilder.Length > 1024)
                {
                    OnLogMessage?.Invoke(this, "Serial line was longer than 1024 characters and was dropped.");
                    lineBuilder.Clear();
                }
            }
        }
    }

    private void ProcessLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (SerialCommand.TryParse(trimmed, out var command, out var error) && command is not null)
        {
            OnCommandReceived?.Invoke(this, new SerialCommandReceivedEventArgs(trimmed, command));
            return;
        }

        OnLogMessage?.Invoke(this, $"Serial parse error for '{trimmed}': {error}");
    }

    private static void ConfigurePort(SafeFileHandle serialHandle, int baudRate)
    {
        var dcb = new Dcb
        {
            Length = (uint)Marshal.SizeOf<Dcb>()
        };

        if (!GetCommState(serialHandle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read COM port state.");
        }

        dcb.BaudRate = (uint)baudRate;
        dcb.Flags = DcbBinary | DcbDtrControlEnable | DcbRtsControlEnable;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;

        if (!SetCommState(serialHandle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure COM port state.");
        }

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 50,
            ReadTotalTimeoutMultiplier = 10,
            ReadTotalTimeoutConstant = 200,
            WriteTotalTimeoutMultiplier = 10,
            WriteTotalTimeoutConstant = 200
        };

        if (!SetCommTimeouts(serialHandle, ref timeouts))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure COM port timeouts.");
        }
    }

    private static string ToDevicePath(string portName)
    {
        var trimmedPortName = portName.Trim();
        return trimmedPortName.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? trimmedPortName
            : @"\\.\" + trimmedPortName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle file, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetupComm(SafeFileHandle file, uint inQueue, uint outQueue);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PurgeComm(SafeFileHandle file, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint Length;
        public uint BaudRate;
        public uint Flags;
        public ushort Reserved;
        public ushort XonLimit;
        public ushort XoffLimit;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EventChar;
        public ushort Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }
}

public sealed class SerialCommandReceivedEventArgs(string line, SerialCommand command) : EventArgs
{
    public string Line { get; } = line;

    public SerialCommand Command { get; } = command;
}
