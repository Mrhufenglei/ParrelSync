using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ParrelSync
{
    // With ScriptableObject derived classes, .cs and .asset filenames MUST be identical
    public class ParrelSyncProjectSettings : ScriptableObject
    {
        private const string ParrelSyncScriptableObjectsDirectory = "Assets/Plugins/ParrelSync/ScriptableObjects";
        private const string ParrelSyncSettingsPath = ParrelSyncScriptableObjectsDirectory + "/" +
                                                       nameof(ParrelSyncProjectSettings) + ".asset";

        [SerializeField]
        [HideInInspector]
        private List<string> m_OptionalSymbolicLinkFolders;

        [SerializeField]
        [HideInInspector]
        private List<string> m_SymbolicLinkFolders;

        [SerializeField]
        [HideInInspector]
        private List<string> m_CopyFolders;

        [SerializeField]
        [HideInInspector]
        private bool m_FolderSettingsInitialized;

        public const string NameOfSymbolicLinkFolders = nameof(m_SymbolicLinkFolders);
        public const string NameOfCopyFolders = nameof(m_CopyFolders);

        private static readonly string[] DefaultSymbolicLinkFolders =
        {
            "Assets",
            "ProjectSettings",
            "AutoBuild",
            "LocalPackages"
        };

        private static readonly string[] DefaultCopyFolders =
        {
            "Library",
            "Packages"
        };

        private static ParrelSyncProjectSettings GetOrCreateSettings()
        {
            ParrelSyncProjectSettings projectSettings;
            if (File.Exists(ParrelSyncSettingsPath))
            {
                projectSettings = AssetDatabase.LoadAssetAtPath<ParrelSyncProjectSettings>(ParrelSyncSettingsPath);

                if (projectSettings == null)
                    Debug.LogError("File Exists, but failed to load: " + ParrelSyncSettingsPath);

                EnsureFolderSettings(projectSettings);
                return projectSettings;
            }

            projectSettings = CreateInstance<ParrelSyncProjectSettings>();
            EnsureFolderSettings(projectSettings);
            if (!Directory.Exists(ParrelSyncScriptableObjectsDirectory))
            {
                Directory.CreateDirectory(ParrelSyncScriptableObjectsDirectory);
            }
            AssetDatabase.CreateAsset(projectSettings, ParrelSyncSettingsPath);
            AssetDatabase.SaveAssets();
            return projectSettings;
        }

        private static void EnsureFolderSettings(ParrelSyncProjectSettings projectSettings)
        {
            if (projectSettings == null || projectSettings.m_FolderSettingsInitialized)
                return;

            projectSettings.m_SymbolicLinkFolders = new List<string>(DefaultSymbolicLinkFolders);
            projectSettings.m_CopyFolders = new List<string>(DefaultCopyFolders);

            if (projectSettings.m_OptionalSymbolicLinkFolders != null)
                AddUniqueFolders(projectSettings.m_SymbolicLinkFolders, projectSettings.m_OptionalSymbolicLinkFolders);

            var legacyPreferenceFolders = Preferences.OptionalSymbolicLinkFolders.GetStoredValue();
            AddUniqueFolders(projectSettings.m_SymbolicLinkFolders, legacyPreferenceFolders);
            Preferences.OptionalSymbolicLinkFolders.ClearStoredValue();

            projectSettings.m_FolderSettingsInitialized = true;
            EditorUtility.SetDirty(projectSettings);
            AssetDatabase.SaveAssets();
        }

        private static void AddUniqueFolders(List<string> target, IEnumerable<string> source)
        {
            foreach (var path in source)
            {
                var normalizedPath = NormalizeFolderPath(path);
                if (!string.IsNullOrEmpty(normalizedPath) && !target.Contains(normalizedPath))
                    target.Add(normalizedPath);
            }
        }

        public static List<string> GetSymbolicLinkFolders()
        {
            var settings = GetOrCreateSettings();
            return GetNormalizedFolderPaths(settings.m_SymbolicLinkFolders);
        }

        public static List<string> GetCopyFolders()
        {
            var settings = GetOrCreateSettings();
            return GetNormalizedFolderPaths(settings.m_CopyFolders);
        }

        public static bool IsCopyFolderConfigured(string folderName)
        {
            var normalizedFolderName = NormalizeFolderPath(folderName);
            return GetCopyFolders().Contains(normalizedFolderName);
        }

        public static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalizedPath = path.Replace('\\', '/').Trim('/');
            return normalizedPath == "." || normalizedPath == ".." || normalizedPath.StartsWith("../") ||
                   normalizedPath.Contains("/../") || normalizedPath.EndsWith("/..")
                ? string.Empty
                : normalizedPath;
        }

        private static List<string> GetNormalizedFolderPaths(IEnumerable<string> folderPaths)
        {
            var normalizedFolderPaths = new List<string>();
            if (folderPaths != null)
                AddUniqueFolders(normalizedFolderPaths, folderPaths);

            return normalizedFolderPaths;
        }

        public static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }
    }

    public class ParrelSyncSettingsProvider : SettingsProvider
    {
        private const string MenuLocationInProjectSettings = "Project/ParrelSync";

        private SerializedObject _parrelSyncProjectSettings;

        private class Styles
        {
            public static readonly GUIContent SymbolicLinkSectionHeading = new GUIContent("Folders to Symbolically Link");
            public static readonly GUIContent CopySectionHeading = new GUIContent("Folders to Copy");
            public static readonly GUIContent FolderConfigurationWarning = new GUIContent(
                "Removing default folders can make a clone project unable to open correctly. A folder cannot be configured for both copy and symbolic link.");
        }

        private ParrelSyncSettingsProvider(string path, SettingsScope scope = SettingsScope.User)
            : base(path, scope)
        {
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            // This function is called when the user clicks on the ParrelSyncSettings element in the Settings window.
            _parrelSyncProjectSettings = ParrelSyncProjectSettings.GetSerializedSettings();
        }

        public override void OnGUI(string searchContext)
        {
            var symbolicLinkFoldersProperty = _parrelSyncProjectSettings.FindProperty(ParrelSyncProjectSettings.NameOfSymbolicLinkFolders);
            var copyFoldersProperty = _parrelSyncProjectSettings.FindProperty(ParrelSyncProjectSettings.NameOfCopyFolders);
            if (!IsFolderArray(symbolicLinkFoldersProperty) || !IsFolderArray(copyFoldersProperty))
                return;

            EditorGUILayout.HelpBox(Styles.FolderConfigurationWarning.text, MessageType.Warning);
            var symbolicLinkFoldersChanged = DrawFolderList(symbolicLinkFoldersProperty, copyFoldersProperty,
                Styles.SymbolicLinkSectionHeading, "Select Folder to Symbolically Link...");
            var copyFoldersChanged = DrawFolderList(copyFoldersProperty, symbolicLinkFoldersProperty,
                Styles.CopySectionHeading, "Select Folder to Copy...");

            if (symbolicLinkFoldersChanged || copyFoldersChanged)
            {
                _parrelSyncProjectSettings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }

        private static bool IsFolderArray(SerializedProperty property)
        {
            return property != null && property.isArray && property.arrayElementType == "string";
        }

        private static bool DrawFolderList(SerializedProperty property, SerializedProperty otherProperty,
            GUIContent heading, string selectFolderTitle)
        {
            var folderPaths = GetFolderPaths(property);
            var otherFolderPaths = GetFolderPaths(otherProperty);
            var isDirty = false;

            GUILayout.BeginVertical("GroupBox");
            GUILayout.Label(heading);
            GUILayout.Space(5);

            for (var i = 0; i < folderPaths.Count; ++i)
            {
                var folderPath = folderPaths[i];
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(folderPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    var selectedPath = EditorUtility.OpenFolderPanel(selectFolderTitle, "", "");
                    var relativePath = GetProjectRelativeFolderPath(selectedPath);
                    if (relativePath == null)
                    {
                        if (!string.IsNullOrEmpty(selectedPath))
                            Debug.LogWarning("Configured folder must be within the project directory");
                    }
                    else if (otherFolderPaths.Contains(relativePath))
                    {
                        Debug.LogWarning("A folder cannot be configured for both copy and symbolic link: " + relativePath);
                    }
                    else if (!folderPaths.Contains(relativePath))
                    {
                        folderPaths[i] = relativePath;
                        isDirty = true;
                    }
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    folderPaths.RemoveAt(i);
                    isDirty = true;
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Folder"))
            {
                var selectedPath = EditorUtility.OpenFolderPanel(selectFolderTitle, "", "");
                var relativePath = GetProjectRelativeFolderPath(selectedPath);
                if (relativePath == null)
                {
                    if (!string.IsNullOrEmpty(selectedPath))
                        Debug.LogWarning("Configured folder must be within the project directory");
                }
                else if (otherFolderPaths.Contains(relativePath) || folderPaths.Contains(relativePath))
                {
                    Debug.LogWarning("Configured folder already exists or belongs to the other folder type: " + relativePath);
                }
                else
                {
                    folderPaths.Add(relativePath);
                    isDirty = true;
                }
            }
            GUILayout.EndVertical();

            if (isDirty)
                SetFolderPaths(property, folderPaths);

            return isDirty;
        }

        private static List<string> GetFolderPaths(SerializedProperty property)
        {
            var folderPaths = new List<string>(property.arraySize);
            for (var i = 0; i < property.arraySize; ++i)
                folderPaths.Add(ParrelSyncProjectSettings.NormalizeFolderPath(
                    property.GetArrayElementAtIndex(i).stringValue));

            folderPaths.RemoveAll(string.IsNullOrEmpty);
            return folderPaths;
        }

        private static void SetFolderPaths(SerializedProperty property, List<string> folderPaths)
        {
            property.arraySize = folderPaths.Count;
            for (var i = 0; i < folderPaths.Count; ++i)
                property.GetArrayElementAtIndex(i).stringValue = folderPaths[i];
        }

        private static string GetProjectRelativeFolderPath(string selectedPath)
        {
            if (string.IsNullOrEmpty(selectedPath))
                return null;

            var projectPath = ClonesManager.GetCurrentProjectPath().Replace('\\', '/').TrimEnd('/');
            var normalizedSelectedPath = selectedPath.Replace('\\', '/').TrimEnd('/');
            if (!normalizedSelectedPath.StartsWith(projectPath + "/"))
                return null;

            return ParrelSyncProjectSettings.NormalizeFolderPath(normalizedSelectedPath.Substring(projectPath.Length));
        }

        // Register the SettingsProvider
        [SettingsProvider]
        public static SettingsProvider CreateParrelSyncSettingsProvider()
        {
            return new ParrelSyncSettingsProvider(MenuLocationInProjectSettings, SettingsScope.Project)
            {
                keywords = GetSearchKeywordsFromGUIContentProperties<Styles>()
            };
        }
    }
}
