using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneDDS;
using Network.InterfaceInfo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UTXR.Publishing;
using UTXR.UI;

namespace UTXR.Tests.Editor
{
    public static class XRPublishConnectionTests
    {
        public static int Run()
        {
            int assertions = 0;
            var root = new GameObject("DDS connection UI test");
            root.SetActive(false);
            XRInputDdsRouter router = null;
            try
            {
                router = root.AddComponent<XRInputDdsRouter>();
                var ui = root.AddComponent<XRPublishingUIController>();
                ui.router = router;
                ui.serviceToggle = Child<Toggle>(root);
                ui.targetIpInputField = Child<TMP_InputField>(root);
                ui.interfaceDropdown = Child<TMP_Dropdown>(root);
                Set(ui, "_initialized", true);
                Set(ui, "_ipInteractable", true);
                Set(ui, "_interfaceInteractable", true);
                router.PublishingChanged += _ => Invoke(ui, "HandlePublishingChanged", false);

                // A rejected start must undo the user's toggle, even before DDS is created.
                ui.serviceToggle.SetIsOnWithoutNotify(true);
                ui.targetIpInputField.SetTextWithoutNotify("192.168.1.2");
                ui.SetPublishing(true); // No selected interface.
                Check(!ui.serviceToggle.isOn && !router.IsPublishing, "Offline/invalid-interface start resets toggle."); assertions++;

                var capture = new Capture();
                var transport = new Transport();
                var source = new XRPublishSource("test", () => capture);
                var route = XRPublishRoute.Create<Capture>(source, new XRPublishChannel("test", DdsMessageType.StdFloat32),
                    "test", _ => new XRPublishSample(scalar: 1));
                var session = (XRPublishSession)Get(router, "_session");
                session.Start(new XRPublishPlan(new[] { route }), transport);
                ui.serviceToggle.SetIsOnWithoutNotify(true);
                Invoke(router, "HandleNetworkChanged", 1);
                Check(!router.IsPublishing && !ui.serviceToggle.isOn, "Network loss stops router and resets subscribed UI."); assertions++;
                Check(capture.Disposes == 1 && transport.Disposes == 1, "Connection failure releases transport and sources once."); assertions++;
                Check(ui.targetIpInputField.interactable && ui.interfaceDropdown.interactable && !string.IsNullOrEmpty(router.LastError),
                    "Network failure unlocks settings and preserves its reason."); assertions++;

                var networkUI = root.AddComponent<NetworkInfoUI>();
                var item = Child<NetworkItemView>(root);
                item.typeImage = Child<Image>(root);
                var texture = new Texture2D(1, 1);
                var offline = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
                try
                {
                    networkUI.offlineIcon = offline;
                    Set(networkUI, "primaryNetworkItem", item);
                    Invoke(networkUI, "RenderSnapshot", NetworkSnapshot.Empty);
                    Check(item.typeImage.enabled && item.typeImage.sprite == offline, "No network displays the configured offline icon."); assertions++;
                    item.Clear(offline);
                    Check(item.typeImage.enabled && item.typeImage.sprite == offline, "Repeated clearing preserves the offline icon."); assertions++;
                }
                finally { UnityEngine.Object.DestroyImmediate(offline); UnityEngine.Object.DestroyImmediate(texture); }

                ui.interfaceDropdown.gameObject.AddComponent<NetworkInterfacePrefBridge>();
                ui.interfaceDropdown.ClearOptions();
                ui.interfaceDropdown.AddOptions(new List<string> { "Network module owns this label" });
                Set(networkUI, "_dropdown", ui.interfaceDropdown);
                Invoke(networkUI, "RefreshDropdown", NetworkSnapshot.Empty);
                Check(ui.interfaceDropdown.options.Count == 1 && ui.interfaceDropdown.options[0].text == "Network module owns this label",
                    "NetworkInfoUI does not overwrite a bridge-owned dropdown."); assertions++;
            }
            finally { if (router != null) router.StopPublishing(); UnityEngine.Object.DestroyImmediate(root); }
            return assertions;
        }

        private static T Child<T>(GameObject parent) where T : Component
        {
            var child = new GameObject(typeof(T).Name, typeof(RectTransform));
            child.transform.SetParent(parent.transform);
            return child.AddComponent<T>();
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private sealed class Capture : IXRDataCapture
        {
            public int Disposes;
            public void Refresh() { }
            public void Dispose() { Disposes++; }
        }
        private sealed class Transport : IXRPublishTransport
        {
            public int Disposes;
            public void Open(IReadOnlyList<XRPublishChannel> channels) { }
            public void Write(int index, XRPublishSample sample, long timestamp) { }
            public void Dispose() { Disposes++; }
        }
    }
}
