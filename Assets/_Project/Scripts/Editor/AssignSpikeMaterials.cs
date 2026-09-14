using UnityEngine;
using UnityEditor;

public class AssignSpikeMaterials
{
    [MenuItem("Tools/Spike/Assign Wheel and Ground Materials")]
    public static void AssignMaterials()
    {
        Material wheelMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/Mat_Wheel.mat");
        Material groundMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/Mat_Ground.mat");
        Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/_Project/Materials/Mat_CarBody.mat");

        string[] wheelMeshNames = { "Wheel_FL_Mesh", "Wheel_FR_Mesh", "Wheel_RL_Mesh", "Wheel_RR_Mesh" };
        foreach (var name in wheelMeshNames)
        {
            GameObject go = GameObject.Find(name);
            if (go != null && wheelMat != null)
            {
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = wheelMat;
            }
            else
            {
                Debug.LogWarning($"Could not find or assign wheel: {name}");
            }
        }

        GameObject plane = GameObject.Find("Plane");
        if (plane != null && groundMat != null)
        {
            var renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = groundMat;
        }

        GameObject car = GameObject.Find("Car");
        if (car != null && bodyMat != null)
        {
            var renderer = car.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = bodyMat;
        }

        Debug.Log("Spike materials assigned.");
    }
}