namespace CNCSS.Machine.Core
{
    /// <summary>Абстракция «станок»: координаты осей и реакции на симулированное время и команды.</summary>
    public interface IMachineCore
    {
        /// <summary>Шаг симуляции (интервал в секундах).</summary>
        void Tick(double deltaTimeSeconds);

        /// <summary>Непосредственная установка положения по осям (мм).</summary>
        void UpdatePosition(double x, double y, double z);

        /// <summary>Сброс внутренних параметров симулятора осей.</summary>
        void Reset();

        /// <summary>Переход в домашнюю точку процесса.</summary>
        void Home();
    }
}
