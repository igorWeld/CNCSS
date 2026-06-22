using System.Windows.Media;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.Data.Tools;

namespace CNCSS.Vis
{
    /// <summary>Контракт заготовки: резание, обновление меша, отображение и финальная заморозка.</summary>
    public interface IStockVolume
    {
        Model3D MainModel { get; }
        bool IsDirty { get; }
        int DirtyChunkCount { get; }
        /// <summary>После <see cref="FreezeModel"/> операции резки игнорируются — нужно создать новый объём перед следующим симуляционным проходом.</summary>
        bool IsCutsFrozen { get; }
        void CutCylinder(Point3D start, Point3D end, double radius, double fluteLength, Color toolColor);
        void ApplyCutMotion(CNCSS.Logic.Voxel.CutMotionDescriptor motion, CNCSS.Logic.Voxel.FluteCutProfile profile, Color toolColor);
        Task UpdateVisualsAsync();
        void AttachBoundingSolidPlaceholder(Color diffuse);
        void FreezeModel();
    }
}
