using System.Collections.Generic;
using System.Windows.Media.Media3D;
using CNCSS.UI.ViewModels;

namespace CNCSS.UI.Views
{
    /// <summary>
    /// Интерфейс представления главного окна.
    /// Определяет методы для обновления UI из презентера.
    /// </summary>
    public interface IMainView
    {
        /// <summary>
        /// Обновляет список строк G-кода в UI.
        /// </summary>
        void SetGCodeLines(IEnumerable<string> lines);

        /// <summary>
        /// Выделяет текущую выполняемую строку.
        /// </summary>
        void SelectGCodeLine(int index);

        /// <summary>
        /// Обновляет позицию инструмента на сцене.
        /// </summary>
        void UpdateToolPosition(Point3D position);

        /// <summary>
        /// Обновляет геометрию инструмента.
        /// </summary>
        void UpdateToolGeometry(ToolViewModel tool);

        /// <summary>
        /// Отображает сообщение об ошибке.
        /// </summary>
        void ShowError(string message);

        /// <summary>
        /// Обновляет состояние заготовки.
        /// </summary>
        void UpdateStockDisplay();
    }
}
