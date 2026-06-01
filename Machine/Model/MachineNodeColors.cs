using System.Windows.Media;

namespace CNCSS.Machine.Model
{
    /// <summary>Default mesh colors for built-in machine nodes (packed ARGB).</summary>
    public static class MachineNodeColors
    {
        public const uint BasePaleBlue = 0xFFADD8E6;
        public const uint TablePurple = 0xFF9370DB;
        public const uint SpindleGreen = 0xFF32CD32;

        public static uint ForKind(MachineNodeKind kind) => kind switch
        {
            MachineNodeKind.Base => BasePaleBlue,
            MachineNodeKind.Table => TablePurple,
            MachineNodeKind.Spindle => SpindleGreen,
            _ => 0
        };

        public static Color ToMediaColor(uint packedArgb)
        {
            if (packedArgb == 0)
            {
                return Colors.Transparent;
            }

            return Color.FromArgb(
                (byte)(packedArgb >> 24),
                (byte)(packedArgb >> 16),
                (byte)(packedArgb >> 8),
                (byte)packedArgb);
        }

        public static void ApplyDefaultBuiltInMeshColors(MachineDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.Base.MeshColorArgb == 0)
            {
                definition.Base.MeshColorArgb = BasePaleBlue;
            }

            if (definition.Table.MeshColorArgb == 0)
            {
                definition.Table.MeshColorArgb = TablePurple;
            }

            if (definition.Spindle.MeshColorArgb == 0)
            {
                definition.Spindle.MeshColorArgb = SpindleGreen;
            }
        }
    }
}
