using System;
using System.IO;
using UnityEngine;

namespace Resource.Scripts.Gyro
{
    /// <summary>Only explicit Save writes preferences; changing live controls never touches disk.</summary>
    public static class GyroSettingsStore
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "gyro_settings.json");

        public static GyroSettings Load(out string status)
        {
            var settings = new GyroSettings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    status = "使用默认参数（尚未保存）";
                    return settings;
                }
                // Overwrite preserves defaults for fields added by a newer version.
                JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
                settings.Validate();
                status = "已读取 " + FilePath;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                settings = new GyroSettings();
                status = "参数文件读取失败，使用默认值：" + exception.Message;
            }
            return settings;
        }

        public static bool Save(GyroSettings settings, out string status)
        {
            try
            {
                settings.Validate();
                Directory.CreateDirectory(Application.persistentDataPath);
                string temporaryPath = FilePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(settings, true));
                if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, null);
                else File.Move(temporaryPath, FilePath);
                status = "已保存 " + FilePath;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                status = "保存失败：" + exception.Message;
                return false;
            }
        }
    }
}
