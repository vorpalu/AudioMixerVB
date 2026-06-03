// This class uses undocumented Windows audio policy APIs. It may break after Windows updates.
// Fallback to manual Windows Volume Mixer routing is required.
using System.Runtime.InteropServices;
using AudioMixerVB.Interop;

namespace AudioMixerVB;

public sealed class UndocumentedAudioPolicyRouter
{
    private const string DevInterfaceAudioRender = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string DevInterfaceAudioCapture = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";
    private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";

    private static readonly ERole[] RolesToApply =
    [
        ERole.eConsole,
        ERole.eMultimedia,
        ERole.eCommunications
    ];

    public AppOutputRoutingResult TrySetAppOutputDevice(
        uint processId,
        string processName,
        string targetEndpointId,
        string targetEndpointFriendlyName)
    {
        if (string.IsNullOrWhiteSpace(processName) || processId == 0 || string.IsNullOrWhiteSpace(targetEndpointId))
        {
            return new AppOutputRoutingResult
            {
                Success = false,
                Status = "Error",
                Message = "Cannot route app output because process or target endpoint data is incomplete.",
                RequiresManualFallback = true
            };
        }

        var result = CreateResult(processId, processName, targetEndpointId, targetEndpointFriendlyName);
        result.DiagnosticMessages.Add(
            $"OS version = {Environment.OSVersion.Version}; AudioPolicy class = {AudioPolicyConfigFactory.AudioPolicyConfigClassId}");

        IAudioPolicyConfigFactory factory;
        try
        {
            result.MethodName = "RoGetActivationFactory";
            result.DiagnosticMessages.Add(
                $"Calling RoGetActivationFactory(classId={AudioPolicyConfigFactory.AudioPolicyConfigClassId}, OS={Environment.OSVersion.Version})");
            factory = AudioPolicyConfigFactory.Create();
            result.InterfaceImplementation = $"{factory.InterfaceImplementation} IID={factory.InterfaceId}";
            result.DiagnosticMessages.Add($"Interface implementation = {result.InterfaceImplementation}");
        }
        catch (Exception ex)
        {
            return FailFromException(result, "RoGetActivationFactory", "Experimental API error", ex);
        }

        try
        {
            var policyDeviceId = PackRenderDeviceId(targetEndpointId);
            var verifiedRoles = new List<string>();
            var setFailures = new List<string>();
            var verificationFailures = new List<string>();
            var exceptionFailures = new List<string>();

            foreach (var role in RolesToApply)
            {
                var setRoleResult = TrySetRole(
                    result,
                    factory,
                    processId,
                    processName,
                    targetEndpointId,
                    targetEndpointFriendlyName,
                    policyDeviceId,
                    role);

                result.DiagnosticMessages.AddRange(setRoleResult.DiagnosticMessages);
                if (!string.IsNullOrWhiteSpace(setRoleResult.ExceptionMessage))
                {
                    exceptionFailures.Add($"{role}: {setRoleResult.ExceptionType}: {setRoleResult.ExceptionMessage}");
                    CopyFailureDetails(result, setRoleResult);
                    continue;
                }

                if (!setRoleResult.Success)
                {
                    setFailures.Add($"{role}: {setRoleResult.Message}");
                    CopyFailureDetails(result, setRoleResult);
                    continue;
                }

                var getRoleResult = TryVerifyRole(
                    result,
                    factory,
                    processId,
                    processName,
                    targetEndpointId,
                    targetEndpointFriendlyName,
                    role);

                result.DiagnosticMessages.AddRange(getRoleResult.DiagnosticMessages);
                if (!string.IsNullOrWhiteSpace(getRoleResult.ExceptionMessage))
                {
                    exceptionFailures.Add($"{role}: {getRoleResult.ExceptionType}: {getRoleResult.ExceptionMessage}");
                    CopyFailureDetails(result, getRoleResult);
                    continue;
                }

                if (getRoleResult.Success)
                {
                    verifiedRoles.Add(role.ToString());
                    result.VerificationEndpointId = getRoleResult.VerificationEndpointId;
                    continue;
                }

                verificationFailures.Add($"{role}: {getRoleResult.Message}");
                CopyFailureDetails(result, getRoleResult);
            }

            if (verifiedRoles.Count > 0)
            {
                result.Success = true;
                result.Status = "Routed preference saved";
                result.Message = "Restart app or playback may be required.";
                result.Role = string.Join(", ", verifiedRoles);
                result.Flow = EDataFlow.eRender.ToString();
                result.HResult = HResult.Format(HResult.S_OK);
                result.RequiresManualFallback = false;
                result.RequiresRestart = true;
                result.RequiresAppRestart = true;
                result.DiagnosticMessages.Add(
                    $"Routing preference verification = success; verified roles = {string.Join(", ", verifiedRoles)}; target = {targetEndpointId}");
                return result;
            }

            result.Success = false;
            result.RequiresManualFallback = true;
            result.RequiresRestart = false;
            result.RequiresAppRestart = false;

            if (exceptionFailures.Count > 0)
            {
                result.Status = "Experimental API error";
                result.Message = $"Automatic routing failed. {string.Join("; ", exceptionFailures)}";
            }
            else if (verificationFailures.Count > 0)
            {
                result.Status = "Verification failed";
                result.Message = $"Persisted endpoint does not match target. {string.Join("; ", verificationFailures)}";
            }
            else
            {
                result.Status = "Experimental API error";
                result.Message = setFailures.Count > 0
                    ? $"SetPersistedDefaultAudioEndpoint failed. {string.Join("; ", setFailures)}"
                    : "Automatic routing failed before verification.";
            }

            result.DiagnosticMessages.Add($"Routing preference verification = failed; status = {result.Status}; message = {result.Message}");
            return result;
        }
        finally
        {
            factory.Dispose();
        }
    }

    public AppOutputRoutingResult TrySetAppOutputDevice(
        string processName,
        int processId,
        string targetEndpointId,
        string targetEndpointFriendlyName)
    {
        return processId < 0
            ? new AppOutputRoutingResult
            {
                Success = false,
                Status = "Error",
                Message = "Cannot route app output because process data is incomplete.",
                RequiresManualFallback = true
            }
            : TrySetAppOutputDevice((uint)processId, processName, targetEndpointId, targetEndpointFriendlyName);
    }

    private static AppOutputRoutingResult TrySetRole(
        AppOutputRoutingResult parent,
        IAudioPolicyConfigFactory factory,
        uint processId,
        string processName,
        string targetEndpointId,
        string targetEndpointFriendlyName,
        string policyDeviceId,
        ERole role)
    {
        var result = CreateResult(processId, processName, targetEndpointId, targetEndpointFriendlyName);
        result.MethodName = "SetPersistedDefaultAudioEndpoint";
        result.Role = role.ToString();
        result.Flow = EDataFlow.eRender.ToString();
        result.InterfaceImplementation = parent.InterfaceImplementation;

        IntPtr endpointHString = IntPtr.Zero;

        try
        {
            endpointHString = CreateHString(policyDeviceId);
            result.DiagnosticMessages.Add(
                $"Calling SetPersistedDefaultAudioEndpoint(processId={processId}, flow={EDataFlow.eRender}, role={role}, endpointId={targetEndpointId})");
            result.DiagnosticMessages.Add(
                $"Parameter info: processId type=uint, flow type=EDataFlow, role type=ERole, endpoint type=HSTRING IntPtr, packedEndpointId={policyDeviceId}, interface={parent.InterfaceImplementation}");

            var hresult = factory.SetPersistedDefaultAudioEndpoint(processId, EDataFlow.eRender, role, endpointHString);
            result.HResult = HResult.Format(hresult);
            result.DiagnosticMessages.Add($"SetPersistedDefaultAudioEndpoint returned HRESULT={result.HResult}");

            if (HResult.Succeeded(hresult))
            {
                result.Success = true;
                result.Status = "Set succeeded";
                result.Message = "SetPersistedDefaultAudioEndpoint returned success.";
                result.RequiresManualFallback = false;
                return result;
            }

            result.Success = false;
            result.Status = "Experimental API error";
            result.Message = $"SetPersistedDefaultAudioEndpoint returned {result.HResult}.";
            result.RequiresManualFallback = true;
            return result;
        }
        catch (Exception ex)
        {
            return FailFromException(result, "SetPersistedDefaultAudioEndpoint", "Experimental API error", ex);
        }
        finally
        {
            if (endpointHString != IntPtr.Zero)
            {
                _ = Combase.WindowsDeleteString(endpointHString);
            }
        }
    }

    private static AppOutputRoutingResult TryVerifyRole(
        AppOutputRoutingResult parent,
        IAudioPolicyConfigFactory factory,
        uint processId,
        string processName,
        string targetEndpointId,
        string targetEndpointFriendlyName,
        ERole role)
    {
        var result = CreateResult(processId, processName, targetEndpointId, targetEndpointFriendlyName);
        result.MethodName = "GetPersistedDefaultAudioEndpoint";
        result.Role = role.ToString();
        result.Flow = EDataFlow.eRender.ToString();
        result.InterfaceImplementation = parent.InterfaceImplementation;

        try
        {
            result.DiagnosticMessages.Add(
                $"Calling GetPersistedDefaultAudioEndpoint(processId={processId}, flow={EDataFlow.eRender}, role={role})");

            var hresult = factory.GetPersistedDefaultAudioEndpoint(
                    processId,
                    EDataFlow.eRender,
                    role,
                    out var persistedDeviceId);

            result.HResult = HResult.Format(hresult);
            result.DiagnosticMessages.Add($"GetPersistedDefaultAudioEndpoint returned HRESULT={result.HResult}, raw={persistedDeviceId}");
            if (!HResult.Succeeded(hresult))
            {
                result.Success = false;
                result.Status = "Experimental API error";
                result.Message = $"GetPersistedDefaultAudioEndpoint returned {result.HResult}.";
                result.RequiresManualFallback = true;
                return result;
            }

            result.VerificationEndpointId = UnpackDeviceId(persistedDeviceId);
            var verified = targetEndpointId.Equals(result.VerificationEndpointId, StringComparison.OrdinalIgnoreCase);
            result.DiagnosticMessages.Add(
                $"GetPersistedDefaultAudioEndpoint returned = {result.VerificationEndpointId}; Verification = {(verified ? "success" : "failed")}");

            result.Success = verified;
            result.Status = verified ? "Routed preference saved" : "Verification failed";
            result.Message = verified
                ? "Restart app or playback may be required."
                : $"Persisted endpoint does not match target. persisted={result.VerificationEndpointId}, target={targetEndpointId}";
            result.RequiresManualFallback = !verified;
            result.RequiresRestart = verified;
            result.RequiresAppRestart = verified;
            return result;
        }
        catch (Exception ex)
        {
            return FailFromException(result, "GetPersistedDefaultAudioEndpoint", "Experimental API error", ex);
        }
    }

    private static AppOutputRoutingResult CreateResult(
        uint processId,
        string processName,
        string targetEndpointId,
        string targetEndpointFriendlyName)
    {
        return new AppOutputRoutingResult
        {
            ProcessId = processId,
            ProcessName = processName,
            TargetEndpointId = targetEndpointId,
            TargetEndpointFriendlyName = targetEndpointFriendlyName,
            Flow = EDataFlow.eRender.ToString(),
            RequiresManualFallback = true
        };
    }

    private static AppOutputRoutingResult FailFromException(
        AppOutputRoutingResult result,
        string methodName,
        string status,
        Exception exception)
    {
        result.Success = false;
        result.MethodName = methodName;
        result.Status = status;
        result.ExceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        result.ExceptionMessage = exception.Message;
        result.HResult = HResult.Format(exception.HResult);
        result.Message = $"{methodName} failed: {exception.Message}";
        result.RequiresManualFallback = true;
        result.RequiresRestart = false;
        result.RequiresAppRestart = false;
        result.DiagnosticMessages.Add($"{methodName} failed:");
        result.DiagnosticMessages.Add($"HRESULT = {result.HResult}");
        result.DiagnosticMessages.Add($"Exception = {result.ExceptionType}: {result.ExceptionMessage}");
        result.DiagnosticMessages.Add(
            $"Parameter info = processId:{result.ProcessId} processName:{result.ProcessName} flow:{result.Flow} role:{result.Role} targetEndpointId:{result.TargetEndpointId} targetEndpointFriendlyName:{result.TargetEndpointFriendlyName}");
        if (!string.IsNullOrWhiteSpace(result.InterfaceImplementation))
        {
            result.DiagnosticMessages.Add($"Interface implementation = {result.InterfaceImplementation}");
        }

        return result;
    }

    private static void CopyFailureDetails(AppOutputRoutingResult aggregate, AppOutputRoutingResult detail)
    {
        aggregate.MethodName = detail.MethodName;
        aggregate.HResult = detail.HResult;
        aggregate.ExceptionType = detail.ExceptionType;
        aggregate.ExceptionMessage = detail.ExceptionMessage;
        aggregate.Role = detail.Role;
        aggregate.Flow = detail.Flow;
        aggregate.VerificationEndpointId = detail.VerificationEndpointId;
        aggregate.InterfaceImplementation = detail.InterfaceImplementation;
    }

    private static IntPtr CreateHString(string value)
    {
        var result = Combase.WindowsCreateString(value, (uint)value.Length, out var hstring);
        if (!HResult.Succeeded(result))
        {
            throw new COMException("WindowsCreateString failed.", result);
        }

        return hstring;
    }

    private static string PackRenderDeviceId(string endpointId)
        => $"{MmDevApiToken}{endpointId}{DevInterfaceAudioRender}";

    private static string UnpackDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return string.Empty;
        }

        if (deviceId.StartsWith(MmDevApiToken, StringComparison.OrdinalIgnoreCase))
        {
            deviceId = deviceId[MmDevApiToken.Length..];
        }

        if (deviceId.EndsWith(DevInterfaceAudioRender, StringComparison.OrdinalIgnoreCase))
        {
            deviceId = deviceId[..^DevInterfaceAudioRender.Length];
        }

        if (deviceId.EndsWith(DevInterfaceAudioCapture, StringComparison.OrdinalIgnoreCase))
        {
            deviceId = deviceId[..^DevInterfaceAudioCapture.Length];
        }

        return deviceId;
    }
}
