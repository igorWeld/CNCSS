namespace CNCSS.UI.ViewModels
{
    public sealed class MachineProfileListItem
    {
        public MachineProfileListItem(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }

        public string DisplayName { get; set; }
    }
}
