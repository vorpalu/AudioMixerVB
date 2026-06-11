namespace AudioMixerVB;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--probe-endpoints", StringComparison.OrdinalIgnoreCase))
        {
            RunEndpointProbe(args);
            return;
        }

        // Low-latency audio runs in user mode here; keep GC pauses short and let the
        // process compete with other audio software for CPU time.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
        }
        catch
        {
            // Keep default priority if the system denies the change.
        }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void RunEndpointProbe(string[] args)
    {
        var outputPath = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "endpoint_probe.txt");

        using var writer = new StreamWriter(outputPath, append: false);

        try
        {
            var controller = new AudioEndpointController();
            controller.LogMessage += (_, message) => writer.WriteLine(message);

            var endpoints = controller.GetRenderEndpoints();
            writer.WriteLine($"Found {endpoints.Count} active render endpoint(s).");

            foreach (var endpoint in endpoints)
            {
                writer.WriteLine($"Endpoint: {endpoint.FriendlyName} [{endpoint.Id}]");
            }
        }
        catch (Exception ex)
        {
            writer.WriteLine($"Endpoint probe error: {ex}");
            Environment.ExitCode = 1;
        }
    }
}
