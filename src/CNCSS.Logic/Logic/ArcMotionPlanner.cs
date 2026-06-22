using CNCSS.Data;

namespace CNCSS.Logic
{
    /// <summary>
    /// Дуги G2/G3: I/J/K в координатах заготовки (WCS), затем перевод в машинные координаты осей.
    /// </summary>
    public static class ArcMotionPlanner
    {
        public static bool TryComputeMachineArc(
            MachineState state,
            double machineStartX,
            double machineStartY,
            double machineStartZ,
            double machineEndX,
            double machineEndY,
            double machineEndZ,
            double? commandedX,
            double? commandedY,
            double? commandedZ,
            GCodeTemplate plane,
            bool isG2,
            double? i,
            double? j,
            double? k,
            double? r,
            out ArcGeometry? machineArc)
        {
            machineArc = null;
            ResolveWorkpieceArcEndpoints(
                state,
                machineStartX, machineStartY, machineStartZ,
                machineEndX, machineEndY, machineEndZ,
                commandedX, commandedY, commandedZ,
                out double wsx, out double wsy, out double wsz,
                out double wex, out double wey, out double wez);

            if (!ArcCalculator.TryComputeArc(
                    wsx, wsy, wsz,
                    wex, wey, wez,
                    plane,
                    isG2,
                    i, j, k, r,
                    out ArcGeometry? workArc))
            {
                return false;
            }

            machineArc = ConvertWorkArcToMachine(state, workArc!);
            return true;
        }

        private static void ResolveWorkpieceArcEndpoints(
            MachineState state,
            double machineStartX,
            double machineStartY,
            double machineStartZ,
            double machineEndX,
            double machineEndY,
            double machineEndZ,
            double? commandedX,
            double? commandedY,
            double? commandedZ,
            out double workpieceStartX,
            out double workpieceStartY,
            out double workpieceStartZ,
            out double workpieceEndX,
            out double workpieceEndY,
            out double workpieceEndZ)
        {
            state.MachineAxisToWorkpieceTip(machineStartX, machineStartY, machineStartZ,
                out workpieceStartX, out workpieceStartY, out workpieceStartZ);
            state.MachineAxisToWorkpieceTip(machineEndX, machineEndY, machineEndZ,
                out double machineEndWorkpieceX, out double machineEndWorkpieceY, out double machineEndWorkpieceZ);

            workpieceEndX = commandedX.HasValue
                ? (state.IsAbsolute ? commandedX.Value : workpieceStartX + commandedX.Value)
                : machineEndWorkpieceX;
            workpieceEndY = commandedY.HasValue
                ? (state.IsAbsolute ? commandedY.Value : workpieceStartY + commandedY.Value)
                : machineEndWorkpieceY;
            workpieceEndZ = commandedZ.HasValue
                ? (state.IsAbsolute ? commandedZ.Value : workpieceStartZ + commandedZ.Value)
                : machineEndWorkpieceZ;
        }

        private static ArcGeometry ConvertWorkArcToMachine(MachineState state, ArcGeometry work)
        {
            state.ProgramTipToMachine(work.StartX, work.StartY, work.StartZ, out double msx, out double msy, out double msz);
            state.ProgramTipToMachine(work.EndX, work.EndY, work.EndZ, out double mex, out double mey, out double mez);

            WorkCenterToMachine(state, work, out double mcu, out double mcv);

            (double startAngle, double sweep) = ArcCalculator.ComputeMachinePlaneAngles(
                work.Plane, msx, msy, msz, mex, mey, mez, mcu, mcv, work.IsClockwise);

            return new ArcGeometry
            {
                Plane = work.Plane,
                Radius = work.Radius,
                CenterU = mcu,
                CenterV = mcv,
                StartAngleRad = startAngle,
                SweepAngleRad = sweep,
                IsClockwise = work.IsClockwise,
                StartX = msx,
                StartY = msy,
                StartZ = msz,
                EndX = mex,
                EndY = mey,
                EndZ = mez
            };
        }

        private static void WorkCenterToMachine(MachineState state, ArcGeometry work, out double machineU, out double machineV)
        {
            switch (work.Plane)
            {
                case 17:
                    state.ProgramTipToMachine(work.CenterU, work.CenterV, work.StartZ, out machineU, out machineV, out _);
                    break;
                case 18:
                    state.ProgramTipToMachine(work.CenterU, work.StartY, work.CenterV, out machineU, out _, out machineV);
                    break;
                case 19:
                    state.ProgramTipToMachine(work.StartX, work.CenterU, work.CenterV, out _, out machineU, out machineV);
                    break;
                default:
                    machineU = work.CenterU;
                    machineV = work.CenterV;
                    break;
            }
        }
    }
}
