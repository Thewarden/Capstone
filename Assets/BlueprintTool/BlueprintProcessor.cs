using UnityEngine;
using OpenCvSharp;
using System.Collections.Generic;

public class BlueprintProcessor : MonoBehaviour
{
    public enum EdgeMode { Canny, Threshold }

    [Header("3D Settings")]
    public float wallHeight = 3f;
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

        if (modelContainer == null)
        {
            modelContainer = new GameObject("GeneratedModel_Container");
        }

        Point[][] contours;
        HierarchyIndex[] hierarchy;
        // Fixed Argument 4: Use 'null' for the offset Point if not needed
        Cv2.FindContours(edgesMat, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);

        int created = 0;
        foreach (var contour in contours)
        {
            if (Cv2.ContourArea(contour) < 50) continue;

            List<Vector3> points = new List<Vector3>(contour.Length);
            for (int i = 0; i < contour.Length; i++)
            {
                float x = contour[i].X * pixelToMeter;
                float z = contour[i].Y * pixelToMeter;
                points.Add(new Vector3(x, 0f, z));
            }

            Mesh mesh = ExtrudePolygon(points, wallHeight);
            GameObject go = new GameObject("Room_" + created);
            go.transform.SetParent(modelContainer.transform);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.mesh = mesh;
            if (wallMaterial != null) mr.material = wallMaterial;

            created++;
            meshExport = mesh; // Note: This only stores the LAST mesh created
        }

        Debug.Log($"Created {created} room meshes.");
        savePath = Application.dataPath + $"/GeneratedRoom_{created}.obj";
    }

    public void deleteAllModel()
    {
        if (modelContainer == null) return;

        Debug.Log("Deleted Meshes");
        List<GameObject> toDestroy = new List<GameObject>();
        foreach (Transform child in modelContainer.transform)
        {
            toDestroy.Add(child.gameObject);
        }

        for (int i = toDestroy.Count - 1; i >= 0; i--)
        {
            DestroyImmediate(toDestroy[i]);
        }
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

    // --- Helper Methods ---

    Mesh ExtrudePolygon(List<Vector3> basePoints, float height)
    {
        int count = basePoints.Count;
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        for (int i = 0; i < count; i++)
        {
            verts.Add(basePoints[i]);
            verts.Add(basePoints[i] + Vector3.up * height);
        }

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int b0 = i * 2, t0 = b0 + 1, b1 = next * 2, t1 = b1 + 1;
            tris.Add(b0); tris.Add(t0); tris.Add(t1);
            tris.Add(b0); tris.Add(t1); tris.Add(b1);
        }

        Vector3[] poly = basePoints.ToArray();
        Triangulator tr = new Triangulator(poly);
        int[] indices = tr.Triangulate();

        for (int i = 0; i < indices.Length; i += 3)
        {
            tris.Add(indices[i] * 2);
            tris.Add(indices[i + 2] * 2);
            tris.Add(indices[i + 1] * 2);
        }

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
