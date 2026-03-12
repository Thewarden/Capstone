using UnityEngine;
using UnityEditor;
using SFB; // Standalone File Browser

[CustomEditor(typeof(BlueprintProcessor))]
public class BlueprintEditor : Editor
{
    // Tracks which category is selected in the dropdown
    private int selectedCategoryIndex = 0;
    private int selectedPrefabIndex = 0;

    private readonly string[] categoryOptions = new string[]
    {
        "Sofa", "Table", "Plant", "Chair", "Lamp"
    };

    public override void OnInspectorGUI()
    {
        BlueprintProcessor processor = (BlueprintProcessor)target;

        // 1. Monitor for any changes in the default inspector (sliders, dropdowns)
        EditorGUI.BeginChangeCheck();

        DrawDefaultInspector();

        // If any value (like threshold) was changed, re-process the image
        if (EditorGUI.EndChangeCheck())
        {
            processor.ProcessImage();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Image Processing", EditorStyles.boldLabel);

        // 2. Pick Image Button
        if (GUILayout.Button("Pick Blueprint Image", GUILayout.Height(30)))
        {
            string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Blueprint", "", "*png;*jpg;*jpeg", false);
            if (paths.Length > 0)
            {
                // Record undo for the processor state
                Undo.RecordObject(processor, "Load Blueprint Image");
                processor.LoadImage(paths[0]);
            }
        }

        GUI.backgroundColor = new Color(1f, 0.75f, 0.25f);
        if(GUILayout.Button("Clear Imported Image", GUILayout.Height(25)))
        {
            Undo.RecordObject(processor, "Clear Blueprint Image");
            processor.ClearImage();
        }
        GUI.backgroundColor = Color.white;

        // 3. Visual Preview
        if (processor.previewTexture != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Edge Preview (Current Settings):");

            // Calculate aspect ratio so the image isn't squashed
            float aspect = (float)processor.previewTexture.height / processor.previewTexture.width;
            float width = EditorGUIUtility.currentViewWidth - 40;
            float height = width * aspect;

            Rect rect = GUILayoutUtility.GetRect(width, height);
            EditorGUI.DrawPreviewTexture(rect, processor.previewTexture);

            // Helpful hint for the user
            EditorGUILayout.HelpBox("Adjust the Threshold slider above to clean up the edges.", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.Separator();

        // 4. Action Buttons
        GUI.backgroundColor = Color.green; // Make the generate button stand out
        if (GUILayout.Button("Generate 3D Model", GUILayout.Height(40)))
        {
            processor.Generate3DFromEdges();
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Export Last Generated Model"))
        {
            processor.exportModel();
        }

        EditorGUILayout.Space();

        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // Reddish for delete
        if (GUILayout.Button("Clear All Generated Models"))
        {
            if (EditorUtility.DisplayDialog("Confirm Delete", "Are you sure you want to delete all generated meshes in the container?", "Yes", "Cancel"))
            {
                processor.deleteAllModel();
            }
        }
        GUI.backgroundColor = Color.white;

        bool modelExists = processor.modelContainer != null && processor.modelContainer.transform.childCount > 0;

        if (modelExists)
        {
            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Interior Design", EditorStyles.boldLabel);

            // Category dropdown
            selectedCategoryIndex = EditorGUILayout.Popup(
                "Object Category", selectedCategoryIndex, categoryOptions);

            // Get the prefab array for the selected category
            GameObject[] currentArray = processor.GetPrefabArray(categoryOptions[selectedCategoryIndex]);

            if (currentArray == null || currentArray.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No {categoryOptions[selectedCategoryIndex]} prefabs assigned. Add them in the Inspector above.",
                    MessageType.Warning);
            }
            else
            {
                // Build prefab name list for second dropdown
                string[] prefabNames = new string[currentArray.Length];
                for (int i = 0; i < currentArray.Length; i++)
                    prefabNames[i] = currentArray[i] != null ? currentArray[i].name : "(empty)";

                // Clamp index in case array shrank
                selectedPrefabIndex = Mathf.Clamp(selectedPrefabIndex, 0, currentArray.Length - 1);
                selectedPrefabIndex = EditorGUILayout.Popup("Select Prefab", selectedPrefabIndex, prefabNames);

                EditorGUILayout.Space();

                // Add Object button
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f); // Light blue
                if (GUILayout.Button($"Add  {prefabNames[selectedPrefabIndex]}  to Scene", GUILayout.Height(35)))
                {
                    GameObject selectedPrefab = currentArray[selectedPrefabIndex];
                    if (selectedPrefab != null)
                    {
                        Undo.RecordObject(processor, "Add Interior Object");
                        processor.PlaceInteriorObject(selectedPrefab);
                    }
                    else
                    {
                        Debug.LogWarning("Selected prefab slot is empty!");
                    }
                }
                GUI.backgroundColor = Color.white;

                // Clear all interior objects
                EditorGUILayout.Space();
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("Clear All Interior Objects"))
                {
                    if (EditorUtility.DisplayDialog("Confirm Delete",
                        "Delete all placed interior objects?", "Yes", "Cancel"))
                        processor.ClearInteriorObjects();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        // 5. Ensure the UI updates smoothly
        if (GUI.changed)
        {
            EditorUtility.SetDirty(processor);
        }
    }
}