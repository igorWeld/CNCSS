using CNCSS.Data;
using CNCSS.Logic.Voxel.Adaptive;
using Xunit;

namespace CNCSS.Tests;

public sealed class ArcCutSegmenterTests
{
    [Fact]
    public void SegmentArc_splits_arc_into_multiple_segments_for_small_chord()
    {
        var arc = new ArcGeometry
        {
            Plane = 17,
            Radius = 10,
            CenterU = 0,
            CenterV = 0,
            StartAngleRad = 0,
            SweepAngleRad = Math.PI * 0.5,
            StartX = 10,
            StartY = 0,
            StartZ = 0,
            EndX = 0,
            EndY = 10,
            EndZ = 0
        };

        IReadOnlyList<VoxelMotionSegment> segments = ArcCutSegmenter.SegmentArc(arc, 1, true, maxChordMm: 0.05);
        Assert.True(segments.Count > 1);
        Assert.True(segments[0].IsCuttingMove);
    }
}
