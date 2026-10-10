using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System;

namespace UTXR.Editor
{
    public class BuildDateProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
    
        public void OnPreprocessBuild(BuildReport report)
        {
            string dateStr = DateTime.Now.ToString("yyMMdd");
            string version = PlayerSettings.bundleVersion;
            
            string utxrPath = "Assets/UTXR";
            string resourcesPath = "Assets/UTXR/Resources";
            if (!AssetDatabase.IsValidFolder(utxrPath))
            {
                AssetDatabase.CreateFolder("Assets", "UTXR");
            }
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder(utxrPath, "Resources");
            }
    
            string filePath = resourcesPath + "/BuildVersion.txt";
            string outputText = version + "_" + dateStr;
            File.WriteAllText(filePath, outputText);
            
            AssetDatabase.ImportAsset(filePath);
            Debug.Log("BuildDateProcessor: Generated BuildVersion.txt with content " + outputText);
        }
    }
}

