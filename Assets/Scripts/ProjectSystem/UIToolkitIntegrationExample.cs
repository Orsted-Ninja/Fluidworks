// Example Integration file for UI Toolkit (For user reference)
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.UIElements;
using FluidWorks.ProjectSystem;

public class UIToolkitIntegrationExample : MonoBehaviour
{
    public UIDocument mainUIDocument;
    public ProjectSaveManager projectSaver;
    public ProjectLoadManager projectLoader;

    // Track active project name for save defaults
    private string activeProjectName = "Untitled_Project";

    private void OnEnable()
    {
        if (mainUIDocument != null)
        {
            var root = mainUIDocument.rootVisualElement;

            // Example generic bindings referencing structural typical naming conventions
            var btnNew = root.Q<Button>("home-btn-new-project");
            var btnOpen = root.Q<Button>("home-btn-open-project");
            var btnSave = root.Q<Button>("home-btn-save-project");
            var btnExport = root.Q<Button>("home-btn-import-file");

            if (btnNew != null) btnNew.clicked += CreateNewProject;
            if (btnOpen != null) btnOpen.clicked += OpenExistingProject;
            if (btnSave != null) btnSave.clicked += QuickSaveProject;
        }
    }

    private void CreateNewProject()
    {
        Debug.Log("Resetting solver and clearing stage for a new Simulation...");
        // Clear variables, empty mesh filters, etc
    }

    private void OpenExistingProject()
    {
#if UNITY_EDITOR
        string selectedPath = EditorUtility.OpenFilePanel("Open FluidWorks Project", Application.persistentDataPath, "fluidworks");
        if (!string.IsNullOrEmpty(selectedPath))
        {
            projectLoader.LoadProject(selectedPath);
            activeProjectName = System.IO.Path.GetFileNameWithoutExtension(selectedPath);
        }
#else
        Debug.LogWarning("File dialog handling required for standalone builds (e.g., SimpleFileBrowser).");
#endif
    }

    private void QuickSaveProject()
    {
#if UNITY_EDITOR
        string selectedPath = EditorUtility.SaveFilePanel("Save FluidWorks Project", Application.persistentDataPath, activeProjectName, "fluidworks");
        if (!string.IsNullOrEmpty(selectedPath))
        {
            activeProjectName = System.IO.Path.GetFileNameWithoutExtension(selectedPath);

            // Construct current state dynamically from runtime solver / managers
            ProjectData currentData = new ProjectData();
            currentData.metadata.projectName = activeProjectName;
            currentData.metadata.creationDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            currentData.metadata.softwareVersion = Application.version;
            
            if (Camera.main != null)
            {
                currentData.visualization.cameraPosition = Camera.main.transform.position;
                currentData.visualization.cameraRotation = Camera.main.transform.rotation;
            }

            // Example dummy model path
            string activeModelOriginPath = ""; // Feed from your model loader
            
            projectSaver.SaveProject(selectedPath, currentData, activeModelOriginPath);
        }
#else
        Debug.LogWarning("File dialog handling required for standalone builds.");
#endif
    }
}
