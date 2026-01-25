using UnityEngine;
using UnityEngine.UI;
using OpenCvSharp;
using SFB; // StandaloneFileBrowser need package. Just google it. Nevermind already in project
using System.Collections.Generic;

public class BlueprintTo3D_UI : MonoBehaviour
{
    public enum EdgeMode
    {
        Canny,
        Threshold
    }


    [Header("3D")]
    public float wallHeight = 3f;
    public Material wallMaterial;
    public float pixelToMeter = 0.01f; // how many meters per image pixel. If someone has time and the braincells, rework this pls
    public GameObject model;

    [Header("UI")]
    public RawImage originalImageUI;
    public RawImage edgesImageUI;
    public Slider thresholdSlider;        // set min/max in Inspector
    public Text thresholdValueText;       // optional label
    public Button pickImageButton;        // wired to PickImage()
    public Button generateButton;         // wired to Generate3DFromEdges()
    public Button deleteModelButton;      // wired to deleteAllModel()
    public Button exportModelButton;      // Gee I wonder what this one does
    public Dropdown edgeModeDropdown;     // Dropdwon to choose between cv techniques
    private EdgeMode currentEdgeMode = EdgeMode.Canny;


    [Header("Data")]
    string savePath;
    private Mesh meshExport;

    // internal, don't want on screen
    private Mat originalMat;   // grayscale source
    private Mat edgesMat;
    private Texture2D originalTex;
    private Texture2D edgesTex;
    private string currentPath;
    private float currentThreshold = 100f;


    void Start()
    {
        // Instead of using Update I just check if any value is changed
        if (thresholdSlider != null) thresholdSlider.onValueChanged.AddListener(OnThresholdChanged);
        if (pickImageButton != null) pickImageButton.onClick.AddListener(PickImage);
        if (generateButton != null) generateButton.onClick.AddListener(Generate3DFromEdges);
        if (deleteModelButton != null) deleteModelButton.onClick.AddListener(deleteAllModel);
        if (exportModelButton != null) exportModelButton.onClick.AddListener(exportModel);

        // initialize label for threashhold
        if (thresholdValueText != null) thresholdValueText.text = $"Threshold: {thresholdSlider?.value ?? currentThreshold:F0}";
        if (thresholdSlider != null) currentThreshold = thresholdSlider.value;


        //Handle the dropdown for edge detection technique dropdown
        if (edgeModeDropdown != null)
        {
            edgeModeDropdown.onValueChanged.AddListener(OnEdgeModeChanged);
            currentEdgeMode = (EdgeMode)edgeModeDropdown.value;
        }

    }



    //ExtensionFilter extensions = new ExtensionFilter("Images", "png", "jpg", "jpeg");
    //No longer neccesary but still keep it here in case I want to check the struct
    public void PickImage()
    {
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Blueprint", "", "*png;*jpg;*jpeg", false);
        if (paths.Length == 0) return;
        currentPath = paths[0];
        LoadAndShowImage(currentPath);
    }

    public void OnThresholdChanged(float val)
    {
        currentThreshold = val;
        if (thresholdValueText != null) thresholdValueText.text = $"Threshold: {currentThreshold:F0}";
        ApplyEdgeDetectionAndUpdatePreview();

    }


    void LoadAndShowImage(string path)
    {
        // Deletes old
        if (originalTex != null) { Destroy(originalTex); originalTex = null; }
        if (edgesTex != null) { Destroy(edgesTex); edgesTex = null; }

        // Load grayscale Mat
        originalMat?.Dispose();
        originalMat = Cv2.ImRead(path, ImreadModes.Grayscale);
        if (originalMat == null || originalMat.Empty())
        {
            Debug.LogError("Failed to load image as Mat: " + path);
            return;
        }

        // Show original in UI
        originalTex = MatToTexture(originalMat);
        if (originalImageUI != null) originalImageUI.texture = originalTex;

        // create initial edges
        ApplyEdgeDetectionAndUpdatePreview();

    }

    void ApplyCannyAndUpdatePreview()
    {
        //Alright this function is replaced. Can be deleted 
        /* Don't delte this yet. Trying out new things
        if (originalMat == null || originalMat.Empty()) return;

        edgesMat?.Dispose();
        edgesMat = new Mat();
        // use lower threshold = currentThreshold, upper = currentThreshold * 2
        Cv2.Canny(originalMat, edgesMat, currentThreshold, currentThreshold * 2);

        // Show edges (edge Mat is single-channel)
        if (edgesTex != null) { Destroy(edgesTex); edgesTex = null; }
        edgesTex = MatToTexture(edgesMat);
        if (edgesImageUI != null) edgesImageUI.texture = edgesTex;

        */

        if (originalMat == null || originalMat.Empty()) return;

        edgesMat?.Dispose();
        edgesMat = new Mat();

        
        Cv2.Threshold(originalMat, edgesMat, currentThreshold, 255, ThresholdTypes.BinaryInv);

        // This is for making the walls thicker
        Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
        Cv2.MorphologyEx(edgesMat, edgesMat, MorphTypes.Close, kernel);
        Cv2.Dilate(edgesMat, edgesMat, kernel, iterations: 2);

        kernel.Dispose();

        
        if (edgesTex != null) Destroy(edgesTex);
        edgesTex = MatToTexture(edgesMat);
        if (edgesImageUI != null) edgesImageUI.texture = edgesTex;

    }

    void ApplyEdgeDetectionAndUpdatePreview()
    {
        if (originalMat == null || originalMat.Empty()) return;

        edgesMat?.Dispose();
        edgesMat = new Mat();

        if (currentEdgeMode == EdgeMode.Canny)
        {
            Cv2.Canny(originalMat, edgesMat, currentThreshold, currentThreshold * 2);
        }
        else // Threshold
        {
            Cv2.Threshold(originalMat, edgesMat, currentThreshold, 255, ThresholdTypes.BinaryInv);

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.MorphologyEx(edgesMat, edgesMat, MorphTypes.Close, kernel);
            Cv2.Dilate(edgesMat, edgesMat, kernel, iterations: 2);
            kernel.Dispose();
        }

        if (edgesTex != null) Destroy(edgesTex);
        edgesTex = MatToTexture(edgesMat);

        if (edgesImageUI != null)
            edgesImageUI.texture = edgesTex;
    }


    /* --- Generate 3D from current edgesMat --- */
    //Add comments from here
    public void Generate3DFromEdges()
    {
        Camera cam = Camera.main;
        Vector3 camCenter = cam != null
            ? new Vector3(cam.transform.position.x, 0f, cam.transform.position.z)
            : Vector3.zero;


        if (edgesMat == null || edgesMat.Empty())
        {
            Debug.LogWarning("No edges to generate from. Pick image first.");
            return;
        }

        // Find contours
        Point[][] contours;
        HierarchyIndex[] hierarchy;
        Cv2.FindContours(edgesMat, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        int created = 0;
        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < 50) continue; // noise filter. Also change this later instead of using static value

            // Convert contour points to Unity world-space Vector3 list
            List<Vector3> points = new List<Vector3>(contour.Length);
            for (int i = 0; i < contour.Length; i++)
            {
                // contour[i].X = col, .Y = row..... Please don't remove this comment
                float x = contour[i].X * pixelToMeter;
                float z = contour[i].Y * pixelToMeter;
                // Optionally center: subtract half-width/height to center the mesh
                points.Add(new Vector3(x, 0f, z));
            }

            // Create mesh from polygon 
            Mesh mesh = ExtrudePolygon(points, wallHeight);
            GameObject go = new GameObject("Room_" + created);
            go.transform.SetParent(model.transform);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.mesh = mesh;
            if (wallMaterial != null) mr.material = wallMaterial;
            created++;
            meshExport = mesh;
        }

        Debug.Log($"Created {created} room meshes.");
        savePath = Application.dataPath + $"/GeneratedRoom_{created}.obj";
    }

    public void deleteAllModel()
    {
        Debug.Log("Deleted Meshes");
        foreach (Transform child in model.transform)
        {
            Destroy(child.gameObject);
        }
    }

    public void exportModel()
    {
        if (savePath != null) SaveModel.exportMesh(meshExport, savePath);
    }

    // --- Simple extrusion because I don't know any other method. Hope to learn something new in a few months ---
    Mesh ExtrudePolygon(List<Vector3> basePoints, float height)
    {
        int count = basePoints.Count;

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        // Bottom + Top vertices
        for (int i = 0; i < count; i++)
        {
            verts.Add(basePoints[i]); // bottom
            verts.Add(basePoints[i] + Vector3.up * height); // top
        }

        // Side walls
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;

            int b0 = i * 2;
            int t0 = b0 + 1;
            int b1 = next * 2;
            int t1 = b1 + 1;

            tris.Add(b0);
            tris.Add(t0);
            tris.Add(t1);

            tris.Add(b0);
            tris.Add(t1);
            tris.Add(b1);
        }

        // --- Floor / Ceiling ---

        Vector3[] poly = basePoints.ToArray();
        Triangulator tr = new Triangulator(poly);
        int[] indices = tr.Triangulate();

        // Floor (bottom)
        for (int i = 0; i < indices.Length; i += 3)
        {
            tris.Add(indices[i] * 2);
            tris.Add(indices[i + 2] * 2);
            tris.Add(indices[i + 1] * 2);
        }

        // Ceiling (top)
        for (int i = 0; i < indices.Length; i += 3)
        {
            tris.Add(indices[i] * 2 + 1);
            tris.Add(indices[i + 1] * 2 + 1);
            tris.Add(indices[i + 2] * 2 + 1);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }




    /*--- Mat -> Texture2D helper chooses fast path for single-channel mats --- */
    Texture2D MatToTexture(Mat mat)
    {
        if (mat == null || mat.Empty()) return null;

        // FAST path for single-channel mats (Canny output is 1-channel)
        if (mat.Channels() == 1)
        {
            // bytes length = width*height for single-channel
            byte[] raw;
            if (!mat.GetArray(out raw))
            {
                // fallback to encode path
                return MatToTexture_Encoded(mat);
            }

            int w = mat.Width;
            int h = mat.Height;
            Color32[] pixels = new Color32[w * h];
            int idx = 0;
            // mat.GetArray gives row-major order
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte v = raw[idx];
                    pixels[idx] = new Color32(v, v, v, 255);
                    idx++;
                }
            }

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
        else
        {
            // Generic path idk
            return MatToTexture_Encoded(mat);
        }
    }

    // encode to PNG + load. Basically returns image. Optional.
    Texture2D MatToTexture_Encoded(Mat mat)
    {
        byte[] png = mat.ToBytes(".png"); // uses OpenCvSharp encoding
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(png); // LoadImage replaces size + format
        return tex;
    }

    void OnDestroy()
    {
        // cleanup
        originalMat?.Dispose();
        edgesMat?.Dispose();
        if (originalTex != null) Destroy(originalTex);
        if (edgesTex != null) Destroy(edgesTex);
    }

    public void OnEdgeModeChanged(int value)
    {
        currentEdgeMode = (EdgeMode)value;
        ApplyEdgeDetectionAndUpdatePreview();
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
