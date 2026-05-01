namespace CNCSS.Controller.Core
{
    /// <summary>Эмуляция ключевых действий оператора над ЧПУ-контроллером (цикл, удержание, режимы, jog, MDI).</summary>
    public interface IControllerCore
    {
        /// <summary>Старт или продолжение автоматического цикла (при допустимом режиме).</summary>
        bool CycleStart();

        /// <summary>Удержание подачи (пауза обработки).</summary>
        bool FeedHold();

        /// <summary>Сброс состояния контроллера до начальных условий.</summary>
        void Reset();

        /// <summary>Смена экранного/логического режима станка (<c>MEM</c>, <c>MDI</c>, <c>JOG</c> и т.д.).</summary>
        bool SetMode(string mode);

        /// <summary>Ручное инкрементальное смещение по оси в режиме jog/handle.</summary>
        bool Jog(string axis, double delta);

        /// <summary>Выполнение одной строки в MDI.</summary>
        bool ExecuteMdi(string command);
    }
}
