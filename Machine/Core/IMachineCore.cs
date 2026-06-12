using CNCSS.Data;
using CNCSS.Machine.Model;

namespace CNCSS.Machine.Core
{
    /// <summary>Абстракция «станок»: координаты осей и реакции на симулированное время и команды.</summary>
    public interface IMachineCore
    {
        MachineAxesState State { get; }

        /// <summary>Шаг симуляции (интервал в секундах).</summary>
        void Tick(double deltaTimeSeconds);

        /// <summary>Непосредственная установка положения по осям (мм).</summary>
        void UpdatePosition(double x, double y, double z);

        /// <summary>Сброс внутренних параметров симулятора осей.</summary>
        void Reset();

        /// <summary>Переход в HOME (физический ноль MCS из профиля).</summary>
        void Home();

        /// <summary>Физические координаты HOME (ноль MCS + HOME осей из профиля).</summary>
        (double X, double Y, double Z) GetHomePhysicalPosition();

        /// <summary>Переход в заданную физическую точку (мм).</summary>
        void HomeTo(double x, double y, double z);

        /// <summary>Обновляет soft limits из профиля станка.</summary>
        void UpdateKinematics(Vmc3AxisKinematicsModel kinematics);

        /// <summary>Синхронизация runtime-состояния из рассчитанного состояния парсера/исполнения.</summary>
        void SyncRuntimeFromState(MachineState source);

        /// <summary>Применяет HOME и MCS-ноль из профиля станка к парсеру (G28/M6).</summary>
        void ApplyProfileHome(MachineDefinition profile);

        /// <summary>Копирует таблицы корректоров H/D из контроллера (MDI) в целевое состояние.</summary>
        void CopyToolOffsetTablesTo(MachineState target);

        /// <summary>Записывает корректор H/D в таблицу контроллера (источник для G43/G41).</summary>
        void SetToolOffsetRow(int row, double? geomH = null, double? wearH = null, double? geomD = null, double? wearD = null);

        /// <summary>Снимок таблицы корректоров для отображения на панели OFFSET.</summary>
        MachineState GetOffsetTableState();
    }
}
