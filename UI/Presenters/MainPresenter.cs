using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using CNCSS.Logic;
using CNCSS.UI.Views;
using CNCSS.Data;
using CNCSS.UI.ViewModels;

namespace CNCSS.UI.Presenters
{
    /// <summary>
    /// Презентер для главного окна.
    /// Управляет логикой взаимодействия между моделью (парсером) и представлением.
    /// </summary>
    public class MainPresenter
    {
        private readonly IMainView _view;
        private readonly IGCodeParser _parser;
        private List<ParsedCommand> _commands = new();

        /// <summary>
        /// Инициализирует новый экземпляр презентера.
        /// </summary>
        /// <param name="view">Интерфейс представления.</param>
        /// <param name="parser">Интерфейс парсера G-кода.</param>
        public MainPresenter(IMainView view, IGCodeParser parser)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <summary>
        /// Загружает и обрабатывает файл G-кода.
        /// </summary>
        /// <param name="filePath">Путь к файлу.</param>
        public void LoadGCode(string filePath)
        {
            try
            {
                _parser.ProcessFile(filePath);
                _commands = _parser.Commands;
                
                var lines = System.IO.File.ReadAllLines(filePath);
                _view.SetGCodeLines(lines);
            }
            catch (Exception ex)
            {
                _view.ShowError($"Ошибка при загрузке файла: {ex.Message}");
            }
        }

        /// <summary>
        /// Обрабатывает выбор строки в списке G-кода.
        /// </summary>
        /// <param name="index">Индекс выбранной строки.</param>
        public void OnLineSelected(int index)
        {
            if (index < 0 || index >= _commands.Count) return;

            var command = _commands.FirstOrDefault(c => c.LineNumber == index + 1);
            if (command != null)
            {
                var state = command.EndState;
                _view.UpdateToolPosition(new Point3D(state.X, state.Y, state.Z));
            }
        }

        /// <summary>
        /// Запускает симуляцию обработки.
        /// </summary>
        public void StartSimulation()
        {
            // Логика запуска таймера или цикла симуляции
        }

        /// <summary>
        /// Останавливает симуляцию.
        /// </summary>
        public void StopSimulation()
        {
            // Логика остановки
        }
    }
}
