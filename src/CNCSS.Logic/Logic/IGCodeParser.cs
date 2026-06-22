using System.Collections.Generic;
using CNCSS.Data;

namespace CNCSS.Logic
{
    /// <summary>
    /// Интерфейс для парсинга G-кода.
    /// Отвечает за преобразование текстовых команд в структурированные данные.
    /// </summary>
    public interface IGCodeParser
    {
        /// <summary>
        /// Текущее состояние станка в процессе парсинга.
        /// </summary>
        MachineState State { get; }

        /// <summary>
        /// Список всех разобранных команд.
        /// </summary>
        List<ParsedCommand> Commands { get; }

        /// <summary>
        /// Обрабатывает файл G-кода целиком.
        /// </summary>
        /// <param name="filePath">Путь к файлу.</param>
        /// <returns>Итоговое состояние станка после выполнения всех команд.</returns>
        MachineState ProcessFile(string filePath);

        /// <summary>
        /// Обрабатывает одну строку G-кода.
        /// </summary>
        /// <param name="line">Текст строки.</param>
        /// <param name="lineNumber">Номер строки для отслеживания ошибок.</param>
        void ProcessLine(string line, int lineNumber);
    }
}
