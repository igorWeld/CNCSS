namespace CNCSS.Data
{
    /// <summary>
    /// Основное состояние станка с ЧПУ.
    /// Хранит текущие координаты, режимы движения, параметры подачи и шпинделя.
    /// </summary>
    public class MachineState
    {
        // Константы домашней позиции (Home)
        public const double HOME_X = 255.0;
        public const double HOME_Y = 255.0;
        public const double HOME_Z = 255.0;
        
        /// <summary>Скорость ускоренного перемещения (G0) по умолчанию, мм/мин.</summary>
        public static double RAPID_FEED = 5000.0;

        /// <summary>Режим позиционирования: true - абсолютный (G90), false - относительный (G91).</summary>
        public bool IsAbsolute { get; set; } = true;
        
        /// <summary>Единицы измерения: true - метрические (G21), false - дюймовые (G20).</summary>
        public bool IsMetric { get; set; } = true;

        /// <summary>Текущая активная система координат (G54-G59).</summary>
        public GCodeTemplate CurrentCoordinateSystem { get; set; } = GCodeRegistry.G54;
        
        /// <summary>Текущая рабочая плоскость (G17, G18, G19).</summary>
        public GCodeTemplate CurrentPlane { get; set; } = GCodeRegistry.G17;
        
        private GCodeTemplate _currentMotionMode = GCodeRegistry.G0;
        /// <summary>Текущий режим движения (G0, G1, G2, G3).</summary>
        public GCodeTemplate CurrentMotionMode 
        { 
            get => _currentMotionMode;
            set 
            {
                _currentMotionMode = value;
                UpdateEffectiveFeed();
            }
        }
        
        /// <summary>Коррекция на радиус инструмента (G40, G41, G42).</summary>
        public GCodeTemplate CutterCompensation { get; set; } = GCodeRegistry.G40;
        
        /// <summary>Коррекция на длину инструмента (G43, G44, G49).</summary>
        public GCodeTemplate ToolLengthCompensation { get; set; } = GCodeRegistry.G49;

        // Текущие координаты осей
        public double X { get; set; } = HOME_X;
        public double Y { get; set; } = HOME_Y;
        public double Z { get; set; } = HOME_Z;
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }

        // Предыдущие координаты (для расчета перемещений)
        public double PreviousX { get; private set; } = HOME_X;
        public double PreviousY { get; private set; } = HOME_Y;
        public double PreviousZ { get; private set; } = HOME_Z;
        public double PreviousA { get; private set; }
        public double PreviousB { get; private set; }
        public double PreviousC { get; private set; }

        private double _programmedFeedRate;
        /// <summary>Запрограммированная рабочая подача (F), мм/мин.</summary>
        public double FeedRate 
        { 
            get => _programmedFeedRate; 
            set 
            { 
                _programmedFeedRate = value; 
                UpdateEffectiveFeed();
            } 
        }
        
        /// <summary>Эффективная подача с учетом режима движения (G0 vs G1/2/3).</summary>
        public double EffectiveFeedRate { get; private set; }
        
        /// <summary>Скорость вращения шпинделя (S), об/мин.</summary>
        public double SpindleSpeed { get; set; }
        
        /// <summary>Состояние шпинделя: true - включен, false - выключен.</summary>
        public bool IsSpindleOn { get; set; }
        
        /// <summary>Направление вращения: true - по часовой (M3), false - против (M4).</summary>
        public bool IsSpindleCW { get; set; }
        
        /// <summary>Состояние системы охлаждения (СОЖ).</summary>
        public bool IsCoolantOn { get; set; }

        public int? ToolNumber { get; set; }
        public int? ToolLengthOffset { get; set; }
        public int? ToolRadiusOffset { get; set; }

        public MachineState() => Reset();

        /// <summary>Обновляет эффективную подачу в зависимости от режима движения.</summary>
        private void UpdateEffectiveFeed()
        {
            if (CurrentMotionMode.Number == 0)
                EffectiveFeedRate = RAPID_FEED;
            else
                EffectiveFeedRate = _programmedFeedRate;
        }

        /// <summary>Сброс состояния станка к начальным значениям.</summary>
        /// <summary>
        /// Сброс состояния станка к начальным значениям.
        /// Устанавливает координаты в Home, сбрасывает подачу, обороты и активные модальные группы.
        /// </summary>
        public void Reset()
        {
            IsAbsolute = true;
            IsMetric = true;
            CurrentCoordinateSystem = GCodeRegistry.G54;
            CurrentPlane = GCodeRegistry.G17;
            _currentMotionMode = GCodeRegistry.G0;
            CutterCompensation = GCodeRegistry.G40;
            ToolLengthCompensation = GCodeRegistry.G49;

            X = PreviousX = HOME_X;
            Y = PreviousY = HOME_Y;
            Z = PreviousZ = HOME_Z;
            A = B = C = 0;
            PreviousA = PreviousB = PreviousC = 0;

            _programmedFeedRate = 0;
            EffectiveFeedRate = RAPID_FEED;
            SpindleSpeed = 0;
            IsSpindleOn = false;
            IsSpindleCW = false;
            IsCoolantOn = false;
            ToolNumber = null;
            ToolLengthOffset = null;
            ToolRadiusOffset = null;
        }

        /// <summary>
        /// Сохраняет текущие координаты как предыдущие.
        /// Используется перед выполнением новой команды перемещения.
        /// </summary>
        public void SavePreviousPosition()
        {
            PreviousX = X;
            PreviousY = Y;
            PreviousZ = Z;
            PreviousA = A;
            PreviousB = B;
            PreviousC = C;
        }

        /// <summary>
        /// Обновляет позицию станка на основе переданных координат.
        /// Учитывает текущий режим позиционирования (абсолютный G90 или относительный G91).
        /// </summary>
        /// <param name="x">Новая координата X (опционально).</param>
        /// <param name="y">Новая координата Y (опционально).</param>
        /// <param name="z">Новая координата Z (опционально).</param>
        public void UpdatePosition(double? x, double? y, double? z, double? a = null, double? b = null, double? c = null)
        {
            SavePreviousPosition();

            if (x.HasValue) X = IsAbsolute ? x.Value : X + x.Value;
            if (y.HasValue) Y = IsAbsolute ? y.Value : Y + y.Value;
            if (z.HasValue) Z = IsAbsolute ? z.Value : Z + z.Value;
            if (a.HasValue) A = IsAbsolute ? a.Value : A + a.Value;
            if (b.HasValue) B = IsAbsolute ? b.Value : B + b.Value;
            if (c.HasValue) C = IsAbsolute ? c.Value : C + c.Value;
        }

        /// <summary>
        /// Принудительно устанавливает абсолютную позицию станка.
        /// </summary>
        public void SetPosition(double x, double y, double z, double a = 0, double b = 0, double c = 0)
        {
            SavePreviousPosition();
            X = x;
            Y = y;
            Z = z;
            A = a;
            B = b;
            C = c;
        }

        /// <summary>Устанавливает режим позиционирования (G90/G91).</summary>
        public void SetCoordinateMode(bool isAbsolute) => IsAbsolute = isAbsolute;
        
        /// <summary>Устанавливает единицы измерения (G20/G21).</summary>
        public void SetUnits(bool isMetric) => IsMetric = isMetric;

        /// <summary>Устанавливает активную рабочую систему координат (G54-G59).</summary>
        public void SetCoordinateSystem(GCodeTemplate system)
        {
            if (system.Letter == GCodeRegistry.LETTER_G && system.Number is >= 54 and <= 59)
                CurrentCoordinateSystem = system;
        }

        /// <summary>Устанавливает активную плоскость интерполяции (G17-G19).</summary>
        public void SetPlane(GCodeTemplate plane)
        {
            if (plane.Letter == GCodeRegistry.LETTER_G && plane.Number is >= 17 and <= 19)
                CurrentPlane = plane;
        }

        /// <summary>Устанавливает текущий режим движения (G0, G1, G2, G3).</summary>
        public void SetMotionMode(GCodeTemplate mode)
        {
            if (GCodeRegistry.IsMovementCode(mode))
                CurrentMotionMode = mode;
        }

        /// <summary>Устанавливает значение рабочей подачи.</summary>
        public void SetFeedRate(double feedRate) => FeedRate = feedRate;
        
        /// <summary>Устанавливает скорость вращения шпинделя.</summary>
        public void SetSpindleSpeed(double speed) => SpindleSpeed = speed;

        /// <summary>Управляет состоянием шпинделя (вкл/выкл, направление).</summary>
        public void SetSpindle(bool on, bool clockwise = true)
        {
            IsSpindleOn = on;
            IsSpindleCW = clockwise;
        }

        /// <summary>Управляет состоянием системы охлаждения.</summary>
        public void SetCoolant(bool on) => IsCoolantOn = on;

        /// <summary>Устанавливает активный инструмент и его корректоры.</summary>
        public void SetTool(int? toolNumber, int? lengthOffset = null, int? radiusOffset = null)
        {
            ToolNumber = toolNumber;
            ToolLengthOffset = lengthOffset;
            ToolRadiusOffset = radiusOffset;
        }

        /// <summary>Устанавливает режим коррекции на радиус инструмента.</summary>
        public void SetCutterCompensation(GCodeTemplate compensation)
        {
            if (compensation.Letter == GCodeRegistry.LETTER_G && compensation.Number is >= 40 and <= 42)
                CutterCompensation = compensation;
        }

        /// <summary>Устанавливает режим коррекции на длину инструмента.</summary>
        public void SetToolLengthCompensation(GCodeTemplate compensation)
        {
            if (compensation.Letter == GCodeRegistry.LETTER_G &&
                compensation.Number is 43 or 44 or 49)
                ToolLengthCompensation = compensation;
        }

        /// <summary>Возвращает текущие координаты всех осей в виде массива.</summary>
        public double[] GetPosition() => new[] { X, Y, Z, A, B, C };
        
        /// <summary>Возвращает текущие координаты X, Y, Z.</summary>
        public double[] GetXYZ() => new[] { X, Y, Z };

        /// <summary>Возвращает предыдущие координаты всех осей.</summary>
        public double[] GetPreviousPosition() =>
            new[] { PreviousX, PreviousY, PreviousZ, PreviousA, PreviousB, PreviousC };

        /// <summary>Проверяет, изменилась ли позиция станка относительно предыдущей.</summary>
        public bool HasPositionChanged(double tolerance = 0.001)
        {
            return Math.Abs(X - PreviousX) > tolerance ||
                   Math.Abs(Y - PreviousY) > tolerance ||
                   Math.Abs(Z - PreviousZ) > tolerance ||
                   Math.Abs(A - PreviousA) > tolerance ||
                   Math.Abs(B - PreviousB) > tolerance ||
                   Math.Abs(C - PreviousC) > tolerance;
        }

        /// <summary>Возвращает вектор перемещения (дельту) по всем осям.</summary>
        public double[] GetDelta() =>
            new[]
            {
                X - PreviousX, Y - PreviousY, Z - PreviousZ,
                A - PreviousA, B - PreviousB, C - PreviousC
            };

        /// <summary>Создает полную копию текущего состояния станка.</summary>
        public MachineState Clone()
        {
            return new MachineState
            {
                IsAbsolute = this.IsAbsolute,
                IsMetric = this.IsMetric,
                CurrentCoordinateSystem = this.CurrentCoordinateSystem,
                CurrentPlane = this.CurrentPlane,
                CurrentMotionMode = this.CurrentMotionMode,
                CutterCompensation = this.CutterCompensation,
                ToolLengthCompensation = this.ToolLengthCompensation,
                X = this.X,
                Y = this.Y,
                Z = this.Z,
                A = this.A,
                B = this.B,
                C = this.C,
                PreviousX = this.PreviousX,
                PreviousY = this.PreviousY,
                PreviousZ = this.PreviousZ,
                PreviousA = this.PreviousA,
                PreviousB = this.PreviousB,
                PreviousC = this.PreviousC,
                _programmedFeedRate = this._programmedFeedRate,
                EffectiveFeedRate = this.EffectiveFeedRate,
                SpindleSpeed = this.SpindleSpeed,
                IsSpindleOn = this.IsSpindleOn,
                IsSpindleCW = this.IsSpindleCW,
                IsCoolantOn = this.IsCoolantOn,
                ToolNumber = this.ToolNumber,
                ToolLengthOffset = this.ToolLengthOffset,
                ToolRadiusOffset = this.ToolRadiusOffset
            };
        }
}
}
