using System;
using System.Collections.Generic;
using CNCSS.Data;
using CNCSS.Logic;

namespace CNCSS.Logic
{
    /// <summary>
    /// Отвечает за применение токенов G-кода к состоянию станка.
    /// Использует паттерн "Команда" для обработки различных типов команд.
    /// </summary>
    public class GCodeInterpreter
    {
        private readonly MachineState _state;
        private readonly ToolpathBuilder _toolpathBuilder;
        private readonly Dictionary<string, Action<GCodeToken>> _handlers;

        public GCodeInterpreter(MachineState state, ToolpathBuilder toolpathBuilder)
        {
            _state = state;
            _toolpathBuilder = toolpathBuilder;
            _handlers = InitializeHandlers();
        }

        private Dictionary<string, Action<GCodeToken>> InitializeHandlers()
        {
            return new Dictionary<string, Action<GCodeToken>>(StringComparer.OrdinalIgnoreCase)
            {
                // Движения
                { "G0", HandleRapidMove },
                { "G1", HandleLinearMove },
                { "G2", HandleArcMoveCW },
                { "G3", HandleArcMoveCCW },
                
                // Плоскости
                { "G17", () => _state.Plane = Plane.XY },
                { "G18", () => _state.Plane = Plane.XZ },
                { "G19", () => _state.Plane = Plane.YZ },
                
                // Единицы измерения
                { "G20", () => _state.Units = Units.Inches },
                { "G21", () => _state.Units = Units.Millimeters },
                
                // Режимы позиционирования
                { "G90", () => _state.DistanceMode = DistanceMode.Absolute },
                { "G91", () => _state.DistanceMode = DistanceMode.Relative },
                
                // Возврат в исходную точку
                { "G28", HandleGoHome },
                
                // Шпиндель
                { "M3", () => _state.SpindleOn = true },
                { "M4", () => _state.SpindleOn = true }, // Обратное вращение (упрощенно)
                { "M5", () => _state.SpindleOn = false },
                
                // Охлаждение
                { "M7", () => _state.CoolantOn = true },
                { "M8", () => _state.CoolantOn = true },
                { "M9", () => _state.CoolantOn = false },
                
                // Завершение программы
                { "M30", HandleProgramEnd },
                { "M2", HandleProgramEnd }
            };
        }

        public void Execute(GCodeToken token)
        {
            if (token.Code == null)
            {
                // Если кода нет, используем последний модальный код
                if (_state.LastCode != null && _handlers.ContainsKey(_state.LastCode))
                {
                    var handler = _handlers[_state.LastCode];
                    handler(token);
                }
                return;
            }

            // Сохраняем текущий код как последний модальный
            _state.LastCode = token.Code;

            if (_handlers.TryGetValue(token.Code, out var handler))
            {
                handler(token);
            }
            else
            {
                // Неизвестная команда - можно логировать или игнорировать
                Console.WriteLine($"Неизвестная команда: {token.Code}");
            }
        }

        public void ExecuteAll(IEnumerable<GCodeToken> tokens)
        {
            foreach (var token in tokens)
            {
                Execute(token);
            }
        }

        #region Handlers

        private void HandleRapidMove(GCodeToken token)
        {
            ApplyMovement(token, isRapid: true);
        }

        private void HandleLinearMove(GCodeToken token)
        {
            ApplyMovement(token, isRapid: false);
        }

        private void HandleArcMoveCW(GCodeToken token)
        {
            ApplyArcMovement(token, clockwise: true);
        }

        private void HandleArcMoveCCW(GCodeToken token)
        {
            ApplyArcMovement(token, clockwise: false);
        }

        private void HandleGoHome(GCodeToken token)
        {
            // Упрощенная реализация: переход в (0,0,0)
            _state.Position = new Vector3(0, 0, 0);
            _toolpathBuilder.AddPoint(_state.Position);
        }

        private void HandleProgramEnd(GCodeToken token)
        {
            _state.IsProgramRunning = false;
            _toolpathBuilder.Finish();
        }

        #endregion

        #region Movement Logic

        private void ApplyMovement(GCodeToken token, bool isRapid)
        {
            var target = CalculateTargetPosition(token);
            
            if (isRapid)
            {
                _state.Position = target;
                _toolpathBuilder.AddPoint(target);
            }
            else
            {
                // Линейная интерполяция с учетом подачи
                _state.Position = target;
                _toolpathBuilder.AddLine(_state.Position);
            }
        }

        private void ApplyArcMovement(GCodeToken token, bool clockwise)
        {
            var target = CalculateTargetPosition(token);
            
            double i = token.Parameters.TryGetValue('I', out var iVal) ? iVal : 0;
            double j = token.Parameters.TryGetValue('J', out var jVal) ? jVal : 0;
            double k = token.Parameters.TryGetValue('K', out var kVal) ? kVal : 0;
            double r = token.Parameters.TryGetValue('R', out var rVal) ? rVal : 0;

            var center = CalculateArcCenter(i, j, k, r, clockwise);
            
            _toolpathBuilder.AddArc(target, center, clockwise);
            _state.Position = target;
        }

        private Vector3 CalculateTargetPosition(GCodeToken token)
        {
            var current = _state.Position;
            var x = current.X;
            var y = current.Y;
            var z = current.Z;

            if (token.Parameters.TryGetValue('X', out var xVal))
                x = _state.DistanceMode == DistanceMode.Absolute ? xVal : current.X + xVal;
            
            if (token.Parameters.TryGetValue('Y', out var yVal))
                y = _state.DistanceMode == DistanceMode.Absolute ? yVal : current.Y + yVal;
            
            if (token.Parameters.TryGetValue('Z', out var zVal))
                z = _state.DistanceMode == DistanceMode.Absolute ? zVal : current.Z + zVal;

            return new Vector3(x, y, z);
        }

        private Vector3 CalculateArcCenter(double i, double j, double k, double r, bool clockwise)
        {
            // Упрощенный расчет центра дуги
            // В реальной реализации нужно учитывать плоскость и радиус
            return new Vector3(i, j, k);
        }

        #endregion
    }
}
