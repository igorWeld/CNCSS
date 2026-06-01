using System.IO;
using System.Windows;
using CNCSS.Machine.Configuration;
using CNCSS.Vis;

namespace CNCSS
{
    /// <summary>Точка входа WPF: объявление приложения в <c>App.xaml</c>.</summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            OcctNativeLoader.EnsureInitialized();

            if (ShouldBootstrapDefaultMachineProfile())
            {
                try
                {
                    MachineProfileBootstrap.ApplyBundledDefaultMachine(new StlMeshLoader());
                    WriteBootstrapDefaultFlag();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Не удалось создать профиль станка по умолчанию из STEP:\n" + ex.Message,
                        "CNCSS",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            base.OnStartup(e);
        }

        /// <summary>One-time bootstrap: bundled STEP present and bootstrap flag not yet written.</summary>
        private static bool ShouldBootstrapDefaultMachineProfile()
        {
            if (!File.Exists(MachineProfileBootstrap.GetBundledStepPath()))
            {
                return false;
            }

            string flagPath = GetBootstrapDefaultFlagPath();
            return !File.Exists(flagPath);
        }

        private static string GetBootstrapDefaultFlagPath() =>
            Path.Combine(MachineProfileStore.GetDefaultRootDirectory(), ".bootstrap_default_v2");

        private static void WriteBootstrapDefaultFlag()
        {
            try
            {
                Directory.CreateDirectory(MachineProfileStore.GetDefaultRootDirectory());
                File.WriteAllText(GetBootstrapDefaultFlagPath(), DateTime.UtcNow.ToString("O"));
            }
            catch
            {
                // non-fatal
            }
        }
    }
}
