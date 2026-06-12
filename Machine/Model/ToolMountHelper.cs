using System.Windows.Media.Media3D;



namespace CNCSS.Machine.Model

{

    /// <summary>Tool holder point on spindle mesh, stored as offset from mesh bbox center (mm).</summary>

    public static class ToolMountHelper

    {

        public static MachineGeometryPoint ToOffsetFromCenter(Point3D meshCenterLocal, Point3D meshLocalPoint) =>

            new()

            {

                X = meshLocalPoint.X - meshCenterLocal.X,

                Y = meshLocalPoint.Y - meshCenterLocal.Y,

                Z = meshLocalPoint.Z - meshCenterLocal.Z

            };



        public static Point3D ToMeshLocal(Point3D meshCenterLocal, MachineGeometryPoint offset) =>

            new(

                meshCenterLocal.X + offset.X,

                meshCenterLocal.Y + offset.Y,

                meshCenterLocal.Z + offset.Z);



        /// <summary>Центр модели в СК узла крепления; Z сохраняется.</summary>
        public static MachineGeometryPoint CenterXY(Point3D centerInNodeFrame, MachineGeometryPoint current) =>
            new()
            {
                X = centerInNodeFrame.X,
                Y = centerInNodeFrame.Y,
                Z = current.Z
            };



        public static MachineGeometryPoint SetZFromFacePick(

            Point3D meshCenterLocal,

            Point3D pickPointMeshLocal,

            MachineGeometryPoint current) =>

            new()

            {

                X = current.X,

                Y = current.Y,

                Z = pickPointMeshLocal.Z - meshCenterLocal.Z

            };

    }

}

