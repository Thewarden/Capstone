using UnityEngine;
using UnityEngine.UI;
using OpenCvSharp;
using SFB; // StandaloneFileBrowser need package. Just google it. Nevermind already in project
using System.Collections.Generic;

public class BlueprintTo3D_UI : MonoBehaviour
{
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

        // initialize label for threashhold
        if (thresholdValueText != null) thresholdValueText.text = $"Threshold: {thresholdSlider?.value ?? currentThreshold:F0}";
        if (thresholdSlider != null) currentThreshold = thresholdSlider.value;
    }

    /* --- UI actions --- */

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
        ApplyCannyAndUpdatePreview();
    }

    // --- Image processing / UI ---
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
        ApplyCannyAndUpdatePreview();
    }

    void ApplyCannyAndUpdatePreview()
    {
        if (originalMat == null || originalMat.Empty()) return;

        edgesMat?.Dispose();
        edgesMat = new Mat();
        // use lower threshold = currentThreshold, upper = currentThreshold * 2
        Cv2.Canny(originalMat, edgesMat, currentThreshold, currentThreshold * 2);

        // Show edges (edge Mat is single-channel)
        if (edgesTex != null) { Destroy(edgesTex); edgesTex = null; }
        edgesTex = MatToTexture(edgesMat);
        if (edgesImageUI != null) edgesImageUI.texture = edgesTex;
    }

    /* --- Generate 3D from current edgesMat --- */
    //Add comments from here
    public void Generate3DFromEdges()
    {
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
        }

        Debug.Log($"Created {created} room meshes.");
    }

    public void deleteAllModel()
    {
        Debug.Log("Deleted Meshes");
        foreach (Transform child in model.transform)
        {
            Destroy(child.gameObject);
        }
    }

    // --- Simple extrusion because I don't know any other method. Hope to learn something new in a few months ---
    Mesh ExtrudePolygon(List<Vector3> basePoints, float height)
    {
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        int count = basePoints.Count;
        for (int i = 0; i < count; i++)
        {
            verts.Add(basePoints[i]); // bottom
            verts.Add(basePoints[i] + Vector3.up * height); // top
        }

        // sides
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int b0 = i * 2;
            int t0 = b0 + 1;
            int b1 = next * 2;
            int t1 = b1 + 1;
            tris.AddRange(new int[] { b0, t0, t1, b0, t1, b1 });
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
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
}
