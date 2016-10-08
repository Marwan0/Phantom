﻿//
// Kino/Bokeh - Depth of field effect
//
// Copyright (C) 2015, 2016 Keijiro Takahashi
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
using UnityEngine;
using UnityEngine.Serialization;

namespace Kino
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    public class Bokeh : MonoBehaviour
    {
        #region Editable properties

        [SerializeField, FormerlySerializedAs("_subject")]
        Transform _pointOfFocus;

        public Transform pointOfFocus {
            get { return _pointOfFocus; }
            set { _pointOfFocus = value; }
        }

        [SerializeField, FormerlySerializedAs("_distance")]
        float _focusDistance = 10.0f;

        public float distance {
            get { return _focusDistance; }
            set { _focusDistance = value; }
        }

        [SerializeField]
        float _fNumber = 1.4f;

        public float fNumber {
            get { return _fNumber; }
            set { _fNumber = value; }
        }

        [SerializeField]
        bool _useCameraFov = true;

        public bool useCameraFov {
            get { return _useCameraFov; }
            set { _useCameraFov = value; }
        }

        [SerializeField]
        float _focalLength = 0.05f;

        public float focalLength {
            get { return _focalLength; }
            set { _focalLength = value; }
        }

        public enum SampleCount { Low, Medium, High, VeryHigh }

        [SerializeField]
        public SampleCount _sampleCount = SampleCount.Medium;

        public SampleCount sampleCount {
            get { return _sampleCount; }
            set { _sampleCount = value; }
        }

        #endregion

        #if UNITY_EDITOR

        #region Debug properties

        [SerializeField]
        bool _visualize;

        #endregion

        #endif

        #region Private members

        // Height of the 35mm full-frame format (36mm x 24mm)
        const float kFilmHeight = 0.024f;

        [SerializeField] Shader _shader;
        Material _material;

        Camera TargetCamera {
            get { return GetComponent<Camera>(); }
        }

        float CalculateFocusDistance()
        {
            if (_pointOfFocus == null) return _focusDistance;
            var cam = TargetCamera.transform;
            return Vector3.Dot(_pointOfFocus.position - cam.position, cam.forward);
        }

        float CalculateFocalLength()
        {
            if (!_useCameraFov) return _focalLength;
            var fov = TargetCamera.fieldOfView * Mathf.Deg2Rad;
            return 0.5f * kFilmHeight / Mathf.Tan(0.5f * fov);
        }

        float CalculateMaxCoCRadius(int screenHeight)
        {
            // Estimate the allowable maximum radius of CoC from the sample
            // count level (the equation below was empirically derived).
            var radiusInPixels = (float)_sampleCount * 4 + 10;

            // Applying a 10% limit to the CoC radius to keep the size of
            // TileMax/NeighborMax small enough.
            return Mathf.Min(0.1f, radiusInPixels / screenHeight);
        }

        void SetUpShaderParameters(RenderTexture source)
        {
            var s1 = CalculateFocusDistance();
            _material.SetFloat("_Distance", s1);

            var f = CalculateFocalLength();
            var coeff = f * f / (_fNumber * (s1 - f) * kFilmHeight * 2);
            _material.SetFloat("_LensCoeff", coeff);

            _material.SetFloat("_MaxCoC", CalculateMaxCoCRadius(source.height));

            var invAspect = (float)source.height / source.width;
            _material.SetFloat("_InvAspect", invAspect);
        }

        #endregion

        #region MonoBehaviour functions

        void OnEnable()
        {
            // Initialize temporary objects (only when not set up yet).
            if (_material == null)
            {
                _material = new Material(Shader.Find("Hidden/Kino/Bokeh"));
                _material.hideFlags = HideFlags.HideAndDontSave;
            }

            // Requires camera depth texture.
            TargetCamera.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnDestroy()
        {
            // Destroy the temporary objects.
            if (_material != null)
                if (Application.isPlaying)
                    Destroy(_material);
                else
                    DestroyImmediate(_material);
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            var width = source.width;
            var height = source.height;
            var format = RenderTextureFormat.ARGBHalf;

            SetUpShaderParameters(source);

            // Pass #1 and #2 can be combined, but are separated to increase
            // the texture cache hits. In some configurations (e.g. PS4 with
            // HDR rendering), this makes a significant performance gain.

            // Pass #1 - Downsampling and CoC calculation
            var rt1 = RenderTexture.GetTemporary(width / 2, height / 2, 0, format);
            source.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt1, _material, 0);

            // Pass #2 - Prefiltering
            var rt2 = RenderTexture.GetTemporary(width / 2, height / 2, 0, format);
            rt1.filterMode = FilterMode.Bilinear;
            Graphics.Blit(rt1, rt2, _material, 1);

            // Pass #3 - Bokeh simulation
            rt2.filterMode = FilterMode.Bilinear;
            Graphics.Blit(rt2, rt1, _material, 2 + (int)_sampleCount);

            // Pass #4 - Composition
            _material.SetTexture("_BlurTex", rt1);
            Graphics.Blit(source, destination, _material, 6);

            #if UNITY_EDITOR

            // Focus range visualization
            if (_visualize)
                Graphics.Blit(rt2, destination, _material, 7);

            #endif

            RenderTexture.ReleaseTemporary(rt1);
            RenderTexture.ReleaseTemporary(rt2);
        }

        #endregion
    }
}