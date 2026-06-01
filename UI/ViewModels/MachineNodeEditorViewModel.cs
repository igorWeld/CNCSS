using System.Windows.Media.Media3D;
using CNCSS.Machine.Model;
using CNCSS.Vis;
using System.Windows.Media;

namespace CNCSS.UI.ViewModels
{
    public sealed class MachineNodeEditorViewModel : BaseViewModel
    {
        private string _stlDisplayName = "(нет модели)";
        private double _meshScale = 1;
        private double _stlSourceMaxExtent;
        private double _targetMaxExtentMm;
        private string _scaleSummary = "Масштаб 1:1 (мм)";
        private double _offsetX;
        private double _offsetY;
        private double _offsetZ;
        private double _meshRotationX;
        private double _meshRotationY;
        private double _meshRotationZ;
        private double _attachParentX;
        private double _attachParentY;
        private double _attachParentZ;
        private double _attachChildX;
        private double _attachChildY;
        private double _attachChildZ;
        private bool _motionX;
        private bool _motionY;
        private bool _motionZ;
        private Color _meshColor = Colors.Transparent;

        public MachineNodeEditorViewModel(MachineNodeKind kind)
        {
            Kind = kind;
            Title = kind switch
            {
                MachineNodeKind.Base => "Основание",
                MachineNodeKind.Table => "Стол",
                MachineNodeKind.Spindle => "Шпиндель",
                _ => kind.ToString()
            };
            IsSpindle = kind == MachineNodeKind.Spindle;
            IsTable = kind == MachineNodeKind.Table;
            ShowsMotionAxes = kind is MachineNodeKind.Table or MachineNodeKind.Spindle;
            if (kind == MachineNodeKind.Table)
            {
                _motionX = true;
                _motionY = true;
            }
            else if (kind == MachineNodeKind.Spindle)
            {
                _motionZ = true;
            }
        }

        public MachineNodeKind Kind { get; }

        public string Title { get; }

        public bool IsSpindle { get; }

        public bool IsTable { get; }

        public bool ShowsMotionAxes { get; }

        /// <summary>Node mesh color; Transparent means "use default".</summary>
        public Color MeshColor
        {
            get => _meshColor;
            set => SetProperty(ref _meshColor, value);
        }

        public bool MotionX
        {
            get => _motionX;
            set => SetProperty(ref _motionX, value);
        }

        public bool MotionY
        {
            get => _motionY;
            set => SetProperty(ref _motionY, value);
        }

        public bool MotionZ
        {
            get => _motionZ;
            set => SetProperty(ref _motionZ, value);
        }

        public string StlDisplayName
        {
            get => _stlDisplayName;
            set => SetProperty(ref _stlDisplayName, value);
        }

        public double MeshScale
        {
            get => _meshScale;
            set => SetProperty(ref _meshScale, value);
        }

        public double StlSourceMaxExtent
        {
            get => _stlSourceMaxExtent;
            set => SetProperty(ref _stlSourceMaxExtent, value);
        }

        public double TargetMaxExtentMm
        {
            get => _targetMaxExtentMm;
            set
            {
                if (SetProperty(ref _targetMaxExtentMm, value))
                {
                    RecalculateMeshScale();
                }
            }
        }

        public string ScaleSummary
        {
            get => _scaleSummary;
            private set => SetProperty(ref _scaleSummary, value);
        }

        public double OffsetX { get => _offsetX; set => SetProperty(ref _offsetX, value); }
        public double OffsetY { get => _offsetY; set => SetProperty(ref _offsetY, value); }
        public double OffsetZ { get => _offsetZ; set => SetProperty(ref _offsetZ, value); }

        /// <summary>Положение узла в MCS (мм), как на схеме — сцена минус ноль MCS.</summary>
        public double PositionMcsX { get; private set; }
        public double PositionMcsY { get; private set; }
        public double PositionMcsZ { get; private set; }

        public void SetPositionMcsDisplay(Point3D assemblyMcs)
        {
            PositionMcsX = assemblyMcs.X;
            PositionMcsY = assemblyMcs.Y;
            PositionMcsZ = assemblyMcs.Z;
            OnPropertyChanged(nameof(PositionMcsX));
            OnPropertyChanged(nameof(PositionMcsY));
            OnPropertyChanged(nameof(PositionMcsZ));
        }

        public double MeshRotationX { get => _meshRotationX; set => SetProperty(ref _meshRotationX, value); }
        public double MeshRotationY { get => _meshRotationY; set => SetProperty(ref _meshRotationY, value); }
        public double MeshRotationZ { get => _meshRotationZ; set => SetProperty(ref _meshRotationZ, value); }

        public double AttachParentX { get => _attachParentX; set => SetProperty(ref _attachParentX, value); }
        public double AttachParentY { get => _attachParentY; set => SetProperty(ref _attachParentY, value); }
        public double AttachParentZ { get => _attachParentZ; set => SetProperty(ref _attachParentZ, value); }

        public double AttachChildX { get => _attachChildX; set => SetProperty(ref _attachChildX, value); }
        public double AttachChildY { get => _attachChildY; set => SetProperty(ref _attachChildY, value); }
        public double AttachChildZ { get => _attachChildZ; set => SetProperty(ref _attachChildZ, value); }

        public void LoadFrom(MachineNodeDefinition node)
        {
            nodeStlFileName = node.StlFileName;
            StlDisplayName = string.IsNullOrWhiteSpace(node.StlFileName) ? "(нет модели)" : node.StlFileName;
            MeshColor = node.MeshColorArgb == 0
                ? MachineNodeColors.ToMediaColor(MachineNodeColors.ForKind(Kind))
                : UnpackColorArgb(node.MeshColorArgb);
            MeshScale = node.MeshScale;
            StlSourceMaxExtent = node.StlSourceMaxExtent;
            TargetMaxExtentMm = node.TargetMaxExtentMm > 0
                ? node.TargetMaxExtentMm
                : StlUnitScaleHelper.InferTargetMaxExtentMm(node.StlSourceMaxExtent);
            RecalculateMeshScale();
            OffsetX = node.MeshOffset.X;
            OffsetY = node.MeshOffset.Y;
            OffsetZ = node.MeshOffset.Z;
            MeshRotationX = node.MeshRotationDegrees.X;
            MeshRotationY = node.MeshRotationDegrees.Y;
            MeshRotationZ = node.MeshRotationDegrees.Z;
            AttachParentX = node.AttachOnParent.X;
            AttachParentY = node.AttachOnParent.Y;
            AttachParentZ = node.AttachOnParent.Z;
            AttachChildX = node.AttachOnChild.X;
            AttachChildY = node.AttachOnChild.Y;
            AttachChildZ = node.AttachOnChild.Z;
            ApplyMotionMask(node.MotionAxes);
        }

        public void ApplyTo(MachineNodeDefinition node)
        {
            node.MotionAxes = BuildMotionMask();
            node.StlSourceMaxExtent = StlSourceMaxExtent;
            node.TargetMaxExtentMm = TargetMaxExtentMm;
            node.SyncMeshScaleFromTarget();
            node.MeshColorArgb = MeshColor.A == 0 ? 0u : PackColorArgb(MeshColor);
            node.MeshOffset = new MachineGeometryPoint { X = OffsetX, Y = OffsetY, Z = OffsetZ };
            node.MeshRotationDegrees = new MachineGeometryPoint
            {
                X = MeshRotationX,
                Y = MeshRotationY,
                Z = MeshRotationZ
            };
            node.AttachOnParent = new MachineGeometryPoint { X = AttachParentX, Y = AttachParentY, Z = AttachParentZ };
            node.AttachOnChild = new MachineGeometryPoint { X = AttachChildX, Y = AttachChildY, Z = AttachChildZ };
        }

        public void SetStlFileName(string fileName)
        {
            nodeStlFileName = fileName;
            StlDisplayName = string.IsNullOrWhiteSpace(fileName) ? "(нет модели)" : fileName;
        }

        private string nodeStlFileName = string.Empty;

        public string GetStlFileName() => nodeStlFileName;

        private static uint PackColorArgb(Color c) =>
            ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

        private static Color UnpackColorArgb(uint argb) =>
            Color.FromArgb(
                (byte)(argb >> 24),
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb);

        public void ClearStl()
        {
            nodeStlFileName = string.Empty;
            StlDisplayName = "(нет модели)";
            StlSourceMaxExtent = 0;
            TargetMaxExtentMm = 0;
            MeshScale = 1;
            ScaleSummary = "Масштаб 1:1 (мм)";
        }

        public void ApplyStlAnalysis(StlAnalysisResult analysis)
        {
            StlSourceMaxExtent = analysis.SourceMaxExtent;
            TargetMaxExtentMm = analysis.SuggestedTargetMaxExtentMm;
            MeshScale = analysis.SuggestedMeshScale;
            UpdateScaleSummary();
        }

        public void RecalculateMeshScale()
        {
            MeshScale = StlUnitScaleHelper.ComputeMeshScale(StlSourceMaxExtent, TargetMaxExtentMm);
            UpdateScaleSummary();
        }

        private void UpdateScaleSummary()
        {
            if (StlSourceMaxExtent <= 1e-9)
            {
                ScaleSummary = "STL не загружен";
                return;
            }

            ScaleSummary = $"Файл: {StlSourceMaxExtent:F3} ед. → в симуляции: {TargetMaxExtentMm:F1} мм (×{MeshScale:F3})";
        }

        private void ApplyMotionMask(MachineProgramAxisMask mask)
        {
            MotionX = mask.HasFlag(MachineProgramAxisMask.X);
            MotionY = mask.HasFlag(MachineProgramAxisMask.Y);
            MotionZ = mask.HasFlag(MachineProgramAxisMask.Z);
        }

        private MachineProgramAxisMask BuildMotionMask()
        {
            var mask = MachineProgramAxisMask.None;
            if (MotionX)
            {
                mask |= MachineProgramAxisMask.X;
            }

            if (MotionY)
            {
                mask |= MachineProgramAxisMask.Y;
            }

            if (MotionZ)
            {
                mask |= MachineProgramAxisMask.Z;
            }

            return mask;
        }
    }
}
