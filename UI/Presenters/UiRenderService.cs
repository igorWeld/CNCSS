using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using CNCSS.Data;
using CNCSS.UI.FanucPanel;
using CNCSS.UI.ViewModels;
using HelixToolkit.Wpf;

namespace CNCSS.UI.Presenters
{
    public sealed class UiRenderService
    {
        public void ApplyRuntimeStatus(
            MachineState state,
            FanucPanelControl fanucPanel,
            TextBlock toolText,
            TextBlock coordSystemText,
            TextBlock refSystemText,
            TextBlock feedStatusText,
            TextBlock spindleStatusText,
            TextBlock coolantText)
        {
            string toolDisplay = state.ToolNumber.HasValue ? $"T{state.ToolNumber.Value}" : "-";
            string wcsDisplay = $"{state.CurrentCoordinateSystem.Letter}{state.CurrentCoordinateSystem.Number}";
            string refDisplay = state.IsAbsolute ? "ABS (G90)" : "INC (G91)";
            string spindleCode = state.IsSpindleOn ? (state.IsSpindleCW ? "M3" : "M4") : "M5";

            toolText.Text = $"Инструмент: {toolDisplay}";
            coordSystemText.Text = $"СК: {wcsDisplay}";
            refSystemText.Text = $"Отсчет: {refDisplay}";
            feedStatusText.Text = $"F: {state.EffectiveFeedRate:F0} мм/мин";
            spindleStatusText.Text = $"S: {state.SpindleSpeed:F0} об/мин ({spindleCode})";
            coolantText.Text = $"СОЖ: {(state.IsCoolantOn ? "ВКЛ" : "ВЫКЛ")}";

            fanucPanel.UpdateFeedAndSpindle(state.EffectiveFeedRate, state.SpindleSpeed, spindleCode);
            fanucPanel.UpdatePosRuntime(state.EffectiveFeedRate, toolDisplay, state.SpindleSpeed, spindleCode, state.IsCoolantOn);
            fanucPanel.UpdateToolAndCoordinate(toolDisplay, wcsDisplay);
            fanucPanel.UpdateCoolantAndReference(state.IsCoolantOn, refDisplay);
        }

        public ToolViewModel? SyncToolVisual(
            int? toolNumber,
            ObservableCollection<ToolViewModel> tools,
            ListBox toolsList,
            HelixViewport3D viewport,
            ModelVisual3D toolVisual,
            Point3D position,
            Action<ToolViewModel, Point3D> updateToolGeometry,
            Action<ToolViewModel> onToolCreated,
            bool allowHideWhenNoTool)
        {
            if (toolNumber.HasValue)
            {
                var toolVm = tools.FirstOrDefault(t => t.Number == toolNumber.Value);
                if (toolVm == null)
                {
                    toolVm = new ToolViewModel { Number = toolNumber.Value };
                    onToolCreated(toolVm);
                    tools.Add(toolVm);
                }

                if (toolsList.SelectedItem != toolVm)
                {
                    toolsList.SelectedItem = toolVm;
                }

                if (!viewport.Children.Contains(toolVisual))
                {
                    viewport.Children.Add(toolVisual);
                }

                updateToolGeometry(toolVm, position);
                return toolVm;
            }

            if (allowHideWhenNoTool && viewport.Children.Contains(toolVisual))
            {
                viewport.Children.Remove(toolVisual);
            }

            return null;
        }
    }
}
