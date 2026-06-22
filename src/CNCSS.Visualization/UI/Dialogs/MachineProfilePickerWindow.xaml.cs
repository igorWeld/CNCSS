using System.Windows;
using CNCSS.Machine.Configuration;

namespace CNCSS.UI.Dialogs
{
    public partial class MachineProfilePickerWindow : Window
    {
        private readonly MachineProfileService _profileService;
        private readonly Action _openSetup;

        public MachineProfilePickerWindow(MachineProfileService profileService, Action openSetup)
        {
            InitializeComponent();
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _openSetup = openSetup ?? throw new ArgumentNullException(nameof(openSetup));
            ProfilesList.ItemsSource = _profileService.ListProfileIds();
            ProfilesList.SelectedItem = _profileService.ActiveProfile.ProfileId;
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesList.SelectedItem is not string profileId)
            {
                MessageBox.Show(this, "Выберите профиль.", "Профиль станка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _profileService.SetActiveProfile(profileId);
            DialogResult = true;
            Close();
        }

        private void Setup_Click(object sender, RoutedEventArgs e)
        {
            _openSetup();
            ProfilesList.ItemsSource = null;
            ProfilesList.ItemsSource = _profileService.ListProfileIds();
            ProfilesList.SelectedItem = _profileService.ActiveProfile.ProfileId;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
