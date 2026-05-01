using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.UI.Presenters
{
    /// <summary>Показывает или скрывает сегменты траектории в зависимости от номера активной строки программы.</summary>
    public sealed class ToolpathRenderService
    {
        private readonly HashSet<Visual3D> _visibleVisuals = new();

        public void Reset()
        {
            _visibleVisuals.Clear();
        }

        public void TrackVisible(Visual3D visual)
        {
            _visibleVisuals.Add(visual);
        }

        public void UpdateVisibilityByLine(
            Dictionary<int, List<Visual3D>> lineVisualsMap,
            HelixViewport3D viewport,
            int selectedLine)
        {
            foreach (var entry in lineVisualsMap)
            {
                bool isVisible = entry.Key <= selectedLine;
                foreach (var visual in entry.Value)
                {
                    bool isAlreadyVisible = _visibleVisuals.Contains(visual);
                    if (isVisible && !isAlreadyVisible)
                    {
                        viewport.Children.Add(visual);
                        _visibleVisuals.Add(visual);
                    }
                    else if (!isVisible && isAlreadyVisible)
                    {
                        viewport.Children.Remove(visual);
                        _visibleVisuals.Remove(visual);
                    }
                }
            }
        }
    }
}
