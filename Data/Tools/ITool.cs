namespace CNCSS.Data.Tools
{
    public enum ToolType
    {
        EndMill,
        FaceMill,
        Drill
    }

    /// <summary>
    /// Интерфейс для всех типов режущего инструмента.
    /// Определяет геометрические параметры, необходимые для визуализации и расчета съема материала.
    /// </summary>
    public interface ITool
    {
        /// <summary>Уникальный номер инструмента в магазине (T).</summary>
        int Number { get; }
        
        /// <summary>Название или описание инструмента.</summary>
        string Name { get; }
        
        /// <summary>Рабочий диаметр режущей части, мм.</summary>
        double Diameter { get; }
        
        /// <summary>Диаметр хвостовика, мм.</summary>
        double ShankDiameter { get; }
        
        /// <summary>Длина режущей части (флейты), мм.</summary>
        double FluteLength { get; }
        
        /// <summary>Общая длина инструмента от торца до патрона, мм.</summary>
        double OverallLength { get; }
        
        /// <summary>Количество режущих кромок (зубьев).</summary>
        int Flutes { get; }
    }


    public abstract class ToolBase : ITool
    {
        public int Number { get; set; }
        public string Name { get; set; } = "New Tool";
        public abstract ToolType Type { get; }
        public double Diameter { get; set; }
        public double ShankDiameter { get; set; }
        public double FluteLength { get; set; }
        public double OverallLength { get; set; }
        public int Flutes { get; set; }

        protected ToolBase(double diameter, double fluteLength, double overallLength)
        {
            Diameter = diameter;
            FluteLength = fluteLength;
            OverallLength = overallLength;
            ShankDiameter = diameter;
            Flutes = 2;
        }
    }}
