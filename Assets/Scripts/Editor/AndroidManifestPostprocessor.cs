#if UNITY_EDITOR
using UnityEditor.Android;
using System.IO;
using System.Xml;
using UnityEngine;

public class AndroidManifestPostprocessor : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 999;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
        
        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[WiFiPermissionInjector] Manifest not found at: {manifestPath}");
            return;
        }

        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(manifestPath);

        XmlNode manifestNode = xmlDoc.SelectSingleNode("/manifest");
        if (manifestNode == null)
        {
            Debug.LogError("[WiFiPermissionInjector] Invalid Manifest format.");
            return;
        }

        string androidNs = manifestNode.GetNamespaceOfPrefix("android");
        if (string.IsNullOrEmpty(androidNs))
        {
            androidNs = "http://schemas.android.com/apk/res/android";
        }

        bool hasPermission = false;
        XmlNodeList permissions = manifestNode.SelectNodes("uses-permission");
        
        if (permissions != null)
        {
            foreach (XmlElement permission in permissions)
            {
                if (permission.GetAttribute("name", androidNs) == "android.permission.ACCESS_WIFI_STATE")
                {
                    hasPermission = true;
                    break;
                }
            }
        }

        bool modified = false;
        if (!hasPermission)
        {
            XmlElement newElement = xmlDoc.CreateElement("uses-permission");
            newElement.SetAttribute("name", androidNs, "android.permission.ACCESS_WIFI_STATE");
            manifestNode.AppendChild(newElement);
            modified = true;
            Debug.Log("[WiFiPermissionInjector] ACCESS_WIFI_STATE injected successfully.");
        }

        XmlNode applicationNode = xmlDoc.SelectSingleNode("/manifest/application");
        if (applicationNode != null)
        {
            XmlElement appElement = (XmlElement)applicationNode;
            if (!appElement.HasAttribute("usesCleartextTraffic", androidNs) || appElement.GetAttribute("usesCleartextTraffic", androidNs) != "true")
            {
                appElement.SetAttribute("usesCleartextTraffic", androidNs, "true");
                modified = true;
                Debug.Log("[WiFiPermissionInjector] usesCleartextTraffic=true injected successfully.");
            }
        }

        if (modified)
        {
            xmlDoc.Save(manifestPath);
        }
    }
}
#endif