namespace CNCSS.Machine.Core
{
    public interface IMachineCore
    {
        void Tick(double deltaTimeSeconds);
        void UpdatePosition(double x, double y, double z);
        void Reset();
        void Home();
    }
}
