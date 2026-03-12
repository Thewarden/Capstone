using OpenCvSharp;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class BlueprintProcessor : MonoBehaviour
{
    public enum EdgeMode { Canny, Threshold }

    [Header("3D Settings")]
    public float wallHeight = 3f;
    public float wallThickness = 0.2f;
    public float pixelToMeter = 0.01f;
    public Material wallMaterial;
    public GameObject modelContainer; // Changed from 'model' to match your previous code

    [Header("CV Settings")]
    public EdgeMode currentEdgeMode = EdgeMode.Canny;
    [Range(0, 255)] public float threshold = 100f;

    // --- Restored Missing Variables ---
    [HideInInspector] public Texture2D previewTexture;
    private Mat originalMat;
    private Mat edgesMat; // Required for generation logic
    private Mesh meshExport;
    private string savePath;

    [Header("Interior Objects")]
    public GameObject[] sofaPrefabs;
    public GameObject[] tablePrefabs;
    public GameObject[] plantPrefabs;
    public GameObject[] chairPrefabs;
    public GameObject[] lampPrefabs;

    [Range(0.05f, 1f)]
    public float prefabScaleMultiplier = 0.15f;  // ← Add this

    [HideInInspector] public GameObject interiorContainer;

    public void LoadImage(string path)
    {
        originalMat?.Dispose();
        originalMat = Cv2.ImRead(path, ImreadModes.Grayscale);
        ProcessImage();
    }

    public void ProcessImage()
    {
        if (originalMat == null || originalMat.Empty()) return;

        edgesMat?.Dispose();
        edgesMat = new Mat();

        if (currentEdgeMode == EdgeMode.Canny)
        {
            Cv2.Canny(originalMat, edgesMat, threshold, threshold * 2);
        }
        else
        {
            Cv2.Threshold(originalMat, edgesMat, threshold, 255, ThresholdTypes.BinaryInv);
            using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5)))
            {
                Cv2.MorphologyEx(edgesMat, edgesMat, MorphTypes.Close, kernel);
                Cv2.Dilate(edgesMat, edgesMat, kernel, null, 2); // Fixed Argument 4 issue
            }
        }

        // Update the preview for the Editor
        if (previewTexture != null) DestroyImmediate(previewTexture);
        previewTexture = MatToTexture(edgesMat);
    }

    public void Generate3DFromEdges()
    {
        if (edgesMat == null || edgesMat.Empty())
        {
            Debug.LogWarning("No edges to generate from. Pick image first.");
            return;
        }

        // Auto-clear existing models
        deleteAllModel();

        if (modelContainer == null)
            modelContainer = new GameObject("GeneratedModel_Container");

        Point[][] contours;
        HierarchyIndex[] hierarchy;
        Cv2.FindContours(edgesMat, out contours, out hierarchy,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);

        int roomIndex = 0;
        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < 50) continue;

            // Parent object to group each room's wall cubes
            GameObject roomParent = new GameObject("Room_" + roomIndex);
            roomParent.transform.SetParent(modelContainer.transform);

            for (int i = 0; i < contour.Length; i++)
            {
                // Get current and next point (wrap around at end)
                Vector3 startPoint = new Vector3(contour[i].X * pixelToMeter, 0f, contour[i].Y * pixelToMeter);
                Vector3 endPoint = new Vector3(contour[(i + 1) % contour.Length].X * pixelToMeter, 0f,
                                                 contour[(i + 1) % contour.Length].Y * pixelToMeter);

                PlaceWallCube(startPoint, endPoint, roomParent.transform, i);
            }

            roomIndex++;
        }

        Debug.Log($"Generated {roomIndex} rooms using cube primitives.");
        savePath = Application.dataPath + $"/GeneratedRoom_{roomIndex}.obj";
    }

    void PlaceWallCube(Vector3 start, Vector3 end, Transform parent, int index)
    {
        float segmentLength = Vector3.Distance(start, end);

        // Skip if points are too close (degenerate edge)
        if (segmentLength < 0.001f) return;

        // 1. Midpoint → where the cube is placed
        Vector3 midPoint = (start + end) / 2f;
        midPoint.y = wallHeight / 2f; // Center cube vertically

        // 2. Direction → used to rotate the cube along the wall edge
        Vector3 direction = (end - start).normalized;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

        // 3. Scale: length of segment × wall thickness × wall height
        Vector3 scale = new Vector3(wallThickness, wallHeight, segmentLength);

        // 4. Create cube and configure it
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_" + index;
        wall.transform.SetParent(parent);
        wall.transform.position = midPoint;
        wall.transform.rotation = rotation;
        wall.transform.localScale = scale;

        // Apply material if assigned
        if (wallMaterial != null)
            wall.GetComponent<MeshRenderer>().material = wallMaterial;
    }

    public void deleteAllModel()
    {
        if (modelContainer == null) return;
        foreach (Transform child in modelContainer.transform)
            DestroyImmediate(child.gameObject);
    }

    public void exportModel()
    {
        // Ensure you have a 'SaveModel' utility class or replace this with your export logic
        if (meshExport != null && !string.IsNullOrEmpty(savePath))
        {
            Debug.Log($"Exporting to {savePath}");
            // SaveModel.exportMesh(meshExport, savePath); 
        }
    }

    public GameObject[] GetPrefabArray(string category)
    {
        switch (category)
        {
            case "Sofa": return sofaPrefabs;
            case "Table": return tablePrefabs;
            case "Plant": return plantPrefabs;
            case "Chair": return chairPrefabs;
            case "Lamp": return lampPrefabs;
            default: return null;
        }
    }

    // Places the selected prefab at the center of the generated model
    public void PlaceInteriorObject(GameObject prefab)
    {
        if (interiorContainer == null)
        {
            interiorContainer = new GameObject("InteriorObjects_Container");
            if (modelContainer != null)
                interiorContainer.transform.SetParent(modelContainer.transform.parent);
        }

        Vector3 spawnPosition = GetModelCenter();

        GameObject placed = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (placed == null)
            placed = Instantiate(prefab);

        placed.transform.SetParent(interiorContainer.transform);
        placed.transform.position = spawnPosition;
        placed.name = prefab.name + "_" + interiorContainer.transform.childCount;

        // ✅ Scale the prefab to match the model's proportions
        ScaleObjectToModel(placed);

        Undo.RegisterCreatedObjectUndo(placed, "Place Interior Object");
        Debug.Log($"Placed {prefab.name} at {spawnPosition}");
        Selection.activeGameObject = placed;
    }

    void ScaleObjectToModel(GameObject placed)
    {
        // 1. Get the model's bounding box
        Bounds modelBounds = GetModelBounds();

        // 2. Get the prefab's bounding box at its current scale (scale = 1,1,1 baseline)
        Bounds prefabBounds = GetObjectBounds(placed);

        if (prefabBounds.size == Vector3.zero || modelBounds.size == Vector3.zero)
        {
            Debug.LogWarning("Could not calculate bounds for scaling.");
            return;
        }

        // 3. Calculate scale ratios per axis
        //    We use wallHeight as the reference for Y so objects don't exceed wall height.
        //    For X and Z we scale relative to the model's footprint size.
        float scaleX = (modelBounds.size.x / prefabBounds.size.x) * prefabScaleMultiplier;
        float scaleY = (wallHeight / prefabBounds.size.y) * prefabScaleMultiplier;
        float scaleZ = (modelBounds.size.z / prefabBounds.size.z) * prefabScaleMultiplier;

        // 4. Use the smallest axis to keep proportions — prevents stretching
        float uniformScale = Mathf.Min(scaleX, scaleY, scaleZ);

        placed.transform.localScale = Vector3.one * uniformScale;

        Debug.Log($"Scaled {placed.name} by {uniformScale:F3} " +
                  $"(ModelBounds: {modelBounds.size}, PrefabBounds: {prefabBounds.size})");
    }

    Bounds GetModelBounds()
    {
        Bounds bounds = new Bounds();
        bool initialized = false;

        foreach (Transform child in modelContainer.transform)
        {
            // Direct renderer (mesh-based)
            Renderer r = child.GetComponent<Renderer>();
            if (r != null)
            {
                if (!initialized) { bounds = r.bounds; initialized = true; }
                else bounds.Encapsulate(r.bounds);
            }

            // Grandchildren (cube-based: Room_X → Wall_X)
            foreach (Transform grandchild in child)
            {
                Renderer gr = grandchild.GetComponent<Renderer>();
                if (gr != null)
                {
                    if (!initialized) { bounds = gr.bounds; initialized = true; }
                    else bounds.Encapsulate(gr.bounds);
                }
            }
        }

        return bounds;
    }

    // Gets the combined local bounds of a placed prefab (all renderers inside it)
    Bounds GetObjectBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    Vector3 GetModelCenter()
    {
        if (modelContainer == null) return Vector3.zero;

        Bounds bounds = new Bounds();
        bool boundsInitialized = false;

        foreach (Transform child in modelContainer.transform)
        {
            Renderer r = child.GetComponent<Renderer>();
            if (r == null)
            {
                // Check grandchildren (room parents contain wall cubes)
                foreach (Transform grandchild in child)
                {
                    Renderer gr = grandchild.GetComponent<Renderer>();
                    if (gr != null)
                    {
                        if (!boundsInitialized) { bounds = gr.bounds; boundsInitialized = true; }
                        else bounds.Encapsulate(gr.bounds);
                    }
                }
            }
            else
            {
                if (!boundsInitialized) { bounds = r.bounds; boundsInitialized = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        // Spawn at floor level (y = 0), center of the model footprint
        return new Vector3(bounds.center.x, 0f, bounds.center.z);
    }

    public void ClearInteriorObjects()
    {
        if (interiorContainer == null) return;

        List<GameObject> toDestroy = new List<GameObject>();
        foreach (Transform child in interiorContainer.transform)
            toDestroy.Add(child.gameObject);

        for (int i = toDestroy.Count - 1; i >= 0; i--)
            DestroyImmediate(toDestroy[i]);

        Debug.Log("Cleared all interior objects.");
    }

    // --- Helper Methods ---

    Texture2D MatToTexture(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;

        if (mat.Channels() == 1)
        {
            byte[] raw;
            if (!mat.GetArray(out raw)) return MatToTexture_Encoded(mat);

            int w = mat.Width;
            int h = mat.Height;
            Color32[] pixels = new Color32[w * h];
            for (int i = 0; i < raw.Length; i++)
            {
                byte v = raw[i];
                pixels[i] = new Color32(v, v, v, 255);
            }

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
        return MatToTexture_Encoded(mat);
    }

    Texture2D MatToTexture_Encoded(Mat mat)
    {
        byte[] png = mat.ToBytes(".png");
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(png);
        return tex;
    }

    void OnDestroy()
    {
        originalMat?.Dispose();
        edgesMat?.Dispose();
        if (previewTexture != null) DestroyImmediate(previewTexture);
    }

    public void ClearImage()
    {
        originalMat?.Dispose();
        originalMat = null;

        edgesMat?.Dispose();
        edgesMat = null;

        if(previewTexture != null)
        {
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }

        Debug.Log("Blueprint image cleared");
    }
}

class Triangulator
{
    List<Vector2> m_points;

    public Triangulator(Vector3[] points)
    {
        m_points = new List<Vector2>();
        for (int i = 0; i < points.Length; i++)
            m_points.Add(new Vector2(points[i].x, points[i].z));
    }

    public int[] Triangulate()
    {
        List<int> indices = new List<int>();

        int n = m_points.Count;
        if (n < 3)
            return indices.ToArray();

        int[] V = new int[n];
        if (Area() > 0)
        {
            for (int v = 0; v < n; v++)
                V[v] = v;
        }
        else
        {
            for (int v = 0; v < n; v++)
                V[v] = (n - 1) - v;
        }

        int nv = n;
        int count = 2 * nv;
        for (int m = 0, v = nv - 1; nv > 2;)
        {
            if ((count--) <= 0)
                return indices.ToArray();

            int u = v;
            if (nv <= u) u = 0;
            v = u + 1;
            if (nv <= v) v = 0;
            int w = v + 1;
            if (nv <= w) w = 0;

            if (Snip(u, v, w, nv, V))
            {
                int a = V[u];
                int b = V[v];
                int c = V[w];
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
                for (int s = v, t = v + 1; t < nv; s++, t++)
                    V[s] = V[t];
                nv--;
                count = 2 * nv;
            }
        }

        return indices.ToArray();
    }

    float Area()
    {
        int n = m_points.Count;
        float A = 0.0f;
        for (int p = n - 1, q = 0; q < n; p = q++)
        {
            Vector2 pval = m_points[p];
            Vector2 qval = m_points[q];
            A += pval.x * qval.y - qval.x * pval.y;
        }
        return A * 0.5f;
    }

    bool Snip(int u, int v, int w, int n, int[] V)
    {
        Vector2 A = m_points[V[u]];
        Vector2 B = m_points[V[v]];
        Vector2 C = m_points[V[w]];

        if (Mathf.Epsilon > (((B.x - A.x) * (C.y - A.y)) - ((B.y - A.y) * (C.x - A.x))))
            return false;

        for (int p = 0; p < n; p++)
        {
            if ((p == u) || (p == v) || (p == w)) continue;
            Vector2 P = m_points[V[p]];
            if (InsideTriangle(A, B, C, P)) return false;
        }

        return true;
    }

    bool InsideTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 P)
    {
        float ax = C.x - B.x; float ay = C.y - B.y;
        float bx = A.x - C.x; float by = A.y - C.y;
        float cx = B.x - A.x; float cy = B.y - A.y;
        float apx = P.x - A.x; float apy = P.y - A.y;
        float bpx = P.x - B.x; float bpy = P.y - B.y;
        float cpx = P.x - C.x; float cpy = P.y - C.y;

        float aCROSSbp = ax * bpy - ay * bpx;
        float cCROSSap = cx * apy - cy * apx;
        float bCROSScp = bx * cpy - by * cpx;

        return ((aCROSSbp >= 0.0f) && (bCROSScp >= 0.0f) && (cCROSSap >= 0.0f));
    }
}
