namespace CNCSS.GpuVerification;

/// <summary>Отрезок съёма без WPF; на GPU воспроизводится XY-профильная модель съёма как в классе VoxelStock на CPU.</summary>
public readonly record struct GpuCylinderCut(
    double StartX,
    double StartY,
    double StartZ,
    double EndX,
    double EndY,
    double EndZ,
    double Radius,
    double FluteLength);
