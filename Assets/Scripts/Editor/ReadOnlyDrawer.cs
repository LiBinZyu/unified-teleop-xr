using UnityEngine;
using UnityEditor;

/// <summary>
/// ReadOnlyAttribute 的绘制器，在 Inspector 中将其对应的属性设置为只读状态。
/// </summary>
[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 记录之前的 GUI 可用状态
        bool previousGUIState = GUI.enabled;

        // 禁用 GUI 绘制能力
        GUI.enabled = false;
        
        // 绘制属性（支持嵌套类型和数组元素）
        EditorGUI.PropertyField(position, property, label, true);
        
        // 恢复 GUI 状态
        GUI.enabled = previousGUIState;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // 根据基础类型和嵌套关系，返回正确绘制需要的高度
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
