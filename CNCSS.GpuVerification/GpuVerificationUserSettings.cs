namespace CNCSS.GpuVerification;

/// <summary>Пользовательские флаги модуля GPU (сохраняются на диск).</summary>
public sealed class GpuVerificationUserSettings
{
    /// <summary>Верификация съёма на GPU: сессия во время симуляции + финальный высокоточный расчёт при допустимой сетке.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Зарезервировано: предпочтение дискретной GPU ( DXGI 1.6 ); пока игнорируется.</summary>
    public bool PreferHighPerformanceGpu { get; set; } = true;

    /// <summary>Максимальный размер сетки по любой оси (вокселей). Выше — автоматический CPU fallback.</summary>
    public int MaxVoxelsPerAxis { get; set; } = 192;

    public GpuVerificationUserSettings Clone() =>
        new()
        {
            Enabled = Enabled,
            PreferHighPerformanceGpu = PreferHighPerformanceGpu,
            MaxVoxelsPerAxis = MaxVoxelsPerAxis
        };
}
