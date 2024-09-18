﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace usfxr
{
    [CustomPropertyDrawer(typeof(TinySynthSound))]
    public class TinySynthEditor : PropertyDrawer
    {
        private bool expand = true;
        private bool expandPresets;
        private float height;

        private const int RangeScale = 100;
        private const float ButtonWidth = 20;
        private const float ButtonMargin = 5;
        private const float RangeScaleToNormalized = 1f / RangeScale;
        private const float Margin = 2;

        private static FieldInfo[] paramFields;
        private static Dictionary<string, ParamData> paramData;

        private static readonly GUIStyle lockButtonStyle = "IN LockButton";
        private static readonly Color curveColor = new(1.0f, 140.0f / 255.0f, 0.0f, 1.0f);


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            UpdateReflection();

            var startY = position.y;
            position.height = EditorGUIUtility.singleLineHeight;

            expand = EditorGUI.BeginFoldoutHeaderGroup(position, expand, property.name, null, rect => ShowHeaderContextMenu(rect, property));

            if (expand)
            {
                OnExpandedGUI(ref position, property);
            }

            EditorGUI.EndFoldoutHeaderGroup();
            height = position.y + position.height - startY;
        }


        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return height;
        }


        private void OnExpandedGUI(ref Rect position, SerializedProperty property)
        {
            position.y += EditorGUIUtility.singleLineHeight + Margin;
            var previewRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight * 5);

            // stick a button behind the preview so we can click it to play
            if (GUI.Button(previewRect, "play"))
            {
                PlayPreview(property);
            }

            // don't render the preview if it's tiny
            if (previewRect.width > 1)
            {
                DoRenderPreview(TinySynthPlayer.GetClip(PropertyToParams(property)), previewRect);
            }

            position.y += previewRect.height + Margin;

            expandPresets = EditorGUI.Foldout(position, expandPresets, "Presets");
            // the change check needs to go after the foldout, if not it'll trigger a preview on open/close
            EditorGUI.BeginChangeCheck();

            if (expandPresets)
            {
                position.y += EditorGUIUtility.singleLineHeight + Margin;
                OnPresetGUI(position, property);
                position.y += EditorGUIUtility.singleLineHeight + Margin;
            }

            foreach (var prop in GetVisibleChildren(property))
            {
                position.y += EditorGUIUtility.singleLineHeight + Margin;

                if (prop.type == "Enum")
                {
                    WidgetWaveType(position, prop);
                }
                else if (prop.type == "float")
                {
                    WidgetSlider(position, prop);
                }
            }

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            property.serializedObject.ApplyModifiedProperties();

            PlayPreview(property);
        }


        private static void WidgetSlider(Rect position, SerializedProperty property)
        {
            if (!paramData.TryGetValue(property.name, out var data))
            {
                return;
            }

            var label = new GUIContent(property.displayName, data.tooltip);

            var sliderPosition = new Rect(position);
            sliderPosition.width -= (ButtonWidth + ButtonMargin) * 2;

            property.floatValue = EditorGUI.IntSlider(
                sliderPosition,
                label,
                Mathf.RoundToInt(property.floatValue * RangeScale),
                data.min,
                data.max) * RangeScaleToNormalized;

            ExtraButtons(position, property);
        }


        private static void ExtraButtons(Rect position, SerializedProperty property)
        {
            if (!paramData.TryGetValue(property.name, out var data))
            {
                return;
            }

            var buttonPosition = new Rect(position) {x = position.x + position.width - ButtonWidth * 2 - ButtonMargin, width = ButtonWidth};

            if (GUI.Button(buttonPosition, new GUIContent("R", "Reset this parameter to its default value")))
            {
                if (property.type == "Enum")
                {
                    property.enumValueIndex = Mathf.FloorToInt(data.@default * RangeScaleToNormalized);
                }
                else if (property.type == "float")
                {
                    property.floatValue = data.@default * RangeScaleToNormalized;
                }
            }

            buttonPosition.x = position.x + position.width - ButtonWidth;
            data.locked = GUI.Toggle(buttonPosition, data.locked, new GUIContent("", "Lock this parameter from changes via templates/mutation"), lockButtonStyle);
        }


        private static void WidgetWaveType(Rect position, SerializedProperty property)
        {
            var wavePickerPos = new Rect(position);
            wavePickerPos.width -= (ButtonWidth + ButtonMargin) * 2;
            EditorGUI.PropertyField(wavePickerPos, property);
            ExtraButtons(position, property);
        }


        private static void OnPresetGUI(Rect position, SerializedProperty property)
        {
            var rect = new Rect(position)
            {
                width = (position.width + ButtonMargin) / 7 - ButtonMargin,
                height = EditorGUIUtility.singleLineHeight * 2
            };

            if (PresetButton(ref rect, "LASER\nSHOOT"))
            {
                SetParam(property, TinySynthPreset.LaserShoot());
            }

            if (PresetButton(ref rect, "PICKUP\nCOIN"))
            {
                SetParam(property, TinySynthPreset.PickupCoin());
            }

            if (PresetButton(ref rect, "EXPLOSION"))
            {
                SetParam(property, TinySynthPreset.Explosion());
            }

            if (PresetButton(ref rect, "POWER UP"))
            {
                SetParam(property, TinySynthPreset.PowerUp());
            }

            if (PresetButton(ref rect, "HIT\nHURT"))
            {
                SetParam(property, TinySynthPreset.HitHurt());
            }

            if (PresetButton(ref rect, "JUMP"))
            {
                SetParam(property, TinySynthPreset.Jump());
            }

            if (PresetButton(ref rect, "BLIP\nSELECT"))
            {
                SetParam(property, TinySynthPreset.BlipSelect());
            }
        }


        private static bool PresetButton(ref Rect position, string label)
        {
            var pressed = GUI.Button(position, label);
            position.x += position.width + ButtonMargin;

            return pressed;
        }


        private static void ShowHeaderContextMenu(Rect position, SerializedProperty property)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Export WAV"), false, () => OnExportWav(property));
            menu.DropDown(position);
        }


        private static void OnExportWav(SerializedProperty property)
        {
            var path = EditorUtility.SaveFilePanel("Export as WAV", "", property.name + ".wav", "wav");

            if (path.Length == 0)
            {
                return;
            }

            var synth = new SfxrRenderer {param = PropertyToParams(property)};
            File.WriteAllBytes(path, synth.GetWavFile());
        }


        /// <summary>
        ///     Gets visible children of `SerializedProperty` at 1 level depth.
        /// </summary>
        /// <param name="serializedProperty">Parent `SerializedProperty`.</param>
        /// <returns>Collection of `SerializedProperty` children.</returns>
        private static IEnumerable<SerializedProperty> GetVisibleChildren(SerializedProperty serializedProperty)
        {
            var currentProperty = serializedProperty.Copy();
            var nextSiblingProperty = serializedProperty.Copy();
            nextSiblingProperty.NextVisible(false);

            if (!currentProperty.NextVisible(true))
            {
                yield break;
            }

            do
            {
                if (SerializedProperty.EqualContents(currentProperty, nextSiblingProperty))
                {
                    break;
                }

                yield return currentProperty;
            } while (currentProperty.NextVisible(false));
        }


        /// <summary>
        ///     Iterates over all the fields in a SfxrParams instance and applies these to a serialized property
        ///     This is needed to make the code that directly modifies SfxrParams play nice with the Unity undo system
        /// </summary>
        private static void SetParam(SerializedProperty property, TinySynthSound param)
        {
            // iterate over all the fields
            foreach (var field in paramFields)
            {
                // find the corresponding property
                var prop = property.FindPropertyRelative(field.Name);

                if (paramData[field.Name].locked)
                {
                    continue;
                }

                // apply the value from the SfxrParams struct to the SerializedProperty
                // only enums and floats for now, add more as needed
                if (prop.type == "Enum")
                {
                    prop.enumValueIndex = Convert.ToInt32(field.GetValue(param));
                }
                else if (prop.type == "float")
                {
                    prop.floatValue = (float) field.GetValue(param);
                }
            }
        }


        private static void UpdateReflection()
        {
            if (paramFields != null && paramFields.Length > 0)
            {
                return;
            }

            // cache the fields on SfxrParams, these won't change 
            paramFields = typeof(TinySynthSound).GetFields(BindingFlags.Public | BindingFlags.Instance);

            // now we build a little lookup table with the range and default value attributes
            paramData = new Dictionary<string, ParamData>();

            foreach (var field in paramFields)
            {
                var data = new ParamData {@default = 0, min = 0, max = 1};

                if (field.GetCustomAttribute(typeof(RangeAttribute)) is RangeAttribute rangeAttribute)
                {
                    data.max = Mathf.RoundToInt(rangeAttribute.max * RangeScale);
                    data.min = Mathf.RoundToInt(rangeAttribute.min * RangeScale);
                }

                if (field.GetCustomAttribute(typeof(TooltipAttribute)) is TooltipAttribute tooltipAttribute)
                {
                    data.tooltip = tooltipAttribute.tooltip;
                }

                if (field.GetCustomAttribute(typeof(SfxrDefault)) is SfxrDefault sfxDefault)
                {
                    data.@default = Mathf.RoundToInt(sfxDefault.value * RangeScale);
                }

                paramData.Add(field.Name, data);
            }
        }


        private static void PlayPreview(SerializedProperty property)
        {
            TinySynthPlayer.Play(PropertyToParams(property), true);
        }


        private static TinySynthSound PropertyToParams(SerializedProperty property)
        {
            var target = property.serializedObject.targetObject;
            var type = target.GetType();
            var field = type.GetField(property.name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
            {
                Debug.LogError($"Field {property.name} not found in {type}");

                return new TinySynthSound();
            }

            return (TinySynthSound) field.GetValue(target);
        }


        private static void DoRenderPreview(AudioClip clip, Rect rect)
        {
            var samples = new float[clip.samples];
            clip.GetData(samples, 0);

            AudioCurveRendering.AudioCurveAndColorEvaluator dlg =
                delegate(float x, out Color color)
                {
                    color = curveColor;

                    if (clip.samples <= 0)
                    {
                        return 0;
                    }

                    var p = Mathf.FloorToInt(Mathf.Clamp(x * (clip.samples - 1), 0.0f, clip.samples - 1));

                    return samples[p];
                };

            rect = AudioCurveRendering.BeginCurveFrame(rect);
            AudioCurveRendering.DrawSymmetricFilledCurve(rect, dlg);
            AudioCurveRendering.EndCurveFrame();
        }


        private class ParamData
        {
            public int min;
            public int max;
            public int @default;
            public string tooltip;
            public bool locked;
        }
    }
}