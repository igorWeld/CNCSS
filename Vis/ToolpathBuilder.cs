using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;

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

        public static List<ToolpathSegmentWithLine> BuildWithLineNumbers(GCodeParser parser)
        {
            var list = new List<ToolpathSegmentWithLine>();
            var cmds = parser.Commands;
            var tempParser = new GCodeParser();

            foreach (var cmd in cmds)
            {
                double x0 = tempParser.State.X;
                double y0 = tempParser.State.Y;
                double z0 = tempParser.State.Z;

                CommandReplayer.ReplayCommand(tempParser, cmd);

                double x1 = tempParser.State.X;
                double y1 = tempParser.State.Y;
                double z1 = tempParser.State.Z;

                if (cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6))
                {
                    // Специальная обработка G28/M6: сначала Z, потом XY
                    // 1. Движение по Z
                    if (Math.Abs(z1 - z0) > PosEps)
                    {
                        list.Add(new ToolpathSegmentWithLine
                        {
                            Segment = new ToolpathSegment
                            {
                                Kind = ToolpathSegmentKind.Rapid,
                                Points = new[] { new Point3D(x0, y0, z0), new Point3D(x0, y0, z1) }
                            },
                            LineNumber = cmd.LineNumber
                        });
                    }
                    // 2. Движение по XY
                    if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps)
                    {
                        list.Add(new ToolpathSegmentWithLine
                        {
                            Segment = new ToolpathSegment
                            {
                                Kind = ToolpathSegmentKind.Rapid,
                                Points = new[] { new Point3D(x0, y0, z1), new Point3D(x1, y1, z1) }
                            },
                            LineNumber = cmd.LineNumber
                        });
                    }
                    continue;
                }

                if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps || Math.Abs(z1 - z0) > PosEps)                {
                    var p0 = new Point3D(x0, y0, z0);
                    var p1 = new Point3D(x1, y1, z1);

                    ToolpathSegment seg;
                    if (cmd.Arc != null)
                    {
                        var pts = SampleArc(cmd.Arc);
                        seg = new ToolpathSegment { Kind = ToolpathSegmentKind.Arc, Points = pts };
                    }
                    else
                    {
                        var kind = tempParser.State.CurrentMotionMode.Number == 0
                            ? ToolpathSegmentKind.Rapid
                            : ToolpathSegmentKind.Linear;

                        seg = new ToolpathSegment
                        {
                            Kind = kind,
                            Points = new[] { p0, p1 }
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

        public static List<ToolpathSegment> Build(IEnumerable<ParsedCommand> commands)        {
            var list = new List<ToolpathSegment>();
            var cmds = commands.ToList();
            var parser = new GCodeParser(); // Временный парсер для отслеживания состояния

            foreach (var cmd in cmds)
            {
                // Сохраняем позицию ДО выполнения команды
                double x0 = parser.State.X;
                double y0 = parser.State.Y;
                double z0 = parser.State.Z;

                // Выполняем команду
                CommandReplayer.ReplayCommand(parser, cmd);

                // Позиция ПОСЛЕ выполнения команды
                double x1 = parser.State.X;
                double y1 = parser.State.Y;
                double z1 = parser.State.Z;

                if (cmd.GCodes.Any(g => g.Number == 28) || cmd.MCodes.Any(m => m.Number == 6))
                {
                    // Специальная обработка G28/M6: сначала Z, потом XY
                    // 1. Движение по Z
                    if (Math.Abs(z1 - z0) > PosEps)
                    {
                        list.Add(new ToolpathSegment
                        {
                            Kind = ToolpathSegmentKind.Rapid,
                            Points = new[] { new Point3D(x0, y0, z0), new Point3D(x0, y0, z1) }
                        });
                    }
                    // 2. Движение по XY
                    if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps)
                    {
                        list.Add(new ToolpathSegment
                        {
                            Kind = ToolpathSegmentKind.Rapid,
                            Points = new[] { new Point3D(x0, y0, z1), new Point3D(x1, y1, z1) }
                        });
                    }
                    continue;
                }

                // Если позиция изменилась
                if (Math.Abs(x1 - x0) > PosEps || Math.Abs(y1 - y0) > PosEps || Math.Abs(z1 - z0) > PosEps)
                {
                    var p0 = new Point3D(x0, y0, z0);
                    var p1 = new Point3D(x1, y1, z1);

                    if (cmd.Arc != null)
                    {
                        var pts = SampleArc(cmd.Arc);
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
                        Points = new[] { p0, p1 }
                    });
                }
            }

            return list;
        }
        public static List<ToolpathSegment> Build(GCodeParser parser)
        {
            return Build(parser.Commands);
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
