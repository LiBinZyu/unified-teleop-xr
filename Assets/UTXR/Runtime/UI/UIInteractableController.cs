using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIInteractableController : MonoBehaviour
{
    [SerializeField] private List<Selectable> targetSelectables = new List<Selectable>();

    private bool _isInteractable = true;

    public bool IsInteractable => _isInteractable;

    private void Awake()
    {
        if (targetSelectables != null)
        {
            for (int i = 0; i < targetSelectables.Count; i++)
            {
                if (targetSelectables[i] != null)
                {
                    _isInteractable = targetSelectables[i].interactable;
                    break;
                }
            }
        }
    }

    public void SetInteractable(bool interactable)
    {
        _isInteractable = interactable;

        if (targetSelectables == null) return;

        for (int i = 0; i < targetSelectables.Count; i++)
        {
            var selectable = targetSelectables[i];
            if (selectable != null)
            {
                selectable.interactable = interactable;

                if (!interactable)
                {
                    if (selectable is TMP_Dropdown tmpDropdown)
                    {
                        tmpDropdown.Hide();
                    }
                    else if (selectable is Dropdown dropdown)
                    {
                        dropdown.Hide();
                    }
                }
            }
        }
    }

    public void SetInteractableInversed(bool interactable)
    {
        SetInteractable(!interactable);
    }

    public void ToggleInteractable()
    {
        SetInteractable(!_isInteractable);
    }

    [ContextMenu("Collect Child Selectables")]
    private void CollectChildSelectables()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Collect Child Selectables");
#endif

        if (targetSelectables == null)
        {
            targetSelectables = new List<Selectable>();
        }

        List<Selectable> sourceList = new List<Selectable>(targetSelectables);
        HashSet<Selectable> seen = new HashSet<Selectable>();

        targetSelectables.Clear();

        if (sourceList.Count > 0)
        {
            for (int i = 0; i < sourceList.Count; i++)
            {
                var item = sourceList[i];
                if (item == null) continue;

                if (seen.Add(item))
                {
                    targetSelectables.Add(item);
                }

                var children = item.GetComponentsInChildren<Selectable>(true);
                for (int j = 0; j < children.Length; j++)
                {
                    var child = children[j];
                    if (child != null && seen.Add(child))
                    {
                        targetSelectables.Add(child);
                    }
                }
            }
        }
        else
        {
            var children = GetComponentsInChildren<Selectable>(true);
            for (int j = 0; j < children.Length; j++)
            {
                var child = children[j];
                if (child != null && seen.Add(child))
                {
                    targetSelectables.Add(child);
                }
            }
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
