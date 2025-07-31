using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using APKToolGUI.Properties;
using APKToolGUI.Utils;
using Java;
using Ionic.Zip;

namespace APKToolGUI.ApkTool
{
    /// <summary>
    /// Clase para convertir archivos AAB (Android App Bundle) a APK
    /// </summary>
    public class AabConverter : JarProcess
    {
        public enum ConversionStep
        {
            ExtractingAab,
            DecompilingApk,
            CompilingResources,
            LinkingResources,
            CreatingBundle,
            GeneratingApks,
            Complete,
            Error
        }

        public class ConversionProgressEventArgs : EventArgs
        {
            public ConversionStep Step { get; set; }
            public string Message { get; set; }
            public int Progress { get; set; }
        }

        public event EventHandler<ConversionProgressEventArgs> ProgressChanged;
        public event EventHandler<string> OutputDataReceived;
        public event EventHandler<string> ErrorDataReceived;

        private string _javaPath;
        private string _toolsPath;
        private string _tempPath;
        private string _bundletoolPath;
        private string _apktoolPath;
        private string _aapt2Path;
        private string _androidJarPath;

        public AabConverter(string javaPath, string toolsPath) : base(javaPath, "")
        {
            _javaPath = javaPath;
            _toolsPath = toolsPath;
            _tempPath = Path.Combine(Path.GetTempPath(), "APKToolGUI_AAB_" + Guid.NewGuid().ToString("N")[..8]);

            // Configurar rutas de herramientas
            _bundletoolPath = Path.Combine(_toolsPath, "bundletool.jar");
            _apktoolPath = Path.Combine(_toolsPath, "apktool.jar");
            _aapt2Path = Path.Combine(_toolsPath, "aapt2.exe");
            _androidJarPath = Path.Combine(_toolsPath, "android.jar");
        }

        /// <summary>
        /// Convierte un archivo AAB a APK usando bundletool
        /// </summary>
        /// <param name="aabPath">Ruta del archivo AAB</param>
        /// <param name="outputPath">Ruta de salida para los APKs</param>
        /// <param name="keystorePath">Ruta del keystore (opcional)</param>
        /// <param name="keystorePassword">Contraseña del keystore (opcional)</param>
        /// <param name="keyAlias">Alias de la clave (opcional)</param>
        /// <param name="keyPassword">Contraseña de la clave (opcional)</param>
        /// <returns>True si la conversión fue exitosa</returns>
        public bool ConvertAabToApk(string aabPath, string outputPath, string keystorePath = null,
            string keystorePassword = null, string keyAlias = null, string keyPassword = null)
        {
            try
            {
                if (!File.Exists(aabPath))
                {
                    OnErrorDataReceived($"El archivo AAB no existe: {aabPath}");
                    return false;
                }

                if (!File.Exists(_bundletoolPath))
                {
                    OnErrorDataReceived($"bundletool.jar no encontrado en: {_bundletoolPath}");
                    return false;
                }

                // Crear directorio temporal
                Directory.CreateDirectory(_tempPath);
                OnProgressChanged(ConversionStep.ExtractingAab, "Iniciando conversión AAB a APK...", 10);

                // Generar APKs desde AAB
                string apksPath = Path.Combine(_tempPath, "app.apks");
                string buildApksCommand;

                if (!string.IsNullOrEmpty(keystorePath) && File.Exists(keystorePath))
                {
                    // Con firma
                    buildApksCommand = $"-jar \"{_bundletoolPath}\" build-apks " +
                        $"--bundle=\"{aabPath}\" " +
                        $"--output=\"{apksPath}\" " +
                        $"--ks=\"{keystorePath}\" " +
                        $"--ks-pass=pass:{keystorePassword} " +
                        $"--ks-key-alias={keyAlias} " +
                        $"--key-pass=pass:{keyPassword}\"";
                }
                else
                {
                    // Sin firma (para testing)
                    buildApksCommand = $"-jar \"{_bundletoolPath}\" build-apks " +
                        $"--bundle=\"{aabPath}\" " +
                        $"--output=\"{apksPath}\" " +
                        $"--mode=universal";
                }

                OnProgressChanged(ConversionStep.GeneratingApks, "Generando APKs desde AAB...", 30);

                if (!ExecuteCommand(buildApksCommand))
                {
                    OnErrorDataReceived("Error al generar APKs desde AAB");
                    return false;
                }

                OnProgressChanged(ConversionStep.GeneratingApks, "Extrayendo APK universal...", 60);

                // Extraer el APK universal del archivo .apks
                if (!ExtractUniversalApk(apksPath, outputPath))
                {
                    OnErrorDataReceived("Error al extraer el APK universal");
                    return false;
                }

                OnProgressChanged(ConversionStep.Complete, "Conversión completada exitosamente", 100);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorDataReceived($"Error durante la conversión: {ex.Message}");
                OnProgressChanged(ConversionStep.Error, "Error en la conversión", 0);
                return false;
            }
            finally
            {
                // Limpiar archivos temporales
                CleanupTempFiles();
            }
        }

        /// <summary>
        /// Convierte AAB a APK usando el método manual (decompilación y recompilación)
        /// </summary>
        public bool ConvertAabToApkManual(string aabPath, string outputPath)
        {
            try
            {
                OnProgressChanged(ConversionStep.ExtractingAab, "Extrayendo AAB...", 10);

                // Extraer el AAB como ZIP
                string extractPath = Path.Combine(_tempPath, "aab_extracted");
                Directory.CreateDirectory(extractPath);

                using (var zip = ZipFile.Read(aabPath))
                {
                    zip.ExtractAll(extractPath);
                }

                // Buscar el base.apk dentro del AAB
                string baseApkPath = Path.Combine(extractPath, "base", "base.apk");
                if (!File.Exists(baseApkPath))
                {
                    // Buscar en otros posibles ubicaciones
                    string[] possiblePaths = {
                        Path.Combine(extractPath, "base.apk"),
                        Path.Combine(extractPath, "splits", "base.apk")
                    };

                    foreach (string path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            baseApkPath = path;
                            break;
                        }
                    }
                }

                if (!File.Exists(baseApkPath))
                {
                    OnErrorDataReceived("No se pudo encontrar base.apk en el AAB");
                    return false;
                }

                OnProgressChanged(ConversionStep.DecompilingApk, "Decompilando APK base...", 30);

                // Decompile el base.apk usando apktool
                string decompiledPath = Path.Combine(_tempPath, "decompiled");
                string decompileCommand = $"-jar \"{_apktoolPath}\" d \"{baseApkPath}\" -o \"{decompiledPath}\" -f";

                if (!ExecuteCommand(decompileCommand))
                {
                    OnErrorDataReceived("Error al decompile el APK base");
                    return false;
                }

                OnProgressChanged(ConversionStep.CompilingResources, "Recompilando APK...", 70);

                // Recompile usando apktool
                string recompiledApkPath = Path.Combine(_tempPath, "recompiled.apk");
                string compileCommand = $"-jar \"{_apktoolPath}\" b \"{decompiledPath}\" -o \"{recompiledApkPath}\"";

                if (!ExecuteCommand(compileCommand))
                {
                    OnErrorDataReceived("Error al recompile el APK");
                    return false;
                }

                // Copiar el resultado al directorio de salida
                string finalApkPath = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(aabPath) + "_converted.apk");
                File.Copy(recompiledApkPath, finalApkPath, true);

                OnProgressChanged(ConversionStep.Complete, "Conversión manual completada", 100);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorDataReceived($"Error en conversión manual: {ex.Message}");
                return false;
            }
        }

        private bool ExtractUniversalApk(string apksPath, string outputPath)
        {
            try
            {
                // El archivo .apks es en realidad un ZIP
                string extractedApksPath = Path.Combine(_tempPath, "apks_extracted");
                Directory.CreateDirectory(extractedApksPath);

                using (var zip = ZipFile.Read(apksPath))
                {
                    zip.ExtractAll(extractedApksPath);
                }

                // Buscar el APK universal
                string universalApkPath = Path.Combine(extractedApksPath, "universal.apk");
                if (!File.Exists(universalApkPath))
                {
                    // Si no hay universal.apk, buscar base-master.apk o similar
                    string[] files = Directory.GetFiles(extractedApksPath, "*.apk");
                    if (files.Length > 0)
                    {
                        universalApkPath = files[0]; // Tomar el primer APK encontrado
                    }
                    else
                    {
                        return false;
                    }
                }

                // Copiar al directorio de salida
                string outputApkPath = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(Path.GetFileName(apksPath)) + ".apk");
                Directory.CreateDirectory(Path.GetDirectoryName(outputApkPath));
                File.Copy(universalApkPath, outputApkPath, true);

                OnOutputDataReceived($"APK extraído exitosamente: {outputApkPath}");
                return true;
            }
            catch (Exception ex)
            {
                OnErrorDataReceived($"Error al extraer APK universal: {ex.Message}");
                return false;
            }
        }

        private bool ExecuteCommand(string arguments)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _javaPath,
                    Arguments = arguments,
                    WorkingDirectory = _toolsPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process.OutputDataReceived += (sender, e) => {
                        if (!string.IsNullOrEmpty(e.Data))
                            OnOutputDataReceived(e.Data);
                    };

                    process.ErrorDataReceived += (sender, e) => {
                        if (!string.IsNullOrEmpty(e.Data))
                            OnErrorDataReceived(e.Data);
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                OnErrorDataReceived($"Error ejecutando comando: {ex.Message}");
                return false;
            }
        }

        private void CleanupTempFiles()
        {
            try
            {
                if (Directory.Exists(_tempPath))
                {
                    Directory.Delete(_tempPath, true);
                }
            }
            catch (Exception ex)
            {
                OnErrorDataReceived($"Error al limpiar archivos temporales: {ex.Message}");
            }
        }

        private void OnProgressChanged(ConversionStep step, string message, int progress)
        {
            ProgressChanged?.Invoke(this, new ConversionProgressEventArgs
            {
                Step = step,
                Message = message,
                Progress = progress
            });
        }

        private void OnOutputDataReceived(string data)
        {
            OutputDataReceived?.Invoke(this, data);
        }

        private void OnErrorDataReceived(string data)
        {
            ErrorDataReceived?.Invoke(this, data);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CleanupTempFiles();
            }
            base.Dispose(disposing);
        }
    }
}
