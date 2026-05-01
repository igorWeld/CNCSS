using System.Windows.Media.Media3D;
using CNCSS.UI.ViewModels;

namespace CNCSS.Vis
{
    /// <summary>Контракт обновления 3D-сцены (инструмент, траектория, заготовка) отдельно от WPF-окна.</summary>
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
