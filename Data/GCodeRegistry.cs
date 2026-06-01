namespace CNCSS.Data
{
    /// <summary>
    /// Реестр поддерживаемых G и M кодов.
    /// Содержит определения буквенных обозначений и шаблонов команд.
    /// </summary>
    public static class GCodeRegistry
    {
        /// <summary>Буква для G-кодов (подготовительные функции).</summary>
        public const string LETTER_G = "G";
        /// <summary>Буква для M-кодов (вспомогательные функции).</summary>
        public const string LETTER_M = "M";

        /// <summary>G0: Ускоренное перемещение (позиционирование).</summary>
        public static readonly GCodeTemplate G0 = new(LETTER_G, 0, CommandModality.Modal);
        /// <summary>G1: Линейная интерполяция (рабочий ход).</summary>
        public static readonly GCodeTemplate G1 = new(LETTER_G, 1, CommandModality.Modal);
        /// <summary>G2: Круговая интерполяция по часовой стрелке.</summary>
        public static readonly GCodeTemplate G2 = new(LETTER_G, 2, CommandModality.Modal);
        /// <summary>G3: Круговая интерполяция против часовой стрелки.</summary>
        public static readonly GCodeTemplate G3 = new(LETTER_G, 3, CommandModality.Modal);
        /// <summary>G4: Выдержка времени (пауза).</summary>
        public static readonly GCodeTemplate G4 = new(LETTER_G, 4, CommandModality.NonModal);
        /// <summary>G10: Программная запись данных (offset/setting).</summary>
        public static readonly GCodeTemplate G10 = new(LETTER_G, 10, CommandModality.NonModal);

        /// <summary>G20: Работа в дюймах.</summary>
        public static readonly GCodeTemplate G20 = new(LETTER_G, 20, CommandModality.Modal);
        /// <summary>G21: Работа в миллиметрах/.</summary>
        public static readonly GCodeTemplate G21 = new(LETTER_G, 21, CommandModality.Modal);

        /// <summary>G90: Абсолютная система координат.</summary>
        public static readonly GCodeTemplate G90 = new(LETTER_G, 90, CommandModality.Modal);
        /// <summary>G91: Относительная (инкрементальная) система координат.</summary>
        public static readonly GCodeTemplate G91 = new(LETTER_G, 91, CommandModality.Modal);

        /// <summary>G17: Выбор плоскости XY.</summary>
        public static readonly GCodeTemplate G17 = new(LETTER_G, 17, CommandModality.Modal);
        /// <summary>G18: Выбор плоскости XZ.</summary>
        public static readonly GCodeTemplate G18 = new(LETTER_G, 18, CommandModality.Modal);
        /// <summary>G19: Выбор плоскости YZ.</summary>
        public static readonly GCodeTemplate G19 = new(LETTER_G, 19, CommandModality.Modal);

        /// <summary>G28: Возврат в референтную точку (через промежуточную).</summary>
        public static readonly GCodeTemplate G28 = new(LETTER_G, 28, CommandModality.NonModal);
        /// <summary>G30: Возврат во вторую референтную точку.</summary>
        public static readonly GCodeTemplate G30 = new(LETTER_G, 30, CommandModality.NonModal);

        /// <summary>
        /// G53: Перемещение в машинной системе координат (MCS) в текущем кадре (нек модальный).
        /// </summary>
        public static readonly GCodeTemplate G53 = new(LETTER_G, 53, CommandModality.NonModal);

        /// <summary>G40: Отмена коррекции на радиус инструмента.</summary>
        public static readonly GCodeTemplate G40 = new(LETTER_G, 40, CommandModality.Modal);
        /// <summary>G41: Коррекция на радиус слева.</summary>
        public static readonly GCodeTemplate G41 = new(LETTER_G, 41, CommandModality.Modal);
        /// <summary>G42: Коррекция на радиус справа.</summary>
        public static readonly GCodeTemplate G42 = new(LETTER_G, 42, CommandModality.Modal);

        /// <summary>G43: Положительная коррекция на длину инструмента.</summary>
        public static readonly GCodeTemplate G43 = new(LETTER_G, 43, CommandModality.Modal);
        /// <summary>G44: Отрицательная коррекция на длину инструмента.</summary>
        public static readonly GCodeTemplate G44 = new(LETTER_G, 44, CommandModality.Modal);
        /// <summary>G49: Отмена коррекции на длину инструмента.</summary>
        public static readonly GCodeTemplate G49 = new(LETTER_G, 49, CommandModality.Modal);

        /// <summary>G54: Рабочая система координат №1.</summary>
        public static readonly GCodeTemplate G54 = new(LETTER_G, 54, CommandModality.Modal);
        /// <summary>G55: Рабочая система координат №2.</summary>
        public static readonly GCodeTemplate G55 = new(LETTER_G, 55, CommandModality.Modal);
        /// <summary>G56: Рабочая система координат №3.</summary>
        public static readonly GCodeTemplate G56 = new(LETTER_G, 56, CommandModality.Modal);
        /// <summary>G57: Рабочая система координат №4.</summary>
        public static readonly GCodeTemplate G57 = new(LETTER_G, 57, CommandModality.Modal);
        /// <summary>G58: Рабочая система координат №5.</summary>
        public static readonly GCodeTemplate G58 = new(LETTER_G, 58, CommandModality.Modal);
        /// <summary>G59: Рабочая система координат №6.</summary>
        public static readonly GCodeTemplate G59 = new(LETTER_G, 59, CommandModality.Modal);

        /// <summary>G80: Отмена циклов сверления.</summary>
        public static readonly GCodeTemplate G80 = new(LETTER_G, 80, CommandModality.Modal);
        /// <summary>G81: Цикл простого сверления.</summary>
        public static readonly GCodeTemplate G81 = new(LETTER_G, 81, CommandModality.Modal);
        /// <summary>G82: Цикл сверления с выдержкой.</summary>
        public static readonly GCodeTemplate G82 = new(LETTER_G, 82, CommandModality.Modal);
        /// <summary>G83: Цикл глубокого сверления с отводом.</summary>
        public static readonly GCodeTemplate G83 = new(LETTER_G, 83, CommandModality.Modal);
        /// <summary>G84: Цикл нарезания резьбы метчиком.</summary>
        public static readonly GCodeTemplate G84 = new(LETTER_G, 84, CommandModality.Modal);
        /// <summary>G85: Цикл растачивания.</summary>
        public static readonly GCodeTemplate G85 = new(LETTER_G, 85, CommandModality.Modal);

        /// <summary>M0: Программный стоп.</summary>
        public static readonly GCodeTemplate M0 = new(LETTER_M, 0, CommandModality.NonModal);
        /// <summary>M1: Опциональный стоп.</summary>
        public static readonly GCodeTemplate M1 = new(LETTER_M, 1, CommandModality.NonModal);
        /// <summary>M2: Конец программы.</summary>
        public static readonly GCodeTemplate M2 = new(LETTER_M, 2, CommandModality.NonModal);
        /// <summary>M30: Конец программы и возврат в начало.</summary>
        public static readonly GCodeTemplate M30 = new(LETTER_M, 30, CommandModality.NonModal);

        /// <summary>M3: Включение шпинделя по часовой стрелке.</summary>
        public static readonly GCodeTemplate M3 = new(LETTER_M, 3, CommandModality.Modal);
        /// <summary>M4: Включение шпинделя против часовой стрелки.</summary>
        public static readonly GCodeTemplate M4 = new(LETTER_M, 4, CommandModality.Modal);
        /// <summary>M5: Остановка шпинделя.</summary>
        public static readonly GCodeTemplate M5 = new(LETTER_M, 5, CommandModality.Modal);

        /// <summary>M6: Смена инструмента.</summary>
        public static readonly GCodeTemplate M6 = new(LETTER_M, 6, CommandModality.NonModal);

        /// <summary>M7: Включение охлаждения (туман).</summary>
        public static readonly GCodeTemplate M7 = new(LETTER_M, 7, CommandModality.Modal);
        /// <summary>M8: Включение охлаждения (полив).</summary>
        public static readonly GCodeTemplate M8 = new(LETTER_M, 8, CommandModality.Modal);
        /// <summary>M9: Выключение охлаждения.</summary>
        public static readonly GCodeTemplate M9 = new(LETTER_M, 9, CommandModality.Modal);

        /// <summary>M98: Вызов подпрограммы.</summary>
        public static readonly GCodeTemplate M98 = new(LETTER_M, 98, CommandModality.NonModal);
        /// <summary>M99: Возврат из подпрограммы.</summary>
        public static readonly GCodeTemplate M99 = new(LETTER_M, 99, CommandModality.NonModal);

        /// <summary>Параметр X: Координата по оси X.</summary>
        public const string PARAM_X = "X";
        /// <summary>Параметр Y: Координата по оси Y.</summary>
        public const string PARAM_Y = "Y";
        /// <summary>Параметр Z: Координата по оси Z.</summary>
        public const string PARAM_Z = "Z";
        /// <summary>Параметр A: Угол поворота вокруг оси X.</summary>
        public const string PARAM_A = "A";
        /// <summary>Параметр B: Угол поворота вокруг оси Y.</summary>
        public const string PARAM_B = "B";
        /// <summary>Параметр C: Угол поворота вокруг оси Z.</summary>
        public const string PARAM_C = "C";

        /// <summary>Параметр F: Скорость подачи (Feedrate).</summary>
        public const string PARAM_F = "F";
        /// <summary>Параметр S: Скорость шпинделя (Spindle speed).</summary>
        public const string PARAM_S = "S";
        /// <summary>Параметр T: Номер инструмента (Tool number).</summary>
        public const string PARAM_T = "T";

        /// <summary>Параметр D: Номер корректора на радиус.</summary>
        public const string PARAM_D = "D";
        /// <summary>Параметр H: Номер корректора на длину.</summary>
        public const string PARAM_H = "H";

        /// <summary>Параметр R: Радиус дуги или плоскость отвода в циклах.</summary>
        public const string PARAM_R = "R";
        /// <summary>Параметр I: Смещение центра дуги по оси X.</summary>
        public const string PARAM_I = "I";
        /// <summary>Параметр J: Смещение центра дуги по оси Y.</summary>
        public const string PARAM_J = "J";
        /// <summary>Параметр K: Смещение центра дуги по оси Z.</summary>
        public const string PARAM_K = "K";

        /// <summary>Параметр N: Номер строки.</summary>
        public const string PARAM_N = "N";
        /// <summary>Параметр P: Время выдержки или номер подпрограммы.</summary>
        public const string PARAM_P = "P";

        /// <summary>Список всех поддерживаемых G-кодов.</summary>
        public static readonly GCodeTemplate[] AllGCodes =
        {
            G0, G1, G2, G3, G4, G10,
            G20, G21, G90, G91,
            G17, G18, G19,
            G28, G30,
            G53,
            G40, G41, G42,
            G43, G44, G49,
            G54, G55, G56, G57, G58, G59,
            G80, G81, G82, G83, G84, G85
        };

        /// <summary>Список всех поддерживаемых M-кодов.</summary>
        public static readonly GCodeTemplate[] AllMCodes =
        {
            M0, M1, M2, M30,
            M3, M4, M5, M6,
            M7, M8, M9,
            M98, M99
        };

        /// <summary>Возвращает шаблон G-кода по его номеру.</summary>
        public static GCodeTemplate? GetGCode(int number) => AllGCodes.FirstOrDefault(g => g.Number == number);

        /// <summary>Возвращает шаблон M-кода по его номеру.</summary>
        public static GCodeTemplate? GetMCode(int number) => AllMCodes.FirstOrDefault(m => m.Number == number);

        /// <summary>Проверяет, является ли код командой движения (G0, G1, G2, G3).</summary>
        public static bool IsMovementCode(GCodeTemplate? code)
        {
            if (!code.HasValue) return false;
            return code.Value.Letter == LETTER_G &&
                   (code.Value.Number == 0 || code.Value.Number == 1 ||
                    code.Value.Number == 2 || code.Value.Number == 3);
        }

        /// <summary>Проверяет, является ли код командой круговой интерполяции (G2, G3).</summary>
        public static bool IsArcCode(GCodeTemplate? code)
        {
            if (!code.HasValue) return false;
            return code.Value.Letter == LETTER_G && (code.Value.Number == 2 || code.Value.Number == 3);
        }
    }
}
