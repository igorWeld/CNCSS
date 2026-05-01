namespace CNCSS.Controller.Model
{
    /// <summary>Виртуальные режимы пульта (MEM, MDI, JOG и т.), влияющие на допустимые действия оператора.</summary>
    public enum ControllerMode
    {
        Edit,
        Mem,
        Mdi,
        Jog,
        Handle,
        ZeroReturn
    }
}
