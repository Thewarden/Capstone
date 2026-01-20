using UnityEngine;
using System.IO;
using System.Text;

public class SaveModel
{
    public static void exportMesh(Mesh mesh, string path)
    {
        StringBuilder sb = new StringBuilder();

        foreach (Vector3 v in mesh.vertices)
            sb.AppendLine($"v {v.x} {v.y} {v.z}");

        foreach (Vector3 n in mesh.normals)
            sb.AppendLine($"vn {n.x} {n.y} {n.z}");

        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            int a = mesh.triangles[i] + 1;
            int b = mesh.triangles[i + 1] + 1;
            int c = mesh.triangles[i + 2] + 1;
            sb.AppendLine($"f {a}//{a} {b}//{b} {c}//{c}");
        }

        File.WriteAllText(path, sb.ToString());

        Debug.Log("Saved model to " + path);
    }
}
