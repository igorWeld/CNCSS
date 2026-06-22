namespace CNCSS.Data
{
    /// <summary>
    /// Основное состояние станка с ЧПУ.
    /// Хранит текущие координаты, режимы движения, параметры подачи и шпинделя.
    /// </summary>
    public class MachineState
    {
        public const int MinWorkOffsetNumber = 54;
        public const int MaxWorkOffsetNumber = 59;

        public sealed class WorkOffset
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }

            public WorkOffset Clone()
            {
                return new WorkOffset { X = X, Y = Y, Z = Z };
            }
        }

        /// <summary>Скорость ускоренного перемещения (G0) по умолчанию, мм/мин.</summary>
        public static double RAPID_FEED = ProjectConstants.DEFAULT_RAPID_FEED;

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

        /// <summary>
        /// Ось Z отражает целевую позицию с учётом G43/G44 (после блока с Z).
        /// После G43/G44 без Z в блоке — false (физическая Z ещё без пересчёта под H).
        /// </summary>
        public bool IsMachineZSyncedWithLengthComp { get; set; }

        // Текущие координаты осей (физические, мм)
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }

        // Предыдущие координаты (для расчета перемещений)
        public double PreviousX { get; private set; }
        public double PreviousY { get; private set; }
        public double PreviousZ { get; private set; }
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

        /// <summary>
        /// Признак, что станок прошёл референс (ZRN) и машинная система координат (MCS) валидна.
        /// В симуляции используется для будущих ограничений G53/абсолютных расчётов.
        /// </summary>
        public bool IsMachineReferenced { get; set; }

        /// <summary>Таблица коррекций длины инструмента (H): GEOM + WEAR.</summary>
        public Dictionary<int, double> ToolLengthGeom { get; } = new();
        public Dictionary<int, double> ToolLengthWear { get; } = new();

        /// <summary>Таблица коррекций радиуса инструмента (D): GEOM + WEAR.</summary>
        public Dictionary<int, double> ToolRadiusGeom { get; } = new();
        public Dictionary<int, double> ToolRadiusWear { get; } = new();
        public Dictionary<int, WorkOffset> WorkOffsets { get; } = new();

        /// <summary>
        /// Смещение нуля машинной системы координат (MCS) относительно физической (сырой) позиции осей.
        /// Физическая_позиция = MCS_позиция + MachineZeroOffset.
        /// </summary>
        public double MachineZeroOffsetX { get; set; }
        public double MachineZeroOffsetY { get; set; }
        public double MachineZeroOffsetZ { get; set; }

        /// <summary>HOME по осям в MCS (мм) из профиля станка. Физическая цель G28/M6 = это + MachineZeroOffsetX/Y/Z.</summary>
        public double AxisHomeMcsX { get; set; }
        public double AxisHomeMcsY { get; set; }
        public double AxisHomeMcsZ { get; set; }

        /// <summary>Физическая позиция нуля MCS (HOME = MCS + смещение профиля по осям).</summary>
        public (double X, double Y, double Z) GetG28PhysicalPosition() =>
            (
                AxisHomeMcsX + MachineZeroOffsetX,
                AxisHomeMcsY + MachineZeroOffsetY,
                AxisHomeMcsZ + MachineZeroOffsetZ);

        /// <summary>Устанавливает физические координаты в HOME (ноль MCS).</summary>
        public void SetPositionToMcsHome()
        {
            var (x, y, z) = GetG28PhysicalPosition();
            SetPosition(x, y, z, A, B, C);
        }

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
        /// Устанавливает координаты в ноль MCS, сбрасывает подачу, обороты и активные модальные группы.
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
            IsMachineZSyncedWithLengthComp = false;

            var (homeX, homeY, homeZ) = GetG28PhysicalPosition();
            X = PreviousX = homeX;
            Y = PreviousY = homeY;
            Z = PreviousZ = homeZ;
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
            IsMachineReferenced = false;
            MachineZeroOffsetX = 0;
            MachineZeroOffsetY = 0;
            MachineZeroOffsetZ = 0;
            AxisHomeMcsX = 0;
            AxisHomeMcsY = 0;
            AxisHomeMcsZ = 0;
            ResetWorkOffsets();
            ToolLengthGeom.Clear();
            ToolLengthWear.Clear();
            ToolRadiusGeom.Clear();
            ToolRadiusWear.Clear();
        }

        public void SetMachineZeroFromPhysical(double physicalX, double physicalY, double physicalZ)
        {
            MachineZeroOffsetX = physicalX;
            MachineZeroOffsetY = physicalY;
            MachineZeroOffsetZ = physicalZ;
        }

        public void ResetMachineZero()
        {
            MachineZeroOffsetX = 0;
            MachineZeroOffsetY = 0;
            MachineZeroOffsetZ = 0;
        }

        public void SetToolLengthOffsetValue(int h, double? geom = null, double? wear = null)
        {
            if (h <= 0) return;
            if (geom.HasValue) ToolLengthGeom[h] = geom.Value;
            if (wear.HasValue) ToolLengthWear[h] = wear.Value;
        }

        public void SetToolRadiusOffsetValue(int d, double? geom = null, double? wear = null)
        {
            if (d <= 0) return;
            if (geom.HasValue) ToolRadiusGeom[d] = geom.Value;
            if (wear.HasValue) ToolRadiusWear[d] = wear.Value;
        }

        public double GetEffectiveToolLength(int h)
        {
            if (h <= 0) return 0;
            ToolLengthGeom.TryGetValue(h, out double g);
            ToolLengthWear.TryGetValue(h, out double w);
            return g + w;
        }

        /// <summary>
        /// Сдвигает целевую машинную координату Z с учётом активной коррекции на длину (G43/G44) и номера H.
        /// </summary>
        public void ApplyToolLengthCompensationToMachineZ(ref double machineZ)
        {
            if (ToolLengthCompensation.Number is not (43 or 44))
            {
                return;
            }

            if (!ToolLengthOffset.HasValue)
            {
                return;
            }

            double h = GetEffectiveToolLength(ToolLengthOffset.Value);
            if (Math.Abs(h) <= 1e-9)
            {
                return;
            }

            // Z+ вверх: при G43 положительная H поднимает шпиндель (+Z), кончик остаётся в запрограммированной Z.
            machineZ += ToolLengthCompensation.Number == 44 ? -h : h;
        }

        /// <summary>
        /// Машинная Z из слова Z блока: G90 — абсолютная координата кончика в WCS; G91 — приращение кончика в WCS.
        /// Учитывает G43/G44, если модальность активна.
        /// </summary>
        public double ResolveMachineZFromProgramValue(double programZValue, double currentMachineZ)
        {
            GetProgramToMachineShift(out _, out _, out double shiftZ);

            double workpieceTipZ;
            if (IsAbsolute)
            {
                workpieceTipZ = programZValue;
            }
            else
            {
                MachineAxisToWorkpieceTip(X, Y, currentMachineZ, out _, out _, out double currentTipZ);
                workpieceTipZ = currentTipZ + programZValue;
            }

            double machineZ = workpieceTipZ + shiftZ;
            ApplyToolLengthCompensationToMachineZ(ref machineZ);
            return machineZ;
        }

        /// <summary>Смещение WCS + ноль MCS для абсолютного режима (мм).</summary>
        public void GetProgramToMachineShift(out double shiftX, out double shiftY, out double shiftZ)
        {
            var wcs = GetActiveWorkOffset();
            double mcsX = IsAbsolute ? MachineZeroOffsetX : 0;
            double mcsY = IsAbsolute ? MachineZeroOffsetY : 0;
            double mcsZ = IsAbsolute ? MachineZeroOffsetZ : 0;
            shiftX = wcs.X + mcsX;
            shiftY = wcs.Y + mcsY;
            shiftZ = wcs.Z + mcsZ;
        }

        /// <summary>
        /// Машинные координаты осей → координаты кончика в WCS (X/Y/Z УП) для G2/G3 (I/J/K в ISO 6983).
        /// По Z учитывается H только если <see cref="IsMachineZSyncedWithLengthComp"/>.
        /// </summary>
        public void MachineAxisToWorkpieceTip(double machineX, double machineY, double machineZ, out double workpieceX, out double workpieceY, out double workpieceZ)
        {
            GetProgramToMachineShift(out double shiftX, out double shiftY, out double shiftZ);
            workpieceX = machineX - shiftX;
            workpieceY = machineY - shiftY;
            workpieceZ = machineZ - shiftZ;
            if (IsMachineZSyncedWithLengthComp)
            {
                ApplyInverseToolLengthCompensationToProgramZ(ref workpieceZ);
            }
        }

        /// <summary>Машинные координаты осей → координаты кончика в программе (G43/G44 учитываются по Z).</summary>
        public void MachineTipToProgram(double machineX, double machineY, double machineZ, out double programX, out double programY, out double programZ)
        {
            GetProgramToMachineShift(out double shiftX, out double shiftY, out double shiftZ);
            programX = machineX - shiftX;
            programY = machineY - shiftY;
            programZ = machineZ - shiftZ;
            ApplyInverseToolLengthCompensationToProgramZ(ref programZ);
        }

        /// <summary>Координаты кончика в программе → машинные координаты осей.</summary>
        public void ProgramTipToMachine(double programX, double programY, double programZ, out double machineX, out double machineY, out double machineZ)
        {
            GetProgramToMachineShift(out double shiftX, out double shiftY, out double shiftZ);
            machineX = programX + shiftX;
            machineY = programY + shiftY;
            machineZ = programZ + shiftZ;
            ApplyToolLengthCompensationToMachineZ(ref machineZ);
        }

        private void ApplyInverseToolLengthCompensationToProgramZ(ref double programZ)
        {
            if (ToolLengthCompensation.Number is not (43 or 44) || !ToolLengthOffset.HasValue)
            {
                return;
            }

            double h = GetEffectiveToolLength(ToolLengthOffset.Value);
            if (Math.Abs(h) <= 1e-9)
            {
                return;
            }

            programZ += ToolLengthCompensation.Number == 44 ? h : -h;
        }

        /// <summary>Копирует таблицы корректоров H/D из другого снимка состояния.</summary>
        public void CopyToolOffsetTablesFrom(MachineState source)
        {
            ToolLengthGeom.Clear();
            ToolLengthWear.Clear();
            ToolRadiusGeom.Clear();
            ToolRadiusWear.Clear();
            foreach (var pair in source.ToolLengthGeom)
            {
                ToolLengthGeom[pair.Key] = pair.Value;
            }

            foreach (var pair in source.ToolLengthWear)
            {
                ToolLengthWear[pair.Key] = pair.Value;
            }

            foreach (var pair in source.ToolRadiusGeom)
            {
                ToolRadiusGeom[pair.Key] = pair.Value;
            }

            foreach (var pair in source.ToolRadiusWear)
            {
                ToolRadiusWear[pair.Key] = pair.Value;
            }
        }

        public double GetEffectiveToolRadius(int d)
        {
            if (d <= 0) return 0;
            ToolRadiusGeom.TryGetValue(d, out double g);
            ToolRadiusWear.TryGetValue(d, out double w);
            return g + w;
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
        /// <param name="a">Ось A в программе (опционально).</param>
        /// <param name="b">Ось B в программе (опционально).</param>
        /// <param name="c">Ось C в программе (опционально).</param>
        public void UpdatePosition(double? x, double? y, double? z, double? a = null, double? b = null, double? c = null)
        {
            SavePreviousPosition();

            var activeOffset = GetActiveWorkOffset();

            if (x.HasValue) X = IsAbsolute ? x.Value + activeOffset.X : X + x.Value;
            if (y.HasValue) Y = IsAbsolute ? y.Value + activeOffset.Y : Y + y.Value;
            if (z.HasValue) Z = IsAbsolute ? z.Value + activeOffset.Z : Z + z.Value;
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

        public WorkOffset GetActiveWorkOffset()
        {
            int systemNumber = CurrentCoordinateSystem.Number;
            if (!WorkOffsets.TryGetValue(systemNumber, out var offset))
            {
                offset = new WorkOffset();
                WorkOffsets[systemNumber] = offset;
            }

            return offset;
        }

        public WorkOffset GetWorkOffset(int systemNumber)
        {
            if (systemNumber < MinWorkOffsetNumber || systemNumber > MaxWorkOffsetNumber)
            {
                throw new ArgumentOutOfRangeException(nameof(systemNumber), $"Expected G{MinWorkOffsetNumber}..G{MaxWorkOffsetNumber}");
            }

            if (!WorkOffsets.TryGetValue(systemNumber, out var offset))
            {
                offset = new WorkOffset();
                WorkOffsets[systemNumber] = offset;
            }

            return offset;
        }

        public void SetWorkOffset(int systemNumber, double? x = null, double? y = null, double? z = null)
        {
            var target = GetWorkOffset(systemNumber);
            if (x.HasValue) target.X = x.Value;
            if (y.HasValue) target.Y = y.Value;
            if (z.HasValue) target.Z = z.Value;
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
            {
                ToolLengthCompensation = compensation;
                if (compensation.Number == 49)
                {
                    IsMachineZSyncedWithLengthComp = false;
                }
            }
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
            var clone = new MachineState
            {
                IsAbsolute = this.IsAbsolute,
                IsMetric = this.IsMetric,
                CurrentCoordinateSystem = this.CurrentCoordinateSystem,
                CurrentPlane = this.CurrentPlane,
                CurrentMotionMode = this.CurrentMotionMode,
                CutterCompensation = this.CutterCompensation,
                ToolLengthCompensation = this.ToolLengthCompensation,
                IsMachineZSyncedWithLengthComp = this.IsMachineZSyncedWithLengthComp,
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
                ToolRadiusOffset = this.ToolRadiusOffset,
                IsMachineReferenced = this.IsMachineReferenced,
                MachineZeroOffsetX = this.MachineZeroOffsetX,
                MachineZeroOffsetY = this.MachineZeroOffsetY,
                MachineZeroOffsetZ = this.MachineZeroOffsetZ,
                AxisHomeMcsX = this.AxisHomeMcsX,
                AxisHomeMcsY = this.AxisHomeMcsY,
                AxisHomeMcsZ = this.AxisHomeMcsZ
            };

            foreach (var pair in WorkOffsets)
            {
                clone.WorkOffsets[pair.Key] = pair.Value.Clone();
            }

            foreach (var pair in ToolLengthGeom) clone.ToolLengthGeom[pair.Key] = pair.Value;
            foreach (var pair in ToolLengthWear) clone.ToolLengthWear[pair.Key] = pair.Value;
            foreach (var pair in ToolRadiusGeom) clone.ToolRadiusGeom[pair.Key] = pair.Value;
            foreach (var pair in ToolRadiusWear) clone.ToolRadiusWear[pair.Key] = pair.Value;

            return clone;
        }

        private void ResetWorkOffsets()
        {
            WorkOffsets.Clear();
            for (int i = MinWorkOffsetNumber; i <= MaxWorkOffsetNumber; i++)
            {
                WorkOffsets[i] = new WorkOffset();
            }
        }
}
}
