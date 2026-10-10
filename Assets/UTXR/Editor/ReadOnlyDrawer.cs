using UnityEngine;
using UnityEditor;

using UTXR.Core;
namespace UTXR.Editor
{
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
    
            // 记录之前的 GUI 可用状态
            GUI.enabled = false;
            
            // 绘制属性（支持嵌套类型和数组元素）
            EditorGUI.PropertyField(position, property, label, true);
            
            // 记录之前的 GUI 可用状态
            GUI.enabled = previousGUIState;
        }
    
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // 绘制属性（支持嵌套类型和数组元素）
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}
