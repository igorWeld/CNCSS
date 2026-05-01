using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace CNCSS.UI.Presenters
{
    public sealed class ToolpathRenderService
    {
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
                    if (isVisible && !viewport.Children.Contains(visual))
                    {
                        viewport.Children.Add(visual);
                    }
                    else if (!isVisible && viewport.Children.Contains(visual))
                    {
                        viewport.Children.Remove(visual);
                    }
                }
            }
        }
    }
}
