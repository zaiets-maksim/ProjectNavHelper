using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectNavHelper
{
    [InitializeOnLoad]
    public class ProjectNavHelper
    {
        private const string AssetsPath = "Assets";
        private const string ProjectWindow = "Project";
        private const int MouseButton4 = 3;
        private const int MouseButton5 = 4;
        private const int LeftButton = 0;
        private const int Null = -1;

        private static List<string> _history = new() { AssetsPath };
        private static List<string> _paths;
        private static Event _currentEvent;
        private static string _currentPath;
        private static string _lastKnownPath;
        private static int _currentIndex = 1;
        private static int _lastClickedButton;
        private static bool _isClicked;
        private static bool _isFirstRun;
        private static readonly Dictionary<int?, string> _projects = new();
        private static int _depth;
        private static Object _pingObj;
        private static StringBuilder _currentPathStringBuilder;
        private static bool _isEmptyFolder;
        private static EditorWindow _currentWindow;
        private static Type _projectBrowserType;
        private static FieldInfo _fieldInfo;
        private static MethodInfo _methodInfo;
        private static PropertyInfo _propertyInfo;
        private static bool _isLock;

        static ProjectNavHelper() => EditorApplication.projectWindowItemOnGUI += DetectMouseButtonsPress;

        ~ProjectNavHelper() => EditorApplication.projectWindowItemOnGUI -= DetectMouseButtonsPress;

        private static void DetectMouseButtonsPress(string guid, Rect selectionRect)
        {
            _currentEvent = Event.current;
            _lastClickedButton = _currentEvent.button;

            if (_currentEvent.button == LeftButton && _currentEvent.type == EventType.MouseDown)
            {
                EditorApplication.delayCall += () => _currentPath = TryGetHoveredProjectFolderPath(out int projectID);
                return;
            }

            if (_currentEvent.button == MouseButton4 || _currentEvent.button == MouseButton5 && _isEmptyFolder)
            {
                EditorApplication.update += () =>
                {
                    _currentPath = TryGetHoveredProjectFolderPath(out int projectID);

                    if (Directory.Exists(_currentPath) && Directory.GetFiles(_currentPath).Length == 0)
                    {
                        _isClicked = true;
                        _isEmptyFolder = true;
                        ExecuteNavigationAction();
                    }
                
                    EditorApplication.update -= EditorApplication.update.GetInvocationList()[0] as EditorApplication.CallbackFunction;
                };
            }

            if (_currentEvent.type == EventType.MouseDown && !_isClicked)
            {
                _isClicked = true;
                _isEmptyFolder = false;
                ExecuteNavigationAction();
            }
            else if (_currentEvent.type == EventType.MouseUp && _currentEvent.button == _lastClickedButton)
            {
                _isClicked = false;
                _isEmptyFolder = false;
            }
        }

        private static void ExecuteNavigationAction()
        {
            UpdateProject();
            switch (_lastClickedButton)
            {
                case MouseButton4:
                    GoBack();
                    _currentEvent.Use();
                    break;
                case MouseButton5:
                    GoForward();
                    _currentEvent.Use();
                    break;
            }
        }

        private static void UpdateProject()
        {
            _currentPath = TryGetHoveredProjectFolderPath(out int projectID);
        
            if (projectID == Null)
                return;

            if (_projects.ContainsKey(projectID))
            {
                if (GetPathDepth(_currentPath) < GetPathDepth(_projects[projectID]))
                {
                    if (_projects[projectID].Contains(_currentPath))
                    {
                        AdjustCurrentPathToSubfolder(projectID);
                        return;
                    }
                }
            }

            RegisterOrUpdateProjectPath(projectID);
            AdjustCurrentPathToSubfolder(projectID);
        }

        private static void RegisterOrUpdateProjectPath(int projectID) => _projects[projectID] = _currentPath;

        private static void AdjustCurrentPathToSubfolder(int projectID)
        {
            _paths = GetParentPaths(_projects[projectID]);
            UpdateHistory(_paths);
            _depth = GetPathDepth(_currentPath);
            _currentIndex = _depth;
        }

        private static void UpdateHistory(List<string> paths) => _history = paths;

        private static List<string> GetParentPaths(string path)
        {
            var paths = new List<string> { AssetsPath };

            if (string.IsNullOrEmpty(path) || path == AssetsPath)
                return paths;

            _currentPathStringBuilder = new StringBuilder(AssetsPath);

            string[] pathSegments = path.Split('/');

            for (int i = pathSegments[0] == AssetsPath ? 1 : 0; i < pathSegments.Length; i++)
            {
                _currentPathStringBuilder.Append('/').Append(pathSegments[i]);
                paths.Add(_currentPathStringBuilder.ToString());
            }

            return paths;
        }

        private static int GetPathDepth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;

            int depth = 1;

            int index = path.IndexOf('/');
            while (index != -1)
            {
                depth++;
                index = path.IndexOf('/', index + 1);
            }

            return depth;
        }

        private static void GoBack()
        {
            if (_currentIndex <= 1) return;
            --_currentIndex;

            if (ProjectNavHelperContextMenu.CanPing)
                PingObject(_history[_currentIndex]);
            else
                OpenDirectory(_history[--_currentIndex]);
        }

        private static void GoForward()
        {
            if (_history.Count <= _currentIndex) return;
            OpenDirectory(_history[_currentIndex++]);
        }

        private static void PingObject(string path)
        {
            _pingObj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (_pingObj == null) return;

            if (ProjectNavHelperContextMenu.IgnoreLock)
            {
                PingObjectWithIgnoreLock();
                return;
            }

            Selection.activeObject = _pingObj;
            EditorGUIUtility.PingObject(_pingObj);
        }

        private static void PingObjectWithIgnoreLock()
        {
            _currentWindow = EditorWindow.GetWindow(_projectBrowserType);
            _propertyInfo = _projectBrowserType.GetProperty("isLocked", BindingFlags.Instance | BindingFlags.NonPublic);
            _isLock = (bool)_propertyInfo.GetValue(_currentWindow);
        
            if (_isLock) _propertyInfo.SetValue(_currentWindow, false);
            Selection.activeObject = _pingObj;
            EditorGUIUtility.PingObject(_pingObj);
            if (_isLock) _propertyInfo.SetValue(_currentWindow, true);
        }

        private static void OpenDirectory(string path)
        {
            if (path.Equals(_currentPath))
                return;

            var asset = AssetDatabase.LoadMainAssetAtPath(path);

            if (asset == null) return;

            _currentWindow = EditorWindow.mouseOverWindow;
            _currentWindow.Focus();

            _projectBrowserType = _currentWindow.GetType();

            var lastBrowser = _projectBrowserType
                ?.GetField("s_LastInteractedProjectBrowser", BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null);

            if (IsProjectWindowLocked() && !ProjectNavHelperContextMenu.IgnoreLock)
                return;

            _methodInfo = _projectBrowserType
                ?.GetMethod("ShowFolderContents", BindingFlags.NonPublic | BindingFlags.Instance);
            _methodInfo?.Invoke(lastBrowser, new object[] { asset.GetInstanceID(), true });
        }

        private static bool IsProjectWindowLocked()
        {
            return (bool)_projectBrowserType.GetProperty(
                    "isLocked",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(_currentWindow)!;
        }

        private static string TryGetHoveredProjectFolderPath(out int projectIndex)
        {
            _currentWindow = EditorWindow.mouseOverWindow;
            if (_currentWindow?.titleContent.text == ProjectWindow)
            {
                _currentWindow.Focus();

                _projectBrowserType = _currentWindow.GetType();
                _fieldInfo = _projectBrowserType.GetField("m_LastFolders", BindingFlags.Instance | BindingFlags.NonPublic);
                var folders = (string[])_fieldInfo.GetValue(_currentWindow);
                projectIndex = _currentWindow.GetInstanceID();
                return folders.Length > 0 ? folders[0] : string.Empty;
            }

            projectIndex = Null;
            return string.Empty;
        }
    }


    public static class ProjectNavHelperContextMenu
    {
        private const string CanPingMenuPath = "Tools/ProjectNavHelper/Can Ping";
        private const string IgnoreLockMenuPath = "Tools/ProjectNavHelper/Ignore Lock";

        private const string CanPinKey = "ProjectNavHelper_CanPing";
        private const string IgnoreLockKey = "ProjectNavHelper_IgnoreLock";

        public static bool CanPing = false;
        public static bool IgnoreLock = true;

        static ProjectNavHelperContextMenu()
        {
            CanPing = EditorPrefs.GetBool(CanPinKey, false);
            IgnoreLock = EditorPrefs.GetBool(IgnoreLockKey, false);
        }

        [MenuItem(CanPingMenuPath, priority = 0)]
        private static void ToggleCanPing()
        {
            CanPing = EditorPrefs.GetBool(CanPinKey, false);
            CanPing = !CanPing;
            EditorPrefs.SetBool(CanPinKey, CanPing);
        }

        [MenuItem(IgnoreLockMenuPath, priority = 1)]
        private static void ToggleIgnoreLockMenuPath()
        {
            IgnoreLock = EditorPrefs.GetBool(IgnoreLockKey, false);
            IgnoreLock = !IgnoreLock;
            EditorPrefs.SetBool(IgnoreLockKey, IgnoreLock);
        }

        [MenuItem(CanPingMenuPath, true)]
        private static bool CanPingValidate()
        {
            Menu.SetChecked(CanPingMenuPath, CanPing);
            return true;
        }

        [MenuItem(IgnoreLockMenuPath, true)]
        private static bool IgnoreLockMenuPathValidate()
        {
            Menu.SetChecked(IgnoreLockMenuPath, IgnoreLock);
            return true;
        }
    }
}