#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BodyTracking
{
    public class SMPLPrefabGenerator
    {
        [MenuItem("Tools/Generate SMPL Skeleton Prefab")]
        public static void GenerateSMPLPrefab()
        {
            string[] jointNames = new string[]
            {
                "Pelvis", "L_Hip", "R_Hip", "Spine1", "L_Knee", "R_Knee", "Spine2",
                "L_Ankle", "R_Ankle", "Spine3", "L_Foot", "R_Foot", "Neck", "L_Collar",
                "R_Collar", "Head", "L_Shoulder", "R_Shoulder", "L_Elbow", "R_Elbow",
                "L_Wrist", "R_Wrist", "L_Hand", "R_Hand"
            };
            
            // SMPL kinematic tree parents
            int[] parents = new int[]
            {
                -1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 12, 13, 14, 16, 17, 18, 19, 20, 21
            };
            
            // Approximate local positions for a 1.75m standard human in A-pose
            Vector3[] localPositions = new Vector3[]
            {
                new Vector3(0, 0.95f, 0),        // 0: Pelvis (Relative to Root)
                new Vector3(-0.09f, -0.05f, 0),  // 1: L_Hip
                new Vector3(0.09f, -0.05f, 0),   // 2: R_Hip
                new Vector3(0, 0.12f, 0),        // 3: Spine1
                new Vector3(0, -0.42f, 0),       // 4: L_Knee
                new Vector3(0, -0.42f, 0),       // 5: R_Knee
                new Vector3(0, 0.12f, 0),        // 6: Spine2
                new Vector3(0, -0.4f, 0),        // 7: L_Ankle
                new Vector3(0, -0.4f, 0),        // 8: R_Ankle
                new Vector3(0, 0.12f, 0),        // 9: Spine3
                new Vector3(0, -0.06f, 0.12f),   // 10: L_Foot
                new Vector3(0, -0.06f, 0.12f),   // 11: R_Foot
                new Vector3(0, 0.15f, 0.02f),    // 12: Neck
                new Vector3(-0.06f, 0.1f, 0),    // 13: L_Collar
                new Vector3(0.06f, 0.1f, 0),     // 14: R_Collar
                new Vector3(0, 0.12f, 0),        // 15: Head
                new Vector3(-0.12f, 0, 0),       // 16: L_Shoulder
                new Vector3(0.12f, 0, 0),        // 17: R_Shoulder
                new Vector3(-0.18f, -0.18f, 0),  // 18: L_Elbow (A-pose: down and out)
                new Vector3(0.18f, -0.18f, 0),   // 19: R_Elbow
                new Vector3(-0.18f, -0.18f, 0),  // 20: L_Wrist
                new Vector3(0.18f, -0.18f, 0),   // 21: R_Wrist
                new Vector3(-0.06f, -0.06f, 0),  // 22: L_Hand
                new Vector3(0.06f, -0.06f, 0),   // 23: R_Hand
            };
            
            // Create a root container so that Pelvis can have an offset of (0, 0.95, 0)
            GameObject rootContainer = new GameObject("SMPL_Skeleton");
            // Only add the debug axis script to the root node
            rootContainer.AddComponent<PoseDebugAxis>();
            
            GameObject[] joints = new GameObject[24];
            
            for (int i = 0; i < 24; i++)
            {
                joints[i] = new GameObject(jointNames[i]);
                
                if (parents[i] != -1)
                {
                    joints[i].transform.parent = joints[parents[i]].transform;
                }
                else
                {
                    joints[i].transform.parent = rootContainer.transform;
                }
                
                joints[i].transform.localPosition = localPositions[i];
            }
            
            string folderPath = Application.dataPath + "/Scripts/BodyTracking";
            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }
            
            string path = "Assets/Scripts/BodyTracking/SMPL_Skeleton.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootContainer, path);
            Object.DestroyImmediate(rootContainer);
            
            Debug.Log("SMPL Skeleton Prefab with A-pose successfully created at " + path);
        }
    }
}
#endif
