using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System;

public class BuildDateProcessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string dateStr = DateTime.Now.ToString("yyMMdd");
        string version = PlayerSettings.bundleVersion;
        
        string resourcesPath = "Assets/Resources";
        if (!AssetDatabase.IsValidFolder(resourcesPath))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        string filePath = resourcesPath + "/BuildVersion.txt";
        string outputText = version + "_" + dateStr;
        File.WriteAllText(filePath, outputText);
        
        AssetDatabase.ImportAsset(filePath);
        Debug.Log("BuildDateProcessor: Generated BuildVersion.txt with content " + outputText);
    }
}
