using System.Windows;
using System.Windows.Controls;
using CNCSS.Vis;

namespace CNCSS.UI.Dialogs
{
    public partial class StepImportQualityDialog : Window
    {
        public StepImportQualityDialog(CadMeshQualitySettings current)
        {
            InitializeComponent();
            QualityCombo.ItemsSource = new[]
            {
                CadMeshQualitySettings.ForPreset(CadMeshQualityPreset.Draft),
                CadMeshQualitySettings.ForPreset(CadMeshQualityPreset.Standard),
                CadMeshQualitySettings.ForPreset(CadMeshQualityPreset.Fine),
                CadMeshQualitySettings.ForPreset(CadMeshQualityPreset.ExtraFine)
            };

            SelectedSettings = current;
            SelectPreset(current.Preset);
        }

        public CadMeshQualitySettings SelectedSettings { get; private set; }

        private void SelectPreset(CadMeshQualityPreset preset)
        {
            foreach (object item in QualityCombo.Items)
            {
                if (item is CadMeshQualitySettings settings && settings.Preset == preset)
                {
                    QualityCombo.SelectedItem = item;
                    break;
                }
            }

            UpdateDescription();
        }

        private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QualityCombo.SelectedItem is CadMeshQualitySettings settings)
            {
                SelectedSettings = settings;
            }

            UpdateDescription();
        }

        private void UpdateDescription()
        {
            if (QualityCombo.SelectedItem is not CadMeshQualitySettings settings)
            {
                return;
            }

            QualitySummaryText.Text = settings.ShortSummary;
            QualityDescriptionText.Text = settings.Description;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (QualityCombo.SelectedItem is CadMeshQualitySettings settings)
            {
                SelectedSettings = settings;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
