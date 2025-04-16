using UnityEditor;
using UnityEngine;

[CreateAssetMenu(menuName = "ProjectNavHelper/Settings", fileName = "ProjectNavHelperSettings", order = 0)]
public class ProjectNavHelperSettings : ScriptableObject
{
    public bool CanPing;
}

[CustomEditor(typeof(ProjectNavHelperSettings))]
public class ProjectNavHelperSettingsEditor : UnityEditor.Editor
{
    private ProjectNavHelperSettings _settings;

    private void OnEnable() => _settings = (ProjectNavHelperSettings)target;

    public override void OnInspectorGUI() => ShowSettings();

    private void ShowSettings()
    {
        EditorGUILayout.HelpBox("For Ping to work correctly, the lock must be disabled in the Project window", MessageType.Info);
        Rect rect = EditorGUILayout.GetControlRect();
        _settings.CanPing = EditorGUI.ToggleLeft(rect, "Can Ping", _settings.CanPing);
    }
}