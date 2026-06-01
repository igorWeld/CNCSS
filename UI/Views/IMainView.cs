using CNCSS.Data;
using CNCSS.Logic.ProgramLoading;

namespace CNCSS.UI.Views
{
    /// <summary>
    /// Интерфейс представления главного окна.
    /// Определяет методы для обновления UI из презентера.
    /// </summary>
    public interface IMainView
    {
        void BindProgram(ProgramLoadResult loadResult);
        void ClearProgramView();
        void SelectProgramLine(int index);
        void ApplyLineState(MachineState state);
        void SetCycleButtons(bool canStart, bool canPause);
        void ShowError(string message);
        void SetStatus(string message);
    }
}
