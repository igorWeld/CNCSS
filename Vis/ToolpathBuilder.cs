using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;
using CNCSS.Machine.Model;

namespace CNCSS.Vis
{
    /// <summary>Тип сегмента траектории для раскраски и построения линий.</summary>
    public enum ToolpathSegmentKind
    {
        Rapid,
        Linear,
        Arc
    }

    /// <summary>Непрерывный отрезок траектории (G0/G1/дуга) и его полилиния в 3D.</summary>
    public sealed class ToolpathSegment
    {
        public ToolpathSegmentKind Kind { get; init; }
        public Point3D[] Points { get; init; } = Array.Empty<Point3D>();
    }

    /// <summary>Сегмент с привязкой к номеру строки исходного файла УП.</summary>
    public sealed class ToolpathSegmentWithLine
    {
        public ToolpathSegment Segment { get; init; } = null!;
        public int LineNumber { get; init; }
    }
    /// <summary>Строит 3D-полилинию траектории из результата парсера.</summary>
    public static class ToolpathBuilder
    {
        private const double PosEps = 1e-6;

        public static List<ToolpathSegmentWithLine> BuildWithLineNumbers(
            GCodeParser parser,
            MachineState? initialState = null,
            MachineDefinition? machine = null,
            double toolStickOutMm = 0)
        {
            var projection = new ToolpathPointProjection(machine, initialState, toolStickOutMm);
            var list = new List<ToolpathSegmentWithLine>();
            var cmds = parser.Commands;
            var tempParser = initialState != null
                ? new GCodeParser(initialState.Clone())
                : new GCodeParser();

            foreach (var cmd in cmds)
            {
                MachineState state0 = tempParser.State.Clone();
                double x0 = state0.X;
                double y0 = state0.Y;
                double z0 = state0.Z;

                CommandReplayer.ReplayCommand(tempParser, cmd);

                double x1 = tempParser.State.X;
                double y1 = tempParser.State.Y;
                double z1 = tempParser.State.Z;

                if (cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6))
                {
                    // Специальная обработка G28/M6: сначала Z, потом XY
                    if (Math.Abs(z1 - z0) > PosEps)
                    {
                        list.Add(new ToolpathSegmentWithLine
                        {
                            Segment = new ToolpathSegment
                            {
                                Kind = ToolpathSegmentKind.Rapid,
                                Points = projection.Map(
                                    state0,
                                    new Point3D(x0, y0, z0),
                                    tempParser.State,
                                    new Point3D(x0, y0, z1))
                            },
                            LineNumber = cmd.LineNumber
                        });
                    }

                    if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps)
                    {
                        list.Add(new ToolpathSegmentWithLine
                        {
                            Segment = new ToolpathSegment
                            {
                                Kind = ToolpathSegmentKind.Rapid,
                                Points = projection.Map(
                                    state0,
                                    new Point3D(x0, y0, z1),
                                    tempParser.State,
                                    new Point3D(x1, y1, z1))
                            },
                            LineNumber = cmd.LineNumber
                        });
                    }

                    continue;
                }

                if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps || Math.Abs(z1 - z0) > PosEps)
                {
                    ToolpathSegment seg;
                    var arc = TryRecomputeArc(tempParser, cmd, x0, y0, z0, x1, y1, z1);
                    if (arc != null)
                    {
                        seg = new ToolpathSegment
                        {
                            Kind = ToolpathSegmentKind.Arc,
                            Points = projection.Map(tempParser.State, SampleArc(arc))
                        };
                    }
                    else
                    {
                        var kind = tempParser.State.CurrentMotionMode.Number == 0
                            ? ToolpathSegmentKind.Rapid
                            : ToolpathSegmentKind.Linear;

                        seg = new ToolpathSegment
                        {
                            Kind = kind,
                            Points = projection.Map(
                                state0,
                                new Point3D(x0, y0, z0),
                                tempParser.State,
                                new Point3D(x1, y1, z1))
                        };
                    }

                    list.Add(new ToolpathSegmentWithLine
                    {
                        Segment = seg,
                        LineNumber = cmd.LineNumber
                    });
                }
            }

            return list;
        }

        public static List<ToolpathSegment> Build(
            IEnumerable<ParsedCommand> commands,
            MachineState? initialState = null,
            MachineDefinition? machine = null,
            double toolStickOutMm = 0)
        {
            var projection = new ToolpathPointProjection(machine, initialState, toolStickOutMm);
            var list = new List<ToolpathSegment>();
            var cmds = commands.ToList();
            var parser = initialState != null
                ? new GCodeParser(initialState.Clone())
                : new GCodeParser();

            foreach (var cmd in cmds)
            {
                MachineState state0 = parser.State.Clone();
                double x0 = state0.X;
                double y0 = state0.Y;
                double z0 = state0.Z;

                CommandReplayer.ReplayCommand(parser, cmd);

                double x1 = parser.State.X;
                double y1 = parser.State.Y;
                double z1 = parser.State.Z;

                if (cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6))
                {
                    if (Math.Abs(z1 - z0) > PosEps)
                    {
                        list.Add(new ToolpathSegment
                        {
                            Kind = ToolpathSegmentKind.Rapid,
                            Points = projection.Map(
                                state0,
                                new Point3D(x0, y0, z0),
                                parser.State,
                                new Point3D(x0, y0, z1))
                        });
                    }

                    if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps)
                    {
                        list.Add(new ToolpathSegment
                        {
                            Kind = ToolpathSegmentKind.Rapid,
                            Points = projection.Map(
                                state0,
                                new Point3D(x0, y0, z1),
                                parser.State,
                                new Point3D(x1, y1, z1))
                        });
                    }

                    continue;
                }

                if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps || Math.Abs(z1 - z0) > PosEps)
                {
                    var arc = TryRecomputeArc(parser, cmd, x0, y0, z0, x1, y1, z1);
                    if (arc != null)
                    {
                        var pts = projection.Map(parser.State, SampleArc(arc));
                        if (pts.Length >= 2)
                        {
                            list.Add(new ToolpathSegment { Kind = ToolpathSegmentKind.Arc, Points = pts });
                            continue;
                        }
                    }

                    var kind = parser.State.CurrentMotionMode.Number == 0
                        ? ToolpathSegmentKind.Rapid
                        : ToolpathSegmentKind.Linear;

                    list.Add(new ToolpathSegment
                    {
                        Kind = kind,
                        Points = projection.Map(
                            state0,
                            new Point3D(x0, y0, z0),
                            parser.State,
                            new Point3D(x1, y1, z1))
                    });
                }
            }

            return list;
        }

        private readonly struct ToolpathPointProjection
        {
            private readonly MachineDefinition? _machine;
            private readonly bool _tableLocalPath;

            public ToolpathPointProjection(MachineDefinition? machine, MachineState? initialState, double toolStickOutMm)
            {
                _ = initialState;
                _ = toolStickOutMm;
                _machine = machine;
                _tableLocalPath = machine != null && MachineKinematics.UsesTableMountedWorkpiece(machine);
            }

            public Point3D[] Map(MachineState state, params Point3D[] axisPhysicalPoints)
            {
                if (_machine == null)
                {
                    return axisPhysicalPoints;
                }

                var mapped = new Point3D[axisPhysicalPoints.Length];
                for (int i = 0; i < axisPhysicalPoints.Length; i++)
                {
                    Point3D p = axisPhysicalPoints[i];
                    mapped[i] = _tableLocalPath
                        ? ToTableLocalProgramPoint(_machine, state, p.X, p.Y, p.Z)
                        : ToSceneProgramPoint(_machine, state, p.X, p.Y, p.Z);
                }

                return mapped;
            }

            public Point3D[] Map(
                MachineState state0,
                Point3D physical0,
                MachineState state1,
                Point3D physical1) =>
                new[]
                {
                    MapPoint(state0, physical0),
                    MapPoint(state1, physical1)
                };

            private Point3D MapPoint(MachineState state, Point3D physical) =>
                _machine == null
                    ? physical
                    : _tableLocalPath
                        ? ToTableLocalProgramPoint(_machine, state, physical.X, physical.Y, physical.Z)
                        : ToSceneProgramPoint(_machine, state, physical.X, physical.Y, physical.Z);

            private Point3D ToTableLocalProgramPoint(
                MachineDefinition definition,
                MachineState state,
                double physicalX,
                double physicalY,
                double physicalZ) =>
                WorkpieceMountPlacement.PhysicalProgramToTableLocal(
                    definition,
                    state,
                    physicalX,
                    physicalY,
                    physicalZ);

            private static Point3D ToSceneProgramPoint(
                MachineDefinition definition,
                MachineState state,
                double physicalX,
                double physicalY,
                double physicalZ)
            {
                state.MachineAxisToWorkpieceTip(physicalX, physicalY, physicalZ, out double programX, out double programY, out double programZ);
                MachineState.WorkOffset wcs = state.GetActiveWorkOffset();
                Point3D wcsScene = MachineAttachmentService.GetWcsOriginScene(
                    definition,
                    new MachineGeometryPoint { X = wcs.X, Y = wcs.Y, Z = wcs.Z });
                return new Point3D(
                    wcsScene.X + programX,
                    wcsScene.Y + programY,
                    wcsScene.Z + programZ);
            }
        }

        private static ArcGeometry? TryRecomputeArc(
            GCodeParser parser,
            ParsedCommand cmd,
            double x0, double y0, double z0,
            double x1, double y1, double z1)
        {
            // Do not trust cmd.Arc: it might be stale if WCS/MCS offsets changed after parsing.
            // Recompute using the parser's current modal plane/motion and the replayed start/end.
            if (!GCodeRegistry.IsArcCode(parser.State.CurrentMotionMode))
            {
                return null;
            }

            bool hasArcData = cmd.I.HasValue || cmd.J.HasValue || cmd.K.HasValue || cmd.R.HasValue;
            if (!hasArcData)
            {
                return null;
            }

            return ArcMotionPlanner.TryComputeMachineArc(
                parser.State,
                x0, y0, z0,
                x1, y1, z1,
                cmd.X, cmd.Y, cmd.Z,
                parser.State.CurrentPlane,
                parser.State.CurrentMotionMode.Number == 2,
                cmd.I, cmd.J, cmd.K, cmd.R,
                out ArcGeometry? arc)
                ? arc
                : null;
        }
        public static List<ToolpathSegment> Build(GCodeParser parser)
        {
            return Build(parser.Commands, null);
        }
        private static bool HasMoved(MachineState a, MachineState b)
        {
            return Math.Abs(a.X - b.X) > PosEps ||
                   Math.Abs(a.Y - b.Y) > PosEps ||
                   Math.Abs(a.Z - b.Z) > PosEps ||
                   Math.Abs(a.A - b.A) > PosEps ||
                   Math.Abs(a.B - b.B) > PosEps ||
                   Math.Abs(a.C - b.C) > PosEps;
        }

        public static int MinArcSegments = 8;
        public static int MaxArcSegments = 512;

        private static Point3D[] SampleArc(ArcGeometry arc)
        {
            double sweep = Math.Abs(arc.SweepAngleRad);
            int n = Math.Max(MinArcSegments, (int)(32 * sweep / (Math.PI * 2)) + 1);
            n = Math.Min(n, MaxArcSegments);

            var pts = new Point3D[n + 1];
            for (int k = 0; k <= n; k++)
            {
                double t = k / (double)n;
                double ang = arc.StartAngleRad + t * arc.SweepAngleRad;
                double cu = arc.CenterU;
                double cv = arc.CenterV;
                double r = arc.Radius;
                double u = cu + r * Math.Cos(ang);
                double v = cv + r * Math.Sin(ang);
                double x, y, z;
                double z0 = arc.StartZ + t * (arc.EndZ - arc.StartZ);
                double y0 = arc.StartY + t * (arc.EndY - arc.StartY);
                double x0 = arc.StartX + t * (arc.EndX - arc.StartX);

                switch (arc.Plane)
                {
                    case 17:
                        x = u;
                        y = v;
                        z = z0;
                        break;
                    case 18:
                        x = u;
                        z = v;
                        y = y0;
                        break;
                    case 19:
                        y = u;
                        z = v;
                        x = x0;
                        break;
                    default:
                        x = u;
                        y = v;
                        z = z0;
                        break;
                }

                pts[k] = new Point3D(x, y, z);
            }

            return pts;
        }

        public static Rect3D ComputeBounds(IEnumerable<ToolpathSegment> segments)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            foreach (var seg in segments)
            {
                foreach (var p in seg.Points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    minZ = Math.Min(minZ, p.Z);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                    maxZ = Math.Max(maxZ, p.Z);
                }
            }

            if (double.IsInfinity(minX))
                return new Rect3D(-50, -50, -50, 100, 100, 100);

            return new Rect3D(minX, minY, minZ, Math.Max(maxX - minX, 1e-3), Math.Max(maxY - minY, 1e-3), Math.Max(maxZ - minZ, 1e-3));
        }
    }
}
