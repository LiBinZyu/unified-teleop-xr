using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UTXR.Publishing;

namespace UTXR.UI
{
    /// <summary>Static message configuration UI. Generate sibling rows in the editor; only bind at runtime.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class XRPublishMessageListUI : MonoBehaviour
    {
        public XRPublishConfiguration configuration;
        [Tooltip("Scene template containing a TMP_Text and a UI Toggle with TMP labels. Generated items are its siblings.")]
        public GameObject itemTemplate;
        [SerializeField, HideInInspector] private List<GameObject> _items = new();

        private readonly List<(Toggle Toggle, UnityAction<bool> Listener)> _bindings = new();
        private XRPublishConfiguration _boundConfiguration;
        private bool _rebind;

        private void OnEnable() => BindItems();
        private void OnDisable() => Unbind();
        private void OnValidate() => _rebind = true;
        private void Update() { if (_rebind) BindItems(); }

        private void BindItems()
        {
            _rebind = false;
            Unbind();
            if (configuration == null) return;
            var messages = configuration.Messages;
            if (_items.Count != messages.Count)
            {
                if (Application.isPlaying)
                    Debug.LogWarning("Generate Message Items in the editor before using the message configuration UI.", this);
                return;
            }
            _boundConfiguration = configuration;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] == null) continue;
                var toggle = _items[i].GetComponentInChildren<Toggle>(true);
                var label = _items[i].GetComponentInChildren<TMP_Text>(true);
                if (toggle == null || label == null)
                    throw new InvalidOperationException("Each message item needs a TMP_Text and a Toggle.");
                string topic = messages[i].Topic;
                label.text = topic;
                UnityAction<bool> listener = value => configuration.SetMessageEnabled(topic, value);
                toggle.onValueChanged.AddListener(listener);
                _bindings.Add((toggle, listener));
            }
            _boundConfiguration.MessagesChanged += Synchronize;
            Synchronize();
        }

        private void Synchronize()
        {
            var messages = _boundConfiguration.Messages;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null)
                    ToggleImageHelper.SetIsOnWithoutNotify(_items[i].GetComponentInChildren<Toggle>(true), messages[i].Option.enabled);
        }

        private void Unbind()
        {
            if (_boundConfiguration != null) _boundConfiguration.MessagesChanged -= Synchronize;
            _boundConfiguration = null;
            foreach (var binding in _bindings)
                if (binding.Toggle != null) binding.Toggle.onValueChanged.RemoveListener(binding.Listener);
            _bindings.Clear();
        }

#if UNITY_EDITOR
        [ContextMenu("Generate Message Items (Editor Only)")]
        public void GenerateItems()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Generate message items in edit mode.");
            if (configuration == null || itemTemplate == null || itemTemplate.transform.parent == null)
                throw new InvalidOperationException("Assign Configuration and a scene Item Template with a parent.");
            if (transform == itemTemplate.transform || transform.IsChildOf(itemTemplate.transform))
                throw new InvalidOperationException("Place this controller outside the item template.");
            if (itemTemplate.GetComponentInChildren<TMP_Text>(true) == null || itemTemplate.GetComponentInChildren<Toggle>(true) == null)
                throw new InvalidOperationException("The template must contain a TMP_Text and a Toggle.");

            Unbind();
            UnityEditor.Undo.IncrementCurrentGroup();
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Generate message configuration items");
            UnityEditor.Undo.RecordObject(this, "Update message items");
            foreach (var item in _items)
                if (item != null && item != itemTemplate) UnityEditor.Undo.DestroyObjectImmediate(item);
            _items.Clear();
            UnityEditor.Undo.RecordObject(itemTemplate, "Hide message template");
            itemTemplate.SetActive(false);
            int sibling = itemTemplate.transform.GetSiblingIndex() + 1;
            foreach (var message in configuration.Messages)
            {
                var item = Instantiate(itemTemplate, itemTemplate.transform.parent);
                UnityEditor.Undo.RegisterCreatedObjectUndo(item, "Create message item");
                item.name = "Message " + message.Topic;
                item.transform.SetSiblingIndex(sibling++);
                item.SetActive(true);
                _items.Add(item);
            }
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.Undo.CollapseUndoOperations(undoGroup);
            BindItems();
        }
#endif
    }
}
