using UnityEditor;
using UnityEngine;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Draws the marked string field with a "Set Random File ID" button next to it.</summary>
    [CustomPropertyDrawer(typeof(FileIdAttribute))]
    public class FileIdDrawer : PropertyDrawer
    {
        private const string ButtonTooltip = "Generates a new random file id (GUID).";
        private const string ButtonLabel = "Set Random File ID";

        /// <summary>Draws the id field with a button beside it that assigns a random GUID.</summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use [FileId] with a string field.");
                return;
            }

            const float buttonWidth = 140f;
            var fieldRect = new Rect(position.x, position.y, position.width - buttonWidth - 4f, position.height);
            var buttonRect = new Rect(position.xMax - buttonWidth, position.y, buttonWidth, position.height);

            EditorGUI.PropertyField(fieldRect, property, label, true);

            if (GUI.Button(buttonRect, new GUIContent(ButtonLabel, ButtonTooltip)))
            {
                property.stringValue = System.Guid.NewGuid().ToString();
                property.serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
