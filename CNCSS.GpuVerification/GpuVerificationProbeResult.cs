namespace CNCSS.GpuVerification;

/// <summary>Результат проверки доступности GPU для compute.</summary>
public readonly record struct GpuVerificationProbeResult(
    bool IsAvailable,
    string Message,
    ulong DedicatedVideoMemoryBytes = 0,
    ulong SharedSystemMemoryBytes = 0);
