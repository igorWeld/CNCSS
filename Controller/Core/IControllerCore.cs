namespace CNCSS.Controller.Core
{
    public interface IControllerCore
    {
        bool CycleStart();
        bool FeedHold();
        void Reset();
        bool SetMode(string mode);
        bool Jog(string axis, double delta);
        bool ExecuteMdi(string command);
    }
}
