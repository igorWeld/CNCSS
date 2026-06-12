using System.Windows.Media;
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
            ModelVisual3D container,
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
                        // Visual3D cannot be added twice; it might already be in the container
                        // (e.g. after rebuild) while our visible set was Reset().
                        if (!container.Children.Contains(visual))
                        {
                            container.Children.Add(visual);
                        }

                        _visibleVisuals.Add(visual);
                    }
                    else if (!isVisible && isAlreadyVisible)
                    {
                        if (container.Children.Contains(visual))
                        {
                            container.Children.Remove(visual);
                        }

                        _visibleVisuals.Remove(visual);
                    }
                }
            }
        }

        /// <summary>
        /// Во время воспроизведения: завершённые строки + «хвост» текущего сегмента до положения инструмента.
        /// </summary>
        public void UpdatePlaybackProgress(
            Dictionary<int, List<Visual3D>> lineVisualsMap,
            ModelVisual3D container,
            int visibleThroughLine,
            LinesVisual3D? capVisual,
            bool showCap,
            Point3D capFrom,
            Point3D capTo,
            Color capColor)
        {
            UpdateVisibilityByLine(lineVisualsMap, container, visibleThroughLine);
            if (capVisual == null)
            {
                return;
            }

            if (showCap)
            {
                capVisual.Color = capColor;
                capVisual.Points = new Point3DCollection { capFrom, capTo };
                if (!container.Children.Contains(capVisual))
                {
                    container.Children.Add(capVisual);
                }

                _visibleVisuals.Add(capVisual);
            }
            else if (container.Children.Contains(capVisual))
            {
                container.Children.Remove(capVisual);
                _visibleVisuals.Remove(capVisual);
            }
        }
    }
}
