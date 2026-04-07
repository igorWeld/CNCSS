using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CNCSS.Data;
using System.Diagnostics;

namespace CNCSS.Logic
{
    /// <summary>
    /// Основной парсер G-кода.
    /// Отвечает за чтение файлов, разбор строк на команды и параметры,
    /// а также за отслеживание состояния станка в процессе разбора.
    /// </summary>
    public class GCodeParser : IGCodeParser
    {
        /// <summary>Текущее состояние станка в процессе парсинга.</summary>
        public MachineState State { get; }
        /// <summary>Список всех разобранных команд из файла.</summary>
        public List<ParsedCommand> Commands { get; } = new();
        
        private readonly GCodeLineParser _lineParser;
        private readonly GCodeStateApplier _stateApplier;

        /// <summary>Инициализирует новый экземпляр парсера с начальным состоянием.</summary>
        public GCodeParser()
        {
            State = new MachineState();
            _lineParser = new GCodeLineParser();
            _stateApplier = new GCodeStateApplier(State);
        }

        /// <summary>Инициализирует парсер с заданным начальным состоянием.</summary>
        public GCodeParser(MachineState state)
        {
            State = state ?? new MachineState();
            _lineParser = new GCodeLineParser();
            _stateApplier = new GCodeStateApplier(State);
        }

        /// <summary>
        /// Обрабатывает файл G-кода целиком.
        /// </summary>
        /// <param name="filePath">Путь к файлу .nc или .txt.</param>
        /// <returns>Итоговое состояние станка после выполнения всех команд.</returns>
        public MachineState ProcessFile(string filePath)
        {
            State.Reset();
            Commands.Clear();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            Debug.WriteLine($"[CNCSS] Загрузка файла: {filePath}");
            var lines = File.ReadAllLines(filePath);

            for (int i = 0; i < lines.Length; i++)
                ProcessLine(lines[i], i + 1);

            Debug.WriteLine($"[CNCSS] Загрузка завершена. Команд: {Commands.Count}");
            return State;
        }

        /// <summary>
        /// Разбирает одну строку G-кода.
        /// </summary>
        /// <param name="line">Текст строки.</param>
        /// <param name="lineNumber">Порядковый номер строки в файле.</param>
        public void ProcessLine(string line, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // Делегируем парсинг строки отдельному классу
            var parsedLine = _lineParser.Parse(line, lineNumber);
            
            if (parsedLine.IsEmpty && string.IsNullOrEmpty(parsedLine.Comment))
                return;

            // Создаем команду
            var command = new ParsedCommand
            {
                LineNumber = parsedLine.LineNumber,
                RawLine = parsedLine.CleanLine,
                Comment = parsedLine.Comment,
                Parameters = parsedLine.Parameters,
                GCodes = parsedLine.GCodes,
                MCodes = parsedLine.MCodes,
                StartState = State.Clone()
            };

            // Делегируем применение состояния отдельному классу
            _stateApplier.Apply(command);
            
            command.EndState = State.Clone();
            Commands.Add(command);
        }
    }
}
