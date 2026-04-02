using System.Windows.Media.Media3D;
using CNCSS.UI.ViewModels;

namespace CNCSS.Vis
{
    public interface IVisualizer
    {
        void UpdateToolPosition(Point3D position);
        void UpdateToolGeometry(ToolViewModel? tool);
        void ClearToolpath();
        void AddToolpathSegment(Point3D start, Point3D end, bool isRapid);
        void UpdateStock(Point3D toolPos, double toolRadius, double toolLength);
        void ResetStock(double width, double height, double depth, double resolution);
    }
}
