using System.Collections.Generic;
using System.Linq;

namespace CNCSS.Data
{
    /// <summary>
    /// Представляет разобранную команду G-кода со всеми её параметрами и состоянием после выполнения.
    /// </summary>
    public class ParsedCommand
    {
        /// <summary>Номер строки в исходном файле.</summary>
        public int LineNumber { get; set; }
        
        /// <summary>Исходный текст строки.</summary>
        public string RawLine { get; set; } = string.Empty;
        
        /// <summary>Комментарий, извлеченный из строки.</summary>
        public string? Comment { get; set; }

        /// <summary>Словарь всех числовых параметров (X, Y, Z, F, S и т.д.).</summary>
        public Dictionary<string, double> Parameters { get; set; } = new();
        
        /// <summary>Список G-кодов, найденных в строке.</summary>
        public List<GCodeTemplate> GCodes { get; set; } = new();
        
        /// <summary>Список M-кодов, найденных в строке.</summary>
        public List<GCodeTemplate> MCodes { get; set; } = new();

        // Координаты (могут быть null, если не указаны в данной строке)
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public double? A { get; set; }
        public double? B { get; set; }
        public double? C { get; set; }

        // Параметры дуги
        public double? I { get; set; }
        public double? J { get; set; }
        public double? K { get; set; }
        public double? R { get; set; }

        // Технологические параметры
        public double? FeedRate { get; set; }
        public double? SpindleSpeed { get; set; }
        public int? ToolNumber { get; set; }
        public int? ToolLengthOffset { get; set; }
        public int? ToolRadiusOffset { get; set; }

        /// <summary>Геометрия дуги для команд G02/G03.</summary>
        public ArcGeometry? Arc { get; set; }

        /// <summary>Является ли команда командой перемещения (G0, G1, G2, G3).</summary>
        public bool IsMovement => GCodes.Any(g => GCodeRegistry.IsMovementCode(g));

        /// <summary>Содержит ли команда явные координаты.</summary>
        public bool HasCoordinates =>
            X.HasValue || Y.HasValue || Z.HasValue || A.HasValue || B.HasValue || C.HasValue;

        /// <summary>Последний (основной) G-код в строке.</summary>
        public GCodeTemplate? GCode => GCodes.LastOrDefault();
        
        /// <summary>Последний (основной) M-код в строке.</summary>
        public GCodeTemplate? MCode => MCodes.LastOrDefault();

        /// <summary>Состояние станка до выполнения данной команды.</summary>
        public MachineState StartState { get; set; } = new();

        /// <summary>Состояние станка после выполнения данной команды.</summary>
        public MachineState EndState { get; set; } = new();
    }
}
