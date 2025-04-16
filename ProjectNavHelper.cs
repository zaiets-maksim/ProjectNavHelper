using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public class ProjectNavHelper
{
    private const string AssetsPath = "Assets";
    private const string ProjectWindow = "Project";
    private const int MouseButton4 = 3;
    private const int MouseButton5 = 4;

    private static ProjectNavHelperSettings _settings;
    private static List<string> _history = new() { AssetsPath };
    private static List<string> _paths;
    private static Event _currentEvent;
    private static string _lastPath = AssetsPath;
    private static string _currentPath;
    private static string _lastKnownPath;
    private static int _currentIndex = 1;
    private static int _lastClickedButton;
    private static bool _isClicked;
    private static bool _isFirstRun;


    static ProjectNavHelper()
    {
        GetSettings();
        EditorApplication.update += UpdateDirectory;
        EditorApplication.projectWindowItemOnGUI += (_, _) => DetectMouseButtonsPress();
    }

    private static void UpdateHistoryForCurrentPath()
    {
        _currentPath = TryGetHoveredProjectFolderPath();

        if (!_isFirstRun && _currentPath == _lastKnownPath) return;

        if (_history.Count == 0 || _currentPath != _history[^1])
        {
            _paths = GetParentPaths(_currentPath);

            if (_paths.Count > _history.Count)
            {
                _history = _paths;
                _currentIndex = _history.Count - 1;
            }

            _lastKnownPath = _currentPath;

            if (_isFirstRun)
                _isFirstRun = false;
        }
    }

    private static List<string> GetParentPaths(string path)
    {
        _paths = new List<string> { "Assets" };

        while (!string.IsNullOrEmpty(path) && path.Contains("/") && path != "Assets")
        {
            _paths.Insert(1, path);
            path = path.Substring(0, path.LastIndexOf('/'));
        }

        return _paths;
    }

    private static void UpdateDirectory()
    {
        UpdateHistoryForCurrentPath();
        if (_lastPath == _currentPath) return;

        _lastPath = _currentPath;

        int depth = GetPathDepth(_currentPath);
        if (_history.Count > depth)
        {
            if (_history[depth] != _currentPath)
            {
                _history[depth] = _currentPath;
                _history.RemoveRange(depth + 1, _history.Count - (depth + 1));
            }
        }

        else
        {
            _history.Add(_currentPath);
        }

        _currentIndex = depth + 1;
    }

    private static int GetPathDepth(string path) => path.Split('/').Length - 1;

    private static void DetectMouseButtonsPress()
    {
        _currentEvent = Event.current;
        if (_currentEvent.type == EventType.MouseDown && !_isClicked)
        {
            _isClicked = true;
            _lastClickedButton = _currentEvent.button;

            if (_lastClickedButton == MouseButton4)
            {
                GoBack();
                _currentEvent.Use();
            }

            if (_lastClickedButton == MouseButton5)
            {
                GoForward();
                _currentEvent.Use();
            }
        }
        else if (_currentEvent.type == EventType.MouseUp && _currentEvent.button == _lastClickedButton)
        {
            _isClicked = false;
        }
    }

    private static void GoBack()
    {
        if (_currentIndex <= 1) return;
        --_currentIndex;

        if (_settings.CanPing)
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
        var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (obj == null) return;
        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);
    }

    private static void OpenDirectory(string path)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(path);

        if (asset == null) return;

        var projectBrowserType = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");
        var lastBrowser = projectBrowserType
            ?.GetField("s_LastInteractedProjectBrowser", BindingFlags.Static | BindingFlags.Public)
            ?.GetValue(null);
        var showFolderMethod = projectBrowserType
            ?.GetMethod("ShowFolderContents", BindingFlags.NonPublic | BindingFlags.Instance);

        showFolderMethod?.Invoke(lastBrowser, new object[] { asset.GetInstanceID(), true });
    }


    private static string TryGetHoveredProjectFolderPath()
    {
        var window = EditorWindow.mouseOverWindow;
        if (window != null && window.titleContent.text == ProjectWindow)
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            var field = type.GetField("m_LastFolders", BindingFlags.Instance | BindingFlags.NonPublic);
            var folders = (string[])field.GetValue(window);
            return folders.Length > 0 ? folders[0] : string.Empty;
        }
        return string.Empty;
    }
    
    private static void GetSettings()
    {
        if (_settings == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:ProjectNavHelperSettings");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<ProjectNavHelperSettings>(path);
            }
            else
            {
                Debug.LogWarning("ProjectNavHelperSettings not found in the project.");
            }
        }
    }
}