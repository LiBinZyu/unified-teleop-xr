#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CycloneDDS.Editor
{
    [CustomEditor(typeof(DdsInputActionChannel))]
    public class DdsInputActionChannelEditor : UnityEditor.Editor
    {
        private SerializedProperty _isEnabledProp;
        private SerializedProperty _channelTypeProp;
        private SerializedProperty _topicNameProp;
        private SerializedProperty _frameIdProp;
        private SerializedProperty _qosProp;
        private SerializedProperty _inputActionProp;
        private SerializedProperty _convertToRosProp;

        private void OnEnable()
        {
            _isEnabledProp = serializedObject.FindProperty("isEnabled");
            _channelTypeProp = serializedObject.FindProperty("channelType");
            _topicNameProp = serializedObject.FindProperty("topicName");
            _frameIdProp = serializedObject.FindProperty("frameId");
            _qosProp = serializedObject.FindProperty("qos");
            _inputActionProp = serializedObject.FindProperty("inputAction");
            _convertToRosProp = serializedObject.FindProperty("convertToRosCoordinates");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var channel = (DdsInputActionChannel)target;
            var type = (DdsChannelType)_channelTypeProp.enumValueIndex;

            EditorGUILayout.LabelField("DDS Channel Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_isEnabledProp);
            EditorGUILayout.PropertyField(_channelTypeProp);
            EditorGUILayout.PropertyField(_topicNameProp);

            if (type == DdsChannelType.Pose || type == DdsChannelType.Point || type == DdsChannelType.Rotation || type == DdsChannelType.Vector3)
            {
                EditorGUILayout.PropertyField(_frameIdProp, new GUIContent("Frame ID", "TF frame ID (Default: map)"));
            }

            EditorGUILayout.PropertyField(_qosProp);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Input Action Binding", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_inputActionProp, new GUIContent(GetActionBindingLabel(type), "InputAction stream for this channel"));

            if (type == DdsChannelType.Pose || type == DdsChannelType.Point || type == DdsChannelType.Rotation || type == DdsChannelType.Vector3 || type == DdsChannelType.Twist)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_convertToRosProp, new GUIContent("Convert to ROS Coords", "Transform Unity LHS (X right, Y up, Z fwd) to ROS RHS (X fwd, Y left, Z up)"));
            }

            if (Application.isPlaying && channel.ddsHandle > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(
                    $"DDS Publisher Handle: {channel.ddsHandle}\n" +
                    $"Matched Subscribers: {channel.matchedSubscribers}\n" +
                    $"Published Samples: {channel.publishedCount}",
                    MessageType.Info
                );
            }

            serializedObject.ApplyModifiedProperties();
        }

        private string GetActionBindingLabel(DdsChannelType type)
        {
            switch (type)
            {
                case DdsChannelType.Pose:
                    return "Pose Action (Pose/Vector3)";
                case DdsChannelType.Point:
                    return "Point Action (Vector3/Pose)";
                case DdsChannelType.Rotation:
                    return "Rotation Action (Quaternion/Pose)";
                case DdsChannelType.Vector3:
                    return "Vector3 Action (Velocity/Accel)";
                case DdsChannelType.Twist:
                    return "Twist Action (Vector2 Thumbstick/Move)";
                case DdsChannelType.Float:
                    return "Float Action (Trigger/Axis Value)";
                case DdsChannelType.Bool:
                    return "Bool Action (Button/Is Tracked)";
                case DdsChannelType.Int32:
                    return "Int32 Action (Tracking State/Flags)";
                default:
                    return "Input Action";
            }
        }
    }
}
#endif
