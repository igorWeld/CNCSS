namespace CNCSS.Data
{
    public enum CommandModality
    {
        NonModal,
        Modal
    }

    public struct GCodeTemplate : IEquatable<GCodeTemplate>
    {
        public string Letter { get; }
        public int Number { get; }
        public CommandModality Modality { get; }

        public string FullCode => $"{Letter}{Number}";

        public GCodeTemplate(string letter, int number, CommandModality modality = CommandModality.Modal)
        {
            Letter = letter?.ToUpper() ?? string.Empty;
            Number = number;
            Modality = modality;
        }

        public bool Equals(GCodeTemplate other) => Letter == other.Letter && Number == other.Number;

        public override bool Equals(object? obj) => obj is GCodeTemplate other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Letter, Number);

        public static bool operator ==(GCodeTemplate left, GCodeTemplate right) => left.Equals(right);

        public static bool operator !=(GCodeTemplate left, GCodeTemplate right) => !(left == right);

        public override string ToString() => FullCode;

        public static GCodeTemplate? Parse(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 2)
                return null;

            code = code.Trim().ToUpper();
            string letter = code[0].ToString();

            if (letter != "G" && letter != "M")
                return null;

            return int.TryParse(code.AsSpan(1), out int number)
                ? new GCodeTemplate(letter, number)
                : null;
        }
    }
}
