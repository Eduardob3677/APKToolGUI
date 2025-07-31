using System;
using System.IO;
using System.Windows.Forms;
using APKToolGUI.ApkTool;
using APKToolGUI.Languages;
using APKToolGUI.Properties;
using APKToolGUI.Utils;

namespace APKToolGUI.Handlers
{
    /// <summary>
    /// Manejadores de eventos para controles relacionados con AAB (Android App Bundle)
    /// </summary>
    public partial class FormMain
    {
        private AabConverter aabConverter;

        /// <summary>
        /// Inicializa los componentes AAB
        /// </summary>
        private void InitializeAabComponents()
        {
            // Configurar event handlers
            button_AAB_BrowseInputFile.Click += Button_AAB_BrowseInputFile_Click;
            button_AAB_BrowseOutputDir.Click += Button_AAB_BrowseOutputDir_Click;
            button_AAB_BrowseKeystore.Click += Button_AAB_BrowseKeystore_Click;
            button_AAB_Convert.Click += Button_AAB_Convert_Click;

            checkBox_AAB_UseOutputDir.CheckedChanged += CheckBox_AAB_UseOutputDir_CheckedChanged;
            checkBox_AAB_UseKeystore.CheckedChanged += CheckBox_AAB_UseKeystore_CheckedChanged;

            // Configurar drag & drop
            textBox_AAB_InputFile.DragEnter += TextBox_AAB_InputFile_DragEnter;
            textBox_AAB_InputFile.DragDrop += TextBox_AAB_InputFile_DragDrop;

            // Inicializar AabConverter
            aabConverter = new AabConverter(javaPath, Path.Combine(Application.StartupPath, "Tools"));
            aabConverter.ProgressChanged += AabConverter_ProgressChanged;
            aabConverter.OutputDataReceived += AabConverter_OutputDataReceived;
            aabConverter.ErrorDataReceived += AabConverter_ErrorDataReceived;

            // Configurar estado inicial
            UpdateAabUIState();
        }

        private void Button_AAB_BrowseInputFile_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Android App Bundle (*.aab)|*.aab|All files (*.*)|*.*";
                openFileDialog.Title = "Seleccionar archivo AAB";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox_AAB_InputFile.Text = openFileDialog.FileName;
                }
            }
        }

        private void Button_AAB_BrowseOutputDir_Click(object sender, EventArgs e)
        {
            using (var folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "Seleccionar directorio de salida para APK";
                folderBrowserDialog.ShowNewFolderButton = true;

                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox_AAB_OutputDir.Text = folderBrowserDialog.SelectedPath;
                }
            }
        }

        private void Button_AAB_BrowseKeystore_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Keystore files (*.jks;*.keystore)|*.jks;*.keystore|All files (*.*)|*.*";
                openFileDialog.Title = "Seleccionar archivo keystore";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox_AAB_KeystorePath.Text = openFileDialog.FileName;
                }
            }
        }

        private async void Button_AAB_Convert_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBox_AAB_InputFile.Text))
            {
                MessageBox.Show("Por favor selecciona un archivo AAB", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!File.Exists(textBox_AAB_InputFile.Text))
            {
                MessageBox.Show("El archivo AAB seleccionado no existe", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string outputDir = GetAabOutputDirectory();
            if (string.IsNullOrEmpty(outputDir))
            {
                MessageBox.Show("Por favor selecciona un directorio de salida válido", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // Deshabilitar UI durante la conversión
                SetAabUIEnabled(false);
                aabProgressBar.Value = 0;
                aabProgressLabel.Text = "Iniciando conversión...";

                // Configurar parámetros de conversión
                string aabPath = textBox_AAB_InputFile.Text;
                string keystorePath = checkBox_AAB_UseKeystore.Checked ? textBox_AAB_KeystorePath.Text : null;
                string keystorePassword = checkBox_AAB_UseKeystore.Checked ? textBox_AAB_KeystorePassword.Text : null;
                string keyAlias = checkBox_AAB_UseKeystore.Checked ? textBox_AAB_KeyAlias.Text : null;
                string keyPassword = checkBox_AAB_UseKeystore.Checked ? textBox_AAB_KeyPassword.Text : null;

                bool success;
                if (radioButton_AAB_Bundletool.Checked)
                {
                    // Usar bundletool (método preferido)
                    success = await System.Threading.Tasks.Task.Run(() =>
                        aabConverter.ConvertAabToApk(aabPath, outputDir, keystorePath, keystorePassword, keyAlias, keyPassword));
                }
                else
                {
                    // Usar método manual
                    success = await System.Threading.Tasks.Task.Run(() =>
                        aabConverter.ConvertAabToApkManual(aabPath, outputDir));
                }

                if (success)
                {
                    aabProgressLabel.Text = "Conversión completada exitosamente";
                    MessageBox.Show("La conversión de AAB a APK se completó exitosamente", "Éxito",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Abrir directorio de salida si se desea
                    if (MessageBox.Show("¿Deseas abrir el directorio de salida?", "Conversión completada",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", outputDir);
                    }
                }
                else
                {
                    aabProgressLabel.Text = "Error en la conversión";
                    MessageBox.Show("Error durante la conversión de AAB a APK. Revisa el log para más detalles.",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                aabProgressLabel.Text = "Error inesperado";
                MessageBox.Show($"Error inesperado durante la conversión: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log.e("AAB Converter", ex.ToString());
            }
            finally
            {
                // Rehabilitar UI
                SetAabUIEnabled(true);
            }
        }

        private void CheckBox_AAB_UseOutputDir_CheckedChanged(object sender, EventArgs e)
        {
            UpdateAabUIState();
        }

        private void CheckBox_AAB_UseKeystore_CheckedChanged(object sender, EventArgs e)
        {
            UpdateAabUIState();
        }

        private void TextBox_AAB_InputFile_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Path.GetExtension(files[0]).ToLower() == ".aab")
                {
                    e.Effect = DragDropEffects.Copy;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
        }

        private void TextBox_AAB_InputFile_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Path.GetExtension(files[0]).ToLower() == ".aab")
                {
                    textBox_AAB_InputFile.Text = files[0];
                }
            }
        }

        private void AabConverter_ProgressChanged(object sender, AabConverter.ConversionProgressEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, AabConverter.ConversionProgressEventArgs>(AabConverter_ProgressChanged), sender, e);
                return;
            }

            aabProgressBar.Value = e.Progress;
            aabProgressLabel.Text = e.Message;
        }

        private void AabConverter_OutputDataReceived(object sender, string data)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, string>(AabConverter_OutputDataReceived), sender, data);
                return;
            }

            Log.i("AAB Converter", data);
        }

        private void AabConverter_ErrorDataReceived(object sender, string data)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, string>(AabConverter_ErrorDataReceived), sender, data);
                return;
            }

            Log.e("AAB Converter", data);
        }

        private string GetAabOutputDirectory()
        {
            if (checkBox_AAB_UseOutputDir.Checked && !string.IsNullOrWhiteSpace(textBox_AAB_OutputDir.Text))
            {
                return textBox_AAB_OutputDir.Text;
            }
            else if (!string.IsNullOrWhiteSpace(textBox_AAB_InputFile.Text))
            {
                return Path.GetDirectoryName(textBox_AAB_InputFile.Text);
            }
            return null;
        }

        private void UpdateAabUIState()
        {
            // Habilitar/deshabilitar controles según configuración
            textBox_AAB_OutputDir.Enabled = checkBox_AAB_UseOutputDir.Checked;
            button_AAB_BrowseOutputDir.Enabled = checkBox_AAB_UseOutputDir.Checked;

            bool useKeystore = checkBox_AAB_UseKeystore.Checked;
            textBox_AAB_KeystorePath.Enabled = useKeystore;
            button_AAB_BrowseKeystore.Enabled = useKeystore;
            textBox_AAB_KeystorePassword.Enabled = useKeystore;
            textBox_AAB_KeyAlias.Enabled = useKeystore;
            textBox_AAB_KeyPassword.Enabled = useKeystore;
            label_AAB_KeystorePath.Enabled = useKeystore;
            label_AAB_KeystorePassword.Enabled = useKeystore;
            label_AAB_KeyAlias.Enabled = useKeystore;
            label_AAB_KeyPassword.Enabled = useKeystore;
        }

        private void SetAabUIEnabled(bool enabled)
        {
            button_AAB_BrowseInputFile.Enabled = enabled;
            button_AAB_Convert.Enabled = enabled;
            button_AAB_BrowseOutputDir.Enabled = enabled && checkBox_AAB_UseOutputDir.Checked;
            button_AAB_BrowseKeystore.Enabled = enabled && checkBox_AAB_UseKeystore.Checked;
            checkBox_AAB_UseOutputDir.Enabled = enabled;
            checkBox_AAB_UseKeystore.Enabled = enabled;
            radioButton_AAB_Bundletool.Enabled = enabled;
            radioButton_AAB_Manual.Enabled = enabled;

            // Los textboxes solo se deshabilitan si están habilitados por sus checkboxes
            if (enabled)
            {
                UpdateAabUIState();
            }
            else
            {
                textBox_AAB_OutputDir.Enabled = false;
                textBox_AAB_KeystorePath.Enabled = false;
                textBox_AAB_KeystorePassword.Enabled = false;
                textBox_AAB_KeyAlias.Enabled = false;
                textBox_AAB_KeyPassword.Enabled = false;
            }
        }
    }
}
