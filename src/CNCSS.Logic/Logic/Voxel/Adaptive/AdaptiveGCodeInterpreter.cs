using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Logic;

namespace CNCSS.Logic.Voxel.Adaptive;

/// <summary>
/// Адаптер над существующим парсером G-кода: формирует линейные motion-сегменты для voxel kernel.
/// </summary>
public sealed class AdaptiveGCodeInterpreter
{
    private readonly IGCodeParser _parser;
    private readonly List<ParsedCommand> _commands = new();

    public AdaptiveGCodeInterpreter(IGCodeParser parser)
    {
        _parser = parser;
    }

    public IReadOnlyList<ParsedCommand> Commands => _commands;

    public IReadOnlyList<ParsedCommand> Load(string filePath)
    {
        _parser.ProcessFile(filePath);
        _commands.Clear();
        if (_parser is GCodeParser concrete)
        {
            _commands.AddRange(concrete.Commands);
        }

        return _commands;
    }

    public IReadOnlyList<VoxelMotionSegment> BuildMotionSegments(
        bool includeRapidMoves = false,
        double maxArcChordMm = 0.05)
    {
        var result = new List<VoxelMotionSegment>(_commands.Count);

        foreach (ParsedCommand cmd in _commands)
        {
            GCodeTemplate? main = cmd.GCode;
            if (main == null || !cmd.IsMovement)
            {
                continue;
            }

            int code = main.Value.Number;
            bool isRapid = code == 0;
            bool cutting = code is 1 or 2 or 3;
            if (isRapid && !includeRapidMoves)
            {
                continue;
            }

            var start = new Point3D(cmd.StartState.X, cmd.StartState.Y, cmd.StartState.Z);
            var end = new Point3D(cmd.EndState.X, cmd.EndState.Y, cmd.EndState.Z);
            if ((end - start).Length <= 1e-9)
            {
                continue;
            }

            if (code is 2 or 3 && cmd.Arc != null)
            {
                result.AddRange(ArcCutSegmenter.SegmentArc(cmd.Arc, cmd.LineNumber, cutting, maxArcChordMm));
                continue;
            }

            result.Add(new VoxelMotionSegment(start, end, cutting, cmd.LineNumber));
        }

        return result;
    }
}
