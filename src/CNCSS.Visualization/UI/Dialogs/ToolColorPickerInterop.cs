using System.Drawing;
using System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace CNCSS.UI.Dialogs
{
    /// <summary>Вызов стандартного диалога выбора цвета Windows (WinForms), без глобальных using Forms/Drawing в проекте.</summary>
    internal static class ToolColorPickerInterop
    {
        public static bool TryPickColor(MediaColor currentWpf, out MediaColor chosenWpf)
        {
            using var dlg = new ColorDialog
            {
                FullOpen = true,
                Color = Color.FromArgb(255, currentWpf.R, currentWpf.G, currentWpf.B)
            };

            if (dlg.ShowDialog() != DialogResult.OK)
            {
                chosenWpf = default;
                return false;
            }

            var c = dlg.Color;
            chosenWpf = MediaColor.FromRgb(c.R, c.G, c.B);
            return true;
        }
    }
}
